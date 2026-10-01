// ============================================================================
// Pruebas Web (Playwright) — Velvet Rose Boutique (incremento 2)
// Recorrido completo en 390 / 768 / 1440 px:
//   registro -> login -> catálogo -> detalle -> variante -> carrito ->
//   confirmar pedido -> mis pedidos
// Admin: login -> panel -> productos -> variantes/stock -> pedidos -> estado
// Uso:  node scripts/web-tests.mjs       (la Web debe estar en :5101)
// Salida: PASS/FAIL por paso + capturas en test-evidence/web/
// ============================================================================
import { chromium, devices } from 'playwright';
import { mkdirSync } from 'node:fs';

const BASE = 'http://localhost:5101';
const OUT = 'test-evidence/web';
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

// Los pedidos de esta suite descuentan stock REAL en SQL Server, así que las
// corridas sucesivas lo agotan. Reponemos a ≥5 unidades por variante antes de
// empezar (sólo desarrollo; usa el admin seed).
async function reponerStock() {
  const login = await fetch('http://localhost:5100/api/auth/login', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email: 'admin@velvetrose.dev', password: 'Velvet123!' }),
  });
  if (!login.ok) return console.log('[setup] no se pudo loguear el admin para reponer stock');
  const { token } = await login.json();
  const auth = { authorization: `Bearer ${token}`, 'content-type': 'application/json' };

  const productos = await (await fetch(
    'http://localhost:5100/api/admin/productos?page=1&pageSize=50', { headers: auth })).json();

  let repuestas = 0;
  for (const producto of productos.items) {
    for (const variante of producto.variantes) {
      if (variante.stock < 5) {
        const resp = await fetch(`http://localhost:5100/api/admin/variantes/${variante.id}`, {
          method: 'PUT',
          headers: auth,
          body: JSON.stringify({
            id: variante.id,
            idProducto: producto.id,
            idTalle: variante.idTalle,
            idColor: variante.idColor,
            stock: 5,
            precio: variante.precio,
          }),
        });
        if (resp.ok) repuestas++;
      }
    }
  }
  console.log(`[setup] variantes repuestas a 5 unidades: ${repuestas}`);
}

