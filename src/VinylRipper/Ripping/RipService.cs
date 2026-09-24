using VinylRipper.Discogs;
using VinylRipper.YouTube;

namespace VinylRipper.Ripping;

/// <summary>
/// Fase del ripeo: preparando un disco, bajando sus pistas, o reintentando al final las que fallaron.
/// </summary>
public enum RipPhase { Resolving, Downloading, Retrying, Done }

/// <summary>Estado de un ripeo en curso, pensado para alimentar una barra de progreso.</summary>
/// <param name="CompletedTracks">Pistas terminadas en esta pasada (la normal o la de reintento).</param>
/// <param name="TotalTracks">Pistas de esta pasada.</param>
/// <param name="CurrentRelease">Disco que se está preparando (fase <see cref="RipPhase.Resolving"/>).</param>
/// <param name="ActiveTracks">Pistas que se están bajando ahora mismo (hasta <see cref="RipService.MaxParallelDownloads"/>).</param>
/// <param name="ActiveFraction">Suma de lo que llevan las pistas en marcha, cada una de 0 a 1.</param>
public sealed record RipProgress(
    RipPhase Phase,
    int CompletedTracks,
    int TotalTracks,
    string CurrentRelease,
    IReadOnlyList<string> ActiveTracks,
    double ActiveFraction)
{
    /// <summary>Porcentaje global 0..100.</summary>
    public double OverallPercent => TotalTracks > 0
        ? Math.Clamp(100.0 * (CompletedTracks + ActiveFraction) / TotalTracks, 0, 100)
        : 0;
}

/// <summary>
/// Una pista lista para bajar, con todo decidido: de dónde (vídeo de Discogs o búsqueda), a qué carpeta
/// y con qué nombre. Reintentarla usa exactamente lo mismo, incluidos los nombres que dependen del disco
/// entero ("Artista - Anonim A1").
/// </summary>
public sealed record RipItem(
    TrackSelection Selection,
    string ReleaseName,
    string ReleaseDirectory,
    string FileName,
    string Source,
    string? CoverUrl);

public sealed record RipFailure(RipItem Item, string Error)
{
    public string Release => Item.ReleaseName;
    public string Track => Item.Selection.Track.Title;
}

/// <param name="Downloaded">MP3 generados, contando los que salieron bien al reintentar.</param>
/// <param name="Failures">Pistas que siguen fallando tras el reintento; se pueden pasar a <see cref="RipService.RetryAsync"/>.</param>
/// <param name="ReleasesWithoutCover">Discos con alguna pista descargada que se ha quedado sin portada.</param>
/// <param name="RecoveredOnRetry">Pistas que fallaron a la primera y salieron bien al reintentarlas.</param>
public sealed record RipResult(string OutputFolder, int Downloaded, IReadOnlyList<RipFailure> Failures,
    IReadOnlyList<string> ReleasesWithoutCover, int RecoveredOnRetry = 0);

/// <summary>
/// Orquesta el ripeo de un conjunto de pistas: por cada disco implicado obtiene sus vídeos de Discogs
/// (de la <see cref="ReleaseDetailsCache"/> si ya se pidieron), empareja cada pista con uno (o recurre a
/// una búsqueda en YouTube) y la baja a MP3 en <c>&lt;salida&gt;/Artista - Título (Año)/Artista - Canción.mp3</c>.
/// Si hay <see cref="CoverArtEmbedder"/>, a cada MP3 recién generado se le incrusta la portada del disco
/// en Discogs (la misma en todas sus pistas).
/// <para>
/// Las pistas que fallan no interrumpen nada: al terminar se reintentan una vez (un error de YouTube o de
/// red suele ser pasajero) y las que sigan fallando se devuelven juntas en <see cref="RipResult.Failures"/>.
/// </para>
/// </summary>
public sealed class RipService
{
    private readonly DiscogsClient _discogs;
    private readonly IAudioDownloader _downloader;
    private readonly CoverArtEmbedder? _covers;
    private readonly ReleaseDetailsCache? _releases;
    private readonly TimeSpan _retryPause;

