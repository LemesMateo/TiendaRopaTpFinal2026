# Asistente conversacional (Gemini) — Velvet Rose Boutique

Fecha: 2026-11-11. Estado: **incremento 4 implementado y verificado** (Web, Android y Windows)
+ **incremento 5 (solicitudes y notificaciones) implementado y verificado**.

Este documento describe el chatbot de atención al cliente: integración con Gemini,
Function Calling, herramientas de solo lectura, **solicitudes reales con confirmación**,
**notificaciones internas de la tienda**, notas públicas de pedido y la UI compartida.

---

## 0-bis. Incremento 5: solicitudes reales y notificaciones a la tienda

### Roles y permisos

Nuevo rol **Vendedor** con permisos **restringidos**: consulta y gestiona pedidos y
solicitudes, **pero no** el catálogo ni los usuarios (eso sigue siendo exclusivo
del Administrador).

| Permiso | Administrador | Vendedor | Cliente |
|---|:---:|:---:|:---:|
| `pedidos.ver` | ✔ | ✔ | — |
| `pedidos.gestionar` | ✔ | ✔ | — |
| `solicitudes.ver` | ✔ | ✔ | — |
| `solicitudes.responder` | ✔ | ✔ | — |
| `notificaciones.ver` | ✔ | ✔ | — |
| `catalogo.gestionar` | ✔ | — | — |
| `usuarios.gestionar` | ✔ | — | — |

- Definidos en `TiendaRopa.Api/Auth/Permisos.cs` (fuente de verdad) y reflejados en
  `TiendaRopa.Contracts/Identidad/UsuarioActualDto.cs` (para que la UI pueda
  mostrarlos sin depender del proyecto de la API).
- Aplicados con `PermisoHandler` + políticas por permiso
  (`[Authorize(Policy = Politicas.VerSolicitudes)]`).
- El JWT lleva el rol; `UsuarioActual.TienePermiso(permiso)` resuelve desde el rol.
- **URLs conservadas**: los endpoints de pedidos siguen en `/api/admin/pedidos`
  pero ahora los sirve `TiendaController` (personal de tienda) en vez de
  `AdminController` (sólo Administrador). El catálogo sigue en
  `/api/admin/productos`, exclusivo del Administrador.

### Modelo de datos (migración `20261112010921_AddSolicitudesYNotificaciones`)

| Tabla | Contenido |
|---|---|
| `SolicitudAtencion` | Id, IdCliente, IdUsuario, Tipo, IdPedido?, IdProducto?, IdProductoVariante?, Talle/Color (texto libre), Mensaje, TelefonoContacto, TelefonoEstado, Estado, fechas, Respuesta, IdUsuarioResuelve, FechaResolucion, ClaveIdempotencia, IdPropuesta |
| `SolicitudPropuesta` | Propuesta **pendiente** de confirmación (Id, IdUsuario, IdCliente, Tipo, referencias, Mensaje, teléfono, estado, IdSolicitudGenerada) |
| `Notificacion` | Id, IdUsuario (destinatario), IdSolicitud, Tipo, Mensaje, FechaCreacion, Leida, FechaLectura |

- Índices únicos filtrados: `ClaveIdempotencia` (una solicitud por clave) e
  `IdPropuesta` (una solicitud por propuesta).
- `Talle`/`Color` en texto libre permiten describir una combinación **inexistente**
  sin inventar un `IdProductoVariante`.

### Tipos de solicitud

`consulta_pedido` · `consulta_general` · `solicitud_reposicion`.

### Flujo de confirmación (Gemini prepara, el cliente envía)

```
Gemini → herramienta preparar_solicitud → crea PROPUESTA (no envía nada)
   ↓
ChatBot muestra la tarjeta: mensaje, tipo, talle/color, teléfono ENMASCARADO
   ↓
"Confirmar y enviar" → POST /api/asistente/propuestas/{id}/confirmar  (protegido)
   ↓
Backend revalida: pertenencia del pedido · existencia de producto/variante ·
                    contenido · destinatarios · teléfono de contacto
   ↓
Crea SolicitudAtencion + Notificacion para cada member del personal
```

- **Idempotencia**: la propuesta sólo genera una solicitud; la clave
  `ClaveIdempotencia` evita duplicados por doble clic o reintento.
- **Gemini nunca envía**: la herramienta `preparar_solicitud` sólo crea la
  propuesta; la escritura vive en un endpoint protegido que llama el botón.

### Teléfono

- El chat muestra **siempre** la versión enmascarada (`***01`).
- "Usar otro teléfono" abre un formulario con el nuevo número y un casillero
  **"Guardar este número en mi perfil"** (consentimiento expreso):
  - con el casillero → `PUT /propuestas/{id}/telefono` actualiza también el perfil;
  - sin el casillero → el número se usa **sólo** para esa solicitud.
- Si el cliente no tiene teléfono, confirmar la solicitud devuelve **400**
  pidiendo uno.
- El número completo **nunca** se envía a Gemini: la herramienta devuelve
  `telefono_registrado` ya enmascarado.

### Herramientas nuevas de Gemini

| Herramienta | Qué hace | Escribe |
|---|---|---|
| `preparar_solicitud` | Crea la **propuesta** pendiente (tipo, mensaje, pedido/producto/variante, talle/color). | Sólo la propuesta |
| `consultar_mis_solicitudes` | Lista las solicitudes del cliente con estado y respuesta. | No |

### Notificaciones a la tienda

- `Notificacion` persistida en SQL Server, vinculada a la solicitud.
- Se crea una por cada usuario activo con rol **Administrador** o **Vendedor**.
- Panel `/admin/solicitudes`: campana con contador de pendientes, filtro por
  estado, listado, detalle, marcar como leída, cambiar estado, responder y cerrar.
