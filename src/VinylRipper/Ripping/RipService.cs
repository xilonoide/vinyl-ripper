using VinylRipper.Discogs;
using VinylRipper.YouTube;

namespace VinylRipper.Ripping;

public enum RipPhase { Resolving, Downloading, Done }

/// <summary>Estado de un ripeo en curso, pensado para alimentar una barra de progreso.</summary>
public sealed record RipProgress(
    RipPhase Phase,
    int CompletedTracks,
    int TotalTracks,
    string CurrentRelease,
    string? CurrentTrack,
    double CurrentTrackPercent)
{
    /// <summary>Porcentaje global 0..100.</summary>
    public double OverallPercent => TotalTracks > 0
        ? Math.Clamp(100.0 * (CompletedTracks + CurrentTrackPercent / 100.0) / TotalTracks, 0, 100)
        : 0;
}

public sealed record RipFailure(string Release, string Track, string Error);

/// <param name="ReleasesWithoutCover">Discos con alguna pista descargada que se ha quedado sin portada.</param>
public sealed record RipResult(string OutputFolder, int Downloaded, IReadOnlyList<RipFailure> Failures,
    IReadOnlyList<string> ReleasesWithoutCover);

/// <summary>
/// Orquesta el ripeo de un conjunto de pistas: por cada disco implicado obtiene sus vídeos de Discogs
/// (de la <see cref="ReleaseDetailsCache"/> si ya se pidieron), empareja cada pista con uno (o recurre a
/// una búsqueda en YouTube) y la baja a MP3 en <c>&lt;salida&gt;/Artista - Título (Año)/Artista - Canción.mp3</c>.
/// Si hay <see cref="CoverArtEmbedder"/>, a cada MP3 recién generado se le incrusta la portada del disco
/// en Discogs (la misma en todas sus pistas).
/// </summary>
public sealed class RipService
{
    private readonly DiscogsClient _discogs;
    private readonly YtDlpDownloader _downloader;
    private readonly CoverArtEmbedder? _covers;
    private readonly ReleaseDetailsCache? _releases;

    /// <param name="releases">Si se indica, el detalle de los discos ya pedidos no se vuelve a pedir a la API.</param>
    public RipService(DiscogsClient discogs, YtDlpDownloader downloader, CoverArtEmbedder? covers = null,
        ReleaseDetailsCache? releases = null)
    {
        _discogs = discogs;
        _downloader = downloader;
        _covers = covers;
        _releases = releases;
    }

    /// <param name="outputFolder">Carpeta ya creada (normalmente <see cref="OutputFolders.CreateNext"/>).</param>
    public async Task<RipResult> RipAsync(IReadOnlyList<TrackSelection> tracks, string outputFolder,
        IProgress<RipProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputFolder);
        var failures = new List<RipFailure>();
        var withoutCover = new List<string>();
        var downloaded = 0;
        var completed = 0;
        var total = tracks.Count;

        foreach (var group in tracks.GroupBy(t => t.Release.ReleaseId))
        {
            ct.ThrowIfCancellationRequested();
            var release = group.First().Release;
            var releaseName = BuildReleaseFolderName(release);
            var releaseDir = Path.Combine(outputFolder, releaseName);

            // Los vídeos que Discogs asocia al disco son la mejor fuente; si falla, buscamos en YouTube.
            // La misma respuesta trae la portada a tamaño completo; si no, vale la miniatura de la lista.
            IReadOnlyList<Video> videos = [];
            string? coverUrl = release.Thumb;
            progress?.Report(new RipProgress(RipPhase.Resolving, completed, total, releaseName, null, 0));
            try
            {
                var id = release.ReleaseId;
                var details = _releases is null
                    ? await _discogs.GetReleaseAsync(id, ct)
                    : await _releases.GetOrFetchAsync(id, fetchCt => _discogs.GetReleaseAsync(id, fetchCt), ct);
                videos = details.Videos;
                coverUrl = details.CoverUrl ?? coverUrl;
            }
            catch (DiscogsException) { /* seguimos con búsqueda */ }

            var coverPath = await DownloadCoverAsync(release.ReleaseId, coverUrl, ct);
            var releaseMissingCover = false;

            var usedVideos = new HashSet<string>();
            var ordered = group.OrderBy(t => t.Index).ToList();
            var fileNames = BuildTrackFileNames(release.Artist, ordered.Select(t => t.Track).ToList());
            for (var i = 0; i < ordered.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sel = ordered[i];
                var track = sel.Track;
                var fileName = fileNames[i];
                var (source, video) = TrackMatcher.ResolveSource(release.Artist, track, videos, usedVideos);
                if (video is not null) usedVideos.Add(video.Uri);

                var snapshot = completed;
                var trackProgress = new Progress<YtDlpProgress>(p =>
                    progress?.Report(new RipProgress(RipPhase.Downloading, snapshot, total, releaseName, track.Title, p.Percent)));
                progress?.Report(new RipProgress(RipPhase.Downloading, completed, total, releaseName, track.Title, 0));

                string mp3;
                try
                {
                    mp3 = await _downloader.DownloadMp3Async(source, releaseDir, fileName, trackProgress, ct);
                    downloaded++;
                }
                catch (YtDlpException ex)
                {
                    failures.Add(new RipFailure(releaseName, track.Title, ex.Message));
                    completed++;
                    continue;
                }

                // La portada no es imprescindible: si no se puede incrustar, el MP3 se queda tal cual.
                if (_covers is not null)
                {
                    if (coverPath is null) releaseMissingCover = true;
                    else
                    {
                        try { await _covers.EmbedAsync(mp3, coverPath, ct); }
                        catch (Exception ex) when (ex is CoverArtException or IOException or UnauthorizedAccessException)
                        {
                            releaseMissingCover = true;
                        }
                    }
                }

                completed++;
            }

            if (releaseMissingCover) withoutCover.Add(releaseName);
            if (coverPath is not null)
                try { File.Delete(coverPath); } catch (IOException) { /* temp se vacía al arrancar */ }
        }

