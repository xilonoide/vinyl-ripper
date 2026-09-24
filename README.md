<div align="center">

<img src="assets/vinyl-256.png" width="128" alt="Vinyl Ripper" />

# Vinyl Ripper

**Convierte tu colección de Discogs en MP3 con un par de clics.**

Elige una de tus listas de Discogs (colección, deseados, inventario, listas personalizadas), marca los discos que quieras, y Vinyl Ripper baja cada pista desde YouTube con [yt-dlp](https://github.com/yt-dlp/yt-dlp) y la convierte a MP3, todo en una interfaz de escritorio en modo oscuro.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-0078D4?logo=windows&logoColor=white)
![Tests](https://img.shields.io/badge/tests-xUnit-5FB832)
![Dark mode](https://img.shields.io/badge/theme-dark%20only-1B1B1F)
![License](https://img.shields.io/badge/license-MIT-yellow)

</div>

---

## ✨ Características

| | |
|---|---|
| 🔐 **Token cifrado** | El token personal de Discogs se guarda cifrado con **AES-256-GCM** (clave derivada con PBKDF2 a partir de la identidad de máquina + usuario). Nunca toca el disco en claro. |
| 🗂 **Tres niveles** | Árbol de fuentes (Colección → carpetas, Deseados, Inventario, Listas → listas) → discos → pistas. |
| ✅ **Selección acumulativa** | Marca discos completos o pistas sueltas con Ctrl / Shift + clic, pásalos a *Seleccionados* (agrupados por disco) y sigue añadiendo desde otras carpetas o listas. |
| 🎵 **Vídeos de Discogs primero** | Si la edición tiene vídeos de YouTube asociados en Discogs se usan esos; si no, se busca `artista + pista`. |
| 📦 **yt-dlp autoinstalable** | Si no hay `yt-dlp` en el sistema se descarga solo a `Documentos/vinyl-ripper/tools`; desde ⚙ se actualiza con un clic. |
| 🧹 **Sin basura** | Los intermedios de yt-dlp (`.webm`, `.part`…) van a `Documentos/vinyl-ripper/temp`, que se vacía en cada arranque; en la carpeta del disco sólo aparecen MP3. |
| 🏷 **Nombres limpios** | Cada MP3 se llama como la pista, sin numerar. Si varios cortes comparten título se distinguen por su posición en el vinilo (`Anonim A1`, `Anonim A2`…). |
| 📊 **Progreso real** | Spinner para lo indeterminado y barra de progreso por pista (el total se conoce desde el principio). |
| 💾 **Todo se recuerda** | Lista elegida, filtro, discos seleccionados, tamaño y posición de ventana… se guardan a cada cambio. |
| 🌙 **Modo oscuro de verdad** | Desplegables, listas, hovers, scrollbars, tooltips, diálogos y hasta la barra de título nativa. |
| ❌ **Sin botones de cerrar** | Ventanas y diálogos se cierran con la X. Sin confirmación al salir. |

## 🖼️ Cómo se usa

```
┌────────────────────────────────────────────────────────────────────────────────────┐
│ 🎧 Vinyl Ripper                                        [ Filtrar discos…      ] ⚙  │
├──────────────────┬─────────────────────────────────┬───────────────────────────────┤
│ Fuente        ↻  │ Discos (48)                     │ Seleccionados (11 pistas)     │
│ ▾ 📚 Colección   │ ▪ Pink Floyd – Animals    1977  │ Pink Floyd – Animals · 2      │
│    🗂 Todo   312 │ ▪ Pink Floyd – Meddle     1971  │   A2  Dogs             17:06  │
│    📁 Rock    48 │ ▪ Radiohead – Kid A       2000  │   B2  Sheep            10:20  │
│    📁 Jazz    27 │        [Añadir discos completos ➜] │ Nirvana – Nevermind · 9    │
│  ♥ Deseados      ├─────────────────────────────────┤   01  Smells Like…      5:01  │
│  🏷 Inventario   │ Pistas · Pink Floyd – Animals   │   …                           │
│ ▾ 📝 Listas      │ A1 Pigs On The Wing (Part One)  │                               │
│    📄 Para el DJ │ A2 Dogs                  17:06  │                               │
│                  │ B1 Pigs (Three Different Ones)  │                               │
│                  │            [Añadir pistas ➜]    │        [⬅ Quitar] [Vaciar]    │
├──────────────────┴─────────────────────────────────┴───────────────────────────────┤
│ [⬇ Descargar MP3] [📂 Abrir carpeta de salida]          ◌ Descargando 7/11 · Dogs… │
│ ██████████████████████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
└────────────────────────────────────────────────────────────────────────────────────┘
```

1. Pulsa **⚙** y pega tu token de Discogs (*discogs.com → Settings → Developers → Generate new token*). Puedes comprobarlo con **Probar**.
2. En **Fuente** elige una carpeta de la colección, deseados, inventario o una lista; los discos se cargan con progreso por páginas.
3. Marca discos y pulsa **Añadir discos completos ➜**, o marca uno para ver sus **Pistas** y añade sólo las que quieras con **Añadir pistas ➜**. Cambia de carpeta o lista y sigue acumulando.
4. **⬇ Descargar MP3**. Cada descarga va a su propia carpeta numerada; el progreso es por pista.
5. **📂 Abrir carpeta de salida** abre la última carpeta creada en el Explorador.

### 📁 Dónde acaba todo

```
Documentos/
└── vinyl-ripper/
    ├── settings.json                  ← configuración (token cifrado incluido)
    ├── tools/
    │   └── yt-dlp.exe                 ← si no lo tenías instalado
    ├── temp/                          ← intermedios de yt-dlp (.webm, .part…); se vacía al arrancar
    └── 639012345678901234/            ← una carpeta por descarga (DateTime.Ticks, siempre creciente)
        └── Pink Floyd - Animals (1977)/
            ├── Pigs On The Wing (Part One).mp3
            ├── Dogs.mp3
            └── …
```

Cada MP3 se llama como la pista. Si varias pistas del disco comparten título (cortes sin nombre, «Anonim», «Untitled»…), se distinguen con su posición en el vinilo: `Anonim A1.mp3`, `Anonim A2.mp3`, `Anonim B1.mp3`.

La carpeta raíz de salida y la calidad MP3 se cambian en ⚙.

## 🧰 Requisitos

- **Windows 10 20H1+ / Windows 11** (barra de título oscura vía DWM).
- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** sólo para compilar (el instalador ya lleva el runtime).
- **ffmpeg** en el `PATH` (o su ruta en ⚙). Es lo que convierte a MP3:
  ```powershell
  winget install Gyan.FFmpeg
  ```
- `yt-dlp` es opcional: si no está, la app lo descarga.

## 📦 Instalar

Descarga `VinylRipper-Setup-<versión>-win-x64.exe` de [Releases](https://github.com/xilonoide/vinyl-ripper/releases) y ejecútalo. El instalador:

- se instala **sólo para tu usuario** (`%LocalAppData%\Programs\Vinyl Ripper`), sin pedir permisos de administrador;
- es **autocontenido**: no necesitas tener .NET instalado;
- crea el acceso en el menú Inicio (y en el escritorio si lo marcas) y un atajo a la carpeta de descargas;
- avisa al terminar si no encuentra `ffmpeg` en el `PATH`;
- al **desinstalar** pregunta si quieres borrar también `Documentos\vinyl-ripper` (configuración, yt-dlp y todos los MP3). Por defecto, no.

## 🚀 Compilar y ejecutar

```powershell
git clone https://github.com/xilonoide/vinyl-ripper.git
cd vinyl-ripper
dotnet build
dotnet run --project src/VinylRipper.Windows
```

### Generar el instalador

Requiere [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`; vale instalado por usuario o por máquina).

```powershell
pwsh installer/VinylRipper.Windows.Installer/build-installer.ps1
```

El script lee la versión del csproj, hace `dotnet publish` (win-x64, self-contained, ReadyToRun) y compila `VinylRipper.Windows.Installer.iss`. El `Setup.exe` queda en `installer/VinylRipper.Windows.Installer/output/`. Para subir versión, cambia `<Version>` en `src/VinylRipper.Windows/VinylRipper.Windows.csproj`.

## 🧪 Tests

```powershell
dotnet test
```

Cubren el cifrado (ida y vuelta, manipulación, clave distinta), el almacén de configuración (guardado atómico, archivo corrupto), el cliente Discogs contra un `HttpMessageHandler` falso (cabeceras, paginación, 401, reintento en 429, parseo de tracklists), el emparejado pista ↔ vídeo, los argumentos y el parser de progreso de yt-dlp (rutas `home`/`temp`), los nombres de archivo (saneado, títulos repetidos por posición del vinilo), la limpieza de `temp` y las carpetas numeradas.

## 🏗️ Arquitectura

```
vinyl-ripper/
├── src/
│   ├── VinylRipper/            🧠 Core multiplataforma (net10.0, sin dependencias de UI)
│   │   ├── Configuration/      AppPaths · AppSettings · SettingsStore
│   │   ├── Security/           TokenProtector (AES-256-GCM + PBKDF2)
│   │   ├── Discogs/            DiscogsClient · modelos
│   │   ├── YouTube/            ToolLocator · YtDlpInstaller · YtDlpDownloader · parser de progreso
│   │   └── Ripping/            RipService · TrackMatcher · OutputFolders · FileNameSanitizer
│   └── VinylRipper.Windows/    🪟 WPF (net10.0-windows), MVVM con CommunityToolkit.Mvvm
│       ├── Themes/Dark.xaml    Tema oscuro completo (TreeView, ListBox, ComboBox, ScrollBar, ProgressBar…)
│       ├── Controls/           Spinner · DarkTitleBar · converters
│       ├── Dialogs/            DarkMessageBox · SettingsWindow
│       └── ViewModels/         MainViewModel · SourceNode · SettingsViewModel
├── tests/
│   └── VinylRipper.Tests/      🧪 xUnit sobre el core
├── installer/
│   └── VinylRipper.Windows.Installer/
│       ├── VinylRipper.Windows.Installer.iss   📦 script Inno Setup (por usuario, bilingüe es/en)
│       └── build-installer.ps1                 publish + ISCC → output/VinylRipper-Setup-<ver>-win-x64.exe
└── assets/
    └── make-icon.ps1           🎨 genera Assets/vinyl.ico (9 tamaños) y vinyl-256.png
```

El core no sabe nada de WPF: un futuro `VinylRipper.Linux` (Avalonia, GTK, CLI…) sólo tiene que aportar la UI y, si quiere, su propio `IKeyMaterialProvider`.

## ⚖️ Aviso

Vinyl Ripper es una herramienta personal para escuchar en digital los discos que ya tienes. Respeta los términos de servicio de YouTube y Discogs y los derechos de los artistas.

## 📄 Licencia

[MIT](LICENSE)