- El cliente ve el estado y la respuesta en `/mis-solicitudes`.
- Implementación con **actualización al consultar** (polling controlado desde la UI);
  SignalR queda como mejora opcional.

### Endpoints nuevos

| Ruta | Auth | Descripción |
|---|---|---|
| `POST /api/asistente/propuestas` | Cliente | Crea la propuesta (lo que hace Gemini). |
| `GET /api/asistente/propuestas` | Cliente | Propuestas pendientes del cliente. |
| `GET /api/asistente/propuestas/{id}` | Cliente | Detalle de una propuesta propia. |
| `PUT /api/asistente/propuestas/{id}/telefono` | Cliente | Fija (y opcionalmente guarda) el teléfono. |
| `POST /api/asistente/propuestas/{id}/confirmar` | Cliente | **Única escritura**: crea la solicitud. |
| `POST /api/asistente/propuestas/{id}/cancelar` | Cliente | Cancela la propuesta (no crea nada). |
| `GET /api/mis-solicitudes` | Cliente | Solicitudes del cliente con su respuesta. |
| `GET /api/solicitudes` | Personal | Listado para la tienda. |
| `GET /api/solicitudes/{id}` | Personal | Detalle (incluye teléfono y email del cliente). |
| `POST /api/solicitudes/{id}/responder` | Personal | Responde la solicitud. |
| `POST /api/solicitudes/{id}/estado` | Personal | Cambia el estado. |
| `GET /api/notificaciones` | Personal | Notificaciones + contador. |
| `GET /api/notificaciones/pendientes` | Personal | Sólo el contador. |
| `POST /api/notificaciones/{id}/leer` | Personal | Marca leída. |
| `POST /api/notificaciones/leer-todas` | Personal | Marca todas leídas. |

---

## 0. Corrección: thought signatures de Gemini 3 (2026-11-11)

### Causa exacta

En Gemini 3 la **thought signature** (una representación cifrada del razonamiento
interno) viaja **dentro del `Part`** que contiene el `functionCall`. La API la exige:
si una parte con `functionCall` llega sin firma, responde

```
HTTP 400 — Function call is missing a thought_signature in functionCall parts.
This is required for tools to work correctly.
```

El código original **reconstruía** el turno del modelo:

```csharp
// ANTES (roto): se creaban Partes nuevos, sin ThoughtSignature.
contents.Add(new Content
{
    Role = "model",
    Parts = functionCalls.Select(fc => new Part
    {
        FunctionCall = new FunctionCall { Id = fc.Id, Name = fc.Name, Args = ... },
    }).ToList(),
});
```

Al crear un `Part` nuevo se perdían (a) la `ThoughtSignature` y (b) cualquier otra
parte del turno original (texto de razonamiento, orden, partes de herramientas
integradas). El segundo request al modelo ya no era válido.

### La corrección

Se conserva el **`Content` original devuelto por Gemini**, sin reconstruirlo:

```csharp
// DESPUÉS (correcto): el Content del modelo entra tal cual vino.
var contenidoModelo = respuesta.Candidates?.FirstOrDefault()?.Content;
...
contents.Add(contenidoModelo.Role == "model"
    ? contenidoModelo
    : new Content { Role = "model", Parts = contenidoModelo.Parts });
```

Reglas de la documentación oficial que ahora se respetan:

| Regla | Cómo se cumple |
|---|---|
| "Always send the `thought_signature` back to the model inside its original `Part`" | se envía el `Content`/`Part` original, sin reconstruir |
| "Don't merge a `Part` containing a signature with one that does not" | no se fusionan ni se filtran partes |
| "Don't combine two `Parts` that both contain signatures" | cada `Part` viaja tal cual |
| "Always include the exact `id` from the `function_call` in your `function_response`" | cada `FunctionResponse` lleva el `Id` y el `Name` de su llamada |

Además:

- **Llamadas en paralelo**: todas las `FunctionResponse` de una ronda van en **un
  único `Content` de rol `user`** (con un `Part` por respuesta), que es la forma
  que usa el SDK oficial (`GoogleGenAIChatClient`).
- **Varias rondas**: el bucle sigue pidiendo herramientas hasta que el modelo
  responda con texto, respetando `MaxLlamadasPorTurno`.
- **Historial del cliente**: llega como texto (rol + texto). No se introduce
  ninguna parte de función ni firma; además se descarta un turno `model` inicial
  (la conversación siempre arranca con el usuario).
- **Sin inventar firmas**: no se genera ni se modifica ninguna
  `ThoughtSignature`; sólo se preserva la que envía Google.

### Archivos modificados

| Archivo | Cambio |
|---|---|
| `TiendaRopa.Api/Asistente/AsistenteGemini.cs` | se conserva el `Content` original del modelo; `FunctionResponse` en un único turno `user`; el historial de texto se sanea |
| `TiendaRopa.Api/Asistente/AsistenteServicio.cs` | `RequiereAutenticacion` refleja si *el asistente* pidió datos personales (no si el usuario no tiene sesión) |
| `TiendaRopa.Tests/Infra/GeminiFalsoServidor.cs` | **nuevo**: doble en C# que replica la validación real de firmas de Gemini 3 |
| `TiendaRopa.Tests/AsistenteGeminiFirmasTests.cs` | **nuevo**: 6 pruebas de regresión de firmas |
| `scripts/gemini-stub.mjs` | ahora valida firmas igual que Gemini 3 (400 si falta la firma) |
| `scripts/gemini-real-tests.ps1` | **nuevo**: conversaciones contra Gemini real |
| `scripts/chat-web-tests.mjs` | espera el texto de la respuesta (no sólo el indicador) → robustez con Gemini real |
| `scripts/asistente-tests.ps1`, `scripts/gemini-real-tests.ps1` | el body HTTP se manda en UTF-8 (PS 5.1 lo mandaba en Latin-1 y rompía el JSON con acentos → 400) |

