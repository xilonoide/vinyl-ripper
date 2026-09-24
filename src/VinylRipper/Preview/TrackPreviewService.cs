using VinylRipper.Discogs;
using VinylRipper.Ripping;
using VinylRipper.YouTube;

namespace VinylRipper.Preview;

/// <summary>
/// Prepara una pista para escucharla en la app: la baja a m4a en <c>temp/previews</c> (la misma fuente
/// que usaría la descarga a MP3) y devuelve la ruta. Si ya se escuchó en esta sesión, reutiliza el
/// archivo sin volver a lanzar yt-dlp. La carpeta temp se vacía al arrancar la app.
/// </summary>
public sealed class TrackPreviewService(YtDlpDownloader downloader, string previewsDirectory)
{
    public string PreviewsDirectory { get; } = previewsDirectory;

    /// <summary>Nombre (sin extensión) del archivo de escucha de una pista.</summary>
    public static string FileNameFor(TrackSelection track) => $"preview-{track.Release.ReleaseId}-{track.Index}";

    public string PathFor(TrackSelection track) => Path.Combine(PreviewsDirectory, FileNameFor(track) + ".m4a");

    /// <param name="videos">Vídeos que Discogs asocia al disco; vacío = buscar en YouTube.</param>
    public async Task<string> GetAsync(TrackSelection track, IReadOnlyList<Video> videos,
        IProgress<YtDlpProgress>? progress = null, CancellationToken ct = default)
    {
        var cached = PathFor(track);
        if (File.Exists(cached) && new FileInfo(cached).Length > 0) return cached;

        var (source, _) = TrackMatcher.ResolveSource(track.Release.Artist, track.Track, videos);
        return await downloader.DownloadPreviewAsync(source, PreviewsDirectory, FileNameFor(track), progress, ct);
    }
}
