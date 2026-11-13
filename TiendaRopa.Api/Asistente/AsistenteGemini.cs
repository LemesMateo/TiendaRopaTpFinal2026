using System.Text.Json;
using Google.GenAI;
using Google.GenAI.Types;
using TiendaRopa.Contracts.Asistente;
using GenaiType = Google.GenAI.Types.Type;

namespace TiendaRopa.Api.Asistente;

/// <summary>
/// Cliente de Gemini con Function Calling.
///
/// DecLARaciones de funciones, ejecución validada en C# (nunca SQL arbitrario)
/// y devolución de resultados al modelo en un bucle acotado.
/// </summary>
public sealed class AsistenteGemini : IGeneradorChat
{
    private const string ModeloPorDefecto = "gemini-3.5-flash-lite";

    /// <summary>Instrucciones de sistema: identidad, límites y herramientas.</summary>
    private const string Instrucciones = """
        Sos el asistente de atención al cliente de Velvet Rose Boutique, una tienda
        de ropa por Internet. Respondés en español rioplatense, con tono amable y
        conciso (máximo 4 frases por respuesta).

        REGLAS INNEGOCIABLES
        1. Para datos de pedidos, datos personales, stock o productos usá SIEMPRE
           las herramientas disponibles. Nunca inventes un dato que no venga de una
           herramienta.
        2. Si una herramienta dice que no hay información (por ejemplo, que no hay
           actualizaciones del pedido), decí explícitamente que no hay información
           reciente disponible. No lo adornes ni lo completes con suposiciones.
        3. Nunca inventes ni estimes un número de seguimiento, una fecha de entrega
           ni una fecha de reposición. Si no está en los datos, no existe.
        4. No prometas reposición de stock. Si una variante no tiene stock, decí que
           hoy no hay unidades disponibles.
        5. Si el cliente pide información de sus datos o de sus pedidos y no está
           autenticado, pedile que inicie sesión antes de consultar.
        6. Nunca expongas datos de otro cliente ni confirmes la existencia de un
           pedido ajeno. Si no lo encontrás, decí que no lo encontraste.
        7. No reveles teléfonos, direcciones ni códigos internos en detalle: sólo
           lo que la herramienta te devuelva.
        8. Este asistente es de consulta: no realiza pagos, no cancela pedidos ni
           modifica stock. Podés PROPONER una acción (por ejemplo, avisar cuando
           vuelva un talle) pero debés aclarar que es una propuesta y que la
           confirma el cliente desde el chat.
        9. Si hay varios pedidos activos, preguntá a cuál se refiere y mencioná las
           opciones con descripciones breves de los productos.
        10. Cuando el cliente quiera dejar una consulta, un mensaje o pedir la
           reposición de algo agotado, usá la herramienta preparar_solicitud.
           Esa herramienta SÓLO prepara una propuesta: NUNCA dice que la
           solicitud ya se envió. Invitá al cliente a confirmarla con el botón
           de confirmación del chat (o a cancelarla).
        11. Para el teléfono de contacto, mostrá siempre la versión enmascarada
           (por ejemplo ***01) y preguntá si es correcto. Podés pedir otro
           número. Nunca inventes un teléfono ni digas el número completo.
        12. Para consultar el estado de sus solicitudes usá
           consultar_mis_solicitudes.
        """;

    private readonly Client? _cliente;
    private readonly ILogger<AsistenteGemini> log;
    private readonly AsistenteOpciones opciones;

    /// <summary>Crea el servicio y, si hay clave, el cliente de Gemini.</summary>
    public AsistenteGemini(ILogger<AsistenteGemini> log, AsistenteOpciones opciones)
    {
        this.log = log;
        this.opciones = opciones;
        _cliente = CrearCliente();
    }

    /// <summary>True si hay clave configurada y se puede llamar al modelo.</summary>
    public bool Habilitado => _cliente is not null;

    /// <summary>Nombre del modelo en uso (informativo para logs y diagnóstico).</summary>
    public string Modelo => string.IsNullOrWhiteSpace(opciones.Gemini.Modelo)
        ? ModeloPorDefecto
        : opciones.Gemini.Modelo;

