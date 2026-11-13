// ============================================================================
// Doble de prueba de la API de Gemini (SOLO para pruebas automatizadas).
//
// Implementa el contrato HTTP que usa Google.GenAI:
//   POST {baseUrl}/v1beta/models/{model}:generateContent
//
// NO es un mock del backend: el backend real (EF Core, SQL Server, herramientas)
// sigue siendo el que ejecuta las consultas. Sólo se reemplaza la llamada
// al modelo para que la prueba sea determinista y no consuma cuota.
//
// REPLICA LA VALIDACIÓN REAL DE GEMINI 3 sobre las thought signatures:
//   - toda parte con functionCall DEBE traer thoughtSignature;
//     si falta, responde 400 con el mensaje exacto de Google.
//   - toda functionResponse debe referenciar el id de una llamada previa.
// Así las suites detectan la pérdida de firmas al reconstruir el historial.
//
// Uso:  node scripts/gemini-stub.mjs [puerto]
// ============================================================================
import { createServer } from 'node:http';

const PORT = Number(process.argv[2] || 5199);

// Firma de ejemplo (la misma que usa el SDK oficial en sus pruebas).
const FIRMA = 'c2tpcF90aG91Z2h0X3NpZ25hdHVyZV92YWxpZGF0b3I=';

let contadorTurnos = 0;

/** Valida el request como lo hace Gemini 3. Devuelve el error o null. */
function validar(peticion) {
  const contents = peticion.contents || [];
  if (contents.length === 0) return 'contents vacío.';

  const idsConocidos = new Set();

  for (const contenido of contents) {
    for (const parte of contenido.parts || []) {
      const tieneFirma = typeof parte.thoughtSignature === 'string'
        && parte.thoughtSignature.length > 0;

      if (parte.functionCall) {
        if (!tieneFirma) {
          return 'Function call is missing a thought_signature in functionCall parts. '
            + 'This is required for tools to work correctly.';
        }
        if (parte.functionCall.id) idsConocidos.add(parte.functionCall.id);
      }

      if (parte.functionResponse) {
        const id = parte.functionResponse.id;
        if (id && !idsConocidos.has(id)) {
          return `Function response with id '${id}' does not match any function call.`;
        }
      }
    }
  }

  return null;
}

/** Parte de functionCall con su thought signature (como la devuelve Gemini 3). */
function parteLlamada(id, name, args = {}) {
  return { functionCall: { id, name, args }, thoughtSignature: FIRMA };
}

const server = createServer((req, res) => {
  let cuerpo = '';
  req.on('data', (c) => { cuerpo += c; });
  req.on('end', () => {
    if (!req.url.includes(':generateContent')) {
      res.writeHead(404, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ error: { code: 404, message: 'ruta no soportada' } }));
      return;
    }

    let peticion = {};
    try { peticion = JSON.parse(cuerpo || '{}'); } catch { /* cuerpo vacío */ }

    // Validación estilo Gemini 3: sin thought signature => 400.
    const error = validar(peticion);
    if (error) {
      console.log(`[gemini-stub] 400 (validación): ${error}`);
      res.writeHead(400, { 'content-type': 'application/json' });
      res.end(JSON.stringify({
        error: { code: 400, message: error, status: 'INVALID_ARGUMENT' },
      }));
      return;
    }

    const textoUsuario = (peticion.contents || [])
      .filter(c => c.role === 'user')
      .flatMap(c => c.parts || [])
      .map(p => p.text || '')
      .join(' ')
      .toLowerCase();

    // Si ya hay una FunctionResponse en el historial, el modelo responde en texto.
    const yaRespondio = (peticion.contents || [])
      .some(c => (c.parts || []).some(p => p.functionResponse));

    let respuesta;

    if (!yaRespondio && /pedido|seguimiento|donde esta|novedad/.test(textoUsuario)) {
      // Primera vuelta: el modelo pide listar los pedidos del cliente.
      respuesta = {
        candidates: [{
          content: {
            role: 'model',
            parts: [parteLlamada('llamada-1', 'listar_mis_pedidos')],
          },
          finishReason: 'STOP',
        }],
      };
    } else if (!yaRespondio && /stock|talle|variante|disponib/.test(textoUsuario)) {
      // Primera vuelta: el modelo pide las variantes del producto.
      respuesta = {
        candidates: [{
          content: {
            role: 'model',
            parts: [parteLlamada('llamada-2', 'consultar_variantes', { id_producto: 1 })],
          },
          finishReason: 'STOP',
        }],
      };
    } else {
      // Respuesta final en texto (con el dato que devolvió la herramienta).
      const ultima = [...(peticion.contents || [])]
        .reverse()
        .flatMap(c => c.parts || [])
        .find(p => p.functionResponse);

      const texto = ultima
        ? 'Ya lo tengo: ' + JSON.stringify(ultima.functionResponse.response).slice(0, 220)
        : '¡Hola! Soy el asistente de Velvet Rose. ¿En qué te ayudo?';

      respuesta = {
        candidates: [{
          content: { role: 'model', parts: [{ text: texto }] },
          finishReason: 'STOP',
        }],
        usageMetadata: {
          promptTokenCount: 100,
          candidatesTokenCount: 40,
          totalTokenCount: 140,
        },
      };
    }

    contadorTurnos++;
    console.log(`[gemini-stub] turno ${contadorTurnos} -> ${JSON.stringify(respuesta.candidates[0].content.parts[0]).slice(0, 160)}`);

    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify(respuesta));
  });
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`[gemini-stub] escuchando en http://127.0.0.1:${PORT}`);
});