### Pruebas de regresión (detectan la pérdida de firmas)

`TiendaRopa.Tests/AsistenteGeminiFirmasTests.cs` — 6 pruebas:

1. `UnaLlamada_LaFirmaSeConservaEnElRequestSiguiente`
2. `DosHerramientasSecuenciales_SeEjecutanEnOrdenYConservanSusFirmas`
3. `LlamadasEnParalelo_UnaSolaParteUserConTodasLasRespuestas`
4. `ElHistorialDelCliente_NoIntroducePartesDeFuncionIncompletas`
5. `HistorialQueArrancaConElAsistente_SeDescarta`
6. `SeRespetaElLimiteDeLlamadasPorTurno`

**Verificado que detectan el bug**: revirtiendo temporalmente el fix a la
implementación anterior, las pruebas **4 de 6 fallan** (las que comprueban la
firma). Con el fix, las 6 pasan.


---

## 1. Arquitectura

```
TiendaRopa.Contracts/Asistente/        DTOs compartidos (UI ↔ backend)
   ChatDto.cs            ChatSolicitudDto, ChatRespuestaDto, ChatMensajeDto,
                         ChatPedidoOpcionDto, ChatSugerencias
   AccionPropuestaDto.cs AccionPropuestaDto, AccionTipos,
                         SolicitudConsultaPedidoDto, SolicitudReposicionVarianteDto
   IChatService.cs       interfaz del cliente HTTP del chat

TiendaRopa.Api/Asistente/               TODO el Gemini vive acá
   AsistenteOpciones.cs  configuración (Asistente:Gemini:*)
   IGeneradorChat.cs     abstracción del modelo (permite pruebas sin Gemini)
   AsistenteGemini.cs    implementación real con Google.GenAI 2.1.0
   HerramientasAsistente.cs  las 5 herramientas de SOLO LECTURA
   AsistenteServicio.cs  orquestador (contexto, desambiguación, propuestas)

TiendaRopa.Api/Controllers/AsistenteController.cs   POST /api/asistente/mensaje

TiendaRopa.Shared/Components/ChatBot.razor(.css)   UI compartida (Web + MAUI)
TiendaRopa.Shared/Services/HttpChatService.cs       cliente HTTP del chat
TiendaRopa.Shared/Services/ChatContexto.cs          contexto de sesión
```

**La API key nunca sale de `TiendaRopa.Api`.** No está en `appsettings.json`, ni en
MAUI, ni en Web, ni en Shared. Vive en los secretos de usuario de la API.

---

## 2. Configuración

### 2.1 Modelos

`appsettings.json` (en `TiendaRopa.Api` y en `TiendaRopa.Web`, porque la Web hospeda
la API en el mismo proceso):

```json
"Asistente": {
  "Gemini": {
    "Modelo": "gemini-3.5-flash-lite",
    "MaxLlamadasPorTurno": 6,
    "MaxTokensSalida": 1024,
    "TimeoutSegundos": 45,
    "MaxTurnosHistorial": 10,
    "Temperatura": 0.4
  }
}
```

- Modelo por defecto: **`gemini-3.5-flash-lite`** (rápido y barato para el nivel gratuito).
- Para usar **`gemini-3.8-flash`** basta cambiar `"Modelo"`: no se toca la arquitectura.
- `MaxLlamadasPorTurno` acota el bucle de Function Calling (protege la cuota).
- `TimeoutSegundos` + `MaxTokensSalida` acotan tiempo y tokens por turno.
- `MaxTurnosHistorial` mantiene el contexto breve (10 turnos por defecto).

### 2.2 API key (secretos de usuario de TiendaRopa.Api)

```powershell
dotnet user-secrets set "Asistente:Gemini:ApiKey" "<TU_CLAVE>" --project TiendaRopa.Api
```

En producción: variable de entorno `Asistente__Gemini__ApiKey`.

Resolución (`ApiIntegration.ResolverOpcionesAsistente`):
1. `Asistente:Gemini:ApiKey` de la configuración del host (env var en producción).
2. Secretos de usuario de `TiendaRopa.Api` (`AddUserSecrets` sobre el assembly de la API).
3. Si no hay clave → el asistente responde `sin_configuracion` con un mensaje claro;
   la app **no** se rompe.

> `TiendaRopa.Api.csproj` declara `<UserSecretsId>velvetrose-api-3f7c1d92</UserSecretsId>`.
> El archivo de secretos vive en `%APPDATA%\Microsoft\UserSecrets\...`, fuera del repo.

---

## 3. Endpoints

| Ruta | Auth | Descripción |
|---|---|---|
| `POST /api/asistente/mensaje` | opcional | Un turno de conversación. `400` si el mensaje está vacío. `503` si el asistente no está configurado. |
| `GET /api/admin/pedidos/{id}/notas` | Administrador | Historial de actualizaciones públicas del pedido. |
| `POST /api/admin/pedidos/{id}/notas` | Administrador | Publica una actualización (texto, empresa opcional, código de seguimiento opcional). `201` / `404`. |

`POST /api/asistente/mensaje` acepta:

```jsonc
{
  "mensaje": "¿Dónde está mi pedido?",
  "historial": [ { "rol": "user", "texto": "Hola" } ],
  "idProductoContexto": 3,      // opcional: el producto que se está mirando
  "idPedidoContexto": 12        // opcional: el pedido que se viene consultando
}
```

Devuelve:

```jsonc
{
  "mensaje": "Tu pedido #12 está en Preparado...",
  "sugerencias": ["¿Y el estado del envío?"],
  "opcionesPedido": [ { "id": 12, "etiqueta": "Pedido #12 · Vestido Satinado Noche", "fecha": "11/11/2026", "estado": "Preparado" } ],
  "acciones": [],
  "requiereAutenticacion": false,
  "errorCodigo": null,
  "errorMensaje": null
}
```

