# Cómo levantar TODO el proyecto — Velvet Rose Boutique

Guía para abrir el proyecto completo (API + Web + app Android + app Windows) **abriendo una consola de PowerShell por cada pieza**.

> **Orden obligatorio:** primero la API (todo depende de ella), después la Web, después el emulador, y al final las apps.
> Si salteás el orden, la Web y las apps arrancan pero muestran pantallas vacías / errores de conexión.

**Carpeta raíz del repo (usala en todas las consolas):**

```powershell
cd C:\Users\mateo\source\repos\TiendaRopa
```

---

## Consola 0 — Requisito previo: SQL Server encendido

No hace falta dejarla abierta, es sólo una comprobación (1 vez por sesión).

```powershell
Get-Service | Where-Object { $_.Name -like "MSSQL*" } | Select-Object Name, Status
```

Resultado esperado: `MSSQLSERVER` con `Status = Running`.

Si estuviera `Stopped`:

```powershell
Start-Service MSSQLSERVER
```

> Ojo: la cadena de conexión es `Server=localhost;Database=VelvetRoseDev;Trusted_Connection=True`.
> **No es LocalDB.** Si `MSSQLSERVER` no está corriendo, la API falla al arrancar.

---

## Consola 1 — Proyecto backend API

Esta es la base de todo: la Web y las dos apps le pegan a ella.

```powershell
dotnet run --project TiendaRopa.Api --launch-profile http
```

- Escucha en **http://localhost:5100**
- La primera vez tarda: aplica migraciones y corre los 3 seeders idempotentes.
- **Dejá esta consola abierta.** Si la cerrás, se cae la API.

Verificación (en **otra** consola, o mirá el log de esta):

```powershell
(Invoke-WebRequest "http://localhost:5100/api/categorias" -UseBasicParsing).StatusCode   # → 200
```

Credenciales de desarrollo (ya sembradas en la BD):

| Rol | Email | Contraseña |
|---|---|---|
| Administrador | `admin@velvetrose.dev` | `Velvet123!` |
| Cliente | `cliente@velvetrose.dev` | `Velvet123!` |

---

## Consola 2 — Proyecto web (Blazor)

Necesita que la API ya esté escuchando. La URL de la API sale de `TiendaRopa.Web/appsettings.json` → `Api:BaseUrl = http://localhost:5100` (no hay que tocar nada).

```powershell
dotnet run --project TiendaRopa.Web --launch-profile http
```

- Escucha en **http://localhost:5101** y abre el navegador solo.
- **Dejala abierta.**

Verificación:

```powershell
(Invoke-WebRequest "http://localhost:5101/" -UseBasicParsing).StatusCode   # → 200
```

> El perfil `https` también existe, pero usá `http` para evitar líos de certificados.

---

## Consola 3 — Emulador Android

**No reinstales nada:** SDK, AVDs y JDK ya están configurados. Ver `emuladorAndroid.md`.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\android-emulator.ps1
```

Ese script hace todo: comprueba si ya hay un emulador encendido, si no lo arranca (`pixel_9_pro_-_api_36_0`) y espera al boot.

- Salida esperada: **`BOOT_OK`** y **`PORT_OPEN`**.
- **Dejá esta consola y la ventana del celular abiertas.** Si cerrás la ventana, se apaga el emulador.

Alternativa manual (si preferís hacerlo a mano):

```powershell
& "C:\Program Files (x86)\Android\android-sdk\emulator\emulator.exe" -avd pixel_9_pro_-_api_36_0 -netdelay none -netspeed full
```

```powershell
& "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe" wait-for-device shell 'while [ "$(getprop sys.boot_completed)" != "1" ]; do sleep 1; done; echo BOOT_OK'
```

Comprobación de que el emulador ve la API (el emulador llega al host por `10.0.2.2`):

```powershell
cmd /c "adb shell nc -z -w 5 10.0.2.2 5100 && echo PORT_OPEN"
```

---

## Consola 4 — App móvil (Android)

Requiere Consola 1 (API) y Consola 3 (emulador) activas. La app usa `http://10.0.2.2:5100`, configurado en `MauiProgram.cs`.

**Paso 1 — compilar e instalar en el emulador:**

```powershell
cd C:\Users\mateo\source\repos\TiendaRopa
dotnet build TiendaRopa\TiendaRopa.csproj -f net10.0-android -t:Install
```

**Paso 2 — lanzar la app:**

```powershell
& "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe" shell monkey -p com.companyname.tiendaropa -c android.intent.category.LAUNCHER 1
```

> ⚠️ **Nunca uses** `am start -n com.companyname.tiendaropa/...`: la activity es un hash `crc64...` que cambia cada vez que recompilás.

**Ver el log de la app (útil si algo falla):**

