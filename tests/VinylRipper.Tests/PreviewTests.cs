using VinylRipper.Discogs;
using VinylRipper.Preview;
using VinylRipper.Ripping;
using VinylRipper.YouTube;

namespace VinylRipper.Tests;

public class PreviewArgumentsTests
{
    [Fact]
    public void Preview_downloads_best_m4a_audio_with_paths_and_ffmpeg()
    {
        var downloader = new YtDlpDownloader(new YtDlpOptions(@"C:\tools\yt-dlp.exe", @"C:\ffmpeg\ffmpeg.exe", 0, @"D:\docs\vinyl-ripper\temp"));
        var args = downloader.BuildPreviewArguments("https://youtu.be/x", @"D:\docs\vinyl-ripper\temp\previews", "preview-1-2").ToList();

        Assert.Equal("bestaudio[ext=m4a]/bestaudio", args[args.IndexOf("--format") + 1]);
        Assert.Contains("--extract-audio", args);
        Assert.Equal("m4a", args[args.IndexOf("--audio-format") + 1]);
        Assert.DoesNotContain("--audio-quality", args);
        Assert.DoesNotContain("--embed-metadata", args);
        Assert.Contains("--no-playlist", args);
        Assert.Equal(@"C:\ffmpeg\ffmpeg.exe", args[args.IndexOf("--ffmpeg-location") + 1]);
        Assert.Equal("preview-1-2.%(ext)s", args[args.IndexOf("--output") + 1]);

        var paths = args.Select((a, i) => (a, i)).Where(x => x.a == "--paths").Select(x => args[x.i + 1]).ToList();
        Assert.Equal([@"home:D:\docs\vinyl-ripper\temp\previews", @"temp:D:\docs\vinyl-ripper\temp"], paths);

        Assert.Equal("--", args[^2]);
        Assert.Equal("https://youtu.be/x", args[^1]);
    }
}

public class ResolveSourceTests
{
    private static readonly Video Dogs = new("https://www.youtube.com/watch?v=dogs", "Pink Floyd - Dogs", 1026);
    private static readonly Video Sheep = new("https://www.youtube.com/watch?v=sheep", "Pink Floyd - Sheep", 620);

    [Fact]
    public void Uses_the_discogs_video_that_matches_the_track()
    {
        var (source, video) = TrackMatcher.ResolveSource("Pink Floyd", new Track("A2", "Dogs", null, null), [Sheep, Dogs]);

        Assert.Equal(Dogs.Uri, source);
        Assert.Same(Dogs, video);
    }

    [Fact]
    public void Falls_back_to_a_youtube_search_without_a_matching_video()
    {
        var (source, video) = TrackMatcher.ResolveSource("Pink Floyd", new Track("B1", "Pigs (Three Different Ones)", null, null), [Dogs]);

        Assert.Null(video);
        Assert.Equal("ytsearch1:Pink Floyd Pigs (Three Different Ones)", source);
    }

    [Fact]
    public void Skips_videos_already_used_by_other_tracks()
    {
        var used = new HashSet<string> { Dogs.Uri };
        var (source, video) = TrackMatcher.ResolveSource("Pink Floyd", new Track("A2", "Dogs", null, null), [Dogs], used);

        Assert.Null(video);
        Assert.StartsWith("ytsearch1:", source);
    }
}

public sealed class TrackPreviewServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vr-preview-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static TrackSelection Track(long releaseId = 42, int index = 3) =>
        new(new ReleaseSummary(releaseId, "Pink Floyd", "Animals", 1977, null, null), new Track("B1", "Sheep", null, null), index, 5);

    /// <summary>Un yt-dlp que no existe: si el servicio intentara ejecutarlo, fallaría.</summary>
    private TrackPreviewService Service() =>
        new(new YtDlpDownloader(new YtDlpOptions(Path.Combine(_dir, "no-existe", "yt-dlp.exe"), null)), _dir);

    [Fact]
    public void Preview_file_is_named_after_release_and_track_index()
    {
        Assert.Equal("preview-42-3", TrackPreviewService.FileNameFor(Track()));
        Assert.Equal(Path.Combine(_dir, "preview-42-3.m4a"), Service().PathFor(Track()));
    }

    [Fact]
    public async Task Reuses_a_track_already_prepared_without_running_yt_dlp()
    {
        Directory.CreateDirectory(_dir);
        var cached = Path.Combine(_dir, "preview-42-3.m4a");
        File.WriteAllBytes(cached, [1, 2, 3]);

        var path = await Service().GetAsync(Track(), []);

        Assert.Equal(cached, path);
    }

    [Fact]
    public async Task Downloads_when_not_prepared_yet()
    {
        // Sin archivo previo tiene que lanzar yt-dlp; como no existe, el error lo demuestra.
        await Assert.ThrowsAsync<YtDlpException>(() => Service().GetAsync(Track(), []));
    }

    [Fact]
    public async Task An_empty_leftover_is_not_reused()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "preview-42-3.m4a"), []);

        await Assert.ThrowsAsync<YtDlpException>(() => Service().GetAsync(Track(), []));
    }
}

public class PreviewTimeTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(83, "1:23")]
    [InlineData(1026, "17:06")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-4, "0:00")]
    public void Formats_minutes_and_seconds(int seconds, string expected) =>
        Assert.Equal(expected, PreviewTime.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Ignores_fractions_of_a_second() =>
        Assert.Equal("1:23", PreviewTime.Format(TimeSpan.FromSeconds(83.9)));
}