---

## 4. Herramientas del asistente (Function Calling)

Declaradas con `FunctionDeclaration` de `Google.GenAI` y **ejecutadas en C#** contra
EF Core. La IA **nunca** ejecuta SQL arbitrario y **nunca** decide a quién se consulta.

| Herramienta | Argumentos | Qué devuelve |
|---|---|---|
| `listar_mis_pedidos` | — | Pedidos del cliente autenticado: estado, total, productos y **última actualización** (o `null` si no hay). |
| `consultar_pedido` | `id_pedido` (int) | Detalle de **un pedido propio**: estado, líneas, total e historial de notas. |
| `consultar_mis_datos` | — | Perfil del cliente: nombre, apellido, email, **teléfono enmascarado** y **dirección resumida**. |
| `buscar_producto` | `texto` (string), `limite` (int) | Productos activos: id, nombre, categoría, precio, stock total, disponibilidad. |
| `consultar_variantes` | `id_producto` (int), `talle?`, `color?` | Variantes con stock y precio + `estado`: `con_stock`, `sin_stock` o `combinacion_inexistente`. |

### Reglas duras (aplicadas en `HerramientasAsistente`)

1. **Identidad desde el JWT.** `IUsuarioActual` (claims del token). El modelo nunca
   elige a quién se consulta: no existe ningún parámetro de cliente.
2. **Aislamiento entre clientes.** `consultar_pedido` filtra por `IdCliente`; si el
   pedido es de otro, la respuesta es *"No encontré ningún pedido tuyo con el
   número N"* — no se confirma la existencia del pedido ajeno.
3. **Datos mínimos y enmascarados.** `consultar_mis_datos` nunca devuelve el
   teléfono completo (`***01`) ni la dirección completa (sólo tipo de vía y
   localidad). El modelo nunca ve esos datos.
4. **Contexto validado.** `IdProductoContexto` y `IdPedidoContexto` se validan
   contra la base antes de usarse: si no existen o no le pertenecen, se ignoran.
5. **Allowlist dura.** `HerramientasAsistente.Permitidas` es el único conjunto de
   nombres ejecutables; cualquier otro nombre se rechaza con log de advertencia.
6. **Sin escrituras.** Ninguna herramienta escribe en la base.

### Bucle de Function Calling

`AsistenteGemini.ProcesarTurnoAsync`:

```
contenidos = [historial recortado, mensaje actual (+ contexto validado)]
loop (máx. MaxLlamadasPorTurno):
    respuesta = Gemini.Models.GenerateContentAsync(modelo, contenidos, config)
    si no hay functionCall  -> devolver el texto final
    por cada functionCall:
        ejecutar la herramienta en C# (allowlist + args tipados)
        append Role="model" con la functionCall
        append Role="user"  con la FunctionResponse (resultado JSON)
```

Errores traducidos a mensajes para el cliente:

| Situación | `errorCodigo` | Mensaje |
|---|---|---|
| Sin API key | `sin_configuracion` | "El asistente todavía no está configurado." |
| HTTP 429 (cuota/rate limit) | `cuota_agotada` | "Se alcanzó el límite de uso del asistente por ahora." |
| HTTP 400/401/403 | `configuracion_invalida` | "El asistente no está disponible en este momento." |
| Timeout | `timeout` | "La consulta tardó demasiado." |
| Sin conexión | `sin_conexion` | "No pudimos conectar con el asistente." |

Reintentos: el SDK reintenta 3 veces con backoff exponencial ante 429 y 5xx
(`HttpRetryOptions`).

---

## 5. Pedidos y seguimiento

### 5.1 Nota de estado pública (nuevo)

Nueva entidad **`PedidoNota`** (historial de actualizaciones por pedido):

| Columna | Tipo | Nota |
|---|---|---|
| `IdPedidoNota` | int (PK) | |
| `IdPedido` | int (FK → `Pedido`, Cascade) | |
| `FechaActualizacion` | datetime2 (UTC) | Cuándo se publicó |
| `Nota` | nvarchar(500) | Texto público que ve el cliente |
| `Empresa` | nvarchar(100)? | Empresa de transporte / courier (opcional) |
| `CodigoSeguimiento` | nvarchar(60)? | Código de seguimiento (opcional) |

Índices: `IX_PedidoNota_IdPedido` y `IX_PedidoNota_IdPedido_FechaActualizacion`.

**Migración**: `20261111224424_AddPedidoNota` (aplicada y verificada en `VelvetRoseDev`).

`PedidoDto` incorpora `Notas` (historial, más reciente primero). El cliente lo ve
en **Mis pedidos**; el chatbot lo usa para responder consultas de envío.

**Cancelar un pedido NO borra su historial de notas** (el historial se conserva).

### 5.2 Flujo de "novedades" del chatbot

1. `listar_mis_pedidos` devuelve los pedidos **activos** (no `Entregado` ni `Cancelado`).
2. Si hay **uno**, el modelo consulta su última actualización con `consultar_pedido`.
3. Si hay **varios**, la API los devuelve en `opcionesPedido` (id + descripción breve
   de los productos) y el modelo pregunta a cuál se refiere. La UI los muestra como
   **botones seleccionables** (no hay que escribir el número).
4. Si **no hay información reciente** (`ultima_actualizacion: null`), el modelo lo
   reconoce expresamente: las instrucciones de sistema prohíben inventar
   actualizaciones, fechas de entrega o códigos de seguimiento.
5. Si el cliente **no está autenticado**, la herramienta devuelve
   `requiere_autenticacion: true` y la UI muestra un botón **Iniciar sesión**.

---

## 6. Variantes y contexto