```powershell
& "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe" logcat -d | Select-String "FATAL|AndroidRuntime|mono-|MonoDroid|Unhandled|Exception"
```

> Si cambiás código y la sesión sigue viva, en VS Code usá Hot Reload (comando `.NET MAUI: Hot Reload`) en vez de repetir el build completo.

---

## Consola 5 — App Windows (MAUI, WinUI)

Requiere Consola 1 (API) activa. Usa `http://localhost:5100`.

**Paso 1 — compilar:**

```powershell
cd C:\Users\mateo\source\repos\TiendaRopa
dotnet build TiendaRopa\TiendaRopa.csproj -f net10.0-windows10.0.19041.0
```

**Paso 2 — lanzar la app:**

```powershell
Start-Process "C:\Users\mateo\source\repos\TiendaRopa\TiendaRopa\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TiendaRopa.exe"
```

- Dejala corriendo; cerrá la ventana para salir.
- En el log de la API (Consola 1) deberían verse `GET /api/categorias → 200` y `GET /api/productos → 200` cuando la app carga.

---

## Consola 6 (opcional) — Pruebas automatizadas

No son necesarias para "usar" el proyecto, pero sirven para verificar todo de una.

```powershell
cd C:\Users\mateo\source\repos\TiendaRopa
```

| Qué prueba | Comando | Requiere |
|---|---|---|
| API (47 pruebas) | `powershell -ExecutionPolicy Bypass -File scripts\api-tests.ps1` | Consola 1 |
| SQL Server (40 pruebas) | `powershell -ExecutionPolicy Bypass -File scripts\sql-verify.ps1` | SQL Server encendido |
| Web con Playwright (66 pruebas) | `node scripts\web-tests.mjs` | Consolas 1 y 2 + `npm install playwright` |
| Capturas Android | `powershell -ExecutionPolicy Bypass -File scripts\adb-evidence.ps1` | Consolas 3 y 4 |

Las evidencias se guardan en `test-evidence/` (está en `.gitignore`).

---

## Resumen rápido (cheat sheet)

| Consola | Qué abre | Comando corto | Puerto / destino |
|---|---|---|---|
| 0 | SQL Server (check) | `Start-Service MSSQLSERVER` | `localhost` / MSSQLSERVER |
| 1 | **API backend** | `dotnet run --project TiendaRopa.Api --launch-profile http` | http://localhost:5100 |
| 2 | **Web Blazor** | `dotnet run --project TiendaRopa.Web --launch-profile http` | http://localhost:5101 |
| 3 | **Emulador Android** | `powershell -ExecutionPolicy Bypass -File scripts\android-emulator.ps1` | AVD `pixel_9_pro_-_api_36_0` |
| 4 | **App Android** | `dotnet build TiendaRopa\TiendaRopa.csproj -f net10.0-android -t:Install` + `adb shell monkey ...` | `10.0.2.2:5100` |
| 5 | **App Windows** | `dotnet build TiendaRopa\TiendaRopa.csproj -f net10.0-windows10.0.19041.0` + `Start-Process ...\win-x64\TiendaRopa.exe` | `localhost:5100` |

---

## Problemas típicos (y qué hacer)

| Síntoma | Causa / solución |
|---|---|
| Consola 1: error de conexión a base de datos | `MSSQLSERVER` apagado → `Start-Service MSSQLSERVER`. La cadena de conexión **no** usa LocalDB. |
| Web carga pero catálogo vacío / errores 500 | La API no está corriendo en `5100` o arrancó después. Arrancá la Consola 1 primero. |
| `adb` no se reconoce como comando | Terminal abierta antes de que se escribieran las variables de entorno. Abrí una consola nueva o usá la ruta absoluta `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`. |
| App Android se abre y no trae datos | El emulador no llega al host. Verificá `cmd /c "adb shell nc -z -w 5 10.0.2.2 5100 && echo PORT_OPEN"`. |
| `screencap` devuelve una imagen negra | Pantalla del emulador dormida. Despertala: `adb shell input keyevent KEYCODE_WAKEUP` + `adb shell input keyevent 82`. |
| App Android no reacciona al relanzar | Activity con hash distinto. Usá `monkey` (Paso 2 de la Consola 4), nunca `am start -n …`. |
| Puerto 5100 / 5101 ocupado | `Get-NetTCPConnection -LocalPort 5100,5101 -State Listen` y cerrá el proceso dueño, o reabrí la consola correspondiente (duplicaste una). |
| App Windows abre y cierra | Falta la API (`localhost:5100`) o el build se hizo sin `-f net10.0-windows10.0.19041.0`. |
| iOS / MacCatalyst | **Requieren macOS.** No se puede compilar ni probar en Windows. |