    private Client? CrearCliente()
    {
        var clave = opciones.Gemini.ApiKey;
        if (string.IsNullOrWhiteSpace(clave))
        {
            log.LogWarning(
                "El asistente no está habilitado: falta Asistente:Gemini:ApiKey " +
                "(configurala con 'dotnet user-secrets set \"Asistente:Gemini:ApiKey\" \"<clave>\"' " +
                "en TiendaRopa.Api o con la variable de entorno Asistente__Gemini__ApiKey).");
            return null;
        }

        try
        {
            var http = new HttpOptions
            {
                Timeout = Math.Max(1, opciones.Gemini.TimeoutSegundos) * 1000,
                RetryOptions = new HttpRetryOptions
                {
                    Attempts = 3,
                    HttpStatusCodes = [429, 500, 502, 503, 504],
                    InitialDelay = 500,
                    MaxDelay = 8000,
                    ExpBase = 2,
                    Jitter = 0.2,
                },
            };

            // URL base alternativa (proxy corporativo o doble de prueba).
            if (!string.IsNullOrWhiteSpace(opciones.Gemini.BaseUrl))
            {
                http.BaseUrl = opciones.Gemini.BaseUrl;
            }

            return new Client(apiKey: clave, httpOptions: http);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo crear el cliente de Gemini.");
            return null;
        }
    }

