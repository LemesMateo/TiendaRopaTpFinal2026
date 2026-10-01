# Backend Velvet Rose Boutique — Incremento 1 (API + catálogo real)

Fecha: 2026-09-26. Estado: **verificado end-to-end en este entorno** (resultados abajo).

## 1. Arquitectura

```
TiendaRopa.Contracts  (net10.0, sin dependencias)
   ├── DTOs + CatalogoQuery + ICatalogoService
   ├── Referenciado por TiendaRopa.Api  (backend)
   └── Referenciado por TiendaRopa.Shared (UI compartida → Web y MAUI)

TiendaRopa.Api        (ASP.NET Core Web API, controladores, .NET 10)
   ├── Entities/ (12 entidades del DER)
   ├── Data/     (DbContext, migraciones, DevelopmentSeeder)
   ├── Controllers/ (GET de catálogo)
   └── EF Core 10.0.12 + SQL Server (LocalDB en desarrollo)

TiendaRopa.Shared     → Services/HttpCatalogoService + CatalogoPresentation
TiendaRopa.Web        → DI con Api:BaseUrl (appsettings)
TiendaRopa.Maui       → DI con URL por plataforma (#if ANDROID)
```

- El backend **no** referencia `TiendaRopa.Shared` (la biblioteca visual).
- `TiendaRopa.Contracts` es la única biblioteca compartida entre API y clientes.
- El backend conserva los proyectos y el diseño existentes: no se tocó ningún
  componente visual, layout ni CSS salvo lo estrictamente necesario en las dos
  páginas integradas (maquetación intacta).

## 2. Modelo de datos y convención

Convención acordada (antes de migrar):

| Tema | Decisión |
|---|---|
| Nombres en C# | PascalCase (`IdProducto`, `PrecioBase`) |
| Clave primaria | `Id<Rol>` (coincide con `idXxx` del documento de propuesta) |
| Clave foránea | `Id<Relacion>` con el nombre de la entidad referenciada |
| Tablas | Singulares (`Producto`, `ProductoVariante`, …) vía `ToTable` |
| Decimal | `decimal(18, 2)` en todos los precios/totales |
| Fechas | `datetime2`, valores UTC en `FechaAlta`/`FechaPedido` |
| Borrado | `Restrict` en las relaciones con datos históricos (Cliente→Pedido, Talle/Color→Variante, Variante→PedidoDetalle); `Cascade` en catálogo (Producto→Imágenes/Variantes) |
| Stock | `CK_ProductoVariante_Stock_NoNegativo` (`Stock >= 0`) + índice único `(IdProducto, IdTalle, IdColor)` |
| Imágenes | índice único parcial: una sola imagen principal por producto (`WHERE Principal = 1`) |

Migración inicial: `TiendaRopa.Api/Data/Migrations/*_InitialCreate.cs`
(creada con `dotnet ef migrations add InitialCreate`).

## 3. Conexión a SQL Server (sin secretos en el repo)

- Desarrollo: **LocalDB** `Server=(localdb)\MSSQLLocalDB;Database=VelvetRoseDev;Trusted_Connection=True;TrustServerCertificate=True`
  en `TiendaRopa.Api/appsettings.Development.json` — autenticación integrada
  de Windows, **no contiene credenciales**.
- Fuera de Development: `ConnectionStrings__VelvetRose` (variable de entorno)
  o user-secrets. `appsettings.json` (producción) no incluye cadena.
- Arranque en Development: `DevelopmentSeeder` ejecuta `MigrateAsync` + carga
  datos de prueba sólo si la tabla `Categoria` está vacía (idempotente).
  En otros entornos las migraciones se aplican manualmente
  (`dotnet ef database update --project TiendaRopa.Api`).

Nota del entorno: el SQL Server de instancia (`MSSQLSERVER`) está detenido y
sin permisos de servicio; `SQLSERVER01` falla por Kerberos/SSPI en loopback y
LocalDB no arrancaba → se recreó la instancia con
`sqllocaldb delete/create MSSQLLocalDB`. Si LocalDB se detiene:
`sqllocaldb start MSSQLLocalDB`.

## 4. Endpoints (GET) + OpenAPI

| Ruta | Descripción |
|---|---|
| `GET /api/productos` | Productos **activos** paginados. Parámetros: `page` (≥1), `pageSize` (1–50), `categoria`, `buscar`, `talle`, `color`, `precioMin`, `precioMax`, `orden` (`relevancia`\|`precio-asc`\|`precio-desc`\|`nombre`) |
| `GET /api/productos/{id}` | Ficha completa (imágenes, descripción). `404` si no existe o está inactivo |
| `GET /api/productos/{id}/variantes` | Variantes con talle/color/HEX/stock/precio/`disponible`. `404` si el producto no existe |
| `GET /api/categorias` | Categorías activas en orden de aparición |
| `GET /openapi/v1.json` | Documentación OpenAPI |
| `GET /scalar/v1` | UI interactiva Scalar (Development) |

- Validaciones de query → `400` con `ProblemDetails` + `errors`.
- 404 → `ProblemDetails` con detalle del identificador.
- Precio a mostrar = menor precio de variante con precio propio, si no, `precioBase`
  (expresión `PrecioAMostrar` traducida a SQL por EF Core).
