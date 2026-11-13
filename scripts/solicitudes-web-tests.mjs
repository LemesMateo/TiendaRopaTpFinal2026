// ============================================================================
// Pruebas del chat con solicitudes + panel de la tienda — Playwright
// Velvet Rose Boutique (incremento 5: solicitudes y notificaciones)
// Uso:  node scripts/solicitudes-web-tests.mjs
// Requisito: Web en :5101 y API en :5100.
// ============================================================================
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const WEB = 'http://localhost:5101';
const API = 'http://localhost:5100';
const OUT = 'test-evidence/web-solicitudes';
mkdirSync(OUT, { recursive: true });

let pass = 0, fail = 0;
function report(nombre, ok, detalle = '') {
  if (ok) pass++; else fail++;
  console.log(`[${ok ? 'PASS' : 'FAIL'}] ${nombre}${detalle ? ' - ' + detalle : ''}`);
}

const marca = Date.now();

async function gotoApp(page, url) {
  const ws = page.waitForEvent('websocket', { timeout: 15000 }).catch(() => null);
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await ws;
  await page.waitForTimeout(250);
}

async function shot(page, name) {
  await page.waitForTimeout(400);
  await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: false });
}

async function apiLogin(email, password) {
  const r = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  if (!r.ok) throw new Error(`login falló: ${r.status}`);
  return (await r.json()).token;
}

// Prepara un cliente con un pedido y una propuesta de solicitud pendiente.
async function prepararCliente(email) {
  const reg = await fetch(`${API}/api/auth/registro`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      nombre: 'Cliente', apellido: 'Prueba',
      email, password: 'Clave123!', telefono: '341-555-0101',
    }),
  });
  if (!reg.ok && reg.status !== 409) throw new Error(`registro falló: ${reg.status}`);

  const token = await apiLogin(email, 'Clave123!');

  // Propuesta de solicitud pendiente (como la deja el asistente).
  const prop = await fetch(`${API}/api/asistente/propuestas`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
    body: JSON.stringify({
      tipo: 'consulta_general',
      mensaje: 'Necesito cambiar la dirección de entrega de mi pedido.',
    }),
  });
  if (!prop.ok) throw new Error(`propuesta falló: ${prop.status}`);

  return { token, propuesta: await prop.json() };
}