- El chatbot conoce el `IdProducto` de la página de detalle desde donde se abrió
  (`ChatContexto.IdProducto`, lo setea `DetalleProducto.razor`). El backend **lo
  valida** antes de usarlo.
- `consultar_variantes` distingue tres estados:
  - `con_stock` — la combinación existe y hay unidades.
  - `sin_stock` — la combinación existe pero `Stock = 0`.
  - `combinacion_inexistente` — la combinación talle/color no existe (o el producto
    no existe → error).
- Las instrucciones de sistema prohíben prometer reposición o fechas de reposición.

---

## 7. Preparación de acciones (segundo incremento)

En este incremento el chatbot **propone** acciones y muestra una **vista previa**;
**no se envía ni se crea ninguna solicitud**.

Contratos ya diseñados (`TiendaRopa.Contracts/Asistente/AccionPropuestaDto.cs`):

- `AccionTipos.ConsultaPedido` → `SolicitudConsultaPedidoDto { IdPedido, Motivo, FechaCreacion }`
- `AccionTipos.ReposicionVariante` → `SolicitudReposicionVarianteDto { IdProducto, IdVariante, Talle, Color, Email, Motivo, FechaCreacion }`

La UI muestra una tarjeta con el resumen y botones **Confirmar** / **Cancelar**.
En este incremento el botón Confirmar sólo cambia el estado local de la tarjeta
(`propuesta` → `confirmada`) y la tarjeta aclara *"Vista previa: todavía no se envía
ninguna solicitud"*: **no simula un envío real**.

---

## 8. Componente `ChatBot` (UI compartida)

`TiendaRopa.Shared/Components/ChatBot.razor` + `.razor.css`, integrado en
`MainLayout.razor`: disponible desde **Inicio, Catálogo, Detalle, Carrito y Mis Pedidos**
en Web y MAUI.

- **Botón flotante** abajo a la derecha (`support_agent`), por encima de la barra de
  navegación móvil y respetando `--vr-safe-*`.
- **Panel**: lateral de 400 px en escritorio (≥1024 px) y casi a pantalla completa
  en móvil, apoyado **sobre** la barra de navegación inferior (queda accesible).
- **Mensajes** diferenciados: cliente (burbuja frambuesa, a la derecha) y asistente
  (burbuja blanca, a la izquierda).
- **Indicador de escritura** (tres puntos animados) y **desactivación del envío** mientras
  espera respuesta.
- **Sugerencias iniciales** clicables: "¿Dónde está mi pedido?", "Consultar stock",
  "Contactar a la tienda" (`ChatSugerencias.Iniciales`, en el contrato para que la
  UI y el backend muestren las mismas).
- **Opciones de pedido** como botones seleccionables.
- **Tarjetas de acción propuesta** con Confirmar / Cancelar.
- **Cerrar y reabrir sin perder la conversación** (el estado vive en el componente,
  que no se destruye al navegar).
- **Estados**: carga, vacío, error, respuesta vacía y "requiere autenticación" (con
  botón para iniciar sesión).
- **Accesibilidad**: `role="listitem"` en los mensajes, `aria-live`, `aria-expanded`
  en el botón, `aria-controls`, nombres accesibles en los botones de sólo icono,
  foco visible y objetivos táctiles de 44 px.
- **Teclado virtual**: el WebView se reencoge (Android) y el panel se adapta.
- **Scroll automático** al último mensaje (`wwwroot/js/velvet-chat.js`).
- Respeta `prefers-reduced-motion` (sin animaciones).

Tokens usados: `--vr-bg` (marfil), `--vr-primary` (frambuesa), `--vr-plum` / `--vr-plum-strong`
(ciruela), `--vr-lavender`, `--vr-surface`, tipografías Playfair Display / Inter y
Material Symbols locales.

---

## 9. Pruebas

### 9.1 Unitarias — `TiendaRopa.Tests` (xUnit) → **53 PASS / 0 FAIL**

Base en memoria (EF Core InMemory), sin tocar SQL Server.

| Archivo | Cubre |
|---|---|
| `HerramientasAsistenteTests.cs` | un pedido activo · varios pedidos activos · ningún pedido activo · nota pública ausente · código de seguimiento disponible · acceso al pedido de otro cliente · variante con stock · variante agotada · combinación inexistente · producto inexistente · falta de autenticación · enmascaramiento de teléfono y dirección · los pedidos ajenos no se listan |
| `AsistenteServicioTests.cs` | un/varios/ningún pedido activo · pedidos ajenos ocultos · contexto de producto válido e inválido · contexto de pedido ajeno ignorado · **cuota agotada** · **timeout** · sin conexión · sin configuración · mensaje vacío · CTA de sesión sólo cuando el asistente pidió datos personales |
| `AsistenteGeminiFirmasTests.cs` | **thought signatures**: firma conservada en el request siguiente · dos herramientas secuenciales · llamadas en paralelo (un único turno `user`) · historial sin partes de función incompletas · historial que arranca con el asistente · límite de llamadas por turno |
| `SolicitudServiceTests.cs` | propuesta de consulta de pedido · producto inexistente rechazado · variante inexistente descrita en texto (sin inventar IdProductoVariante) · sin sesión rechazada · teléfono ausente → `pendiente_confirmacion` · teléfono confirmado en la propuesta · **sin guardar en el perfil** el perfil no cambia · **con consentimiento** el perfil sí se actualiza · teléfono inválido rechazado · propuesta de otro usuario intocable · creación de la solicitud + notificaciones · teléfono del perfil usado al confirmar · **sin teléfono** → 400 · **pedido ajeno** → 403 · **confirmar dos veces no duplica** · **clave de idempotencia no duplica** · propuesta de otro usuario no confirmable · cancelar no crea solicitud · **aislamiento**: sólo se ven las solicitudes propias |

```powershell
dotnet test TiendaRopa.Tests\TiendaRopa.Tests.csproj
```

