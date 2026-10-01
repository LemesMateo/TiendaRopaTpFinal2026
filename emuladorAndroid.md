# Emulador Android — Velvet Rose Boutique

> **Para qué sirve este archivo.** Cómo abrís el emulador de Android **a mano**
> y dejás todo preparado para que yo (el agente de VS Code) lo use en tiempo
> real para probar lo que programo — igual que uso el browser.
>
> Última verificación: **2026-09-30** · SDK, AVDs y rutas comprobados sobre
> esta máquina.

---

## 0. Cómo funciona la "extensión" del browser para Android

Con el browser tenés que compartirme la pestaña desde la UI de VS Code
( Share page ). Para Android **no hay que compartir nada por la UI**: mientras
el emulador esté encendido y `adb` lo vea, yo lo uso directo desde la terminal.

| | Browser | Emulador Android |
|---|---|---|
| Cómo lo "compartís" | Botón *Share page* | **Basta con que esté encendido** |
| Qué tenés que verificar | Que la pestaña aparece adjunta | `adb devices` muestra `device` |
| Qué puedo hacer yo | navegar, leer DOM, screenshot | screenshot, logcat, toques, swipes, instalar APK |
| Cómo "veo" la imagen | screenshot del browser | `adb exec-out screencap` → `view_image` |

**El único paso que necesitás de tu lado es encender el emulador y la API.**
El resto lo hago yo.

> ⚠️ **No hace falta** compartir ninguna pestaña ni ventana. Si me decís
> "el emulador está listo", yo compruebo el estado con `adb` y arranco.

---

## 1. Qué ya está instalado — **no reinstales nada**

Todo esto ya existe en la máquina. No instales SDKs, emuladores ni
plataformas nuevos, ni corras `sdkmanager --install`.

| Componente | Valor |
|---|---|
| **Android SDK** | `C:\Program Files (x86)\Android\android-sdk` |
| **adb** | `...\android-sdk\platform-tools\adb.exe` (v36.0.0-13206524) |
| **Emulador** | `...\android-sdk\emulator\emulator.exe` (v35.5.10.0) |
| **cmdline-tools** | `...\android-sdk\cmdline-tools\latest` (12.0) |
| **Plataformas** | `android-35`, `android-36` |
| **Build-tools** | `35.0.0`, `36.0.0` |
| **Imagen de sistema** | `android-36 / google_apis_playstore / x86_64` (la única) |
| **JDK** | `C:\Program Files\Android\openjdk\jdk-21.0.8` |
| **Licencias** | aceptadas (`licenses/android-sdk-license`) |
| **Workload .NET** | `android 36.1.69/10.0.100` (instalado por Visual Studio) |
| **Extensión VS Code** | `ms-dotnettools.dotnet-maui` 1.17.266 |
| **Android Studio** | ❌ **no está instalado** — por eso hay que lanzar a mano o desde VS Code |

### Variables de entorno (ya configuradas a nivel *Usuario*)

```
ANDROID_HOME       = C:\Program Files (x86)\Android\android-sdk
ANDROID_SDK_ROOT   = C:\Program Files (x86)\Android\android-sdk
JAVA_HOME          = C:\Program Files\Android\openjdk\jdk-21.0.8
PATH (User)       += ...\android-sdk\platform-tools
                      ...\android-sdk\emulator
                      ...\android-sdk\cmdline-tools\latest\bin
```

> **Nota:** las terminales abiertas **antes** de escribir estas variables no las
> ven. Si `adb` no se encuentra, abrí una terminal nueva o usá la ruta completa
> (abajo uso rutas absolutas para evitarlo).

### AVDs disponibles (3, ninguno se crea de nuevo)

| AVD | Disp. | Resolución | DPI | Último uso |
|---|---|---|---|---|
| **`pixel_9_pro_-_api_36_0`** ✅ **el que venimos usando** | Pixel 9 Pro | 1280×2856 | 480 | 26/09/2026 18:35 |
| `MAUI_Emulator_API_36` | Pixel 6 | 1080×2400 | 420 | 26/09/2026 18:32 |
| `pixel_8_-_api_36_0` | Pixel 8 | 1080×2400 | 420 | 18/06/2026 21:25 |

