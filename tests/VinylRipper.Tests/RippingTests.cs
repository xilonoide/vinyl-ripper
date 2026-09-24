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

        Assert.All(a, c => Assert.True(char.IsAsciiDigit(c)));
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
        Assert.Equal("Dogs", TrackMatcher.BuildSearchQuery("Various", new Track("1", "Dogs", null, null)));
        Assert.Equal("Pink Floyd Dogs", TrackMatcher.BuildSearchQuery("Various", new Track("1", "Dogs", "Pink Floyd", null)));
        Assert.Equal("Pink Floyd Dogs", TrackMatcher.BuildSearchQuery("Pink Floyd", new Track("1", "Dogs", null, null)));
    }
}

public class RipServiceNamingTests
{
    [Fact]
    public void Release_folder_name_includes_artist_title_and_year()
    {
        var release = new ReleaseSummary(1, "AC/DC", "Back In Black", 1980, null, null);
        Assert.Equal("AC_DC - Back In Black (1980)", RipService.BuildReleaseFolderName(release));
    }

    [Fact]
    public void Release_folder_name_without_artist_or_year()
    {
        var release = new ReleaseSummary(7, "", "Sin Nombre", null, null, null);
        Assert.Equal("Sin Nombre", RipService.BuildReleaseFolderName(release));
    }

    [Fact]
    public void TrackSelection_key_and_position()
    {
        var release = new ReleaseSummary(1, "A", "B", null, null, null);
        var sel = new TrackSelection(release, new Track("A2", "Dogs", null, null), 2, 5);
        Assert.Equal("1:2", sel.Key);
        Assert.Equal("A2", sel.DisplayPosition);
        Assert.Equal("3", new TrackSelection(release, new Track("", "X", null, null), 3, 5).DisplayPosition);
    }

    [Fact]
    public void Track_file_name_is_title_with_optional_track_artist()
    {
        Assert.Equal("Sheep", RipService.BuildTrackFileName(new Track("A3", "Sheep", null, null)));
        Assert.Equal("Nirvana - Lithium", RipService.BuildTrackFileName(new Track("B1", "Lithium", "Nirvana", null)));
        Assert.Equal("AC_DC - T.N.T", RipService.BuildTrackFileName(new Track("B2", "T.N.T.", "AC/DC", null)));
        // Un título que se queda vacío al sanear cae en la posición del disco.
        Assert.Equal("A1", RipService.BuildTrackFileName(new Track("A1", "...", null, null)));
    }

    [Fact]
    public void Repeated_titles_are_distinguished_by_vinyl_position()
    {
        Track[] tracks =
        [
            new("A1", "Anonim", null, null),
            new("A2", "Anonim", null, null),
            new("A3", "Interludio", null, null),
            new("B1", "Anonim", null, null),
            new("B2", "Final", null, null),
        ];

        var names = RipService.BuildTrackFileNames(tracks);

        Assert.Equal(["Anonim A1", "Anonim A2", "Interludio", "Anonim B1", "Final"], names);
    }

    [Fact]
    public void Repeated_titles_without_position_fall_back_to_numeric_suffix()
    {
        Track[] tracks = [new("", "Anonim", null, null), new("", "Anonim", null, null), new("", "anonim", null, null)];

        var names = RipService.BuildTrackFileNames(tracks);

        Assert.Equal(["Anonim", "Anonim (2)", "anonim (3)"], names);
    }

    [Fact]
    public void Unique_titles_keep_plain_names()
    {
        Track[] tracks = [new("A1", "Dogs", null, null), new("A2", "Sheep", null, null)];
        Assert.Equal(["Dogs", "Sheep"], RipService.BuildTrackFileNames(tracks));
    }

    [Fact]
    public void Duplicate_track_titles_get_a_numeric_suffix()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Intro", RipService.UniqueName("Intro", used));
        Assert.Equal("Intro (2)", RipService.UniqueName("Intro", used));
        Assert.Equal("Intro (3)", RipService.UniqueName("Intro", used));
        Assert.Equal("intro (4)", RipService.UniqueName("intro", used)); // sin distinguir mayúsculas
        Assert.Equal("Outro", RipService.UniqueName("Outro", used));
    }
}