### 9.2 API — `scripts/asistente-tests.ps1` → **26 PASS / 0 FAIL**

Contra la API real (:5100) y SQL Server real.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\asistente-tests.ps1
```

Cubre: registro/login de dos clientes · creación de pedidos reales · notas públicas
(alta, historial, código de seguimiento, visibilidad para el cliente) · **el cliente B
no puede ver el pedido del cliente A (404)** · endpoint del chat (validación, sin
sesión, con sesión, contexto de producto válido e inválido, contexto de pedido ajeno,
historial, mensaje demasiado largo) · aislamiento verificado **contra SQL Server**
directamente · contraseñas hasheadas · limpieza.

### 9.2 bis API de solicitudes — `scripts/solicitudes-tests.ps1` → **39 PASS / 0 FAIL**

```powershell
powershell -ExecutionPolicy Bypass -File scripts\solicitudes-tests.ps1
```

Cubre: **permisos del Vendedor** (puede listar pedidos 200 · catálogo 403 · solicitudes
200 · el cliente no puede listar solicitudes de la tienda 403 · notificaciones 403) ·
creación de propuesta · **teléfono enmascarado** en la respuesta · **el teléfono
completo no viaja al cliente** · confirmar el teléfono · **el perfil no se modifica sin
consentimiento** · confirmar la propuesta → 201 con estado `Abierta` · **confirmar dos
veces no duplica** (mismo id) · **una sola fila en `SolicitudAtencion`** en SQL Server ·
notificaciones recibidas por el Vendedor y el Administrador · el vendedor ve el detalle
con el cliente · **el vendedor responde** y el cliente ve la respuesta · **el cliente
ve la respuesta** · **aislamiento**: el otro cliente no ve la solicitud · el vendedor
cierra la solicitud · **teléfono ausente** → 400 pidiendo número · **teléfono
modificado** con consentimiento actualiza el perfil · **aislamiento de propuestas**:
otro cliente no puede ver, confirmar ni cancelar (404) · reposición de variante
agotada → 201 · **combinación inexistente** → 201 con talle/color en texto y **sin
inventar `IdProductoVariante`** · cancelar propuesta → no crea solicitud · limpieza.

### 9.3 Web (Playwright) — `scripts/chat-web-tests.mjs` → **61 PASS / 0 FAIL**

Ejecutado **dos veces**: contra el doble estricto (determinista) y contra
**Gemini real**.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\start-test-hosts.ps1   # API + Web + doble de Gemini
node scripts/chat-web-tests.mjs
```

En **390 / 768 / 1440 px**: botón flotante visible · panel que abre · sugerencias
iniciales · campo y botón · envío deshabilitado sin texto · mensaje del cliente ·
respuesta del asistente · indicador de escritura · cierre · **reapertura conserva la
conversación** · chat disponible en Catálogo / Carrito / Mis Pedidos · chat desde el
detalle de producto · **sin scroll horizontal** · sin errores de consola · sin recursos
404 · sesión iniciada con pedido real · panel de administración (publicar y consultar
actualizaciones, con código de seguimiento) · el cliente ve la nota en su pedido.

### 9.3 ter Web de solicitudes — `scripts/solicitudes-web-tests.mjs` → **23 PASS / 0 FAIL**

```powershell
node scripts/solicitudes-web-tests.mjs
```

Cubre (390 px para el cliente, 1440 px para la tienda): login del cliente · **la
tarjeta de la propuesta aparece en el chat** con el mensaje del cliente · la tarjeta
dice **"Esto todavía NO se envió"** · la tarjeta ofrece **Confirmar y Cancelar** · la
tarjeta muestra el **teléfono enmascarado** (`***01`) · **cancelar no crea solicitud**
· confirmar → **la solicitud queda `Abierta`** con el teléfono confirmado · confirmar
dos veces con la misma clave **no duplica** · **"Mis solicitudes"** lista la solicitud
· sin errores de consola ni recursos 404 · login del **vendedor** · el vendedor ve
el **listado de solicitudes** · la **campana muestra el contador de pendientes** · el
detalle muestra el **teléfono y el email del cliente** · **el vendedor responde y el
cliente recibe la respuesta** · el vendedor **cierra** la solicitud · **el vendedor no
accede al catálogo** (pantalla de sección restringida).

### 9.3 bis Gemini real — `scripts/gemini-real-tests.ps1` → **14 PASS / 0 FAIL**

Usa la API key de los secretos de usuario y **sólo datos ficticios**
(cliente `gemini.real.<marca>@velvetrose.dev`, pedido y nota de prueba).

```powershell
powershell -ExecutionPolicy Bypass -File scripts\gemini-real-tests.ps1
```

| Conversación | Resultado real |
|---|---|
| "¿Este vestido viene en negro talle L?" | 200 · *"Sí, el Abrigo Corte Redondo en negro y talle L está disponible y hay 5 unidades en stock."* |
| "¿Qué variantes hay disponibles?" | 200 · lista talles/colores con precio |
| "¿Tengo pedidos pendientes?" | 200 · *"Sí, tenés un pedido pendiente (nro. 1058) de un Abrigo Corte Redondo en talle M y color Marfil, por un total de $89.990. Fue despachado por Velvet Express el 12/11/2026 y está en camino."* |
| "¿Cuál es la última actualización de mi pedido?" | 200 · cita la nota real y el **código de seguimiento VR-FICTICIO-001** |
| Consulta con **dos herramientas seguidas** | 200 · respuesta final coherente (buscó el producto del pedido y consultó sus variantes) |
| Turno con **historial previo** | 200 · mantiene el contexto |
| Sin sesión | 200 · pide iniciar sesión |

**0 errores de Gemini** en el log durante toda la corrida.

### 9.4 Android (emulador + ADB) — verificado