Los tres usan la **misma** imagen (`android-36 google_apis_playstore x86_64`).
Usá **`pixel_9_pro_-_api_36_0`** para que las capturas coincidan con las
anteriores (1280×2856 @ 480dpi).

---

## 2. Arranque rápido (lo que tenés que hacer cada vez)

Abrí **una terminal de PowerShell** y ejecutá estos 3 pasos:

### Paso 1 — Encender el emulador

```powershell
& "C:\Program Files (x86)\Android\android-sdk\emulator\emulator.exe" `
    -avd pixel_9_pro_-_api_36_0 -netdelay none -netspeed full
```

Se abre una ventana con el celular. **Dejala abierta** (si la cerrás, se apaga).

### Paso 2 — Esperar a que termine de arrancar

En **otra terminal** (o esperá ~45 s):

```powershell
& "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe" wait-for-device shell `
  'while [ "$(getprop sys.boot_completed)" != "1" ]; do sleep 1; done; echo BOOT_OK'
```

`BOOT_OK` = listo.

### Paso 3 — Arrancar la API (en otra terminal)

```powershell
cd C:\Users\mateo\source\repos\TiendaRopa
dotnet run --project TiendaRopa.Api --launch-profile http
```

Tiene que levantar en **`http://localhost:5100`**. Verificalo:

```powershell
(Invoke-WebRequest "http://localhost:5100/api/categorias" -UseBasicParsing).StatusCode   # → 200
```

### Paso 4 — Decime "el emulador está listo"

Yo corro la verificación (§4) y arranco a probar.

---

## 3. Cómo abrirlo manualmente — 3 opciones

### Opción A — Desde VS Code (recomendada)

1. **Ctrl+Shift+P** → **`.NET MAUI: Select Startup Project`**
   *(español: `.NET MAUI: Seleccionar proyecto de inicio`)* → `TiendaRopa`.
2. **Ctrl+Shift+P** → **`.NET MAUI: Select Startup Device`**
   *(`.NET MAUI: Seleccionar dispositivo de inicio`)* → elegí el AVD
   `pixel_9_pro_-_api_36_0` bajo la sección *Emulators*.
3. **F5**.

La extensión arranca el emulador sola, compila, instala y abre la app.
Esto es lo más parecido a "dar F5 en el browser".

> Otros comandos útiles:
> - **`.NET MAUI: Manage Android Emulators`** — lista/borra AVDs.
> - **`.NET MAUI: Create Android Emulator`** — crear uno nuevo (no hace falta).

### Opción B — Desde la terminal (la más fiable)

Ver **§2 Paso 1**. Es lo que recomiendo cuando sólo querés el emulador encendido
para que yo lo use, sin lanzar la app todavía.

### Opción C — Desde Visual Studio

**Tools → Android → Android Emulator Manager** (o *Device Manager*) →
`pixel_9_pro_-_api_36_0` → **Start**.
Útil si ya tenés Visual Studio abierto. No hace falta compilar desde VS.

---

## 4. Checklist "listo para el agente"

Antes de decirme que arranque, esto tiene que ser verde. Podés copiar/pegar
el bloque entero:

```powershell
$sdk = "C:\Program Files (x86)\Android\android-sdk"
Write-Host "--- 1. Emulador ---" -ForegroundColor Cyan
& "$sdk\platform-tools\adb.exe" devices

Write-Host "--- 2. Boot ---" -ForegroundColor Cyan
& "$sdk\platform-tools\adb.exe" shell getprop sys.boot_completed

Write-Host "--- 3. API ---" -ForegroundColor Cyan
try { (Invoke-WebRequest "http://localhost:5100/api/categorias" -UseBasicParsing -TimeoutSec 5).StatusCode }
catch { "API NO RESPONDE: $($_.Exception.Message)" }

Write-Host "--- 4. Ruta host->API desde Android (10.0.2.2:5100) ---" -ForegroundColor Cyan
& "$sdk\platform-tools\adb.exe" shell "nc -z -w 5 10.0.2.2 5100 && echo PORT_OPEN || echo PORT_CLOSED"
```

