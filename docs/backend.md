# Backend Velvet Rose Boutique — Incrementos 1 y 2

Fecha: 2026-10-01. Estado: **incremento 2 verificado end-to-end** (resultados abajo).

## 1. Arquitectura

```
TiendaRopa.Contracts  (net10.0, sin dependencias)
   ├── Catalogo/   DTOs + CatalogoQuery + ICatalogoService
   ├── Identidad/  Registro/Login/AuthResponse/UsuarioActual + IAuthService
   ├── Pedidos/    CrearPedido/PedidoDto/PedidoDetalle + IPedidoService
   ├── Admin/      Producto/Pedido/Catálogo DTOs + IAdminService
   ├── Referenciado por TiendaRopa.Api  (backend)
   └── Referenciado por TiendaRopa.Shared (UI compartida → Web y MAUI)

TiendaRopa.Api        (ASP.NET Core Web API, controladores, .NET 10)
   ├── Auth/         TokenService (JWT), Roles, UsuarioActual + handler de rol
   ├── Controllers/  Catálogo (GET) + Auth + Pedidos + Admin (/api/admin/*)
   ├── Services/     AuthService, PedidoService (transacciones), AdminService
   ├── Data/         DbContext, migraciones, DevelopmentSeeder (+ usuarios e imágenes)
   └── EF Core 10.0.12 + SQL Server (localhost/MSSQLSERVER en desarrollo)

TiendaRopa.Shared     → servicios HTTP (catálogo/auth/pedidos/admin),
                        SesionService (JWT), CarritoService (persistente),
                        PreferenciasLocal (IPreferenciasLocal → localStorage),
                        páginas: detalle, carrito, mis pedidos, admin/*
TiendaRopa.Web        → host Blazor Server (interactive server)
TiendaRopa            → host MAUI Blazor Hybrid (Android / Windows / iOS / MacCatalyst)
```

- El backend **no** referencia `TiendaRopa.Shared` (la biblioteca visual).
- `TiendaRopa.Contracts` es la única biblioteca compartida entre API y clientes.
- El frontend **nunca** recibe entidades EF: sólo DTOs.
- La autorización se decide en backend con el JWT; el frontend sólo evita mostrar UI inútil.

## 2. Modelo de datos y convención (sin cambios respecto del incremento 1)

| Tema | Decisión |
|---|---|
| Nombres en C# | PascalCase (`IdProducto`, `PrecioBase`) |
| Clave primaria / FK | `Id<Rol>` / `Id<Relacion>` |
| Tablas | Singulares (`Producto`, `ProductoVariante`, …) |
| Decimal | `decimal(18, 2)` en todos los precios/totales |
| Fechas | `datetime2`, valores UTC |
| Borrado | `Restrict` con historial (Cliente→Pedido, Variante→PedidoDetalle); `Cascade` en catálogo |
| Stock | `CK_ProductoVariante_Stock_NoNegativo` + índice único `(IdProducto, IdTalle, IdColor)` |
| Imágenes | índice único parcial `IX_ProductoImagen_IdProducto` `WHERE Principal = 1` |
| Usuarios | índice único de `Email`; hash con `PasswordHasher<Usuario>` (PBKDF2) |

Migración única: `20260926205250_InitialCreate` (sin cambios de esquema en el incremento 2).

## 3. Conexión a SQL Server — **LocalDB dejó de usarse**

- Desarrollo: **`Server=localhost;Database=VelvetRoseDev;Trusted_Connection=True;TrustServerCertificate=True`**
  en `TiendaRopa.Api/appsettings.Development.json` (autenticación Windows integrada,
  **sin credenciales en el repo**). `DesignTimeDbContextFactory` usa la misma cadena
  como fallback para las herramientas EF.
- Instancia real verificada: `localhost` / instancia predeterminada **MSSQLSERVER**
  (Windows Auth `Markhan\mateo`, motor 17.0.1135.8). `(localdb)\MSSQLLocalDB` y
  `MSSQLSERVER01` **ya no se usan**.
- Fuera de Development: variable `ConnectionStrings__VelvetRose` (producción).
- Arranque en Development: `DevelopmentSeeder` (migración + datos) →
  `DevelopmentUserSeeder` (admin/cliente de dev) → `DevelopmentImageSeeder`
  (imágenes de desarrollo). Los tres son idempotentes.
- Nota histórica: en el incremento 1 la base estaba en LocalDB porque el servicio
  `MSSQLSERVER` no podía iniciarse; el servicio ya funciona y la base se recreó
  con las mismas migraciones EF (no se copiaron `.mdf/.ldf`).