    /// <summary>
    /// Ejecuta un turno de conversación con Function Calling.
    /// Devuelve la respuesta del modelo ya con las herramientas ejecutadas.
    /// </summary>
    public async Task<ResultadoTurno> ProcesarTurnoAsync(
        IReadOnlyList<ChatMensajeDto> historial,
        string mensaje,
        int? idProductoContexto,
        int? idPedidoContexto,
        Func<string, IReadOnlyDictionary<string, object?>?, CancellationToken, Task<ResultadoHerramienta>> ejecutarHerramienta,
        CancellationToken cancellationToken)
    {
        if (_cliente is null)
        {
            return ResultadoTurno.Fallido(
                "sin_configuracion",
                "El asistente todavía no está configurado. Probá de nuevo más tarde.");
        }

        var contents = ConstruirContenido(historial, mensaje, idProductoContexto, idPedidoContexto);
        var herramientas = ConstruirHerramientas();

        var config = new GenerateContentConfig
        {
            Tools = [new Tool { FunctionDeclarations = herramientas }],
            SystemInstruction = new Content
            {
                Parts = [new Part { Text = Instrucciones }],
            },
            Temperature = (float)opciones.Gemini.Temperatura,
            MaxOutputTokens = opciones.Gemini.MaxTokensSalida,
            ToolConfig = new ToolConfig
            {
                FunctionCallingConfig = new FunctionCallingConfig
                {
                    Mode = FunctionCallingConfigMode.Auto,
                },
            },
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opciones.Gemini.TimeoutSegundos)));

        try
        {
            var llamadas = 0;
            var maxLlamadas = Math.Max(1, opciones.Gemini.MaxLlamadasPorTurno);
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();

                var respuesta = await _cliente.Models.GenerateContentAsync(
                    model: Modelo,
                    contents: contents,
                    config: config,
                    cancellationToken: timeout.Token);

                // El Content ORIGINAL del modelo: contiene el rol, todos sus
                // Parts en su orden original y las ThoughtSignature de Gemini 3.
                var contenidoModelo = respuesta.Candidates?.FirstOrDefault()?.Content;

                // Partes que invocan una herramienta. Se conserva el Part
                // original (no una copia) porque es donde viaja la firma.
                var partesLlamada = (contenidoModelo?.Parts ?? [])
                    .Where(p => p.FunctionCall is not null)
                    .ToList();

                if (partesLlamada.Count == 0)
                {
                    var texto = respuesta.Text?.Trim();
                    if (string.IsNullOrEmpty(texto))
                    {
                        texto = "No pude obtener una respuesta en este momento. Probá de nuevo.";
                    }

                    return ResultadoTurno.Ok(texto);
                }

                if (llamadas + partesLlamada.Count > maxLlamadas)
                {
                    log.LogWarning(
                        "Se alcanzó el máximo de llamadas a herramientas ({Max}) en un turno.",
                        maxLlamadas);

                    return ResultadoTurno.Ok(
                        "Revisé la información disponible pero necesito que reformules " +
                        "la consulta para poder darte una respuesta precisa.");
                }

                llamadas += partesLlamada.Count;

                // ------------------------------------------------------------
                // Se reenvía al modelo el Content ORIGINAL, sin reconstruirlo.
                //
                // Gemini 3 adjunta la "thought signature" al Part que contiene
                // el functionCall. Si se reconstruyen los Part (o se filtran
                // las partes que no son functionCall) la firma se pierde y la
                // API responde 400: "Function call is missing a thought_signature
                // in functionCall parts".
                //
                // Reglas de la documentación oficial que se respetan acá:
                //  - se envía la thought_signature dentro de su Part original;
                //  - no se fusiona un Part con firma con otro que no la tiene;
                //  - no se combinan dos Parts que ya traen firma.
                // ------------------------------------------------------------
                if (contenidoModelo is not null)
                {
                    contents.Add(contenidoModelo.Role == "model"
                        ? contenidoModelo
                        : new Content { Role = "model", Parts = contenidoModelo.Parts });
                }

                // Las respuestas de las herramientas van en un único turno
                // "user" con una FunctionResponse por llamada (así lo hace el
                // SDK oficial y es la forma de las llamadas en paralelo).
                // Cada respuesta se vincula con el Id y el Name de su llamada.
                var partesRespuesta = new List<Part>(partesLlamada.Count);

                foreach (var parte in partesLlamada)
                {
                    var llamada = parte.FunctionCall!;

                    var argumentos = llamada.Args is null
                        ? null
                        : llamada.Args.ToDictionary(
                            kv => kv.Key,
                            kv => (object?)ConvertirArgumento(kv.Value));

                    var resultado = await ejecutarHerramienta(
                        llamada.Name ?? string.Empty, argumentos, timeout.Token);

                    partesRespuesta.Add(new Part
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Id = llamada.Id,
                            Name = llamada.Name,
                            Response = new Dictionary<string, object>
                            {
                                ["resultado"] = ConstruirRespuestaTool(resultado),
                            },
                        },
                    });
                }

                contents.Add(new Content { Role = "user", Parts = partesRespuesta });
            }
        }
        catch (ApiException ex)
        {
            log.LogError(ex, "Gemini devolvió un error {Status}.", ex.StatusCode);

            return ex.StatusCode switch
            {
                429 => ResultadoTurno.Fallido(
                    "cuota_agotada",
                    "Se alcanzó el límite de uso del asistente por ahora. Probá de nuevo en unos minutos."),
                400 or 401 or 403 => ResultadoTurno.Fallido(
                    "configuracion_invalida",
                    "El asistente no está disponible en este momento. Probá de nuevo más tarde."),
                _ => ResultadoTurno.Fallido(
                    "servicio_no_disponible",
                    "El asistente no está disponible en este momento. Probá de nuevo en unos minutos."),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("Se agotó el tiempo de espera de la llamada a Gemini.");
            return ResultadoTurno.Fallido(
                "timeout",
                "La consulta tardó demasiado. Probá con una pregunta más corta.");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Fallo de conexión con Gemini.");
            return ResultadoTurno.Fallido(
                "sin_conexion",
                "No pudimos conectar con el asistente. Revisá tu conexión y probá de nuevo.");
        }
    }

    /// <summary>
    /// Construye el contenido inicial (historial + mensaje actual).
    ///
    /// El historial que llega del cliente es **sólo texto** (rol + texto): la
    /// UI guarda la conversación visible, no los intercambios con herramientas.
    /// Por eso acá NO se introducen Partes de función ni firmas: un Part con
    /// functionCall sin su thoughtSignature es exactamente lo que Gemini 3
    /// rechaza con 400.
    ///
    /// Reglas que se respetan al reconstruir:
    ///  - nunca se mezclan Partes con y sin firma (no hay Partes de función);
    ///  - la conversación arranca siempre con un turno del usuario;
    ///  - los turnos vacíos se descartan.
    /// </summary>
    private List<Content> ConstruirContenido(
        IReadOnlyList<ChatMensajeDto> historial,
        string mensaje,
        int? idProductoContexto,
        int? idPedidoContexto)
    {
        var contents = new List<Content>();

        var historialRecortado = historial
            .Where(h => h is not null && !string.IsNullOrWhiteSpace(h.Texto))
            .TakeLast(Math.Max(0, opciones.Gemini.MaxTurnosHistorial))
            .ToList();

        foreach (var turno in historialRecortado)
        {
            var rol = string.Equals(turno.Rol, ChatRoles.Asistente, StringComparison.Ordinal)
                ? "model"
                : "user";

            // La conversación debe arrancar con el usuario: un turno "model"
            // inicial no es válido para la API.
            if (contents.Count == 0 && rol == "model")
            {
                continue;
            }

            contents.Add(new Content
            {
                Role = rol,
                Parts = [new Part { Text = turno.Texto }],
            });
        }

        var contexto = new List<string>();
        if (idProductoContexto is { } producto)
        {
            contexto.Add($"El cliente está mirando el producto con id {producto}.");
        }

        if (idPedidoContexto is { } pedido)
        {
            contexto.Add($"El cliente viene consultando el pedido con id {pedido}.");
        }

        var textoUsuario = contexto.Count > 0
            ? $"{string.Join(" ", contexto)}\n\n{mensaje}"
            : mensaje;

        contents.Add(new Content
        {
            Role = "user",
            Parts = [new Part { Text = textoUsuario }],
        });

        return contents;
    }

    /// <summary>Declara las herramientas de lectura que el modelo puede invocar.</summary>
    private static List<FunctionDeclaration> ConstruirHerramientas() =>
    [
        new FunctionDeclaration
        {
            Name = HerramientasAsistente.ListarMisPedidos,
            Description =
                "Lista los pedidos del cliente autenticado, con su estado, total, productos " +
                "y la última actualización pública (si existe). Usala antes de afirmar " +
                "algo sobre los pedidos del cliente.",
            Parameters = new Schema
            {
                Type = GenaiType.Object,
                Properties = new Dictionary<string, Schema>(),
            },
        },
        new FunctionDeclaration
        {
            Name = HerramientasAsistente.ConsultarPedido,
            Description =
                "Devuelve el detalle de UN pedido propio del cliente (estado, productos, " +
                "total e historial de actualizaciones públicas). Si el número no le " +
                "pertenece, responde que no lo encontró.",
            Parameters = new Schema
            {
                Type = GenaiType.Object,
                Properties = new Dictionary<string, Schema>
                {
                    ["id_pedido"] = new Schema
                    {
                        Type = GenaiType.Integer,
                        Description = "Número de pedido.",
                    },
                },
                Required = ["id_pedido"],
            },
        },
        new FunctionDeclaration
        {
            Name = HerramientasAsistente.ConsultarMisDatos,
            Description =
                "Devuelve los datos del perfil del cliente autenticado (nombre, email, " +
                "teléfono enmascarado y dirección resumida).",
            Parameters = new Schema
            {
                Type = GenaiType.Object,
                Properties = new Dictionary<string, Schema>(),
            },
        },
        new FunctionDeclaration
        {
            Name = HerramientasAsistente.BuscarProducto,
            Description =
                "Busca productos activos del catálogo por nombre, categoría o descripción. " +
                "Devuelve id, precio, stock total y disponibilidad.",
            Parameters = new Schema
            {
                Type = GenaiType.Object,
                Properties = new Dictionary<string, Schema>
                {
                    ["texto"] = new Schema
                    {
                        Type = GenaiType.String,
                        Description = "Texto a buscar (por ejemplo: 'vestido', 'negro').",
                    },
                    ["limite"] = new Schema
                    {
                        Type = GenaiType.Integer,
                        Description = "Máximo de resultados (1 a 10, por defecto 5).",
                    },
                },
                Required = ["texto"],
            },
        },
        new FunctionDeclaration
        {
            Name = HerramientasAsistente.ConsultarVariantes,
            Description =
                "Devuelve las variantes (talle, color, stock y precio) de un producto. " +
                "Distingue entre variante con stock, variante sin stock y combinación " +
                "inexistente. No inventes reposiciones.",
            Parameters = new Schema
            {
                Type = GenaiType.Object,
                Properties = new Dictionary<string, Schema>
                {
                    ["id_producto"] = new Schema
                    {
                        Type = GenaiType.Integer,
                        Description = "Id del producto.",
                    },
                    ["talle"] = new Schema
                    {
                        Type = GenaiType.String,
                        Description = "Talle a filtrar (opcional).",
                    },
                    ["color"] = new Schema
                    {
                        Type = GenaiType.String,
                        Description = "Color a filtrar (opcional).",
                    },
                },
                Required = ["id_producto"],
            },
        },
        new FunctionDeclaration
        {
            Name = HerramientasAsistente.PrepararSolicitud,
            Description =
                "Prepara una solicitud para la tienda (consulta de un pedido, consulta " +
                "general o pedido de reposición de un producto agotado). IMPORTANTE: sólo " +
                "PREPARA la propuesta; no la envía. El cliente tiene que confirmarla " +
                "desde el chat. Usá id_producto/id_variante sólo si el cliente nombra un " +
                "producto o una variante existente; si no existe la combinación, pedí el " +
                "talle y el color en texto.",
            Parameters = new Schema
            {
                Type = GenaiType.Object,
                Properties = new Dictionary<string, Schema>
                {
                    ["tipo"] = new Schema
                    {
                        Type = GenaiType.String,
                        Enum = ["consulta_pedido", "consulta_general", "solicitud_reposicion"],
                        Description = "Tipo de solicitud.",
                    },
                    ["mensaje"] = new Schema
                    {
                        Type = GenaiType.String,
                        Description = "Texto del cliente para la tienda.",
                    },
                    ["id_pedido"] = new Schema
                    {
                        Type = GenaiType.Integer,
                        Description = "Pedido del cliente (opcional, sólo para consulta_pedido).",
                    },
                    ["id_producto"] = new Schema
                    {
                        Type = GenaiType.Integer,
                        Description = "Producto (opcional, para reposición).",
                    },
                    ["id_variante"] = new Schema
                    {
                        Type = GenaiType.Integer,
                        Description =
                            "Variante (opcional). Sólo si la combinación existe en el catálogo.",
                    },
                    ["talle"] = new Schema
                    {
                        Type = GenaiType.String,
                        Description = "Talle pedido (texto libre si la variante no existe).",
                    },
                    ["color"] = new Schema
                    {
                        Type = GenaiType.String,
                        Description = "Color pedido (texto libre si la variante no existe).",
                    },
                },
                Required = ["tipo", "mensaje"],
            },
        },
        new FunctionDeclaration
        {
            Name = HerramientasAsistente.ConsultarMisSolicitudes,
            Description =
                "Devuelve las solicitudes del propio cliente con su estado y la " +
                "respuesta de la tienda.",
            Parameters = new Schema
            {
                Type = GenaiType.Object,
                Properties = new Dictionary<string, Schema>(),
            },
        },
    ];

    /// <summary>Construye el cuerpo de la respuesta de una herramienta.</summary>
    private static Dictionary<string, object> ConstruirRespuestaTool(ResultadoHerramienta resultado)
    {
        var cuerpo = new Dictionary<string, object>
        {
            ["exito"] = resultado.Exito,
        };

        if (resultado.Exito && resultado.Datos is not null)
        {
            cuerpo["datos"] = Normalizar(resultado.Datos);
        }
        else
        {
            cuerpo["error"] = resultado.Error ?? "No se pudo completar la consulta.";
        }

        if (resultado.RequiereAutenticacion)
        {
            cuerpo["requiere_autenticacion"] = true;
        }

        return cuerpo;
    }

    /// <summary>Convierte los datos de la herramienta en un objeto serializable.</summary>
    private static object Normalizar(object datos) => datos switch
    {
        JsonElement je => je,
        _ => JsonSerializer.SerializeToElement(datos, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
    };

    /// <summary>Normaliza los argumentos que llegan del modelo.</summary>
    private static object? ConvertirArgumento(object valor) => valor switch
    {
        null => null,
        JsonElement je => je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.TryGetInt32(out var n) ? n : je.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => je.ToString(),
        },
        _ => valor,
    };
}

/// <summary>Resultado de un turno de conversación con el asistente.</summary>
public sealed record ResultadoTurno(
    bool Exito,
    string? Mensaje,
    string? ErrorCodigo = null,
    string? ErrorMensaje = null)
{
    public static ResultadoTurno Ok(string mensaje) => new(true, mensaje);

    public static ResultadoTurno Fallido(string codigo, string mensaje) => new(false, null, codigo, mensaje);
}