**Salida esperada:**

```
emulator-5554   device        ← línea 1: un dispositivo en estado "device"
1                                    ← sys.boot_completed
200                                  ← API viva
PORT_OPEN                            ← Android llega a la API del host
```

Si `adb devices` sale vacío → el emulador no está encendido (vuelvo al §2).
Si sale `offline`/`unauthorized` → `adb kill-server` y volvé a conectar.

---

## 5. Una vez listo: esto es lo que yo hago

Cuando me digas *"probá X en Android"*, mi secuencia típica:

1. **`adb devices`** → confirmo que hay un dispositivo.
2. **Instalo/actualizo la APK** (si cambié código):
   ```powershell
   dotnet build TiendaRopa\TiendaRopa.csproj -f net10.0-android -t:Install
   ```
3. **Lanzo la app** (robusto, no depende del nombre de la activity):
   ```powershell
   adb shell monkey -p com.companyname.tiendaropa -c android.intent.category.LAUNCHER 1
   ```
4. **Capturo pantalla** → `adb exec-out screencap -p > test-evidence\NN-nombre.png`
5. **Abro la captura con `view_image`** y la comparo con `docs/mockups/`.
6. **Interactúo**: `adb shell input tap X Y` y `adb shell input swipe ...`
7. **Reviso errores**: `adb logcat -d`
8. **Verifico el backend de verdad**: pido a la API y comparo con lo pintado.

### Datos fijos de la app

| Dato | Valor |
|---|---|
| Paquete | `com.companyname.tiendaropa` |
| Label | `Velvet Rose Boutique` |
| minSdk / targetSdk | 24 / **36** |
| API desde el emulador | `http://10.0.2.2:5100` |
| API en el host | `http://localhost:5100` (perfil `http`) |
| Web (Blazor Server) | `http://localhost:5101` |

> 📌 **El nombre de la activity (`crc6480ced0ceddf7f394.MainActivity`) es un hash
> generado y puede cambiar al recompilar.** Por eso arriba uso `monkey` en vez
> de `am start -n <paquete>/<activity>`.

> 🔒 **`usesCleartextTraffic="true"` está sólo para desarrollo** porque la API
> se publica por HTTP. En producción hay que usar HTTPS y quitar ese atributo.
> Está declarado en `TiendaRopa/Platforms/Android/AndroidManifest.xml`.

---

## 6. Comandos ADB de referencia

Guardá `scripts/` (ver §8). Resumen rápido — siempre con la ruta absoluta:

```powershell
$adb = "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe"

# --- Estado ---
& $adb devices -l                    # lista dispositivos
& $adb shell wm size                 # resolución   → 1280x2856
& $adb shell wm density              # densidad     → 480

# --- Capturas ---
& $adb exec-out screencap -p > test-evidence\pantalla.png

# --- Logcat ---
& $adb logcat -c                     # limpiar
& $adb logcat -d                     # volcado completo (dump y sigue)
& $adb logcat -d *:E                 # sólo errores
& $adb logcat -d | Select-String "tiendaropa|mono|crash|FATAL"

# --- Toques y desplazamientos (coords físicas 1280x2856) ---
& $adb shell input tap 640 1400      # toque
& $adb shell input swipe 640 2200 640 900 300     # desplazar arriba (300 ms)
& $adb shell input swipe 640 900 640 2200 300     # desplazar abajo
& $adb shell input keyevent 4        # botón ATRÁS
& $adb shell input keyevent 3        # botón HOME

# --- Aplicación ---
& $adb shell monkey -p com.companyname.tiendaropa -c android.intent.category.LAUNCHER 1
& $adb shell am force-stop com.companyname.tiendaropa

# --- Prueba de red desde el dispositivo ---
& $adb shell "nc -z -w 5 10.0.2.2 5100 && echo OPEN || echo CLOSED"

# --- Limpieza ---
& $adb kill-server ; & $adb start-server
```