## 4. Endpoints

### Catálogo (públicos)
| Ruta | Descripción |
|---|---|
| `GET /api/productos` | Productos activos; paginación, filtros y orden (ver incremento 1) |
| `GET /api/productos/{id}` | Ficha completa con imágenes (orden + principal) |
| `GET /api/productos/{id}/variantes}` | Variantes talle/color/HEX/stock/disponible |
| `GET /api/categorias` | Categorías activas |

### Autenticación
| Ruta | Auth | Descripción |
|---|---|---|
| `POST /api/auth/registro` | anónimo | Alta de cliente (rol Cliente) → `201` + JWT. `409` email duplicado, `400` validaciones |
| `POST /api/auth/login` | anónimo | → `200` + JWT (12 h) o `401` |
| `GET /api/auth/me` | Bearer | Usuario del token vigente; `401` sin token |

### Pedidos (Bearer)
| Ruta | Descripción |
|---|---|
| `POST /api/pedidos` | Confirma el pedido → `201`; `409` si falta stock (sin descuento parcial); `401` |
| `GET /api/pedidos` | Pedidos **propios** (se resuelven desde el JWT, nunca desde un IdCliente del cliente) |
| `GET /api/pedidos/{id}` | Detalle propio (`404` si es ajeno o inexistente) |
| `POST /api/pedidos/{id}/cancelar` | Cancelación propia → `200`; `409` si no corresponde; `404` si es ajeno |

### Administración (Bearer + rol Administrador, política `SoloAdministrador`)
| Ruta | Descripción |
|---|---|
| `GET/POST/PUT /api/admin/productos`, `DELETE /api/admin/productos/{id}` | CRUD + baja lógica (`Activo=false`) |
| `PUT /api/admin/productos/{id}/imagenes` | Reemplaza imágenes (orden + principal) |
| `GET/POST/PUT/DELETE /api/admin/categorias`, `/talles`, `/colores` | Tablas de referencia (alta/edición/desactivación) |
| `POST/PUT /api/admin/variantes[{id}]` | Variantes: rechaza duplicado producto+talle+color (`409`) y stock negativo (`400`) |
| `GET /api/admin/pedidos[?estado=]` | Listado con transiciones permitidas por pedido |
| `GET /api/admin/pedidos/{id}` | Detalle con líneas |
| `PUT /api/admin/pedidos/{id}/estado` | Cambio de estado aplicando las transiciones (`409` si no corresponde) |

Sin token → `401`; cliente en un endpoint admin → `403` (handler `AdministradorHandler` sobre el claim `rol` del JWT).

## 5. Autenticación y roles

- **JWT simétrico (HS256)** emitido por `TokenService`. En Development, si no hay
  `Auth:SigningKey`, se genera una clave **efímera por proceso** → no hay secretos
  en el repositorio y los tokens viven sólo mientras vive la API de desarrollo.
  En producción se configura con la variable `Auth__SigningKey`.
- Claims: `sub`, `email`, `idUsuario`, `nombre`, `apellido`, `rol` e `idCliente`.
- **Hash de contraseñas**: `PasswordHasher<Usuario>` de ASP.NET Core (PBKDF2 + salt).
  Nunca texto plano (verificado directamente en SQL: 0 coincidencias).
- Entidades existentes sin modelos paralelos: `Usuario`, `Rol`, `Cliente`.
- Roles: `Administrador` y `Cliente` (seed). La autorización de endpoints usa la
  política `SoloAdministrador` sobre el claim `rol`; **nunca** se confía en un
  `IdCliente` enviado por el frontend (el usuario se resuelve desde el JWT y su
  `Cliente` se busca en backend).
- **Credenciales de desarrollo documentadas (sólo local)**:
  - `admin@velvetrose.dev` / `Velvet123!` → Administrador
  - `cliente@velvetrose.dev` / `Velvet123!` → Cliente
- No se implementa todavía OAuth externo (Google/Apple).

## 6. Pedidos y política de stock

**El carrito no reserva ni descuenta stock.** Al confirmar (`POST /api/pedidos`):

1. Se abre una transacción (dentro de `CreateExecutionStrategy`, porque la API
   usa `EnableRetryOnFailure` y EF rechaza transacciones directas con esa estrategia).
2. Se releen las variantes desde SQL Server.
3. Cada descuento es un **UPDATE condicional atómico**:
   `UPDATE ProductoVariante SET Stock = Stock - @c WHERE Id = @id AND Stock >= @c`.
4. Si alguna línea afecta 0 filas (stock insuficiente) se revierte **la transacción
   completa**: no hay descuento parcial.