- Emulador `MAUI_Emulator_API_36` (API 36): `BOOT_OK`, `nc 10.0.2.2 5100` → `PORT_OPEN`.
- App instalada con `dotnet build -f net10.0-android -t:Install` y lanzada con `monkey`.
- Script reproducible: `scripts/android-solicitudes.ps1` (escribe con **keycodes de
  Android** porque el teclado del emulador —Gboard— mangla el texto de
  `adb shell input text`).

**Flujo de cliente (verificado con requests reales en el log de la API):**

| Paso | Evidencia |
|---|---|
| Login en la app | `POST http://10.0.2.2:5100/api/auth/login → 200` |
| Chat abierto | `GET /api/asistente/propuestas → 200` |
| Tarjeta de la propuesta | captura con el mensaje, **teléfono enmascarado `***01`**, botones **Confirmar y enviar** / **Cancelar** y el enlace **Usar otro teléfono** |
| Formulario de teléfono alternativo | captura con el campo, el casillero *"Guardar este número en mi perfil"* y los botones **Guardar teléfono** / **Cancelar** |
| **Confirmar y enviar** | `POST http://10.0.2.2:5100/api/asistente/propuestas/52/confirmar → 201` |
| En SQL Server | `SolicitudAtencion` Id 25 · Estado `Abierta` · `TelefonoContacto = 341-555-0101` · `TelefonoEstado = confirmado` |
| Notificaciones | 2 filas: `admin@velvetrose.dev` y `vendedor@velvetrose.dev` (`solicitud_nueva`, sin leer) |
| En pantalla | *"¡Solicitud enviada! La tienda te va a responder."* |

**Flujo de la tienda (Vendedor) en Android:**

- Login del vendedor (`ven@velvetrose.dev`, rol `Vendedor`) → 200.
- Menú lateral: aparecen **"Solicitudes"** y **"Pedidos"**, **sin** "Administración"
  (permisos restringidos verificados también en la API).
- `/admin/solicitudes`: `GET /api/solicitudes → 200` y
  `GET /api/notificaciones?soloPendientes=true → 200`.
- Captura con la estética Velvet Rose: campana, filtro de estado, tarjetas con
  badges **Consulta / Abierta / Cerrada**, nombre del cliente y fecha.

**logcat del proceso de la app: 0 excepciones.**

> ⚠️ Limitación del entorno: los **keycodes de dígitos no funcionan** en este
> emulador (`KEYCODE_3` se pierde), por eso el usuario de prueba no lleva
> dígitos en el email ni en la contraseña. El desfase vertical entre la captura
> (900×2400) y el toque es de **+273 px** en los elementos del WebView; el
> script lo tiene calibrado.

### 9.5 Windows (MAUI WinUI) — verificado

- `net10.0-windows10.0.19041.0` compila y **se ejecuta** (`TiendaRopa.exe`).
- El FAB aparece abajo a la derecha; al tocarlo se abre el **panel lateral** con el
  estado vacío, las sugerencias y el campo de escritura.
- Captura en `test-evidence/windows/`.

### 9.5 Windows (MAUI WinUI) — verificado

- `net10.0-windows10.0.19041.0` compila y **se ejecuta** (`TiendaRopa.exe`).
- El FAB aparece abajo a la derecha; al tocarlo se abre el **panel lateral**
  con el estado vacío, las sugerencias y el campo de escritura.
- Captura en `test-evidence/windows/`.

---

## 10. Doble de prueba de Gemini

`scripts/gemini-stub.mjs` implementa el contrato HTTP que consume `Google.GenAI`
(`POST {baseUrl}/v1beta/models/{model}:generateContent`) y devuelve `functionCall`
o texto.

**No es un mock del backend**: el backend real (EF Core, SQL Server, herramientas,
bucle de Function Calling) sigue siendo el que ejecuta todo. Sólo se reemplaza la
llamada al modelo, para que las pruebas sean deterministas y no consuman cuota.

Se activa con la variable de entorno `Asistente__Gemini__BaseUrl` (además sirve
para un proxy corporativo):

```powershell
$env:Asistente__Gemini__BaseUrl = "http://127.0.0.1:5199"
```

Sin esa variable, el asistente llama a `generativelanguage.googleapis.com`.

> **Estado de la verificación con Gemini real:** la API key quedó configurada en los
> secretos de usuario de `TiendaRopa.Api` y **todo el recorrido se verificó contra
> `generativelanguage.googleapis.com`** (sección 9.3 bis): 14/14 conversaciones,
> 61/61 pruebas de la UI web y el chat funcionando en Blazor Web y en MAUI Android
> con respuestas que citan los datos reales del catálogo y de los pedidos.
>
> Para volver a correr la verificación: `dotnet user-secrets set "Asistente:Gemini:ApiKey" "<clave>" --project TiendaRopa.Api`.

---

## 11. Archivos del incremento

