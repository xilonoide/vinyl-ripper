using System.Net;
using System.Text;
using VinylRipper.Discogs;
using VinylRipper.Ripping;
using VinylRipper.YouTube;

namespace VinylRipper.Tests;

public class DiscogsServerErrorRetryTests
{
    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var status = statuses[Math.Min(Calls, statuses.Length - 1)];
            Calls++;
            var body = request.RequestUri!.Host.StartsWith("i.") ? (HttpContent)new ByteArrayContent([0xFF, 0xD8, 0xFF])
                : new StringContent("""{ "id": 1, "username": "u" }""", Encoding.UTF8, "application/json");
            return Task.FromResult(new HttpResponseMessage(status) { Content = body });
        }
    }

    private static DiscogsClient Client(SequenceHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(DiscogsClient.BaseUrl) }, "tok") { ServerErrorDelay = TimeSpan.Zero };

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_passing_server_error_is_retried(HttpStatusCode error)
    {
        var handler = new SequenceHandler(error, error, HttpStatusCode.OK);

        var me = await Client(handler).GetIdentityAsync();

        Assert.Equal("u", me.Username);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task A_persistent_server_error_gives_up_with_a_clear_message()
    {
        var handler = new SequenceHandler(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<DiscogsException>(() => Client(handler).GetIdentityAsync());

        Assert.Equal(1 + DiscogsClient.ServerErrorRetries, handler.Calls);
        Assert.Equal(500, ex.StatusCode);
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task Client_errors_are_not_retried()
    {
        var handler = new SequenceHandler(HttpStatusCode.NotFound);
        await Assert.ThrowsAsync<DiscogsException>(() => Client(handler).GetIdentityAsync());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Cover_download_also_retries_server_errors()
    {
        var handler = new SequenceHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        var bytes = await Client(handler).DownloadImageAsync("https://i.discogs.com/x.jpg");

        Assert.Equal([0xFF, 0xD8, 0xFF], bytes);
        Assert.Equal(2, handler.Calls);
    }
}

public sealed class RipServiceRetryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vr-rip-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Descargador falso (seguro con varias descargas a la vez): falla las pistas indicadas las primeras N
    /// veces, apunta cada llamada y cuántas hay en marcha a la vez. Con <see cref="Mp3Template"/> deja un
    /// MP3 de verdad en su sitio (para que ffmpeg pueda incrustarle la portada).
    /// </summary>
    private sealed class FakeDownloader : IAudioDownloader
    {
        private readonly object _gate = new();
        private int _running;

        public Dictionary<string, int> FailuresLeft { get; } = new();
        public List<(string Source, string Directory, string FileName)> Calls { get; } = [];
        public int MaxConcurrent { get; private set; }
        public TimeSpan Duration { get; init; } = TimeSpan.Zero;
        public string? Mp3Template { get; init; }
        public Action<string>? OnDownload { get; init; }

        public async Task<string> DownloadMp3Async(string urlOrSearch, string outputDirectory, string fileNameWithoutExtension,
            IProgress<YtDlpProgress>? progress = null, CancellationToken ct = default)
        {
            bool fail;
            lock (_gate)
            {
                Calls.Add((urlOrSearch, outputDirectory, fileNameWithoutExtension));
                MaxConcurrent = Math.Max(MaxConcurrent, ++_running);
                fail = FailuresLeft.TryGetValue(fileNameWithoutExtension, out var left) && left > 0;
                if (fail) FailuresLeft[fileNameWithoutExtension] = left - 1;
            }
            try
            {
                OnDownload?.Invoke(fileNameWithoutExtension);
                if (Duration > TimeSpan.Zero) await Task.Delay(Duration, ct);
                if (fail) throw new YtDlpException("ERROR: HTTP Error 500: Internal Server Error");

                var path = Path.Combine(outputDirectory, fileNameWithoutExtension + ".mp3");
                if (Mp3Template is not null)
                {
                    Directory.CreateDirectory(outputDirectory);
                    File.Copy(Mp3Template, path, overwrite: true);
                }
                return path;
            }
            finally
            {
                lock (_gate) _running--;
            }
        }

        public int CallsFor(string fileName)
        {
            lock (_gate) return Calls.Count(c => c.FileName == fileName);
        }
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static readonly ReleaseSummary Animals = new(99, "Pink Floyd", "Animals", 1977, null, null);

    private static IReadOnlyList<TrackSelection> Tracks(params (string Position, string Title)[] tracks) =>
        tracks.Select((t, i) => new TrackSelection(Animals, new Track(t.Position, t.Title, null, null), i + 1, tracks.Length)).ToList();

    private RipService Service(FakeDownloader downloader)
    {
        // Detalle ya en caché (con el vídeo de "Dogs"): el ripeo no hace ninguna petición HTTP.
        var cache = new ReleaseDetailsCache(Path.Combine(_dir, "cache"));
        cache.Store(new ReleaseDetails(99, "Pink Floyd", "Animals", 1977, [],
            [new Video("https://www.youtube.com/watch?v=dogs", "Pink Floyd - Dogs", 1026)]));
        var discogs = new DiscogsClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9/") }, "tok");
        return new RipService(discogs, downloader, covers: null, releases: cache, retryPause: TimeSpan.Zero);
    }

    [Fact]
    public async Task Everything_ok_means_one_call_per_track_and_no_failures()
    {
        var downloader = new FakeDownloader();

        var result = await Service(downloader).RipAsync(Tracks(("A1", "Pigs"), ("A2", "Dogs")), _dir);

        Assert.Equal(2, result.Downloaded);
        Assert.Empty(result.Failures);
        Assert.Equal(0, result.RecoveredOnRetry);
        Assert.Equal(2, downloader.Calls.Count);
        Assert.Equal("https://www.youtube.com/watch?v=dogs", downloader.Calls.Single(c => c.FileName == "Pink Floyd - Dogs").Source);
    }

    [Fact]
    public async Task A_passing_failure_is_retried_at_the_end_with_the_same_source_and_name()
    {
        var downloader = new FakeDownloader();
        downloader.FailuresLeft["Pink Floyd - Dogs"] = 1;
        var phases = new List<RipPhase>();

        var result = await Service(downloader).RipAsync(Tracks(("A1", "Pigs"), ("A2", "Dogs"), ("B1", "Sheep")), _dir,
            new SyncProgress<RipProgress>(p => phases.Add(p.Phase)));

        Assert.Equal(3, result.Downloaded);
        Assert.Empty(result.Failures);
        Assert.Equal(1, result.RecoveredOnRetry);
        var dogs = downloader.Calls.Where(c => c.FileName == "Pink Floyd - Dogs").ToList();
        Assert.Equal(2, dogs.Count);
        Assert.Equal(dogs[0], dogs[1]);
        // El reintento va después de todas las demás, no justo tras el fallo.
        var lastDogs = downloader.Calls.FindLastIndex(c => c.FileName == "Pink Floyd - Dogs");
        Assert.True(downloader.Calls.FindIndex(c => c.FileName == "Pink Floyd - Sheep") < lastDogs);
        Assert.Equal(downloader.Calls.Count - 1, lastDogs);
        Assert.Contains(RipPhase.Retrying, phases);
    }

    [Fact]
    public async Task Persistent_failures_are_returned_together_and_can_be_retried_later()
    {
        var downloader = new FakeDownloader();
        downloader.FailuresLeft["Pink Floyd - Dogs"] = 2;   // falla a la primera y en el reintento automático
        downloader.FailuresLeft["Pink Floyd - Sheep"] = 2;
        var service = Service(downloader);

        var result = await service.RipAsync(Tracks(("A1", "Pigs"), ("A2", "Dogs"), ("B1", "Sheep")), _dir);

        Assert.Equal(1, result.Downloaded);
        Assert.Equal(["Dogs", "Sheep"], result.Failures.Select(f => f.Track));
        Assert.All(result.Failures, f => Assert.Contains("500", f.Error));
        Assert.Equal("Pink Floyd - Animals (1977)", result.Failures[0].Release);

        var retry = await service.RetryAsync(result.Failures, _dir);

        Assert.Equal(2, retry.Downloaded);
        Assert.Empty(retry.Failures);
        Assert.Equal(3, downloader.CallsFor("Pink Floyd - Dogs"));
    }

    [Fact]
    public async Task Retrying_keeps_names_that_depend_on_the_whole_release()
    {
        var downloader = new FakeDownloader();
        downloader.FailuresLeft["Pink Floyd - Anonim A2"] = 5;
        var service = Service(downloader);

        var result = await service.RipAsync(Tracks(("A1", "Anonim"), ("A2", "Anonim")), _dir);
        downloader.FailuresLeft.Clear();
        await service.RetryAsync(result.Failures, _dir);

        // Si se recalculase sólo con la pista fallida, se llamaría "Pink Floyd - Anonim" y pisaría a la A1.
        Assert.Equal("Pink Floyd - Anonim A2", downloader.Calls[^1].FileName);
        Assert.Equal(Path.Combine(_dir, "Pink Floyd - Animals (1977)"), downloader.Calls[^1].Directory);
    }

    [Fact]
    public async Task Downloads_three_at_a_time_and_no_more()
    {
        var downloader = new FakeDownloader { Duration = TimeSpan.FromMilliseconds(60) };
        var tracks = Tracks(Enumerable.Range(1, 8).Select(i => ($"A{i}", $"Pista {i}")).ToArray());

        var result = await Service(downloader).RipAsync(tracks, _dir);

        Assert.Equal(8, result.Downloaded);
        Assert.Equal(RipService.MaxParallelDownloads, downloader.MaxConcurrent);
    }

    [Fact]
    public async Task Progress_counts_finished_and_running_tracks()
    {
        var downloader = new FakeDownloader { Duration = TimeSpan.FromMilliseconds(40) };
        var reports = new List<RipProgress>();
        var gate = new object();

        await Service(downloader).RipAsync(Tracks(("A1", "Pigs"), ("A2", "Dogs"), ("B1", "Sheep"), ("B2", "Pigs 2")), _dir,
            new SyncProgress<RipProgress>(p => { lock (gate) reports.Add(p); }));

        var downloading = reports.Where(r => r.Phase == RipPhase.Downloading).ToList();
        Assert.All(downloading, r => Assert.InRange(r.ActiveTracks.Count, 0, RipService.MaxParallelDownloads));
        Assert.Contains(downloading, r => r.ActiveTracks.Count == RipService.MaxParallelDownloads);
        Assert.Equal(4, downloading.Max(r => r.CompletedTracks));
        Assert.Equal(100, reports[^1].OverallPercent);
    }

    [Fact]
    public async Task Cover_is_kept_until_every_track_of_the_release_has_it_in_its_id3()
    {
        var ffmpeg = ToolLocator.FindOnPath(ToolLocator.FfmpegFileName);
        if (ffmpeg is null) return; // sin ffmpeg no se puede incrustar nada

        var temp = Path.Combine(_dir, "temp");
        var mp3 = await RunFfmpegAsync(ffmpeg, Path.Combine(_dir, "silencio.mp3"),
            "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo", "-t", "1", "-c:a", "libmp3lame", "-b:a", "96k");
        var jpeg = await RunFfmpegAsync(ffmpeg, Path.Combine(_dir, "portada.jpg"),
            "-f", "lavfi", "-i", "color=c=red:s=32x32", "-frames:v", "1");
        var coverPath = Path.Combine(temp, "cover-99.jpg");

        // La portada debe seguir ahí en cada descarga: si se borrase antes de tiempo, las pistas
        // posteriores del disco se quedarían sin ella en el ID3.
        var missingCoverAt = new List<string>();
        var downloader = new FakeDownloader
        {
            Duration = TimeSpan.FromMilliseconds(30),
            Mp3Template = mp3,
            OnDownload = name => { if (!File.Exists(coverPath)) lock (missingCoverAt) missingCoverAt.Add(name); },
        };
        var cache = new ReleaseDetailsCache(Path.Combine(_dir, "cache"));
        cache.Store(new ReleaseDetails(99, "Pink Floyd", "Animals", 1977, [], [], "https://i.discogs.com/animals.jpg"));
        var images = new HttpClient(new ImageHandler(File.ReadAllBytes(jpeg))) { BaseAddress = new Uri(DiscogsClient.BaseUrl) };
        var service = new RipService(new DiscogsClient(images, "tok"), downloader, new CoverArtEmbedder(ffmpeg, temp), cache, TimeSpan.Zero);

        var result = await service.RipAsync(Tracks(Enumerable.Range(1, 5).Select(i => ($"A{i}", $"Pista {i}")).ToArray()), _dir);

        Assert.Equal(5, result.Downloaded);
        Assert.Empty(result.ReleasesWithoutCover);
        Assert.Empty(missingCoverAt);
        foreach (var file in Directory.GetFiles(Path.Combine(_dir, "Pink Floyd - Animals (1977)"), "*.mp3"))
            Assert.Contains("APIC", Encoding.Latin1.GetString(File.ReadAllBytes(file)));
        Assert.False(File.Exists(coverPath)); // y al terminar, fuera de temp
    }

    private sealed class ImageHandler(byte[] image) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
    }

    private static async Task<string> RunFfmpegAsync(string ffmpeg, string output, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" }.Concat(args).Append(output)) psi.ArgumentList.Add(a);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var p = System.Diagnostics.Process.Start(psi)!;
        await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Assert.True(File.Exists(output), $"ffmpeg no generó {output}");
        return output;
    }
}

public class RipEtaTests
{
    [Fact]
    public void Says_nothing_until_there_is_some_history()
    {
        var now = TimeSpan.Zero;
        var eta = new RipEta(() => now);

        now = TimeSpan.FromSeconds(10);
        Assert.Null(eta.Remaining(0.5));
        now = TimeSpan.FromSeconds(30);
        Assert.Null(eta.Remaining(0));
    }

    [Fact]
    public void Extrapolates_the_pace_so_far()
    {
        var now = TimeSpan.Zero;
        var eta = new RipEta(() => now);

        now = TimeSpan.FromMinutes(10);
        Assert.Equal(TimeSpan.FromMinutes(30), eta.Remaining(0.25));   // 10 min por cada 25 %
        Assert.Equal(TimeSpan.Zero, eta.Remaining(1));
    }

    [Fact]
    public void Restart_counts_from_now()
    {
        var now = TimeSpan.Zero;
        var eta = new RipEta(() => now);
        now = TimeSpan.FromMinutes(60);
        eta.Restart();

        now = TimeSpan.FromMinutes(61);
        Assert.Equal(TimeSpan.FromMinutes(1), eta.Remaining(0.5));
    }

    [Theory]
    [InlineData(20, "queda menos de 1 min")]
    [InlineData(60, "quedan ~1 min")]
    [InlineData(61, "quedan ~2 min")]
    [InlineData(47 * 60 + 10, "quedan ~48 min")]
    [InlineData(3600, "quedan ~1 h 00 min")]
    [InlineData(2 * 3600 + 4 * 60 + 1, "quedan ~2 h 05 min")]
    public void Formats_rounding_up(int seconds, string expected) =>
        Assert.Equal(expected, RipEta.Format(TimeSpan.FromSeconds(seconds)));
}