5. Se crea `Pedido` + `PedidoDetalle` con el precio vigente de ese momento y se
   confirma.

**Concurrencia (dos pedidos sobre el mismo stock):** el `UPDATE … WHERE Stock >= @c`
es atómico en SQL Server: la segunda solicitud afecta 0 filas y se revierte completa.
`CK_ProductoVariante_Stock_NoNegativo` actúa como red de seguridad final.
*Limitación conocida (ver deuda técnica):* se lee el stock informando en el mensaje,
pero el precio se toma de la variante leída antes del descuento — en la práctica el
UPDATE condicional garantiza la cantidad y el CHECK garantiza no negatividad.

**Cancelación:** sólo desde `Pendiente | Confirmado | Preparado` (los estados que
descontaron stock). El cambio de estado también es un `UPDATE … WHERE IdEstadoPedido
IN (…)` dentro de la misma transacción que restituye el stock: un segundo intento
afecta 0 filas y **el stock no se restituye dos veces** (verificado: 1 → 2 una sola vez).

**Transiciones válidas** (`PedidoService.Transiciones`):

```
Pendiente  → Confirmado | Cancelado
Confirmado → Preparado  | Cancelado
Preparado  → Entregado  | Cancelado
Entregado  → (fin)
Cancelado  → (fin)
```

Estados sembrados: Pendiente, Confirmado, Preparado, Entregado, Cancelado.

## 7. Carrito (política del cliente)

- `CarritoService` en `TiendaRopa.Shared`: agregar, quitar, aumentar/disminuir
  cantidad, vaciar, total y cantidad total. Cada línea conserva
  `IdProducto, IdProductoVariante, Nombre, Imagen, Talle, Color, Cantidad,
  PrecioUnitario, Subtotal`.
- Se persiste en el almacenamiento local del host detrás de la abstracción
  `IPreferenciasLocal` (localStorage del navegador y del WebView): `TiendaRopa.Shared`
  **no** referencia APIs de MAUI ni del navegador directamente.
- **Agregalar carrito NO toca SQL Server** (verificado: sin cambios de stock en BD
  tras agregar). La validación definitiva es la del backend al confirmar.
- El precio del carrito es informativo; el precio real se fija en backend al confirmar.

## 8. Imágenes de productos (ProductoImagen)

- Soporte completo: múltiples imágenes por producto, una principal (índice único
  parcial) y orden.
- **Activos locales de desarrollo** generados por `scripts/gen-product-images.ps1`
  (27 PNG × 3 vistas para los 9 productos seed; no se descargan fotos de Internet).
  Los mismos PNG se empaquetan en `TiendaRopa.Api/wwwroot/img/productos/` (Web) y
  `TiendaRopa/wwwroot/img/productos/` (MAUI).
- La BD guarda **rutas relativas** (`/img/productos/p1-1.png`); el cliente las
  resuelve con `AssetsBase`:
  - Blazor Web → absolutas contra la API (`http://localhost:5100`);
  - MAUI → relativas al **origin local del WebView**. En Android el WebView sirve
    la app desde `https://0.0.0.1` y bloquearía como *mixed content* una imagen
    HTTP de la API; sirviéndola desde el propio paquete se evita el problema.
- Los DTOs (`UrlImagen`) están preparados para migrar a hosting/CDN externo sin
  tocar la UI: basta con cambiar `AssetsBase`.

## 9. Integración con la UI (incremento 2)

- Páginas nuevas en `TiendaRopa.Shared`:
  - `/producto/{id}` — detalle con galería (3 vistas, principal, thumbs), talles y
    colores **sólo combinaciones existentes**, bloqueo de variantes con `Stock = 0`,
    precio/disponibilidad por variante, cantidad limitada al stock, agregar al carrito.
  - `/carrito` — carrito real con cantidades, resumen y "Confirmar pedido"
    (si no hay sesión → `/login?redirect=/carrito`).
  - `/mis-pedidos` — lista con estado, detalle y cancelación con confirmación.
  - `/admin`, `/admin/productos`, `/admin/pedidos`, `/admin/catalogos` — panel completo.
  - Login/Registro conectados a la API (JWT + redirect por rol y por `?redirect=`).
- `MainLayout`: "Mis pedidos", "Administración" (sólo rol Administrador) y
  "Cerrar sesión" en cajón (móvil) y sidebar (escritorio).
- Servicios registrados en `TiendaRopa.Web/Program.cs` y `TiendaRopa/MauiProgram.cs`:
  `HttpClient` único (cabecera Bearer por request vía `ApiHttp.Preparar`),
  `SesionService`, `CarritoService`, `IPreferenciasLocal`.
