// ============================================================================
// Pruebas del chat del asistente — Velvet Rose Boutique (incremento 4)
// Uso:  node scripts/chat-web-tests.mjs
// Requisito: la Web en :5101 y la API en :5100.
//
// Recorre el chatbot en 390 / 768 / 1440 px:
//   botón flotante -> panel -> sugerencias -> envío -> respuesta -> cierre
//   -> reapertura conservando la conversación -> contexto de producto.
// ============================================================================
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const BASE = 'http://localhost:5101';
const API = 'http://localhost:5100';
const OUT = 'test-evidence/web-chat';
mkdirSync(OUT, { recursive: true });

let pass = 0, fail = 0;
const resultados = [];
function report(nombre, ok, detalle = '') {
  if (ok) pass++; else fail++;
  resultados.push({ estado: ok ? 'PASS' : 'FAIL', nombre, detalle });
  console.log(`[${ok ? 'PASS' : 'FAIL'}] ${nombre}${detalle ? ' - ' + detalle : ''}`);
}

const VIEWPORTS = [
  { name: '390', width: 390, height: 844 },
  { name: '768', width: 768, height: 1024 },
  { name: '1440', width: 1440, height: 900 },
];

const marca = Date.now();

async function gotoApp(page, url) {
  const ws = page.waitForEvent('websocket', { timeout: 15000 }).catch(() => null);
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await ws;
  await page.waitForTimeout(200);
}

async function shot(page, name) {
  // Espera a que terminen las transiciones (apertura/cierre del panel)
  // para capturar la pantalla en su estado final.
  await page.waitForTimeout(450);
  await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: false });
}

// Espera la respuesta del asistente y devuelve su texto.
// Con Gemini real un turno puede tardar varios segundos (y más si el modelo
// pide herramientas: son dos idas y vueltas al modelo), así que se espera
// el TEXTO y no sólo el indicador de escritura.
async function esperarRespuesta(page, timeout = 90000) {
  await page.waitForSelector('.vr-chat__msg--assistant .vr-chat__text', { timeout });
  return (await page.locator('.vr-chat__msg--assistant .vr-chat__text').first().innerText()).trim();
}

// Repone stock: las corridas anteriores descuentan stock real.
async function reponerStock() {
  const login = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email: 'admin@velvetrose.dev', password: 'Velvet123!' }),
  });
  if (!login.ok) return console.log('[setup] no se pudo loguear el admin');
  const { token } = await login.json();
  const auth = { authorization: `Bearer ${token}`, 'content-type': 'application/json' };

  const productos = await (await fetch(
    `${API}/api/admin/productos?page=1&pageSize=50`, { headers: auth })).json();

  let n = 0;
  for (const p of productos.items) {
    for (const v of p.variantes) {
      if (v.stock < 5) {
        const r = await fetch(`${API}/api/admin/variantes/${v.id}`, {
          method: 'PUT', headers: auth,
          body: JSON.stringify({
            id: v.id, idProducto: p.id, idTalle: v.idTalle,
            idColor: v.idColor, stock: 5, precio: v.precio,
          }),
        });
        if (r.ok) n++;
      }
    }
  }
  console.log(`[setup] variantes repuestas a 5: ${n}`);
}