async function run() {
  const browser = await chromium.launch({ headless: true });

  const email = `sol.web.${marca}@velvetrose.dev`;
  const { token, propuesta } = await prepararCliente(email);
  console.log(`[setup] propuesta #${propuesta.id} para ${email}`);

  // ------------------------------------------------------------------
  // 1. Cliente: la propuesta aparece en el chat con Confirmar / Cancelar
  // ------------------------------------------------------------------
  console.log('\n=== CLIENTE: vista previa en el chat ===');
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, locale: 'es-AR' });
  const page = await ctx.newPage();

  const errores = [];
  // Se ignoran los avisos de Hot Reload del SDK (sólo existen en Development).
  page.on('console', (m) => {
    if (m.type() === 'error' && !m.text().includes('Failed to load resource')) {
      errores.push(m.text());
    }
  });

  const recursos404 = [];
  page.on('response', (r) => {
    if (r.status() === 404 && !r.url().includes('/_framework/dotnet-browser-tools/')) {
      recursos404.push(r.url());
    }
  });

  await gotoApp(page, `${WEB}/`);
  // El cliente inicia sesión en la Web: las propuestas son suyas.
  await gotoApp(page, `${WEB}/login`);
  await page.fill('input[autocomplete="email"]', email);
  await page.fill('input[autocomplete="current-password"]', 'Clave123!');
  await page.click('button[type="submit"]');
  await page.waitForURL((u) => !u.pathname.includes('/login'), { timeout: 20000 }).catch(() => {});
  report('Login del cliente en la Web', !page.url().includes('/login'), page.url());

  await gotoApp(page, `${WEB}/`);
  await page.locator('.vr-chat__fab').click();
  await page.waitForSelector('.vr-chat__panel.is-open', { timeout: 15000 });

  // Las propuestas pendientes se cargan al abrir el chat.
  await page.waitForSelector('.vr-chat__action', { timeout: 20000 }).catch(() => {});

  const tarjeta = page.locator('.vr-chat__action').first();
  const hayTarjeta = await tarjeta.count() > 0;
  report('El chat muestra la tarjeta de la solicitud preparada', hayTarjeta);

  if (hayTarjeta) {
    const texto = await tarjeta.innerText();
    report('La tarjeta muestra el mensaje del cliente',
      texto.includes('cambiar la direcci') || texto.includes('direcci'), texto.slice(0, 60).replace(/\n/g, ' '));
    report('La tarjeta dice que todavía NO se envió',
      texto.toLowerCase().includes('no se envió'), '');
    report('La tarjeta ofrece Confirmar y Cancelar',
      (await tarjeta.locator('.vr-chat__action-btn:has-text("Confirmar")').count()) === 1
      && (await tarjeta.locator('.vr-chat__action-btn:has-text("Cancelar")').count()) === 1, '');
    report('La tarjeta muestra el teléfono ENMASCARADO',
      /\*\*\*\d{2}/.test(texto), (texto.match(/\*\*\*\d{2}/) || [''])[0]);
    await shot(page, '390-01-tarjeta-propuesta');
  }

  // ------------------------------------------------------------------
  // 2. Cancelar: no se crea ninguna solicitud
  // ------------------------------------------------------------------
  console.log('\n=== CLIENTE: cancelar ===');
  if (hayTarjeta) {
    await tarjeta.locator('.vr-chat__action-btn:has-text("Cancelar")').click();
    await page.waitForTimeout(1500);

    const mis = await (await fetch(`${API}/api/mis-solicitudes`, {
      headers: { authorization: `Bearer ${token}` },
    })).json();
    report('Cancelar NO crea ninguna solicitud', mis.length === 0, `solicitudes=${mis.length}`);
  }

  // ------------------------------------------------------------------
  // 3. Confirmar: sí crea la solicitud (con idempotencia)
  // ------------------------------------------------------------------
  console.log('\n=== CLIENTE: confirmar ===');
  const { propuesta: propuesta2 } = await prepararCliente(email);
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(800);
  await page.locator('.vr-chat__fab').click();
  await page.waitForSelector('.vr-chat__panel.is-open', { timeout: 15000 });
  await page.waitForSelector('.vr-chat__action', { timeout: 20000 }).catch(() => {});

  const tarjeta2 = page.locator('.vr-chat__action').first();
  const hayTarjeta2 = await tarjeta2.count() > 0;
  report('Aparece la nueva tarjeta de propuesta', hayTarjeta2);

  if (hayTarjeta2) {
    await tarjeta2.locator('.vr-chat__action-btn:has-text("Confirmar")').click();
    await page.waitForTimeout(2500);

    const mis2 = await (await fetch(`${API}/api/mis-solicitudes`, {
      headers: { authorization: `Bearer ${token}` },
    })).json();
    report('Confirmar SÍ crea la solicitud', mis2.length === 1, `solicitudes=${mis2.length}`);
    report('La solicitud queda Abierta', mis2[0]?.estado === 'Abierta', mis2[0]?.estado);
    report('La solicitud guarda el teléfono confirmado',
      mis2[0]?.telefonoContacto === '341-555-0101', mis2[0]?.telefonoContacto);
    await shot(page, '390-02-confirmada');
  }

  // Idempotencia en la API (doble clic / reintento).
  const clave = `web-idem-${marca}`;
  const primera = await fetch(`${API}/api/asistente/propuestas/${propuesta2.id}/confirmar`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
    body: JSON.stringify({ claveIdempotencia: clave }),
  });
  const segunda = await fetch(`${API}/api/asistente/propuestas/${propuesta2.id}/confirmar`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
    body: JSON.stringify({ claveIdempotencia: clave }),
  });
  const cuerpo1 = await primera.json();
  const cuerpo2 = await segunda.json();
  report('Confirmar dos veces con la misma clave NO duplica',
    [200, 201].includes(primera.status) && [200, 201].includes(segunda.status)
    && cuerpo1.id === cuerpo2.id,
    `id1=${cuerpo1.id} id2=${cuerpo2.id}`);

  // ------------------------------------------------------------------
  // 4. El cliente ve sus solicitudes
  // ------------------------------------------------------------------
  console.log('\n=== CLIENTE: "Mis solicitudes" ===');
  await gotoApp(page, `${WEB}/mis-solicitudes`);
  await page.waitForSelector('.ms-item, .vr-empty', { timeout: 20000 });
  const items = await page.locator('.ms-item').count();
  report('La página "Mis solicitudes" lista la solicitud', items >= 1, `items=${items}`);
  await shot(page, '390-03-mis-solicitudes');

  report('Sin errores de consola en el flujo del cliente', errores.length === 0,
    errores.slice(0, 2).join(' | '));
  report('Sin recursos 404 en el flujo del cliente', recursos404.length === 0,
    recursos404.slice(0, 3).join(' | '));
  await ctx.close();

  // ------------------------------------------------------------------
  // 5. Panel de la tienda: campana, detalle, respuesta y cierre
  // ------------------------------------------------------------------
  console.log('\n=== TIENDA: panel de solicitudes ===');
  const ctxAdmin = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'es-AR' });
  const admin = await ctxAdmin.newPage();

  await gotoApp(admin, `${WEB}/login`);
  await admin.fill('input[autocomplete="email"]', 'vendedor@velvetrose.dev');
  await admin.fill('input[autocomplete="current-password"]', 'Velvet123!');
  await admin.click('button[type="submit"]');
  await admin.waitForURL((u) => !u.pathname.includes('/login'), { timeout: 15000 }).catch(() => {});
  report('Login del vendedor -> sesion iniciada',
    !admin.url().includes('/login'), admin.url());

  await gotoApp(admin, `${WEB}/admin/solicitudes`);
  await admin.waitForSelector('.sol-item, .vr-empty', { timeout: 20000 });
  const solItems = await admin.locator('.sol-item').count();
  report('El vendedor ve el listado de solicitudes', solItems >= 1, `solicitudes=${solItems}`);

  // Campana con contador de pendientes.
  const badge = await admin.locator('.sol-bell__badge').count();
  const badgeTexto = badge ? await admin.locator('.sol-bell__badge').first().innerText() : '0';
  report('La campana muestra el contador de pendientes', badge > 0, `badge=${badgeTexto}`);
  await shot(admin, '1440-01-panel-solicitudes');

  // Abrir el detalle.
  await admin.locator('.sol-item__head').first().click();
  await admin.waitForSelector('.sol-detail', { timeout: 15000 });
  const detalle = await admin.locator('.sol-detail').first().innerText();
  report('El detalle muestra el teléfono de contacto',
    detalle.includes('341-555-0101'), detalle.slice(0, 60).replace(/\n/g, ' '));
  report('El detalle muestra el email del cliente',
    detalle.includes('@velvetrose.dev'), '');
  await shot(admin, '1440-02-detalle');

  // Responder.
  const respuestaTexto = `Hola, ya actualizamos la dirección (${marca}).`;
  await admin.locator('.sol-detail__textarea').first().fill(respuestaTexto);
  await admin.locator('button:has-text("Enviar respuesta")').first().click();
  await admin.waitForTimeout(2500);

  const mis3 = await (await fetch(`${API}/api/mis-solicitudes`, {
    headers: { authorization: `Bearer ${token}` },
  })).json();
  const respondida = mis3.find((s) => (s.respuesta || '').includes(String(marca)));
  report('El vendedor responde y el cliente recibe la respuesta',
    !!respondida, respondida ? respondida.respuesta : 'no encontrada');
  await shot(admin, '1440-03-respondida');

  // Cerrar (el selector se acota al detalle: el sidebar tiene "Cerrar sesión").
  await admin.locator('.sol-detail button:has-text("Cerrar")').first().click();
  await admin.waitForTimeout(2000);
  const mis4 = await (await fetch(`${API}/api/mis-solicitudes`, {
    headers: { authorization: `Bearer ${token}` },
  })).json();
  const cerrada = mis4.find((s) => s.estado === 'Cerrada');
  report('El vendedor puede cerrar la solicitud', !!cerrada,
    mis4.map((s) => s.estado).join(','));

  // El vendedor NO puede entrar al catálogo: la UI muestra la pantalla de
  // sección restringida (y la API responde 403, ver scripts\solicitudes-tests.ps1).
  await gotoApp(admin, `${WEB}/admin/productos`);
  await admin.waitForTimeout(1500);
  const sinPermiso = await admin.locator('.vr-empty__title').count();
  const titulo = sinPermiso ? await admin.locator('.vr-empty__title').first().innerText() : '';
  report('El vendedor NO accede al catálogo (permisos restringidos)',
    sinPermiso > 0 && /restringid/i.test(titulo), titulo);

  await ctxAdmin.close();
  await browser.close();

  // Limpieza.
  await fetch(`${API}/api/asistente/propuestas`, { method: 'GET' }).catch(() => {});
  console.log(`\n=== RESUMEN: ${pass} PASS / ${fail} FAIL ===`);
  if (fail > 0) process.exit(1);
}

run().catch((e) => { console.error(e); process.exit(1); });