- `ProductCard` enlaza al detalle (`Href`) cuando la tarjeta no ofrece `OnAdd`.

## 10. URLs de la API por plataforma (sin cambios)

| Cliente | URL |
|---|---|
| Web (server-side) | `Api:BaseUrl = http://localhost:5100` |
| MAUI Windows | `http://localhost:5100` |
| MAUI Android (emulador) | `http://10.0.2.2:5100` (`usesCleartextTraffic` sólo en desarrollo) |

## 11. Resultados de verificación (reales, 2026-10-01)

Compilado ≠ probado ≠ ejecutado. Todo lo de abajo se ejecutó de verdad:

### Compilación
- `dotnet build` Api, Web, MAUI `net10.0-windows10.0.19041.0` y MAUI
  `net10.0-android`: **0 errores, 0 warnings de C#**.
  (Quedan warnings CA1416 **preexistentes** en `Platforms/Android/MainActivity.cs`,
  código del incremento 1 que no se modificó en este incremento.)
- iOS/MacCatalyst: **no compilados ni probados** (requieren macOS).

### Pruebas de API — `scripts/api-tests.ps1` → **47 PASS / 0 FAIL**
Registro válido/duplicado/inválido; login correcto/incorrecto; usuarios seed
(admin con rol Administrador, cliente con rol Cliente); hash sin texto plano;
`/me` con y sin token; admin sin token → 401; cliente → admin → 403; admin → 200;
detalle y variantes; **agregar al carrito no altera SQL Server**; crear pedido
(201) con descuento verificado en BD; pedir más stock → 409 sin descuento;
pedido mixto (una variante sin stock) → 409 sin descuento parcial (suma de stock
igual antes/después); listar pedidos propios; pedido ajeno → 404; cancelar → 200
con stock restituido; segunda cancelación → 409 sin doble restitución; CRUD
admin (producto crear/editar/baja lógica, variante crear, duplicado → 409,
stock negativo → 400, categoría/talle/color, listado de pedidos); transiciones
válidas e inválidas (Pendiente→Confirmado→Preparado→Entregado correctas,
Confirmado→Entregado → 409, cancelar Entregado → 409, cliente → 403); identidad
de la instancia (no LocalDB); limpieza de datos de prueba al final.

### Pruebas directas de SQL Server — `scripts/sql-verify.ps1` → **40 PASS / 0 FAIL**
`@@SERVERNAME`/`SUSER_SNAME`/versión; `VelvetRoseDev` en la instancia correcta;
12 tablas; migración `InitialCreate`; 11 FK; 2 CHECK (`Stock >= 0`, `Cantidad > 0`);
índice único producto+talle+color; índice único de email; índice único parcial
de imagen principal; seed (4+ categorías, talles, colores, 5 estados, 2 roles,
9 productos, 26+ variantes); 28 usuarios con hash `AQAAAA…` (PBKDF2), 0 en texto
plano; admin/cliente con sus roles; 0 stock negativo; pedidos con Total = Σ subtotales
y Subtotal = precio × cantidad; 0 pedidos sin detalle; 0 huérfanos (variante,
cliente, producto).

### Pruebas Web — `scripts/web-tests.mjs` (Playwright) → **66 PASS / 0 FAIL**
Recorrido completo en **390 / 768 / 1440 px**: registro → catálogo (9 productos) →
detalle (talles/colores/precio) → agregar al carrito → carrito con el producto →
login (correcto e incorrecto) → confirmar pedido → Mis Pedidos con el nuevo pedido
→ cerrar sesión; admin: login, panel (4 secciones), productos (15), variantes,
pedidos con cambio de estado, catálogos; cliente sin rol bloqueado en `/admin`;
0 errores de consola. Capturas en `test-evidence/web/` (gitignored).
*Nota:* la suite repone stock al inicio (`reponerStock`) porque los pedidos
descuentan stock real.

### Pruebas Android (emulador real + ADB) — ejecutado
- Emulador `emulator-5554` (API 36): `BOOT_OK`, `nc 10.0.2.2 5100` → `PORT_OPEN`.
- App instalada con `dotnet build -f net10.0-android -t:Install` y lanzada con
  `monkey` (la activity es hash `crc64…`).
