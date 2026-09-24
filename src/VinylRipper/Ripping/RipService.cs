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

public sealed record RipResult(string OutputFolder, int Downloaded, IReadOnlyList<RipFailure> Failures);

/// <summary>
/// Orquesta el ripeo de un conjunto de pistas: por cada disco implicado pide a Discogs los vídeos
/// asociados, empareja cada pista con uno (o recurre a una búsqueda en YouTube) y la baja a MP3 en
/// <c>&lt;salida&gt;/Artista - Título (Año)/Pista.mp3</c>.
/// </summary>
public sealed class RipService
{
    private readonly DiscogsClient _discogs;
    private readonly YtDlpDownloader _downloader;

    public RipService(DiscogsClient discogs, YtDlpDownloader downloader)
    {
        _discogs = discogs;
        _downloader = downloader;
    }

    /// <param name="outputFolder">Carpeta ya creada (normalmente <see cref="OutputFolders.CreateNext"/>).</param>
    public async Task<RipResult> RipAsync(IReadOnlyList<TrackSelection> tracks, string outputFolder,
        IProgress<RipProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputFolder);
        var failures = new List<RipFailure>();
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
            IReadOnlyList<Video> videos = [];
            progress?.Report(new RipProgress(RipPhase.Resolving, completed, total, releaseName, null, 0));
            try
            {
                videos = (await _discogs.GetReleaseAsync(release.ReleaseId, ct)).Videos;
            }
            catch (DiscogsException) { /* seguimos con búsqueda */ }

            var usedVideos = new HashSet<string>();
            var ordered = group.OrderBy(t => t.Index).ToList();
            var fileNames = BuildTrackFileNames(ordered.Select(t => t.Track).ToList());
            for (var i = 0; i < ordered.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sel = ordered[i];
                var track = sel.Track;
                var fileName = fileNames[i];
                var video = TrackMatcher.FindVideo(track, videos, usedVideos);
                var source = video?.Uri ?? YtDlpDownloader.SearchUrl(TrackMatcher.BuildSearchQuery(release.Artist, track));
                if (video is not null) usedVideos.Add(video.Uri);

                var snapshot = completed;
                var trackProgress = new Progress<YtDlpProgress>(p =>
                    progress?.Report(new RipProgress(RipPhase.Downloading, snapshot, total, releaseName, track.Title, p.Percent)));
                progress?.Report(new RipProgress(RipPhase.Downloading, completed, total, releaseName, track.Title, 0));

                try
                {
                    await _downloader.DownloadMp3Async(source, releaseDir, fileName, trackProgress, ct);
                    downloaded++;
                }
                catch (YtDlpException ex)
                {
                    failures.Add(new RipFailure(releaseName, track.Title, ex.Message));
                }

                completed++;
            }
        }

        progress?.Report(new RipProgress(RipPhase.Done, completed, total, string.Empty, null, 100));
        return new RipResult(outputFolder, downloaded, failures);
    }

    internal static string BuildReleaseFolderName(ReleaseSummary release)
    {
        var name = string.IsNullOrWhiteSpace(release.Artist) ? release.Title : $"{release.Artist} - {release.Title}";
        if (release.Year is { } y and > 0) name += $" ({y})";
        return FileNameSanitizer.Sanitize(name, fallback: release.ReleaseId.ToString());
    }

    /// <summary>Nombre del MP3: el título de la pista (precedido del artista si es distinto al del disco).</summary>
    internal static string BuildTrackFileName(Track track)
    {
        var title = track.Artist is { Length: > 0 } a ? $"{a} - {track.Title}" : track.Title;
        return FileNameSanitizer.Sanitize(title, fallback: string.IsNullOrEmpty(track.Position) ? "pista" : track.Position);
    }

    /// <summary>
    /// Nombres de archivo para las pistas de un disco, en el mismo orden. Si varias comparten título
    /// (p. ej. un disco con todos los cortes llamados "Anonim") se distinguen con su posición en el
    /// vinilo: "Anonim A1", "Anonim A2", "Anonim B1"… Sin posición, o si aun así coinciden, se añade " (2)", " (3)"…
    /// </summary>
    internal static IReadOnlyList<string> BuildTrackFileNames(IReadOnlyList<Track> tracks)
    {
        var baseNames = tracks.Select(BuildTrackFileName).ToList();
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