        progress?.Report(new RipProgress(RipPhase.Done, completed, total, string.Empty, null, 100));
        return new RipResult(outputFolder, downloaded, failures, withoutCover);
    }

    /// <summary>Baja la portada una vez por disco a la carpeta temporal; null si no hay o falla.</summary>
    private async Task<string?> DownloadCoverAsync(long releaseId, string? url, CancellationToken ct)
    {
        if (_covers is null || string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var image = await _discogs.DownloadImageAsync(url, ct);
            return _covers.SaveCover(image, $"cover-{releaseId}");
        }
        catch (Exception ex) when (ex is DiscogsException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string BuildReleaseFolderName(ReleaseSummary release)
    {
        var name = string.IsNullOrWhiteSpace(release.Artist) ? release.Title : $"{release.Artist} - {release.Title}";
        if (release.Year is { } y and > 0) name += $" ({y})";
        return FileNameSanitizer.Sanitize(name, fallback: release.ReleaseId.ToString());
    }

    /// <summary>
    /// Nombre del MP3: "Artista - Canción". El artista es el de la pista si Discogs lo indica y, si no,
    /// el del disco. En recopilatorios ("Various") sin artista por pista queda sólo la canción.
    /// </summary>
    internal static string BuildTrackFileName(string releaseArtist, Track track)
    {
        // El título se sanea aparte: si se queda vacío ("..."), cae en la posición del vinilo.
        var title = FileNameSanitizer.Sanitize(track.Title, fallback: string.IsNullOrEmpty(track.Position) ? "pista" : track.Position);
        var artist = FileArtist(releaseArtist, track);
        return artist is null ? title : FileNameSanitizer.Sanitize($"{artist} - {title}", fallback: title);
    }

    private static string? FileArtist(string releaseArtist, Track track)
    {
        var artist = (string.IsNullOrWhiteSpace(track.Artist) ? releaseArtist : track.Artist)?.Trim();
        return string.IsNullOrEmpty(artist) || artist.Equals("Various", StringComparison.OrdinalIgnoreCase) ? null : artist;
    }

    /// <summary>
    /// Nombres de archivo para las pistas de un disco, en el mismo orden. Si varias comparten nombre
    /// (p. ej. un disco con todos los cortes llamados "Anonim") se distinguen con su posición en el
    /// vinilo: "Artista - Anonim A1", "Artista - Anonim A2"… Sin posición, o si aun así coinciden, se añade " (2)", " (3)"…
    /// </summary>
    internal static IReadOnlyList<string> BuildTrackFileNames(string releaseArtist, IReadOnlyList<Track> tracks)
    {
        var baseNames = tracks.Select(t => BuildTrackFileName(releaseArtist, t)).ToList();
        var repeated = baseNames
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(tracks.Count);
        for (var i = 0; i < tracks.Count; i++)
        {
            var name = baseNames[i];
            var position = tracks[i].Position.Trim();
            if (repeated.Contains(name) && position.Length > 0)
                name = FileNameSanitizer.Sanitize($"{name} {position}", fallback: position);
            result.Add(UniqueName(name, used));
        }
        return result;
    }

    /// <summary>Último recurso contra colisiones: "Intro", "Intro (2)", "Intro (3)"…</summary>
    internal static string UniqueName(string name, ISet<string> used)
    {
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++)
            candidate = $"{name} ({n})";
        return candidate;
    }
}
