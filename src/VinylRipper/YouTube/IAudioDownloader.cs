namespace VinylRipper.YouTube;

/// <summary>
/// Lo que <see cref="Ripping.RipService"/> necesita para bajar una pista a MP3. Lo implementa
/// <see cref="YtDlpDownloader"/>; existe para poder probar el ripeo (reintentos, nombres) sin yt-dlp.
/// </summary>
public interface IAudioDownloader
{
    /// <summary>Baja <paramref name="urlOrSearch"/> a <paramref name="outputDirectory"/>/<paramref name="fileNameWithoutExtension"/>.mp3.</summary>
    /// <exception cref="YtDlpException">No se ha podido bajar.</exception>
    Task<string> DownloadMp3Async(string urlOrSearch, string outputDirectory, string fileNameWithoutExtension,
        IProgress<YtDlpProgress>? progress = null, CancellationToken ct = default);
}