- Recorrido real (UI automatizada con `uiautomator dump` + ADB Keyboard para el
  texto, helpers en `scripts/android-ui.ps1`):
  Inicio → catálogo → **detalle** (galería, "Últimas unidades: 2", talles/colores) →
  seleccionar variante → **agregar al carrito** (aviso visible) → carrito con
  producto y resumen → "Confirmar pedido" → **redirect a login** → **registro**
  completo (4 campos + términos) con usuario creado en BD (rol Cliente) →
  **login** (drawer con "Mis pedidos"/"Cerrar sesión") → confirmar pedido →
  **pedido #23 creado** → cancelar → **Cancelado + stock restituido (1 → 2)** →
  **logout** → carrito vacío (empty state).
- **Los requests llegan a la API por `http://10.0.2.2:5100`** (log de
  `TiendaRopa.Api`: `POST /api/pedidos → 201`, `GET /api/pedidos → 200`, etc.).
- Evidencia SQL del pedido: #23 Pendiente→Cancelado, total 89990, stock 2→1→2.
- logcat: **0 excepciones** (`FATAL|AndroidRuntime|mono|Unhandled`).
- Capturas en `test-evidence/android/` (gitignored).

### Pruebas MAUI Windows — ejecutado
- Ejecutado el `win-x64\TiendaRopa.exe` real (no sólo compilación).
- La app llamó a la API: `GET /api/categorias → 200`, `GET /api/productos → 200`.
- Navegación real por clics en la ventana (helpers `scripts/windows-ui.ps1`):
  **Inicio** (captura con imágenes de novedades) → **Catálogo**
  (`GET /api/productos?page=1&pageSize=24 → 200` + captura con scroll y tarjetas)
  → **Detalle** (`GET /api/productos/9 → 200` y `/variantes → 200`) →
  **Login** (captura del panel Auth completo) → **Carrito** (empty state).
- Evidencia en `test-evidence/windows/` (gitignored).

### Verificación visual
- Capturas Web (3 viewports), Android y Windows revisadas: identidad Velvet Rose
  (Playfair/Inter, paleta, mobile-first, safe areas, bottom nav) intacta.
- Contraste: textos `#2B1D2F`/`#41284C` sobre marfil `#F8F4ED` y botones blancos
  sobre `#C51E3A` — verificado en capturas reales.

## 12. Scripts de prueba

| Script | Qué hace |
|---|---|
| `scripts/api-tests.ps1` | Suite de API (47 pruebas) + limpieza de datos de prueba |
| `scripts/sql-verify.ps1` | Verificación directa de SQL Server (40 pruebas) |
| `scripts/web-tests.mjs` | Suite Playwright en 3 viewports (66 pruebas) |
| `scripts/android-ui.ps1` | Helpers de UI Android (dump/tap/scroll/captura) |
| `scripts/windows-ui.ps1` | Helpers de UI Windows (clic/scroll/captura DPI-aware) |
| `scripts/gen-product-images.ps1` | Genera los PNG de desarrollo (2 destinos) |

Requisitos: API `:5100` y Web `:5101` corriendo; Playwright
(`npm install playwright && npx playwright install chromium`); PowerShell 5.1
los `.ps1` van **con BOM UTF-8** (si no, rompen con acentos).

## 13. Pendientes / deuda técnica

- **HTTPS en producción**: en Android la app se sirve desde `https://0.0.0.1` y
  las imágenes viven en el paquete; las llamadas API van por HTTP con
  `usesCleartextTraffic` sólo en Debug. Para producción: API con HTTPS y revisar
  `Auth__SigningKey`.
- **Precio en el pedido**: se lee la variante antes del UPDATE condicional; para
  un escenario de edición concurrente de precios convendría leer el precio dentro
  de la misma transacción con lock (hoy la cantidad está protegida, el precio no).
- **Imágenes**: son placeholders generados; sustituir por fotos con derechos
  (el contrato y la UI ya lo soportan) y migrar `AssetsBase` a un CDN.
- **Android físico**: URL LAN + revisar `usesCleartextTraffic`.
- **iOS / MacCatalyst**: compatibilidad de código mantenida, **no compilados ni
  probados** (no hay macOS en el entorno).
- `git status` incluye muchos `bin/obj` trackeados del incremento 0 (pendiente de
  `git rm -r --cached` en un commit dedicado, no hecho por impacto).
- Recuperación de contraseña y "mostrar contraseña" futuros; OAuth externo fuera
  del MVP (ver lista del enunciado).

## 14. Próximo incremento recomendado

1. HTTPS + variables de entorno de producción (requiere decisión de hosting).
2. Editor de imágenes en `/admin/productos` con la API `PUT …/imagenes` ya existente
   (hoy las imágenes se cargan por seeder).
3. Recuperación de contraseña y gestión de perfil de cliente.
4. Commit de limpieza de `bin/obj` del historial git + CI con las tres suites.