async function shot(page, name) {
  await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: true });
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
    page.on('console', (msg) => {
      if (msg.type() === 'error') erroresConsola.push(msg.text());
    });

    const email = `web.test.${vp.name}.${marca}@velvetrose.dev`;
    const password = 'Clave123!';

    // ---------- 1. Registro ----------
    await page.goto(`${BASE}/registro`, { waitUntil: 'networkidle' });
    await page.fill('input[autocomplete="given-name"]', 'Web');
    await page.fill('input[autocomplete="family-name"]', `Test${vp.name}`);
    await page.fill('input[autocomplete="email"]', email);
    await page.fill('input[autocomplete="new-password"]', password);
    await page.check('#terms');
    await shot(page, `${vp.name}-01-registro`);
    await page.click('button[type="submit"]');
    await page.waitForURL((u) => !u.pathname.includes('/registro'), { timeout: 15000 });
    report(`[${vp.name}] Registro -> redirige a inicio`, !page.url().includes('/registro'), page.url());

    // ---------- 2. Catálogo ----------
    await page.goto(`${BASE}/catalogo`, { waitUntil: 'networkidle' });
    await page.waitForSelector('.vr-product-card', { timeout: 15000 });
    const nCards = await page.locator('.vr-product-card').count();
    report(`[${vp.name}] Catálogo muestra productos`, nCards > 0, `n=${nCards}`);
    await shot(page, `${vp.name}-02-catalogo`);

    // ---------- 3. Detalle ----------
    // Elegimos un producto SIN badge "Sin stock" (los pedidos anteriores
    // descuentan stock real, por eso no fijamos el producto).
    const conStock = page.locator('.vr-product-card', {
      hasNot: page.locator('.vr-badge', { hasText: 'Sin stock' }),
    });
    const nConStock = await conStock.count();
    report(`[${vp.name}] Catálogo tiene productos con stock`, nConStock > 0, `conStock=${nConStock}`);
    await conStock.first().locator('.vr-product-card__link').first().click();
    await page.waitForSelector('.det-info__name', { timeout: 15000 });
    const nombre = await page.locator('.det-info__name').innerText();
    report(`[${vp.name}] Detalle de producto`, nombre.length > 0, nombre);

    const talles = await page.locator('.det-chip').count();
    const colores = await page.locator('.det-swatch').count();
    report(`[${vp.name}] Detalle muestra talles y colores`, talles > 0 && colores > 0,
      `talles=${talles} colores=${colores}`);

    // Seleccionar talle y color explícitamente
    const talleDisponible = page.locator('.det-chip:not(.is-disabled)').first();
    await talleDisponible.click();
    const colorDisponible = page.locator('.det-swatch:not(.is-disabled)').first();
    await colorDisponible.click();

    const precio = await page.locator('.det-info__price-now').innerText();
    report(`[${vp.name}] Precio visible tras elegir variante`, precio.startsWith('$'), precio);
    await shot(page, `${vp.name}-03-detalle`);

    // ---------- 4. Agregar al carrito ----------
    // Busca una combinación talle+color CON stock (algunas variantes son 0 a propósito).
    const btnAdd = page.locator('.det-actions button').first();
    let agregado = false;
    const tallesDisp = await page.locator('.det-chip:not(.is-disabled)').count();
    const coloresDisp = await page.locator('.det-swatch:not(.is-disabled)').count();

    for (let t = 0; t < tallesDisp && !agregado; t++) {
      await page.locator('.det-chip:not(.is-disabled)').nth(t).click();
      for (let c = 0; c < coloresDisp && !agregado; c++) {
        await page.locator('.det-swatch:not(.is-disabled)').nth(c).click();
        if (!(await btnAdd.isDisabled())) {
          await btnAdd.click();
          await page.waitForSelector('.det-info__notice', { timeout: 10000 });
          const aviso = await page.locator('.det-info__notice').innerText();
          agregado = aviso.toLowerCase().includes('agregado');
          report(`[${vp.name}] Agregar al carrito`, agregado, aviso.slice(0, 60));
        }
      }
    }
    if (!agregado) {
      report(`[${vp.name}] Agregar al carrito`, false, 'sin combinación con stock');
    }

    // ---------- 5. Carrito ----------
    await page.goto(`${BASE}/carrito`, { waitUntil: 'networkidle' });
    await page.waitForSelector('.cart-item, .vr-empty', { timeout: 10000 }).catch(() => {});
    const nItems = await page.locator('.cart-item').count();
    report(`[${vp.name}] Carrito con el producto agregado`, nItems > 0, `items=${nItems}`);
    await shot(page, `${vp.name}-04-carrito`);

    // ---------- 6. Login (sesión del registro sigue activa; lo probamos igual) ----------
    await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
    const yaLogueado = !page.url().includes('/login');
    if (!yaLogueado) {
      await page.fill('input[autocomplete="email"]', email);
      await page.fill('input[autocomplete="current-password"]', password);
      await page.click('button[type="submit"]');
      await page.waitForURL((u) => !u.pathname.includes('/login'), { timeout: 15000 });
    }
    report(`[${vp.name}] Login con sesión`, !page.url().includes('/login'), page.url());

    // Login con credenciales incorrectas (debe quedarse en /login con aviso)
    const ctx2 = await browser.newContext({ viewport: { width: vp.width, height: vp.height } });
    const page2 = await ctx2.newPage();
    await page2.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
    await page2.fill('input[autocomplete="email"]', `nadie.${marca}@velvetrose.dev`);
    await page2.fill('input[autocomplete="current-password"]', 'incorrecta');
    await page2.click('button[type="submit"]');
    await page2.waitForTimeout(1500);
    const aviso = await page2.locator('.auth-panel__notice').count();
    report(`[${vp.name}] Login incorrecto muestra aviso`, aviso > 0 && page2.url().includes('/login'));
    await ctx2.close();

    // ---------- 7. Confirmar pedido ----------
    await page.goto(`${BASE}/carrito`, { waitUntil: 'networkidle' });
    const nAntes = await page.locator('.cart-item').count();
    if (nAntes > 0) {
      await page.click('text=Confirmar pedido');
      const ok = await page.waitForURL((u) => u.pathname.includes('/mis-pedidos'), { timeout: 20000 })
        .then(() => true).catch(() => false);
      report(`[${vp.name}] Confirmar pedido -> Mis Pedidos`, ok, page.url());

      if (ok) {
        await page.waitForSelector('.mp-card', { timeout: 15000 });
        const nPedidos = await page.locator('.mp-card').count();
        report(`[${vp.name}] Mis Pedidos lista pedidos`, nPedidos > 0, `pedidos=${nPedidos}`);
        await shot(page, `${vp.name}-05-mis-pedidos`);
      }
    } else {
      report(`[${vp.name}] Confirmar pedido`, false, 'carrito vacío');
    }

    // ---------- 8. Cerrar sesión ----------
    // En móvil el botón vive en el cajón (oculto); en desktop en el sidebar.
    const logoutVisible = page.locator('.app-navlist__logout:visible');
    if (await logoutVisible.count() === 0) {
      await page.locator('.app-header__menu').click();
      await page.waitForTimeout(400);
    }
    const logout = page.locator('.app-navlist__logout:visible').first();
    if (await logout.count() > 0) {
      await logout.click();
      await page.waitForURL((u) => u.pathname === '/', { timeout: 10000 }).catch(() => {});
      report(`[${vp.name}] Cerrar sesión`, page.url().replace(BASE, '') === '/', page.url());
    } else {
      report(`[${vp.name}] Cerrar sesión`, false, 'botón no encontrado');
    }

    // ---------- 9. ADMIN (sólo en 1440 para no duplicar; se repite en todos) ----------
    // Login como admin en contexto nuevo
    const ctxAdmin = await browser.newContext({ viewport: { width: vp.width, height: vp.height } });
    const admin = await ctxAdmin.newPage();
    await admin.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
    await admin.fill('input[autocomplete="email"]', 'admin@velvetrose.dev');
    await admin.fill('input[autocomplete="current-password"]', 'Velvet123!');
    await admin.click('button[type="submit"]');
    await admin.waitForURL((u) => !u.pathname.includes('/login'), { timeout: 15000 }).catch(() => {});
    report(`[${vp.name}] Admin login`, admin.url().includes('/admin'), admin.url());

    await admin.goto(`${BASE}/admin`, { waitUntil: 'networkidle' });
    const nCardsAdmin = await admin.locator('.admin-card').count();
    report(`[${vp.name}] Panel admin`, nCardsAdmin >= 4, `secciones=${nCardsAdmin}`);
    await shot(admin, `${vp.name}-06-admin`);

    // Productos + variantes
    await admin.goto(`${BASE}/admin/productos`, { waitUntil: 'networkidle' });
    await admin.waitForSelector('.admin-list__item', { timeout: 15000 });
    const nProd = await admin.locator('.admin-list__item').count();
    report(`[${vp.name}] Admin lista productos`, nProd > 0, `n=${nProd}`);

    // Editar el primero -> formulario con variantes
    await admin.locator('.admin-list__item').first().locator('text=Editar').click();
    await admin.waitForSelector('.admin-variants', { timeout: 10000 });
    const nVar = await admin.locator('.admin-variants__list li').count();
    report(`[${vp.name}] Admin variantes/stock del producto`, nVar > 0, `variantes=${nVar}`);
    await shot(admin, `${vp.name}-07-admin-producto`);
    await admin.locator('text=Cancelar').first().click().catch(() => {});

    // Pedidos + cambio de estado
    await admin.goto(`${BASE}/admin/pedidos`, { waitUntil: 'networkidle' });
    await admin.waitForSelector('.admin-pedido, .vr-empty', { timeout: 15000 }).catch(() => {});
    const nPed = await admin.locator('.admin-pedido').count();
    report(`[${vp.name}] Admin lista pedidos`, nPed > 0, `pedidos=${nPed}`);
    await shot(admin, `${vp.name}-08-admin-pedidos`);

    if (nPed > 0) {
      // Cambiar estado en el primer pedido que tenga transiciones
      const selects = admin.locator('.admin-pedido__estado select');
      const nSel = await selects.count();
      let cambiado = false;
      for (let i = 0; i < nSel && !cambiado; i++) {
        const opciones = await selects.nth(i).locator('option').allInnerTexts();
        const siguiente = opciones.find((o) => o && o !== 'Cambiar estado…');
        if (siguiente) {
          await selects.nth(i).selectOption({ label: siguiente });
          const fila = admin.locator('.admin-pedido').nth(i);
          await fila.locator('text=Aplicar').click();
          await admin.waitForTimeout(2500);
          const notice = await admin.locator('.admin-notice').count();
          const okNotice = notice > 0
            ? !(await admin.locator('.admin-notice').first().innerText()).toLowerCase().includes('no ')
            : true;
          report(`[${vp.name}] Admin cambia estado (${siguiente})`, okNotice,
            (notice > 0 ? await admin.locator('.admin-notice').first().innerText() : 'sin aviso')?.slice(0, 70));
          cambiado = true;
        }
      }
      if (!cambiado) {
        report(`[${vp.name}] Admin cambia estado`, false, 'ninguna transición disponible');
      }
      await shot(admin, `${vp.name}-09-admin-estado`);
    }

    // Catálogos admin
    await admin.goto(`${BASE}/admin/catalogos`, { waitUntil: 'networkidle' });
    const nSecc = await admin.locator('.admin-form').count();
    report(`[${vp.name}] Admin catálogos (categorías/talles/colores)`, nSecc >= 3, `secciones=${nSecc}`);

    // Cliente SIN permisos no debe ver el admin
    await admin.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
    await ctxAdmin.close();

    const ctxCliente = await browser.newContext({ viewport: { width: vp.width, height: vp.height } });
    const cliente = await ctxCliente.newPage();
    await cliente.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
    await cliente.fill('input[autocomplete="email"]', email);
    await cliente.fill('input[autocomplete="current-password"]', password);
    await cliente.click('button[type="submit"]');
    await cliente.waitForTimeout(1500);
    await cliente.goto(`${BASE}/admin`, { waitUntil: 'networkidle' });
    const sinPermiso = await cliente.locator('text=No tenés permisos').count()
      + await cliente.locator('text=Sección restringida').count();
    report(`[${vp.name}] Cliente sin rol no ve panel admin`, sinPermiso > 0, `bloqueos=${sinPermiso}`);
    await shot(cliente, `${vp.name}-10-admin-denegado`);
    await ctxCliente.close();

    // Errores de consola (excluyo avisos conocidos de Blazor/reconnect)
    const erroresReales = erroresConsola.filter(
      (t) => !t.includes('blazor.websocket') && !t.includes('Failed to load resource: net::ERR'));
    report(`[${vp.name}] Sin errores de consola`, erroresReales.length === 0,
      erroresReales.slice(0, 2).join(' | ').slice(0, 120));

    await context.close();
  }

  await browser.close();

  console.log('\n==============================================');
  console.log(`RESULTADO WEB: ${pass} PASS / ${fail} FAIL`);
  for (const r of resultados.filter((x) => x.estado === 'FAIL')) {
    console.log(`  FAIL: ${r.nombre} ${r.detalle}`);
  }
  process.exit(fail > 0 ? 1 : 0);
}

run().catch((err) => {
  console.error('ERROR FATAL:', err);
  process.exit(2);
});
