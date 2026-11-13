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

// La app mantiene un WebSocket de Blazor Server abierto, asi que
// 'networkidle' nunca se alcanza: se espera el circuito de Blazor.
async function gotoApp(page, url) {
  const ws = page.waitForEvent('websocket', { timeout: 15000 }).catch(() => null);
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await ws;
  await page.waitForTimeout(150);
}


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

// ---------------------------------------------------------------------------
// Formulario de imágenes (límite de 5) — se ejecuta una sola vez, no por
// viewport, y usa un producto de PRUEBA creado por API para no tocar el catálogo
// real. Verifica los escenarios del reporte:
//   1) agregar hasta llegar a 5
//   2) contador coherente con la API
//   3) quitar 2 y agregar 2 en el MISMO guardado (antes fallaba con
//      "El producto ya tiene el máximo de 5 imágenes")
//   4) el aviso de error aparece DENTRO del formulario, junto a Guardar/Cancelar
// ---------------------------------------------------------------------------
async function pruebasImagenesAdmin(browser) {
  console.log('\n=== FORMULARIO DE IMÁGENES (límite de 5) ===');

  const API = 'http://localhost:5100';
  const login = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email: 'admin@velvetrose.dev', password: 'Velvet123!' }),
  });
  if (!login.ok) return report('Imágenes: login admin para la prueba', false, 'sin sesión API');
  const { token } = await login.json();
  const auth = { authorization: `Bearer ${token}`, 'content-type': 'application/json' };

  // Producto de prueba sin imágenes.
  const creado = await fetch(`${API}/api/admin/productos`, {
    method: 'POST',
    headers: auth,
    body: JSON.stringify({
      id: 0,
      nombre: `Producto Imágenes ${marca}`,
      idCategoria: 1,
      descripcion: 'Prueba automatizada del límite de 5 imágenes.',
      precioBase: 1000,
      activo: true,
    }),
  });
  if (!creado.ok) return report('Imágenes: crear producto de prueba', false, `status=${creado.status}`);
  const producto = await creado.json();
  const idProducto = producto.id;

  const leerImagenes = async () => {
    const r = await fetch(`${API}/api/admin/productos/${idProducto}`, { headers: auth });
    return (await r.json()).imagenes ?? [];
  };

  // PNG mínimo válido (firma + IHDR parcial) para las subidas de prueba.
  const png = Buffer.from(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==',
    'base64');
  const archivos = [1, 2, 3, 4, 5].map((n) => ({ name: `prueba-${n}.png`, mimeType: 'image/png', buffer: png }));

  let contexto;
  try {
    contexto = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const admin = await contexto.newPage();
    const errores = [];
    admin.on('console', (m) => { if (m.type() === 'error') errores.push(m.text()); });

    await gotoApp(admin, `${BASE}/login`);
    await admin.fill('input[autocomplete="email"]', 'admin@velvetrose.dev');
    await admin.fill('input[autocomplete="current-password"]', 'Velvet123!');
    await admin.click('button[type="submit"]');
    await admin.waitForTimeout(1500);

    await gotoApp(admin, `${BASE}/admin/productos`);
    await admin.waitForSelector('.admin-list__item', { timeout: 15000 });
    await admin.locator('.admin-list__item', { hasText: `Producto Imágenes ${marca}` })
      .locator('text=Editar').first().click();
    await admin.waitForSelector('.admin-form', { timeout: 15000 });

    const contador = () => admin.locator('.vr-imagenes__count').innerText();

    // 1) Agregar 5 imágenes.
    await admin.locator('.vr-imagenes__file').setInputFiles(archivos);
    await admin.waitForTimeout(2000);
    report('Imágenes: agregar 5 en el formulario', (await contador()).trim() === '5 de 5',
      `contador=${(await contador()).trim()}`);

    // 2) Guardar y comprobar que la API quedó con 5 (fuente autoritativa).
    await admin.locator('.admin-form__actions button', { hasText: 'Guardar' }).first().click();
    await admin.waitForTimeout(4000);
    let enApi = await leerImagenes();
    report('Imágenes: 5 persistidas en la API', enApi.length === 5, `api=${enApi.length}`);

    // 3) El input de subida desaparece al llegar al máximo.
    report('Imágenes: alta oculta al llegar a 5',
      (await admin.locator('.vr-imagenes__file').count()) === 0
      && (await admin.locator('.vr-imagenes__max').count()) === 1);

    // 4) Quitar 2 y agregar 2 en el mismo guardado (el caso que fallaba).
    await admin.locator('button[aria-label="Quitar la imagen 5"]').click();
    await admin.waitForTimeout(400);
    await admin.locator('button[aria-label="Quitar la imagen 4"]').click();
    await admin.waitForTimeout(600);
    report('Imágenes: quitar 2 libera lugares', (await contador()).trim() === '3 de 5',
      `contador=${(await contador()).trim()}`);

    await admin.locator('.vr-imagenes__file').setInputFiles([archivos[0], archivos[1]]);
    await admin.waitForTimeout(2000);
    report('Imágenes: agregar 2 de vuelta', (await contador()).trim() === '5 de 5',
      `contador=${(await contador()).trim()}`);

    await admin.locator('.admin-form__actions button', { hasText: 'Guardar' }).first().click();
    await admin.waitForTimeout(4500);
    enApi = await leerImagenes();
    const avisoForm = await admin.locator('.admin-notice--form').count();
    report('Imágenes: reemplazo 5→3→5 se guarda sin error de máximo',
      enApi.length === 5 && avisoForm === 0, `api=${enApi.length} avisos=${avisoForm}`);
    report('Imágenes: contador del formulario coincide con la API',
      (await contador()).trim() === `${enApi.length} de ${5}`,
      `ui=${(await contador()).trim()} api=${enApi.length}`);

    // 5) El aviso de error se muestra DENTRO del formulario, encima de Guardar.
    await admin.locator('.admin-field--wide input[type="text"]').first().fill('');
    await admin.locator('.admin-form__actions button', { hasText: 'Guardar' }).first().click();
    await admin.waitForTimeout(1200);

    const ubicacion = await admin.evaluate(() => {
      const aviso = document.querySelector('.admin-notice--form');
      const acciones = document.querySelector('.admin-form__actions');
      if (!aviso || !acciones) return null;
      const a = aviso.getBoundingClientRect();
      const b = acciones.getBoundingClientRect();
      return {
        texto: aviso.textContent.trim(),
        // El aviso está antes de los botones y dentro de la tarjeta del form.
        antesDeBotones: a.bottom <= b.top + 2,
        dentroDelForm: !!aviso.closest('.admin-form'),
        role: aviso.getAttribute('role'),
        foco: document.activeElement === aviso,
        fueraDeVista: a.top >= 0 && a.bottom <= window.innerHeight,
      };
    });

    report('Imágenes: aviso de validación dentro del formulario', ubicacion?.dentroDelForm === true,
      ubicacion?.texto?.slice(0, 60) ?? 'sin aviso');
    report('Imágenes: aviso encima de Guardar/Cancelar', ubicacion?.antesDeBotones === true);
    report('Imágenes: aviso accesible (role=alert + foco)',
      ubicacion?.role === 'alert' && ubicacion?.foco === true,
      `role=${ubicacion?.role} foco=${ubicacion?.foco}`);
    report('Imágenes: aviso visible sin desplazarse', ubicacion?.fueraDeVista === true);

    // 6) El aviso desaparece al corregir el dato (no queda bloqueado).
    await admin.locator('.admin-field--wide input[type="text"]').first()
      .fill(`Producto Imágenes ${marca}`);
    await admin.locator('.admin-form__actions button', { hasText: 'Guardar' }).first().click();
    await admin.waitForTimeout(3500);
    report('Imágenes: aviso desaparece tras corregir',
      (await admin.locator('.admin-notice--form').count()) === 0);

    report('Imágenes: sin errores de consola en el formulario',
      errores.filter((t) => !t.includes('status of 404')).length === 0,
      errores.filter((t) => !t.includes('status of 404')).slice(0, 2).join(' | ').slice(0, 100));

    await shot(admin, 'imagenes-admin-formulario');
  } finally {
    if (contexto) await contexto.close();

    // Limpieza: se borran las imágenes y se desactiva el producto de prueba.
    await fetch(`${API}/api/admin/productos/${idProducto}/imagenes`, {
      method: 'PUT', headers: auth, body: JSON.stringify([]),
    }).catch(() => {});
    await fetch(`${API}/api/admin/productos/${idProducto}`, {
      method: 'DELETE', headers: { authorization: `Bearer ${token}` },
    }).catch(() => {});
  }
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

    // URLs de los recursos que devolvieron 404: el mensaje de consola no
    // incluye la URL, asi que se registran aparte para poder distinguirlos.
    const recursos404 = [];
    page.on('response', (res) => {
      if (res.status() === 404) recursos404.push(res.url());
    });

    const email = `web.test.${vp.name}.${marca}@velvetrose.dev`;
    const password = 'Clave123!';

    // ---------- 1. Registro ----------
    await gotoApp(page, `${BASE}/registro`);
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
    await gotoApp(page, `${BASE}/catalogo`);
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
    await gotoApp(page, `${BASE}/carrito`);
    await page.waitForSelector('.cart-item, .vr-empty', { timeout: 10000 }).catch(() => {});
    const nItems = await page.locator('.cart-item').count();
    report(`[${vp.name}] Carrito con el producto agregado`, nItems > 0, `items=${nItems}`);
    await shot(page, `${vp.name}-04-carrito`);

    // ---------- 6. Login (sesión del registro sigue activa; lo probamos igual) ----------
    await gotoApp(page, `${BASE}/login`);
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
    await gotoApp(page2, `${BASE}/login`);
    await page2.fill('input[autocomplete="email"]', `nadie.${marca}@velvetrose.dev`);
    await page2.fill('input[autocomplete="current-password"]', 'incorrecta');
    await page2.click('button[type="submit"]');
    await page2.waitForTimeout(1500);
    const aviso = await page2.locator('.auth-panel__notice').count();
    report(`[${vp.name}] Login incorrecto muestra aviso`, aviso > 0 && page2.url().includes('/login'));
    await ctx2.close();

    // ---------- 7. Confirmar pedido ----------
    await gotoApp(page, `${BASE}/carrito`);
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
    await gotoApp(admin, `${BASE}/login`);
    await admin.fill('input[autocomplete="email"]', 'admin@velvetrose.dev');
    await admin.fill('input[autocomplete="current-password"]', 'Velvet123!');
    await admin.click('button[type="submit"]');
    await admin.waitForURL((u) => !u.pathname.includes('/login'), { timeout: 15000 }).catch(() => {});
    report(`[${vp.name}] Admin login`, admin.url().includes('/admin'), admin.url());

    await gotoApp(admin, `${BASE}/admin`);
    const nCardsAdmin = await admin.locator('.admin-card').count();
    report(`[${vp.name}] Panel admin`, nCardsAdmin >= 4, `secciones=${nCardsAdmin}`);
    await shot(admin, `${vp.name}-06-admin`);

    // Productos + variantes
    await gotoApp(admin, `${BASE}/admin/productos`);
    await admin.waitForSelector('.admin-list__item', { timeout: 15000 });
    const nProd = await admin.locator('.admin-list__item').count();
    report(`[${vp.name}] Admin lista productos`, nProd > 0, `n=${nProd}`);

    // Editar el primero -> formulario con variantes
    await admin.locator('.admin-list__item').first().locator('text=Editar').click();
    // Las variantes van en un acordeon plegado por defecto: se abre para
    // comprobar que el panel de variantes sigue disponible.
    await admin.waitForSelector('.admin-accordion__header', { timeout: 10000 });
    await admin.locator('.admin-accordion__header').click();
    await admin.waitForSelector('.admin-variants', { timeout: 10000 });
    const nVar = await admin.locator('.admin-variants__list li').count();
    report(`[${vp.name}] Admin variantes/stock del producto`, nVar > 0, `variantes=${nVar}`);
    await shot(admin, `${vp.name}-07-admin-producto`);
    await admin.locator('text=Cancelar').first().click().catch(() => {});

    // Pedidos + cambio de estado
    await gotoApp(admin, `${BASE}/admin/pedidos`);
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
    await gotoApp(admin, `${BASE}/admin/catalogos`);
    const nSecc = await admin.locator('.admin-form').count();
    report(`[${vp.name}] Admin catálogos (categorías/talles/colores)`, nSecc >= 3, `secciones=${nSecc}`);

    // Cliente SIN permisos no debe ver el admin
    await gotoApp(admin, `${BASE}/login`);
    await ctxAdmin.close();

    const ctxCliente = await browser.newContext({ viewport: { width: vp.width, height: vp.height } });
    const cliente = await ctxCliente.newPage();
    await gotoApp(cliente, `${BASE}/login`);
    await cliente.fill('input[autocomplete="email"]', email);
    await cliente.fill('input[autocomplete="current-password"]', password);
    await cliente.click('button[type="submit"]');
    await cliente.waitForTimeout(1500);
    await gotoApp(cliente, `${BASE}/admin`);
    const sinPermiso = await cliente.locator('text=No tenés permisos').count()
      + await cliente.locator('text=Sección restringida').count();
    report(`[${vp.name}] Cliente sin rol no ve panel admin`, sinPermiso > 0, `bloqueos=${sinPermiso}`);
    await shot(cliente, `${vp.name}-10-admin-denegado`);
    await ctxCliente.close();

    // Errores de consola (excluyo avisos conocidos de Blazor/reconnect)
    // - blazor.websocket / net::ERR: avisos de reconexión.
    // - 404 de dotnet-browser-tools/hot-reload-settings.json: lo pide el Hot
    //   Reload del SDK en Development y responde 404 si no hay sesión de Hot
    //   Reload activa; no es un error de la aplicación.
    const ignorables = ['blazor.websocket', 'hot-reload-settings.json'];
    const soloHotReload = recursos404.length > 0
      && recursos404.every((u) => u.includes('hot-reload-settings.json'));

    const erroresReales = erroresConsola.filter((t) => {
      if (ignorables.some((i) => t.includes(i))) return false;
      if (soloHotReload && t.includes('status of 404')) return false;
      return true;
    });
    report(`[${vp.name}] Sin errores de consola`, erroresReales.length === 0,
      erroresReales.slice(0, 2).join(' | ').slice(0, 120));

    await context.close();
  }

  // El formulario de imágenes se prueba una sola vez (no depende del viewport).
  await pruebasImagenesAdmin(browser);

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
