using VinylRipper.Discogs;
using VinylRipper.YouTube;

namespace VinylRipper.Ripping;

public enum RipPhase { Resolving, Downloading, Done }

/// <summary>Estado de un ripeo en curso, pensado para alimentar una barra de progreso.</summary>
public sealed record RipProgress(
    RipPhase Phase,
    int CompletedTracks,
    int? TotalTracks,
    string CurrentRelease,
    string? CurrentTrack,
    double CurrentTrackPercent)
{
    /// <summary>Porcentaje global 0..100, o null mientras no se conoce el total de pistas.</summary>
    public double? OverallPercent => TotalTracks is { } t and > 0
        ? Math.Clamp(100.0 * (CompletedTracks + CurrentTrackPercent / 100.0) / t, 0, 100)
        : null;
}

public sealed record RipFailure(string Release, string Track, string Error);

public sealed record RipResult(string OutputFolder, int Downloaded, IReadOnlyList<RipFailure> Failures);

/// <summary>
/// Orquesta el ripeo: para cada disco pide el tracklist a Discogs, busca el vídeo de cada pista
/// (primero entre los vídeos que Discogs asocia al disco, si no en YouTube) y lo baja a MP3 en
/// <c>&lt;salida&gt;/&lt;ticks&gt;/Artista - Título (Año)/NN - Pista.mp3</c>.
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
    public async Task<RipResult> RipAsync(IReadOnlyList<ReleaseSummary> releases, string outputFolder,
        IProgress<RipProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputFolder);
        var failures = new List<RipFailure>();
        var downloaded = 0;

        // Fase 1: resolver tracklists para conocer el total de pistas y poder mostrar progreso real.
        var details = new List<ReleaseDetails>();
        for (var i = 0; i < releases.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var r = releases[i];
            progress?.Report(new RipProgress(RipPhase.Resolving, i, releases.Count, r.DisplayName, null, 0));
            try
            {
                details.Add(await _discogs.GetReleaseAsync(r.ReleaseId, ct));
            }
            catch (DiscogsException ex)
            {
                failures.Add(new RipFailure(r.DisplayName, "*", ex.Message));
            }
        }

        var totalTracks = details.Sum(d => d.Tracks.Count);
        var completed = 0;

        // Fase 2: descargar pista a pista.
        foreach (var release in details)
        {
            ct.ThrowIfCancellationRequested();
            var releaseName = BuildReleaseFolderName(release);
            var releaseDir = Path.Combine(outputFolder, releaseName);
            var usedVideos = new HashSet<string>();
            var index = 0;

            foreach (var track in release.Tracks)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                var fileName = BuildTrackFileName(index, release.Tracks.Count, track);
                var video = TrackMatcher.FindVideo(track, release.Videos, usedVideos);
                var source = video?.Uri ?? YtDlpDownloader.SearchUrl(TrackMatcher.BuildSearchQuery(release, track));
                if (video is not null) usedVideos.Add(video.Uri);

                var snapshot = completed;
                var trackProgress = new Progress<YtDlpProgress>(p =>
                    progress?.Report(new RipProgress(RipPhase.Downloading, snapshot, totalTracks, releaseName, track.Title, p.Percent)));
                progress?.Report(new RipProgress(RipPhase.Downloading, completed, totalTracks, releaseName, track.Title, 0));

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

        progress?.Report(new RipProgress(RipPhase.Done, completed, totalTracks, string.Empty, null, 100));
        return new RipResult(outputFolder, downloaded, failures);
    }

    internal static string BuildReleaseFolderName(ReleaseDetails release)
    {
        var name = string.IsNullOrWhiteSpace(release.Artist) ? release.Title : $"{release.Artist} - {release.Title}";
        if (release.Year is { } y and > 0) name += $" ({y})";
        return FileNameSanitizer.Sanitize(name, fallback: release.ReleaseId.ToString());
    }

    internal static string BuildTrackFileName(int index, int total, Track track)
    {
        var digits = Math.Max(2, total.ToString().Length);
        var prefix = index.ToString().PadLeft(digits, '0');
        var title = track.Artist is { Length: > 0 } a ? $"{a} - {track.Title}" : track.Title;
        return FileNameSanitizer.Sanitize($"{prefix} - {title}", fallback: prefix);
    }
}