- CORS no aplica: la UI llama a la API **desde el servidor** (Blazor
  Interactive Server) o desde la app (MAUI), nunca desde el navegador.
- Sin HTTPS en la API de desarrollo (puerto 5100); producción deberá usarlo.

## 5. Datos de desarrollo

`Data/DevelopmentSeeder.cs`: 4 categorías, 4 talles, 4 colores, 5 estados de
pedido, 2 roles y **9 productos con variantes y stock** (incluye variantes con
`stock = 0` para probar disponibilidad). **Sin imágenes**: `urlImagen` queda
`null` a propósito hasta revisar derechos de uso de fotos (la UI muestra el
marcador con icono). Se identifica como desarrollo en el nombre/clase y sólo
corre en `Development`.

## 6. Integración con la UI

- `HttpCatalogoService` (Shared) implementa `ICatalogoService` con `HttpClient`
  y `BaseAddress` configurado en DI (singleton de larga vida, un solo cliente).
- `CatalogoPresentation.ToCardItem` mapea DTO → `ProductCardItem` y formatea
  precios `es-AR` (`$42.990`).
- `Home.razor`: categorías + 4 novedades en paralelo, con `LoadingState`,
  `EmptyState` de error con **Reintentar** y estado vacío.
- `Catalogo.razor`: mismos filtros visuales, ahora enviados a la API
  (búsqueda con debounce de 350 ms, `@bind:after` en selects, chips,
  `?cat=` de Inicio). Estados: carga / error+reintento / sin resultados.
- `Samples/SampleCatalog.cs` **ya no se usa en ninguna página** (verificado por
  grep); se conserva sólo como recurso de desarrollo.

### URLs de la API por plataforma

| Cliente | URL | Nota |
|---|---|---|
| Web (server-side) | `Api:BaseUrl` = `http://localhost:5100` | `appsettings.json` |
| MAUI Windows | `http://localhost:5100` | constante en `MauiProgram` |
| MAUI Android (emulador) | `http://10.0.2.2:5100` | `10.0.2.2` = host; `usesCleartextTraffic="true"` sólo en `AndroidManifest.xml` de desarrollo |
| Android físico | *pendiente* | usar la IP en LAN de la PC y verificar que la API escuche en la red |

## 7. Resultados de verificación (reales)

1. `dotnet build` **Api / Shared / Web**: 0 errores, 0 warnings.
2. `dotnet build` MAUI `-f net10.0-windows10.0.19041.0` y `-f net10.0-android`: 0 errores, 0 warnings.
3. `dotnet ef migrations add InitialCreate` + `dotnet ef database update` →
   base `VelvetRoseDev` con las 12 tablas + `__EFMigrationsHistory` (verificado con `sqlcmd`).
4. API corriendo y probada con `curl`:
   - `/api/categorias` → 4 categorías.
   - `/api/productos?page=1&pageSize=3` → `totalItems: 9`, `totalPages: 3`.
   - `?categoria=Vestidos&orden=precio-asc` → 3 vestidos ordenados 34.990 → 62.990.
   - `/api/productos/1` → ficha; `/api/productos/1/variantes` → 3 variantes
     (una con `stock: 0`, `disponible: false`).
   - `/api/productos/999` → `404` ProblemDetails; `?page=0` → `400` con `errors`.
   - `?buscar=abrig&talle=XL` → 1 producto (filtros combinados).
   - `/openapi/v1.json` → 200 (~14 KB).
5. Web (`http://localhost:5101`): catálogo muestra **"9 productos"** de la API
   (nombres distintos a `SampleCatalog`), chips desde `/api/categorias`,
   Inicio muestra 4 novedades reales; filtros de categoría/búsqueda/orden
   responden en servidor (verificado con Playwright).
6. MAUI Android: sólo compilación (sin emulador en el entorno).
7. MAUI Windows: **ejecutada** (app en proceso, ventana abierta) con logs de
   la API en Information: la app realizó `GET /api/categorias` y
   `GET /api/productos?page=1&pageSize=4&orden=relevancia` → ambos **200 OK**
   (las llamadas de Inicio), mientras el navegador no estaba en uso: prueba
   de que MAUI → API → SQL Server funciona end-to-end.

## 8. Pendientes / próximos incrementos

- Detalle de producto, variantes en UI, carrito y pedidos (los contratos ya
  existen: `GetProductoAsync`, `GetVariantesAsync`).
- Autenticación y roles (entidades `Rol`/`Usuario`/`Cliente` modeladas, sin
  endpoints todavía) — junto con hash de contraseñas.
- Panel administrativo y alta/edición de productos (requiere POST/PUT/DELETE).
- Imágenes de productos: definir activos con derechos y servirla
  (hoy `urlImagen = null` y la UI usa marcador).
- Android físico: URL LAN + revisar `usesCleartextTraffic` para producción (HTTPS).
- Política explícita de reserva/descuento de stock al confirmar pedidos
  (la restricción `Stock >= 0` ya está en la base).