    /// <param name="releases">Si se indica, el detalle de los discos ya pedidos no se vuelve a pedir a la API.</param>
    /// <param name="retryPause">Espera antes de reintentar las pistas que fallaron (3 s si no se indica).</param>
    public RipService(DiscogsClient discogs, IAudioDownloader downloader, CoverArtEmbedder? covers = null,
        ReleaseDetailsCache? releases = null, TimeSpan? retryPause = null)
    {
        _discogs = discogs;
        _downloader = downloader;
        _covers = covers;
        _releases = releases;
        _retryPause = retryPause ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>Baja las pistas y, al final, reintenta una vez las que hayan fallado.</summary>
    /// <param name="outputFolder">Carpeta ya creada (normalmente <see cref="OutputFolders.CreateNext"/>).</param>
    public async Task<RipResult> RipAsync(IReadOnlyList<TrackSelection> tracks, string outputFolder,
        IProgress<RipProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputFolder);
        var items = await PlanAsync(tracks, outputFolder, progress, ct);
        var first = await DownloadAsync(items, RipPhase.Downloading, progress, ct);

        var result = first;
        if (first.Failures.Count > 0)
        {
            progress?.Report(new RipProgress(RipPhase.Retrying, 0, first.Failures.Count, string.Empty, [], 0));
            await Task.Delay(_retryPause, ct);
            var retry = await DownloadAsync(first.Failures.Select(f => f.Item).ToList(), RipPhase.Retrying, progress, ct);
            result = new Pass(first.Downloaded + retry.Downloaded, retry.Failures,
                first.WithoutCover.Union(retry.WithoutCover).ToList());
        }

        progress?.Report(new RipProgress(RipPhase.Done, items.Count, items.Count, string.Empty, [], 0));
        return new RipResult(outputFolder, result.Downloaded, result.Failures, result.WithoutCover,
            RecoveredOnRetry: first.Failures.Count - result.Failures.Count);
    }

    /// <summary>Vuelve a intentar pistas que fallaron (p. ej. cuando el usuario pulsa "Reintentar").</summary>
    public async Task<RipResult> RetryAsync(IReadOnlyList<RipFailure> failures, string outputFolder,
        IProgress<RipProgress>? progress = null, CancellationToken ct = default)
    {
        var pass = await DownloadAsync(failures.Select(f => f.Item).ToList(), RipPhase.Retrying, progress, ct);
        progress?.Report(new RipProgress(RipPhase.Done, failures.Count, failures.Count, string.Empty, [], 0));
        return new RipResult(outputFolder, pass.Downloaded, pass.Failures, pass.WithoutCover,
            RecoveredOnRetry: pass.Downloaded);
    }

    private sealed record Pass(int Downloaded, IReadOnlyList<RipFailure> Failures, IReadOnlyList<string> WithoutCover);

    /// <summary>
    /// Decide, disco a disco, la fuente, la carpeta y el nombre de cada pista. Los vídeos de Discogs son la
    /// mejor fuente (si no hay, se busca en YouTube); la misma respuesta trae la portada a tamaño completo.
    /// </summary>
    private async Task<List<RipItem>> PlanAsync(IReadOnlyList<TrackSelection> tracks, string outputFolder,
        IProgress<RipProgress>? progress, CancellationToken ct)
    {
        var items = new List<RipItem>(tracks.Count);
        foreach (var group in tracks.GroupBy(t => t.Release.ReleaseId))
        {
            ct.ThrowIfCancellationRequested();
            var release = group.First().Release;
            var releaseName = BuildReleaseFolderName(release);
            progress?.Report(new RipProgress(RipPhase.Resolving, items.Count, tracks.Count, releaseName, [], 0));

            IReadOnlyList<Video> videos = [];
            var coverUrl = release.Thumb; // si Discogs no responde, vale la miniatura de la lista
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

            var ordered = group.OrderBy(t => t.Index).ToList();
            var fileNames = BuildTrackFileNames(release.Artist, ordered.Select(t => t.Track).ToList());
            var usedVideos = new HashSet<string>();
            for (var i = 0; i < ordered.Count; i++)
            {
                var (source, video) = TrackMatcher.ResolveSource(release.Artist, ordered[i].Track, videos, usedVideos);
                if (video is not null) usedVideos.Add(video.Uri);
                items.Add(new RipItem(ordered[i], releaseName, Path.Combine(outputFolder, releaseName), fileNames[i], source, coverUrl));
            }
        }
        return items;
    }

    /// <summary>
    /// Pistas que se bajan a la vez. Cada una es una visita de yt-dlp a YouTube: con 2-3 simultáneas no
    /// pasa nada, pero con más YouTube empieza a responder 429/403 o a pedir "confirma que no eres un bot".
    /// </summary>
    public const int MaxParallelDownloads = 3;