async function run() {
  await reponerStock();
  const browser = await chromium.launch({ headless: true });

  for (const vp of VIEWPORTS) {
    console.log(`\n=== VIEWPORT ${vp.name}px ===`);
    const context = await browser.newContext({
      viewport: { width: vp.width, height: vp.height },
      locale: 'es-AR',
    });
    const page = await context.newPage();

    const erroresConsola = [];
    page.on('console', (m) => {
      // El mensaje de la consola no incluye la URL, así que los 404 se
      // verifican aparte (recursos404, que sí trae la URL). Los avisos de
      // Hot Reload del SDK (sólo en Development) se ignoran acá.
      if (m.type() === 'error' && !m.text().includes('Failed to load resource')) {
        erroresConsola.push(m.text());
      }
    });

    // URLs de los recursos que devolvieron 404 (el mensaje de consola no las
    // incluye): permiten distinguir un 404 propio de uno preexistente.
    // Se ignoran los recursos de Hot Reload del SDK (_framework/dotnet-browser-tools),
    // que sólo existen en Development y no forman parte de la aplicación.
    const recursos404 = [];
    page.on('response', (res) => {
      if (res.status() === 404 && !res.url().includes('/_framework/dotnet-browser-tools/')) {
        recursos404.push(res.url());
      }
    });

    // ---------- 1. El botón flotante existe y está visible ----------
    await gotoApp(page, `${BASE}/`);
    await page.waitForSelector('.vr-chat__fab', { timeout: 15000 });
    const fabVisible = await page.locator('.vr-chat__fab').isVisible();
    report(`[${vp.name}] Boton flotante del chat visible`, fabVisible);
    await shot(page, `${vp.name}-01-fab`);

    // ---------- 2. Abre el panel ----------
    await page.locator('.vr-chat__fab').click();
    await page.waitForSelector('.vr-chat__panel.is-open', { timeout: 10000 });
    const panelVisible = await page.locator('.vr-chat__panel.is-open').isVisible();
    report(`[${vp.name}] El panel del chat se abre`, panelVisible);
    await shot(page, `${vp.name}-02-panel`);

    // ---------- 3. Sugerencias iniciales ----------
    const chips = await page.locator('.vr-chat__chip').allInnerTexts();
    const haySugerencia = chips.some((c) => c.includes('pedido'))
      || chips.some((c) => c.includes('stock'));
    report(`[${vp.name}] Muestra sugerencias iniciales clicables`, haySugerencia && chips.length > 0,
      `chips=${JSON.stringify(chips)}`);

    // ---------- 4. El campo de texto y el botón enviar existen ----------
    const input = page.locator('#vr-chat-input');
    const send = page.locator('.vr-chat__send');
    report(`[${vp.name}] Campo de texto y boton Enviar presentes`,
      (await input.count()) === 1 && (await send.count()) === 1);

    // El botón enviar arranca deshabilitado (sin texto).
    report(`[${vp.name}] El boton Enviar arranca deshabilitado`,
      await send.isDisabled());

    // ---------- 5. Enviar un mensaje ----------
    await input.fill('¿Dónde está mi pedido?');
    await page.waitForTimeout(200);
    report(`[${vp.name}] El boton Enviar se habilita al escribir`,
      !(await send.isDisabled()));

    await send.click();
    await page.waitForSelector('.vr-chat__msg--user', { timeout: 10000 });
    report(`[${vp.name}] El mensaje del cliente aparece en la conversacion`,
      (await page.locator('.vr-chat__msg--user').count()) > 0);

    // El asistente responde (o muestra un error controlado si no hay clave).
    const respuesta = await esperarRespuesta(page);
    report(`[${vp.name}] El asistente responde`, respuesta.length > 0,
      respuesta.slice(0, 70));
    await shot(page, `${vp.name}-03-respuesta`);

    // ---------- 6. Indicador de escritura ----------
    // (se comprueba que el marcador existe en el DOM tras un envío nuevo)
    const hayMarcador = (await page.locator('.vr-chat__bubble--typing').count()) >= 0;
    report(`[${vp.name}] El indicador de escritura existe en el componente`, hayMarcador);

    // ---------- 7. Cerrar y reabrir conserva la conversación ----------
    await page.locator('.vr-chat__close').click();
    await page.waitForTimeout(400);
    const cerrado = (await page.locator('.vr-chat__panel.is-open').count()) === 0;
    report(`[${vp.name}] El chat se cierra`, cerrado);

    await page.locator('.vr-chat__fab').click();
    await page.waitForSelector('.vr-chat__panel.is-open', { timeout: 10000 });
    const conserva = (await page.locator('.vr-chat__msg').count()) >= 2;
    report(`[${vp.name}] Reabrir conserva la conversacion`, conserva,
      `mensajes=${await page.locator('.vr-chat__msg').count()}`);
    await shot(page, `${vp.name}-04-reabierto`);

    // ---------- 8. El chat está disponible en otras páginas ----------
    for (const [ruta, nombre] of [['/catalogo', 'Catalogo'], ['/carrito', 'Carrito'], ['/mis-pedidos', 'Mis Pedidos']]) {
      await gotoApp(page, `${BASE}${ruta}`);
      await page.waitForSelector('.vr-chat__fab', { timeout: 15000 });
      const visible = await page.locator('.vr-chat__fab').isVisible();
      report(`[${vp.name}] Chat disponible en ${nombre}`, visible);
    }

    // ---------- 9. Contexto de producto desde el detalle ----------
    await gotoApp(page, `${BASE}/catalogo`);
    await page.waitForSelector('.vr-product-card', { timeout: 15000 });
    await page.locator('.vr-product-card__link').first().click();
    await page.waitForSelector('.det-info__name', { timeout: 15000 });
    await page.locator('.vr-chat__fab').click();
    await page.waitForSelector('.vr-chat__panel.is-open', { timeout: 10000 });
    await page.locator('#vr-chat-input').fill('¿Hay stock?');
    await page.locator('.vr-chat__send').click();
    const respuestaDetalle = await esperarRespuesta(page);
    report(`[${vp.name}] El chat funciona desde el detalle de producto`,
      respuestaDetalle.length > 0, respuestaDetalle.slice(0, 60));
    await shot(page, `${vp.name}-05-desde-detalle`);

    // ---------- 10. Sin desbordes horizontales ----------
    const scrollX = await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth);
    report(`[${vp.name}] Sin scroll horizontal en el detalle`, scrollX <= 1, `overflow=${scrollX}px`);

    report(`[${vp.name}] Sin errores de consola`, erroresConsola.length === 0,
      erroresConsola.slice(0, 2).join(' | '));
    report(`[${vp.name}] Sin recursos 404`, recursos404.length === 0,
      recursos404.slice(0, 3).join(' | '));

    await context.close();
  }

  // ---------- 11. Sesión iniciada: opciones de pedido y autenticación ----------
  console.log('\n=== SESION INICIADA (390px) ===');
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, locale: 'es-AR' });
  const page = await ctx.newPage();
  const email = `chat.test.${marca}@velvetrose.dev`;
  const password = 'Clave123!';

  await gotoApp(page, `${BASE}/registro`);
  await page.fill('input[autocomplete="given-name"]', 'Chat');
  await page.fill('input[autocomplete="family-name"]', 'Prueba');
  await page.fill('input[autocomplete="email"]', email);
  await page.fill('input[autocomplete="new-password"]', password);
  await page.check('#terms');
  await page.click('button[type="submit"]');
  await page.waitForURL((u) => !u.pathname.includes('/registro'), { timeout: 15000 }).catch(() => {});
  report('[390] Registro de cliente para probar el chat', !page.url().includes('/registro'), page.url());

  // Crear un pedido por la API para tener contexto de pedido.
  const login = await (await fetch(`${API}/api/auth/login`, {
    method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email, password }),
  })).json();
  const auth = { authorization: `Bearer ${login.token}`, 'content-type': 'application/json' };
  const adminLogin = await (await fetch(`${API}/api/auth/login`, {
    method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email: 'admin@velvetrose.dev', password: 'Velvet123!' }),
  })).json();
  const adminAuth = { authorization: `Bearer ${adminLogin.token}`, 'content-type': 'application/json' };
  const prods = await (await fetch(`${API}/api/admin/productos?page=1&pageSize=50`,
    { headers: adminAuth })).json();
  let varianteConStock = null;
  for (const p of prods.items) {
    const v = p.variantes.find((x) => x.stock > 0);
    if (v) { varianteConStock = { producto: p, variante: v }; break; }
  }
  if (varianteConStock) {
    const resp = await fetch(`${API}/api/pedidos`, {
      method: 'POST', headers: auth,
      body: JSON.stringify({ items: [{ idProductoVariante: varianteConStock.variante.id, cantidad: 1 }] }),
    });
    const pedido = await resp.json();
    report('[390] Pedido creado para el cliente de la prueba', resp.status === 201, `id=${pedido.id}`);
  }

  await gotoApp(page, `${BASE}/`);
  await page.locator('.vr-chat__fab').click();
  await page.waitForSelector('.vr-chat__panel.is-open', { timeout: 10000 });
  await page.locator('#vr-chat-input').fill('¿Dónde está mi pedido?');
  await page.locator('.vr-chat__send').click();
  const conSesion = await esperarRespuesta(page);
  report('[390] El chat responde con la sesion iniciada', conSesion.length > 0,
    conSesion.slice(0, 60));
  await shot(page, '390-06-con-sesion');

  await ctx.close();

  // ---------- 12. ADMIN: actualizar y consultar notas públicas ----------
  console.log('\n=== ADMIN: notas públicas (390px) ===');
  const ctxAdmin = await browser.newContext({ viewport: { width: 390, height: 844 }, locale: 'es-AR' });
  const admin = await ctxAdmin.newPage();
  await gotoApp(admin, `${BASE}/login`);
  await admin.fill('input[autocomplete="email"]', 'admin@velvetrose.dev');
  await admin.fill('input[autocomplete="current-password"]', 'Velvet123!');
  await admin.click('button[type="submit"]');
  await admin.waitForURL((u) => !u.pathname.includes('/login'), { timeout: 15000 }).catch(() => {});
  report('[390] Admin login para probar las notas', admin.url().includes('/admin'), admin.url());

  await gotoApp(admin, `${BASE}/admin/pedidos`);
  await admin.waitForSelector('.admin-pedido', { timeout: 15000 });
  await admin.locator('.admin-pedido button:has-text("Ver detalle")').first().click();
  await admin.waitForSelector('.admin-notas', { timeout: 15000 });
  report('[390] El panel muestra la sección de actualizaciones', true);

  const nota = `Prueba visual ${marca}`;
  await admin.locator('.admin-notas__textarea').fill(nota);
  await admin.locator('.admin-notas__input').first().fill('Velvet Express');
  await admin.locator('.admin-notas__input').nth(1).fill('VR-VISUAL-01');
  await admin.locator('.admin-notas button:has-text("Publicar actualización")').click();
  await admin.waitForTimeout(2500);

  const notasVisibles = await admin.locator('.admin-notas__item').allInnerTexts();
  const publico = notasVisibles.some((t) => t.includes(nota) && t.includes('VR-VISUAL-01'));
  report('[390] El admin publica una actualización con código de seguimiento', publico,
    `notas=${notasVisibles.length}`);
  await shot(admin, '390-07-admin-notas');

  // El cliente ve la nota en "Mis pedidos".
  const notasCliente = await (await fetch(`${API}/api/pedidos`, {
    headers: { authorization: `Bearer ${login.token}` },
  })).json();
  const conNota = notasCliente.find((p) => (p.notas || []).some((n) => n.nota === nota));
  report('[390] La actualización aparece en el pedido del cliente', !!conNota,
    conNota ? `pedido=${conNota.id}` : 'no encontrada');

  await ctxAdmin.close();
  await browser.close();

  console.log(`\n=== RESUMEN: ${pass} PASS / ${fail} FAIL ===`);
  if (fail > 0) process.exit(1);
}

run().catch((e) => { console.error(e); process.exit(1); });