### Backend (`TiendaRopa.Api`)
| Archivo | Cambio |
|---|---|
| `Asistente/AsistenteOpciones.cs` | nuevo — configuración |
| `Asistente/IGeneradorChat.cs` | nuevo — abstracción del modelo |
| `Asistente/AsistenteGemini.cs` | nuevo — Gemini + Function Calling |
| `Asistente/HerramientasAsistente.cs` | nuevo — las 5 herramientas de lectura + `preparar_solicitud` + `consultar_mis_solicitudes` |
| `Asistente/AsistenteServicio.cs` | nuevo — orquestador |
| `Auth/Permisos.cs` | **nuevo** — permisos por rol + `Politicas` |
| `Auth/Roles.cs` | nuevo rol `Vendedor` |
| `Auth/UsuarioActual.cs` | `EsPersonalTienda`, `TienePermiso`, handlers de permisos |
| `Entities/Solicitudes.cs` | **nuevo** — `SolicitudAtencion`, `SolicitudPropuesta`, `Notificacion` |
| `Data/Migrations/20261112010921_AddSolicitudesYNotificaciones.cs` | **nuevo** — migración |
| `Services/SolicitudService.cs` | **nuevo** — propuestas, teléfono, confirmación idempotente |
| `Services/NotificacionService.cs` | **nuevo** — notificaciones, responder, cambiar estado |
| `Controllers/SolicitudesController.cs` | **nuevo** — `/api/solicitudes`, `/api/mis-solicitudes` |
| `Controllers/NotificacionesController.cs` | **nuevo** — `/api/notificaciones` |
| `Controllers/TiendaController.cs` | **nuevo** — pedidos y notas con permisos del personal de tienda |
| `Controllers/AdminController.cs` | se le quitaron los endpoints de pedidos (pasan a `TiendaController`) |
| `Controllers/AsistenteController.cs` | nuevo — chat + propuestas (crear/listar/teléfono/confirmar/cancelar) |
| `Entities/Pedidos.cs` | `PedidoNota` |
| `Data/VelvetRoseDbContext.cs` | `DbSet<PedidoNota>` + mapeo |
| `Data/Migrations/20261111224424_AddPedidoNota.cs` | nuevo — migración |
| `Services/PedidoService.cs` | `PedidoDto.Notas` |
| `Services/AdminService.cs` | alta/lectura de notas |
| `Controllers/AdminController.cs` | endpoints de notas |
| `ApiIntegration.cs` | registro del asistente + resolución de la clave |
| `TiendaRopa.Api.csproj` | `Google.GenAI` 2.1.0, `UserSecretsId`, Pkcs parcheado |
| `appsettings.json` | sección `Asistente:Gemini` (sin clave) |

### Contratos (`TiendaRopa.Contracts`)
`Asistente/ChatDto.cs`, `Asistente/AccionPropuestaDto.cs` (con `IdPropuesta`,
`TelefonoEnmascarado`, `RequiereConfirmarTelefono`), `Asistente/IChatService.cs`,
`Asistente/ISolicitudService.cs` (**nuevo**), `Solicitudes/SolicitudDtos.cs` (**nuevo**),
`Solicitudes/NotificacionDtos.cs` (**nuevo**), `Pedidos/PedidoNotaDto.cs`,
`Admin/PedidoNotaAdminDto.cs`, `Admin/IAdminService.cs` (métodos de notas),
`Pedidos/PedidoDto.cs` (`Notas`), `Identidad/UsuarioActualDto.cs` (permisos + rol Vendedor).

### UI compartida (`TiendaRopa.Shared`)
`Components/ChatBot.razor` + `.css` (tarjeta de propuesta, Confirmar/Cancelar,
formulario de teléfono), `Services/HttpChatService.cs` (propuestas),
`Services/HttpSolicitudService.cs` (**nuevo**), `Services/ChatContexto.cs`,
`Services/SesionService.cs` (`EsPersonalTienda`, `TienePermiso`),
`wwwroot/js/velvet-chat.js`, `Layout/MainLayout.razor` (integra el chat + enlaces),
`Pages/DetalleProducto.razor` (contexto de producto),
`Pages/MisPedidos.razor` + `.css` (notas públicas),
`Pages/MisSolicitudes.razor` (**nuevo** — vista del cliente),
`Pages/Admin/AdminSolicitudes.razor` (**nuevo** — panel de la tienda),
`Pages/Admin/AdminPedidos.razor` (panel de actualizaciones),
`Pages/Admin/AdminIndex.razor` (tarjeta "Solicitudes"),
`wwwroot/css/velvet-solicitudes.css` (**nuevo**), `wwwroot/css/velvet-admin.css`.

### Hosts
`TiendaRopa.Web/Program.cs` y `TiendaRopa/MauiProgram.cs` (registro de `IChatService`,
`ISolicitudService`, `INotificacionService` y `ChatContexto`),
`TiendaRopa.Web/Components/App.razor` y `TiendaRopa/wwwroot/index.html`
(css del chat/solicitudes + script del chat).

### Pruebas
`TiendaRopa.Tests/` (proyecto nuevo; `Infra/ContextoPrueba.cs`,
`Infra/GeminiFalsoServidor.cs`, `Infra/GeneradorChatFalso.cs`,
`HerramientasAsistenteTests.cs`, `AsistenteServicioTests.cs`,
`AsistenteGeminiFirmasTests.cs`, `SolicitudServiceTests.cs`),
`scripts/asistente-tests.ps1`, `scripts/solicitudes-tests.ps1`,
`scripts/chat-web-tests.mjs`, `scripts/solicitudes-web-tests.mjs`,
`scripts/android-solicitudes.ps1`, `scripts/gemini-stub.mjs`,
`scripts/gemini-real-tests.ps1`, `scripts/start-test-hosts.ps1`.

---

## 12. Pendiente (tercer incremento)

1. **Notificaciones al cliente**: hoy el cliente ve su respuesta al abrir
   "Mis solicitudes"; falta un aviso proactivo (campana para el cliente o
   notificación push/web).
2. **Seguimiento automático**: calcular el estado del envío a partir de la empresa y
   el código de seguimiento (hoy la nota la carga el administrador a mano).
3. **Escalado de la solicitud**: permitir reasignar una solicitud a un vendedor
   concreto y que sólo ese la vea.
4. **Recuperación del historial**: hoy la conversación vive en la sesión; si se
   quiere persistir, definir la retención y el consentimiento.
5. **SignalR**: actualizar el contador de la campana en vivo en vez de al consultar.
6. **Cuota**: monitorear el uso del nivel gratuito y avisar antes de agotarlo.
7. **Correo / WhatsApp**: no implementado (la primera versión usa notificaciones
   internas, según el enunciado).