    /// <summary>
    /// Baja las pistas (<see cref="MaxParallelDownloads"/> a la vez, en orden de lista) e incrusta la
    /// portada de su disco; los fallos se apuntan y se sigue con las demás.
    /// </summary>
    private async Task<Pass> DownloadAsync(IReadOnlyList<RipItem> items, RipPhase phase,
        IProgress<RipProgress>? progress, CancellationToken ct)
    {
        var tracker = new PassTracker(phase, items.Count, progress);
        var covers = items.GroupBy(i => i.ReleaseDirectory)
            .ToDictionary(g => g.Key, g => new ReleaseCover(this, g.First(), g.Count()));
        var failures = new List<RipFailure>();
        var downloaded = 0;
        var gate = new object();

        using var slots = new SemaphoreSlim(MaxParallelDownloads);
        var work = items.Select(async item =>
        {
            await slots.WaitAsync(ct);
            try
            {
                var error = await DownloadOneAsync(item, covers[item.ReleaseDirectory], tracker, ct);
                lock (gate)
                {
                    if (error is null) downloaded++;
                    else failures.Add(new RipFailure(item, error));
                }
            }
            finally
            {
                slots.Release();
            }
        }).ToList();
        await Task.WhenAll(work);

        // Mismo orden que la lista, aunque hayan terminado en otro.
        var order = items.Select((item, index) => (item, index)).ToDictionary(x => x.item, x => x.index);
        var sortedFailures = failures.OrderBy(f => order[f.Item]).ToList();
        var withoutCover = covers.Values.Where(c => c.Missing).Select(c => c.ReleaseName).ToList();
        return new Pass(downloaded, sortedFailures, withoutCover);
    }

    /// <summary>Baja una pista e incrusta la portada. Devuelve null si ha ido bien, o el motivo del fallo.</summary>
    private async Task<string?> DownloadOneAsync(RipItem item, ReleaseCover cover, PassTracker tracker, CancellationToken ct)
    {
        tracker.Started(item);
        try
        {
            var coverPath = await cover.GetPathAsync(ct);
            var trackProgress = new Progress<YtDlpProgress>(p => tracker.Update(item, p.Percent));

            string mp3;
            try
            {
                mp3 = await _downloader.DownloadMp3Async(item.Source, item.ReleaseDirectory, item.FileName, trackProgress, ct);
            }
            catch (YtDlpException ex)
            {
                return ex.Message;
            }

            // La portada no es imprescindible: si no se puede incrustar, el MP3 se queda tal cual.
            if (_covers is not null)
            {
                if (coverPath is null) cover.Missing = true;
                else
                {
                    try { await _covers.EmbedAsync(mp3, coverPath, ct); }
                    catch (Exception ex) when (ex is CoverArtException or IOException or UnauthorizedAccessException)
                    {
                        cover.Missing = true;
                    }
                }
            }
            return null;
        }
        finally
        {
            tracker.Finished(item);
            cover.TrackDone();
        }
    }

    /// <summary>
    /// Portada de un disco durante una pasada: se baja una sola vez, la primera pista que la necesita, y
    /// se borra de temp cuando termina la última pista del disco.
    /// </summary>
    private sealed class ReleaseCover(RipService owner, RipItem first, int tracks)
    {
        private readonly object _gate = new();
        private Task<string?>? _download;
        private int _remaining = tracks;

        public string ReleaseName { get; } = first.ReleaseName;

        /// <summary>Alguna pista descargada se ha quedado sin portada.</summary>
        public bool Missing { get; set; }

        public Task<string?> GetPathAsync(CancellationToken ct)
        {
            lock (_gate)
                return _download ??= owner.DownloadCoverAsync(first.Selection.Release.ReleaseId, first.CoverUrl, ct);
        }

        public void TrackDone()
        {
            if (Interlocked.Decrement(ref _remaining) != 0) return;
            if (_download is { IsCompletedSuccessfully: true, Result: { } path })
                try { File.Delete(path); } catch (IOException) { /* temp se vacía al cerrar la app */ }
        }
    }

    /// <summary>Lleva la cuenta de una pasada (terminadas, en marcha y cuánto llevan) y la va informando.</summary>
    private sealed class PassTracker(RipPhase phase, int total, IProgress<RipProgress>? progress)
    {
        private readonly object _gate = new();
        private readonly List<(RipItem Item, double Fraction)> _active = [];
        private int _completed;

        public void Started(RipItem item)
        {
            lock (_gate) _active.Add((item, 0));
            Report();
        }

        public void Update(RipItem item, double percent)
        {
            lock (_gate)
            {
                var i = _active.FindIndex(a => ReferenceEquals(a.Item, item));
                if (i >= 0) _active[i] = (item, Math.Clamp(percent / 100.0, 0, 1));
            }
            Report();
        }

        public void Finished(RipItem item)
        {
            lock (_gate)
            {
                _active.RemoveAll(a => ReferenceEquals(a.Item, item));
                _completed++;
            }
            Report();
        }

        private void Report()
        {
            if (progress is null) return;
            RipProgress snapshot;
            lock (_gate)
            {
                snapshot = new RipProgress(phase, _completed, total, string.Empty,
                    _active.Select(a => a.Item.Selection.Track.Title).ToList(), _active.Sum(a => a.Fraction));
            }
            progress.Report(snapshot);
        }
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
