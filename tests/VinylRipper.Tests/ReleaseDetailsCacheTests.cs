using VinylRipper.Discogs;

namespace VinylRipper.Tests;

public sealed class ReleaseDetailsCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vr-releases-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static ReleaseDetails Animals(long id = 99) => new(id, "Pink Floyd", "Animals", 1977,
        [new Track("A1", "Pigs On The Wing (Part One)", null, "1:25"), new Track("A2", "Dogs", "Waters & Gilmour", "17:06")],
        [new Video("https://www.youtube.com/watch?v=x", "Pink Floyd - Dogs", 1026)],
        "https://i.discogs.com/animals.jpg");

    private static void AssertSame(ReleaseDetails expected, ReleaseDetails actual)
    {
        Assert.Equal(expected.ReleaseId, actual.ReleaseId);
        Assert.Equal(expected.Artist, actual.Artist);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Year, actual.Year);
        Assert.Equal(expected.CoverUrl, actual.CoverUrl);
        Assert.Equal(expected.Tracks, actual.Tracks);
        Assert.Equal(expected.Videos, actual.Videos);
    }

    [Fact]
    public void Survives_a_restart()
    {
        new ReleaseDetailsCache(_dir).Store(Animals());

        var afterRestart = new ReleaseDetailsCache(_dir);

        Assert.True(afterRestart.TryGet(99, out var details));
        AssertSame(Animals(), details);
        Assert.True(File.Exists(Path.Combine(_dir, "99.json")));
    }

    [Fact]
    public void Keeps_nulls_and_empty_lists()
    {
        var bare = new ReleaseDetails(5, "", "Sin datos", null, [], []);
        new ReleaseDetailsCache(_dir).Store(bare);

        Assert.True(new ReleaseDetailsCache(_dir).TryGet(5, out var details));
        Assert.Null(details.Year);
        Assert.Null(details.CoverUrl);
        Assert.Empty(details.Tracks);
        Assert.Empty(details.Videos);
    }

    [Fact]
    public async Task Asks_the_api_only_the_first_time_even_across_restarts()
    {
        var calls = 0;
        Task<ReleaseDetails> Fetch(CancellationToken _) { calls++; return Task.FromResult(Animals()); }

        await new ReleaseDetailsCache(_dir).GetOrFetchAsync(99, Fetch);
        var cache = new ReleaseDetailsCache(_dir);
        await cache.GetOrFetchAsync(99, Fetch);
        var details = await cache.GetOrFetchAsync(99, Fetch);

        Assert.Equal(1, calls);
        AssertSame(Animals(), details);
    }

    [Fact]
    public async Task Simultaneous_requests_for_the_same_release_share_one_api_call()
    {
        var calls = 0;
        var gate = new TaskCompletionSource<ReleaseDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ReleaseDetails> Fetch(CancellationToken _) { Interlocked.Increment(ref calls); return gate.Task; }
        var cache = new ReleaseDetailsCache(_dir);

        var first = cache.GetOrFetchAsync(99, Fetch);
        var second = cache.GetOrFetchAsync(99, Fetch);
        gate.SetResult(Animals());

        Assert.Same(await first, await second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_failed_request_is_retried_next_time()
    {
        var cache = new ReleaseDetailsCache(_dir);

        await Assert.ThrowsAsync<DiscogsException>(() =>
            cache.GetOrFetchAsync(99, _ => Task.FromException<ReleaseDetails>(new DiscogsException("429"))));
        var details = await cache.GetOrFetchAsync(99, _ => Task.FromResult(Animals()));

        AssertSame(Animals(), details);
        Assert.False(File.Exists(Path.Combine(_dir, "99.json.tmp")));
    }

    [Fact]
    public async Task Cancelling_one_caller_does_not_lose_the_result()
    {
        var gate = new TaskCompletionSource<ReleaseDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ReleaseDetailsCache(_dir);
        using var cts = new CancellationTokenSource();

        var cancelled = cache.GetOrFetchAsync(99, _ => gate.Task, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        gate.SetResult(Animals());
        await Task.Delay(50); // deja que termine de guardarse

        Assert.True(new ReleaseDetailsCache(_dir).TryGet(99, out _));
    }

    [Theory]
    [InlineData("{ esto no es json")]
    [InlineData("""{ "version": 0, "fetchedUtc": "2026-01-01T00:00:00Z", "release": { "releaseId": 99, "artist": "x", "title": "x", "tracks": [], "videos": [] } }""")]
    [InlineData("""{ "version": 1, "fetchedUtc": "2026-01-01T00:00:00Z", "release": { "releaseId": 12345, "artist": "x", "title": "x", "tracks": [], "videos": [] } }""")]
    [InlineData("")]
    public async Task Broken_old_or_mismatched_files_are_fetched_again_and_overwritten(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "99.json"), content);
        var calls = 0;

        var cache = new ReleaseDetailsCache(_dir);
        Assert.False(cache.TryGet(99, out _));
        var details = await cache.GetOrFetchAsync(99, _ => { calls++; return Task.FromResult(Animals()); });

        Assert.Equal(1, calls);
        AssertSame(Animals(), details);
        Assert.True(new ReleaseDetailsCache(_dir).TryGet(99, out _));
    }

    [Fact]
    public void Null_lists_in_a_hand_edited_file_become_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "7.json"),
            """{ "version": 1, "fetchedUtc": "2026-01-01T00:00:00Z", "release": { "releaseId": 7, "artist": "a", "title": "t", "tracks": null, "videos": null } }""");

        Assert.True(new ReleaseDetailsCache(_dir).TryGet(7, out var details));
        Assert.Empty(details.Tracks);
        Assert.Empty(details.Videos);
    }

    [Fact]
    public async Task Clear_forgets_everything_so_releases_are_asked_again()
    {
        var cache = new ReleaseDetailsCache(_dir);
        cache.Store(Animals(1));
        cache.Store(Animals(2));
        File.WriteAllText(Path.Combine(_dir, "3.json.tmp"), "resto de un guardado a medias");
        Assert.Equal(2, cache.Count);

        var removed = cache.Clear();

        Assert.Equal(2, removed);
        Assert.Equal(0, cache.Count);
        Assert.Empty(Directory.GetFiles(_dir));
        Assert.False(cache.TryGet(1, out _));                 // tampoco queda en memoria
        var calls = 0;
        await cache.GetOrFetchAsync(1, _ => { calls++; return Task.FromResult(Animals(1)); });
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Count_and_Clear_without_folder_are_zero()
    {
        var cache = new ReleaseDetailsCache(_dir);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Clear());
    }

    [Fact]
    public void Unwritable_folder_still_caches_in_memory()
    {
        // Un archivo donde debería ir la carpeta: no se puede crear, pero no revienta.
        File.WriteAllText(_dir, "no soy una carpeta");
        try
        {
            var cache = new ReleaseDetailsCache(_dir);
            cache.Store(Animals());
            Assert.True(cache.TryGet(99, out _));
        }
        finally { File.Delete(_dir); }
    }
}
