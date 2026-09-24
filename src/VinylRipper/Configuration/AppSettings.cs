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

    /// <summary>Clave (Kind:Id) de la hoja del árbol de fuentes seleccionada (carpeta, deseados, inventario o lista).</summary>
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

    /// <summary>Pistas acumuladas en la lista de seleccionados.</summary>
    public List<SavedTrack> SelectedTracks { get; set; } = [];

    /// <summary>Ancho (en estrellas) del panel de fuente/árbol.</summary>
    public double SourcePaneWidth { get; set; } = 0.6;

    public WindowPlacement Window { get; set; } = new();
}

public sealed class SavedTrack
{
    public long ReleaseId { get; set; }
    public string Artist { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? Format { get; set; }
    public string? Thumb { get; set; }
    public int Index { get; set; }
    public int TotalTracks { get; set; }
    public string Position { get; set; } = string.Empty;
    public string TrackTitle { get; set; } = string.Empty;
    public string? TrackArtist { get; set; }
    public string? Duration { get; set; }
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
