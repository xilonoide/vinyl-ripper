<div align="center">

# 🎧 Vinyl Ripper

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
| 📚 **Todas tus listas** | Colección (todas las carpetas), deseados, inventario y listas personalizadas, desde un único desplegable. |
| ✅ **Selección acumulativa** | Marca discos con Ctrl / Shift + clic, pásalos a *Seleccionados* y sigue añadiendo desde otras listas. |
| 🎵 **Vídeos de Discogs primero** | Si la edición tiene vídeos de YouTube asociados en Discogs se usan esos; si no, se busca `artista + pista`. |
| 📦 **yt-dlp autoinstalable** | Si no hay `yt-dlp` en el sistema se descarga solo a `Documentos/vinyl-ripper/tools`. |
| 📊 **Progreso real** | Spinner para lo indeterminado y barra de progreso con pista actual / total cuando se conoce. |
| 💾 **Todo se recuerda** | Lista elegida, filtro, discos seleccionados, tamaño y posición de ventana… se guardan a cada cambio. |
| 🌙 **Modo oscuro de verdad** | Desplegables, listas, hovers, scrollbars, tooltips, diálogos y hasta la barra de título nativa. |
| ❌ **Sin botones de cerrar** | Ventanas y diálogos se cierran con la X. Sin confirmación al salir. |

## 🖼️ Cómo se usa

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Lista de Discogs [ Colección · Todo (312) ▾ ] ↻     [ Filtrar…     ] ⚙  │
├───────────────────────────────┬──────────┬───────────────────────────────┤
│ Discos (312)                  │          │ Seleccionados (3)             │
│ ▪ Pink Floyd – Animals  1977  │ Añadir ➜ │ ▪ Nirvana – Nevermind    1991 │
│ ▪ Pink Floyd – Meddle   1971  │ ⬅ Quitar │ ▪ Tool – Lateralus       2001 │
│ ▪ Radiohead – Kid A     2000  │  Vaciar  │ ▪ Portishead – Dummy     1994 │
│ …                             │          │                               │
├───────────────────────────────┴──────────┴───────────────────────────────┤
│ [⬇ Descargar MP3] [📂 Abrir carpeta de salida]     ◌ Descargando 7/38 …  │
│ ████████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
└──────────────────────────────────────────────────────────────────────────┘
```

1. Pulsa **⚙** y pega tu token de Discogs (*discogs.com → Settings → Developers → Generate new token*). Puedes comprobarlo con **Probar**.
2. Elige una lista en el desplegable; los discos se cargan con progreso por páginas.
3. Selecciona discos y pulsa **Añadir ➜**. Cambia de lista y sigue añadiendo.
4. **⬇ Descargar MP3**. Cada descarga va a su propia carpeta numerada.
5. **📂 Abrir carpeta de salida** abre la última carpeta creada en el Explorador.

### 📁 Dónde acaba todo

```
Documentos/
└── vinyl-ripper/
    ├── settings.json                  ← configuración (token cifrado incluido)
    ├── tools/
    │   └── yt-dlp.exe                 ← si no lo tenías instalado
    └── 639012345678901234/            ← una carpeta por descarga (DateTime.Ticks, siempre creciente)
        └── Pink Floyd - Animals (1977)/
            ├── 01 - Pigs On The Wing (Part One).mp3
            ├── 02 - Dogs.mp3
            └── …
```

La carpeta raíz de salida y la calidad MP3 se cambian en ⚙.

## 🧰 Requisitos

- **Windows 10 20H1+ / Windows 11** (barra de título oscura vía DWM).
- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** para compilar.
- **ffmpeg** en el `PATH` (o su ruta en ⚙). Es lo que convierte a MP3:
  ```powershell
  winget install Gyan.FFmpeg
  ```
- `yt-dlp` es opcional: si no está, la app lo descarga.

## 🚀 Compilar y ejecutar

```powershell
git clone https://github.com/xilonoide/vinyl-ripper.git
cd vinyl-ripper
dotnet build
dotnet run --project src/VinylRipper.Windows
```

Ejecutable autocontenido:

```powershell
dotnet publish src/VinylRipper.Windows -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## 🧪 Tests

```powershell
dotnet test
```

Cubren el cifrado (ida y vuelta, manipulación, clave distinta), el almacén de configuración (guardado atómico, archivo corrupto), el cliente Discogs contra un `HttpMessageHandler` falso (cabeceras, paginación, 401, reintento en 429, parseo de tracklists), el emparejado pista ↔ vídeo, el parser de progreso de yt-dlp, los nombres de archivo y las carpetas numeradas.

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
│       ├── Themes/Dark.xaml    Tema oscuro completo (ComboBox, ListBox, ScrollBar, ProgressBar…)
│       ├── Controls/           Spinner · DarkTitleBar · converters
│       ├── Dialogs/            DarkMessageBox · SettingsWindow
│       └── ViewModels/         MainViewModel · SettingsViewModel
└── tests/
    └── VinylRipper.Tests/      🧪 xUnit sobre el core
```

El core no sabe nada de WPF: un futuro `VinylRipper.Linux` (Avalonia, GTK, CLI…) sólo tiene que aportar la UI y, si quiere, su propio `IKeyMaterialProvider`.

## ⚖️ Aviso

Vinyl Ripper es una herramienta personal para escuchar en digital los discos que ya tienes. Respeta los términos de servicio de YouTube y Discogs y los derechos de los artistas.

## 📄 Licencia

MIT
