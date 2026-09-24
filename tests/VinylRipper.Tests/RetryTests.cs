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

    /// <summary>Descargador falso: falla las pistas indicadas las primeras N veces y apunta cada llamada.</summary>
    private sealed class FakeDownloader : IAudioDownloader
    {
        public Dictionary<string, int> FailuresLeft { get; } = new();
        public List<(string Source, string Directory, string FileName)> Calls { get; } = [];

        public Task<string> DownloadMp3Async(string urlOrSearch, string outputDirectory, string fileNameWithoutExtension,
            IProgress<YtDlpProgress>? progress = null, CancellationToken ct = default)
        {
            Calls.Add((urlOrSearch, outputDirectory, fileNameWithoutExtension));
            if (FailuresLeft.TryGetValue(fileNameWithoutExtension, out var left) && left > 0)
            {
                FailuresLeft[fileNameWithoutExtension] = left - 1;
                throw new YtDlpException("ERROR: HTTP Error 500: Internal Server Error");
            }
            return Task.FromResult(Path.Combine(outputDirectory, fileNameWithoutExtension + ".mp3"));
        }

        public int CallsFor(string fileName) => Calls.Count(c => c.FileName == fileName);
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
        Assert.Equal("Pink Floyd - Sheep", downloader.Calls[2].FileName);
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
}
