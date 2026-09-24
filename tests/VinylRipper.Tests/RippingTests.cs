using VinylRipper.Discogs;
using VinylRipper.Ripping;

namespace VinylRipper.Tests;

public class OutputFoldersTests
{
    [Fact]
    public void NextName_is_ticks_and_grows()
    {
        var a = OutputFolders.NextName(new DateTime(2026, 1, 1));
        var b = OutputFolders.NextName(new DateTime(2026, 1, 2));

        Assert.True(OutputFolders.IsTicksFolderName(a));
        Assert.True(long.Parse(b) > long.Parse(a));
    }

    [Fact]
    public void CreateNext_creates_distinct_increasing_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "vinyl-ripper-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var first = OutputFolders.CreateNext(root);
            var second = OutputFolders.CreateNext(root);

            Assert.True(Directory.Exists(first));
            Assert.True(Directory.Exists(second));
            Assert.NotEqual(first, second);
            Assert.True(long.Parse(Path.GetFileName(second)) > long.Parse(Path.GetFileName(first)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("638000000000000000", true)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    [InlineData("12a", false)]
    public void IsTicksFolderName(string name, bool expected) => Assert.Equal(expected, OutputFolders.IsTicksFolderName(name));
}

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("AC/DC - Back In Black", "AC_DC - Back In Black")]
    [InlineData("What?: The <Best> \"Of\"|*", "What__ The _Best_ _Of___")]
    [InlineData("  varios   espacios  ", "varios espacios")]
    [InlineData("acaba en punto...", "acaba en punto")]
    [InlineData("con\ttab\ny\rcontrol", "contabycontrol")]
    public void Replaces_invalid_characters(string input, string expected) =>
        Assert.Equal(expected, FileNameSanitizer.Sanitize(input));

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    public void Prefixes_reserved_windows_names(string name) =>
        Assert.Equal("_" + name, FileNameSanitizer.Sanitize(name));

    [Fact]
    public void Empty_or_all_invalid_falls_back()
    {
        Assert.Equal("sin_titulo", FileNameSanitizer.Sanitize(""));
        Assert.Equal("x", FileNameSanitizer.Sanitize("...", fallback: "x"));
    }

    [Fact]
    public void Truncates_to_max_length()
    {
        var result = FileNameSanitizer.Sanitize(new string('a', 300), maxLength: 50);
        Assert.Equal(50, result.Length);
    }

    [Fact]
    public void Keeps_unicode()
    {
        Assert.Equal("Café Tacvba – Ré", FileNameSanitizer.Sanitize("Café Tacvba – Ré"));
    }
}

public class TrackMatcherTests
{
    private static readonly Video[] Videos =
    [
        new("https://youtu.be/1", "Pink Floyd - Dogs (Official Audio)", 1028),
        new("https://youtu.be/2", "Pink Floyd - Pigs (Three Different Ones)", 685),
        new("https://youtu.be/3", "Pink Floyd - Sheep", 620),
        new("https://youtu.be/4", "Making of Animals documentary", 3600),
    ];

    [Theory]
    [InlineData("Dogs", "https://youtu.be/1")]
    [InlineData("Pigs (Three Different Ones)", "https://youtu.be/2")]
    [InlineData("SHEEP", "https://youtu.be/3")]
    public void Finds_matching_video(string title, string expectedUri)
    {
        var video = TrackMatcher.FindVideo(new Track("A1", title, null, null), Videos);
        Assert.Equal(expectedUri, video?.Uri);
    }

    [Fact]
    public void Returns_null_when_nothing_matches()
    {
        Assert.Null(TrackMatcher.FindVideo(new Track("A1", "Pigs On The Wing (Part One)", null, null), Videos));
        Assert.Null(TrackMatcher.FindVideo(new Track("A1", "Dogs", null, null), []));
    }

    [Fact]
    public void Skips_already_used_videos()
    {
        var used = new HashSet<string> { "https://youtu.be/3" };
        Assert.Null(TrackMatcher.FindVideo(new Track("B2", "Sheep", null, null), Videos, used));
    }

    [Fact]
    public void Ignores_accents_and_punctuation()
    {
        var videos = new[] { new Video("u", "Héroes Del Silencio – Entre Dos Tierras (Video Oficial)", null) };
        var video = TrackMatcher.FindVideo(new Track("1", "Entre dos tierras", null, null), videos);
        Assert.NotNull(video);
    }

    [Theory]
    [InlineData("Héroes Del Silencio – ¡Entre dos tierras!", "heroes del silencio entre dos tierras")]
    [InlineData("  A   B ", "a b")]
    [InlineData("", "")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, TrackMatcher.Normalize(input));

    [Fact]
    public void BuildSearchQuery_prefers_track_artist_and_skips_various()
    {
        var release = new ReleaseDetails(1, "Various", "Compilation", 2000, [], []);
        Assert.Equal("Dogs", TrackMatcher.BuildSearchQuery(release, new Track("1", "Dogs", null, null)));
        Assert.Equal("Pink Floyd Dogs", TrackMatcher.BuildSearchQuery(release, new Track("1", "Dogs", "Pink Floyd", null)));

        var single = new ReleaseDetails(1, "Pink Floyd", "Animals", 1977, [], []);
        Assert.Equal("Pink Floyd Dogs", TrackMatcher.BuildSearchQuery(single, new Track("1", "Dogs", null, null)));
    }
}

public class RipServiceNamingTests
{
    [Fact]
    public void Release_folder_name_includes_artist_title_and_year()
    {
        var release = new ReleaseDetails(1, "AC/DC", "Back In Black", 1980, [], []);
        Assert.Equal("AC_DC - Back In Black (1980)", RipService.BuildReleaseFolderName(release));
    }

    [Fact]
    public void Release_folder_name_without_artist_or_year()
    {
        var release = new ReleaseDetails(7, "", "Sin Nombre", null, [], []);
        Assert.Equal("Sin Nombre", RipService.BuildReleaseFolderName(release));
    }

    [Fact]
    public void Track_file_name_is_zero_padded_and_includes_track_artist()
    {
        Assert.Equal("03 - Sheep", RipService.BuildTrackFileName(3, 5, new Track("A3", "Sheep", null, null)));
        Assert.Equal("007 - Nirvana - Lithium", RipService.BuildTrackFileName(7, 120, new Track("B1", "Lithium", "Nirvana", null)));
    }
}
