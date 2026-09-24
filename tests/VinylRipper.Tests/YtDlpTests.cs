using VinylRipper.YouTube;

namespace VinylRipper.Tests;

public class YtDlpProgressParserTests
{
    [Theory]
    [InlineData("[download]  45.2% of 3.40MiB at 1.20MiB/s ETA 00:02", 45.2, "1.20MiB/s", "00:02")]
    [InlineData("[download] 100% of 3.40MiB in 00:03", 100, null, null)]
    [InlineData("[download]   0.0% of ~ 5.12MiB at  Unknown B/s ETA Unknown", 0, "Unknown B/s", "Unknown")]
    [InlineData("[download]  99.9% of 3.40MiB at 500.00KiB/s ETA 00:00", 99.9, "500.00KiB/s", "00:00")]
    public void Parses_progress_lines(string line, double pct, string? speed, string? eta)
    {
        Assert.True(YtDlpProgressParser.TryParse(line, out var p));
        Assert.Equal(pct, p.Percent, 3);
        Assert.Equal(speed, p.Speed);
        Assert.Equal(eta, p.Eta);
    }

    [Theory]
    [InlineData("[youtube] abc: Downloading webpage")]
    [InlineData("[ExtractAudio] Destination: C:\\out\\01 - Dogs.mp3")]
    [InlineData("[download] Destination: C:\\out\\01 - Dogs.webm")]
    [InlineData("")]
    [InlineData(null)]
    public void Ignores_other_lines(string? line)
    {
        Assert.False(YtDlpProgressParser.TryParse(line, out _));
    }

    [Fact]
    public void Parses_destination_line()
    {
        Assert.Equal(@"C:\out\01 - Dogs.mp3", YtDlpProgressParser.TryParseDestination(@"[ExtractAudio] Destination: C:\out\01 - Dogs.mp3"));
        Assert.Null(YtDlpProgressParser.TryParseDestination("[download] Destination: x.webm"));
    }
}

public class YtDlpDownloaderTests
{
    [Fact]
    public void BuildArguments_includes_mp3_extraction_quality_ffmpeg_and_paths()
    {
        var downloader = new YtDlpDownloader(new YtDlpOptions(@"C:\tools\yt-dlp.exe", @"C:\ffmpeg\bin\ffmpeg.exe", 3, @"D:\docs\vinyl-ripper\temp"));
        var args = downloader.BuildArguments("https://youtu.be/x", @"C:\out\Disco", "Dogs").ToList();

        Assert.Contains("--extract-audio", args);
        Assert.Equal("mp3", args[args.IndexOf("--audio-format") + 1]);
        Assert.Equal("3", args[args.IndexOf("--audio-quality") + 1]);
        Assert.Equal(@"C:\ffmpeg\bin\ffmpeg.exe", args[args.IndexOf("--ffmpeg-location") + 1]);
        Assert.Contains("--no-playlist", args);
        Assert.Contains("--newline", args);
        Assert.Equal("https://youtu.be/x", args[^1]);
        Assert.Equal("--", args[^2]);

        // Plantilla relativa + rutas home/temp: los intermedios no tocan la carpeta del disco.
        Assert.Equal("Dogs.%(ext)s", args[args.IndexOf("--output") + 1]);
        var paths = args.Select((a, i) => (a, i)).Where(x => x.a == "--paths").Select(x => args[x.i + 1]).ToList();
        Assert.Equal([@"home:C:\out\Disco", @"temp:D:\docs\vinyl-ripper\temp"], paths);
    }

    [Fact]
    public void BuildArguments_omits_ffmpeg_and_temp_when_not_configured_and_clamps_quality()
    {
        var downloader = new YtDlpDownloader(new YtDlpOptions("yt-dlp", null, 42));
        var args = downloader.BuildArguments("q", "out", "n").ToList();

        Assert.DoesNotContain("--ffmpeg-location", args);
        Assert.Equal("9", args[args.IndexOf("--audio-quality") + 1]);
        Assert.Single(args, "--paths");
        Assert.Equal("home:out", args[args.IndexOf("--paths") + 1]);
    }

    [Fact]
    public void SearchUrl_uses_ytsearch1_and_strips_quotes()
    {
        Assert.Equal("ytsearch1:Pink Floyd Dogs", YtDlpDownloader.SearchUrl("Pink Floyd \"Dogs\""));
    }
}

public class ToolLocatorTests
{
    [Fact]
    public void Prefers_configured_file_then_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "vinyl-ripper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var locator = new ToolLocator(new Configuration.AppPaths(Path.Combine(root, "app")));
            var exe = Path.Combine(root, ToolLocator.YtDlpFileName);
            File.WriteAllText(exe, "");

            Assert.Equal(exe, locator.FindYtDlp(exe));
            Assert.Equal(exe, locator.FindYtDlp(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Falls_back_to_bundled_tools_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "vinyl-ripper-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new Configuration.AppPaths(root);
            paths.EnsureCreated();
            var locator = new ToolLocator(paths);
            File.WriteAllText(locator.BundledYtDlpPath, "");

            Assert.Equal(locator.BundledYtDlpPath, locator.FindYtDlp(null));
            Assert.Equal(locator.BundledYtDlpPath, locator.FindYtDlp(@"C:\no\existe\yt-dlp.exe"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindOnPath_returns_null_for_unknown_binary()
    {
        Assert.Null(ToolLocator.FindOnPath("seguro-que-esto-no-existe-12345.exe"));
    }
}