> **Tip de coordenadas:** las apps MAUI/Blazor suelen usar una bottom nav en
> la parte inferior. Como el emulador es 1280×2856 con barra de gestos
> sobre la parte baja, **desplazá un poco antes de tocar** y verificá con
> screenshot que tocaste lo que querías.

---

## 7. Qué pasa si algo falla

| Síntoma | Causa probable | Solución |
|---|---|---|
| `adb` no se encuentra | Terminal vieja sin el PATH nuevo | Usá la ruta absoluta o abrí terminal nueva |
| `adb devices` vacío | Emulador apagado | §2 Paso 1 |
| `offline` tras arrancar | server adb desincronizado | `adb kill-server` → `adb start-server` |
| Emulador abre y se cierra | Falta HAXM/WHPX o GPU | Probá añadiendo `-gpu swiftshader_indirect` |
| La app muestra datos vacíos / timeout | API apagada | §2 Paso 3, revisá que responda `200` |
| `PORT_CLOSED` desde Android | API no escuchando en loopback | Mirá que `applicationUrl` sea `http://localhost:5100` |
| Error `Android SDK not found` en VS Code | La extensión no ve el SDK | `.vscode/settings.json` ya tiene `maui.configuration.androidSdkPreferredPath` — recargá VS Code |
| `usesCleartextTraffic` denegado | Release sin HTTPS | Sólo debe pasar en Debug || **Captura casi negra y de pocos KB** | **La pantalla del emulador está dormida** | `adb shell input keyevent KEYCODE_WAKEUP` y luego `input keyevent 82`. *`screencap` no falla: devuelve un PNG negro.* El script de evidencias ya lo hace solo. |
| Captura de ~20 KB con app en foco | Pantalla apagada (mismo caso) | Fijate en el tamaño: una captura real pesa **400–600 KB** en 1280×2856 || Captura corrupta alrededor de 1 MB | PowerShell rompe el binario con `>` | Usá **siempre** `adb exec-out screencap -p >` (no `adb shell screencap`) y validá el tamaño |

---

## 8. Archivos de apoyo en este repo

| Ruta | Qué es |
|---|---|
| `.vscode/settings.json` | Apunta la extensión MAUI a nuestro SDK y JDK existentes |
| `scripts/android-emulator.ps1` | Enciende el AVD y espera a `boot_completed` |
| `scripts/adb-evidence.ps1` | Capturas + logcat + eventos a `test-evidence/` |
| `test-evidence/` | Evidencias (capturas, logs). **Está en `.gitignore`** |
| `docs/android-testing.md` | Cómo ejecutar, desplegar, capturar y diagnosticar |
| `docs/mockups/` | Mockups de referencia para comparar visualmente |

### Instalar los scripts (una sola vez)

```powershell
# No hace falta: ya están en el repo. Ejecutar con:
powershell -ExecutionPolicy Bypass -File scripts\android-emulator.ps1
powershell -ExecutionPolicy Bypass -File scripts\adb-evidence.ps1 -Tag catalogo
```

---

## 9. Resumen — tu rutina en 4 líneas

```powershell
# 1) Emulador
& "C:\Program Files (x86)\Android\android-sdk\emulator\emulator.exe" -avd pixel_9_pro_-_api_36_0
# 2) Esperar boot  (en otra terminal)
& "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe" wait-for-device shell 'while [ "$(getprop sys.boot_completed)" != "1" ]; do sleep 1; done; echo BOOT_OK'
# 3) API  (en otra terminal)
dotnet run --project C:\Users\mateo\source\repos\TiendaRopa\TiendaRopa.Api --launch-profile http
# 4) Avisarme
```

Decime **"el emulador está listo"** y yo me encargo del resto: compilar,
instalar, navegar, capturar, comparar con los mockups y diagnosticar.
