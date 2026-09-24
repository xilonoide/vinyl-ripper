using System.Text.Json.Serialization;

namespace VinylRipper.Configuration;

/// <summary>
/// Configuración persistida en <c>settings.json</c>. Todo lo que el usuario toca en la UI
/// acaba aquí para que al reabrir la aplicación se vea exactamente igual.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Token personal de Discogs cifrado con AES-256 (ver <see cref="Security.TokenProtector"/>).</summary>
    public string? EncryptedDiscogsToken { get; set; }

    /// <summary>Clave (Kind:Id) de la lista de Discogs seleccionada en el desplegable.</summary>
    public string? SelectedListKey { get; set; }

    /// <summary>Raíz de salida. Si es null se usa Documentos/vinyl-ripper.</summary>
    public string? OutputRoot { get; set; }

    /// <summary>Última carpeta de salida creada (para el botón "Abrir carpeta").</summary>
    public string? LastOutputFolder { get; set; }

    /// <summary>Ruta a yt-dlp. Null = autodetectar / descargar a tools/.</summary>
    public string? YtDlpPath { get; set; }

    /// <summary>Ruta a ffmpeg. Null = autodetectar en PATH.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>Calidad de audio para yt-dlp (0 = mejor, 9 = peor).</summary>
    public int AudioQuality { get; set; } = 0;

    /// <summary>Texto del filtro de la lista de discos.</summary>
    public string SearchFilter { get; set; } = string.Empty;

    /// <summary>Discos acumulados en la lista de seleccionados.</summary>
    public List<SavedRelease> SelectedReleases { get; set; } = [];

    public WindowPlacement Window { get; set; } = new();
}

public sealed class SavedRelease
{
    public long ReleaseId { get; set; }
    public string Artist { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? Format { get; set; }
    public string? Thumb { get; set; }
}

public sealed class WindowPlacement
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 780;
    public bool Maximized { get; set; }

    [JsonIgnore]
    public bool HasPosition => Left.HasValue && Top.HasValue;
}
