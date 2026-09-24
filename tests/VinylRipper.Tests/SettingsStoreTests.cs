using VinylRipper.Configuration;

namespace VinylRipper.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vinyl-ripper-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public SettingsStoreTests()
    {
        _paths = new AppPaths(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var settings = new SettingsStore(_paths).Load();

        Assert.Null(settings.EncryptedDiscogsToken);
        Assert.Equal(0, settings.AudioQuality);
        Assert.Empty(settings.SelectedTracks);
        Assert.Equal(1280, settings.Window.Width);
    }

    [Fact]
    public void Save_then_Load_roundtrips_everything()
    {
        var store = new SettingsStore(_paths);
        var settings = new AppSettings
        {
            EncryptedDiscogsToken = "blob",
            SelectedListKey = "Wantlist:0",
            OutputRoot = @"D:\musica",
            LastOutputFolder = @"D:\musica\638000000000000000",
            YtDlpPath = @"C:\tools\yt-dlp.exe",
            AudioQuality = 5,
            SearchFilter = "pink",
            SelectedTracks = [new SavedTrack { ReleaseId = 42, Artist = "Pink Floyd", ReleaseTitle = "Animals", Year = 1977, Format = "Vinyl", Index = 2, TotalTracks = 5, Position = "A2", TrackTitle = "Dogs" }],
            Window = new WindowPlacement { Left = 10, Top = 20, Width = 800, Height = 600, Maximized = true },
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.True(File.Exists(_paths.SettingsFile));
        Assert.Equal("blob", loaded.EncryptedDiscogsToken);
        Assert.Equal("Wantlist:0", loaded.SelectedListKey);
        Assert.Equal(@"D:\musica", loaded.OutputRoot);
        Assert.Equal(@"D:\musica\638000000000000000", loaded.LastOutputFolder);
        Assert.Equal(@"C:\tools\yt-dlp.exe", loaded.YtDlpPath);
        Assert.Null(loaded.FfmpegPath);
        Assert.Equal(5, loaded.AudioQuality);
        Assert.Equal("pink", loaded.SearchFilter);
        var r = Assert.Single(loaded.SelectedTracks);
        Assert.Equal(42, r.ReleaseId);
        Assert.Equal("Animals", r.ReleaseTitle);
        Assert.Equal("Dogs", r.TrackTitle);
        Assert.Equal(2, r.Index);
        Assert.Equal(1977, r.Year);
        Assert.True(loaded.Window.Maximized);
        Assert.Equal(10, loaded.Window.Left);
        Assert.True(loaded.Window.HasPosition);
    }

    [Fact]
    public void Save_creates_directories_and_no_temp_file_is_left()
    {
        var store = new SettingsStore(_paths);
        store.Save(new AppSettings());

        Assert.True(Directory.Exists(_paths.Root));
        Assert.True(Directory.Exists(_paths.ToolsDirectory));
        Assert.True(Directory.Exists(_paths.TempDirectory));
        Assert.False(File.Exists(_paths.SettingsFile + ".tmp"));
    }

    [Fact]
    public void ClearTemp_empties_temp_but_keeps_the_folder_and_other_data()
    {
        _paths.EnsureCreated();
        File.WriteAllText(Path.Combine(_paths.TempDirectory, "x.webm"), "");
        File.WriteAllText(Path.Combine(_paths.TempDirectory, "x.mp3.part"), "");
        Directory.CreateDirectory(Path.Combine(_paths.TempDirectory, "sub"));
        File.WriteAllText(Path.Combine(_paths.TempDirectory, "sub", "y.ytdl"), "");
        File.WriteAllText(Path.Combine(_paths.ToolsDirectory, "yt-dlp.exe"), "");

        var removed = _paths.ClearTemp();

        Assert.Equal(3, removed);
        Assert.True(Directory.Exists(_paths.TempDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_paths.TempDirectory));
        Assert.True(File.Exists(Path.Combine(_paths.ToolsDirectory, "yt-dlp.exe")));
    }

    [Fact]
    public void ClearTemp_is_a_no_op_when_folder_is_missing()
    {
        Assert.Equal(0, _paths.ClearTemp());
    }

    [Fact]
    public void Load_quarantines_corrupt_file_and_returns_defaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_paths.SettingsFile, "{ esto no es json");

        var settings = new SettingsStore(_paths).Load();

        Assert.NotNull(settings);
        Assert.False(File.Exists(_paths.SettingsFile));
        Assert.True(File.Exists(_paths.SettingsFile + ".corrupt"));
    }

    [Fact]
    public void Null_properties_are_omitted_from_json()
    {
        var store = new SettingsStore(_paths);
        store.Save(new AppSettings());

        var json = File.ReadAllText(_paths.SettingsFile);
        Assert.DoesNotContain("encryptedDiscogsToken", json);
        Assert.Contains("audioQuality", json);
    }

    /// <summary>
    /// Lo que hace un antivirus al analizar el archivo recién escrito: lo abre sin permitir borrarlo ni
    /// renombrar encima. Mientras está abierto, File.Move da "Access to the path is denied".
    /// </summary>
    private FileStream HoldOpenLikeAnAntivirus() =>
        new(_paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read);

    [Fact]
    public async Task Save_waits_for_a_briefly_locked_file()
    {
        var store = new SettingsStore(_paths);
        store.Save(new AppSettings { SearchFilter = "antes" });

        var handle = HoldOpenLikeAnAntivirus();
        var release = Task.Delay(150).ContinueWith(_ => handle.Dispose());

        store.Save(new AppSettings { SearchFilter = "después" });
        await release;

        Assert.Equal("después", store.Load().SearchFilter);
        Assert.False(File.Exists(_paths.SettingsFile + ".tmp"));
    }

    [Fact]
    public void Save_gives_up_on_a_file_that_stays_locked_and_keeps_the_previous_one()
    {
        var store = new SettingsStore(_paths);
        store.Save(new AppSettings { SearchFilter = "antes" });

        using (HoldOpenLikeAnAntivirus())
        {
            var ex = Record.Exception(() => store.Save(new AppSettings { SearchFilter = "después" }));
            Assert.True(ex is UnauthorizedAccessException or IOException, $"Excepción inesperada: {ex}");
        }

        Assert.Equal("antes", store.Load().SearchFilter);
    }

    [Fact]
    public void Retry_delays_grow_and_are_capped()
    {
        var delays = Enumerable.Range(1, SettingsStore.SaveAttempts - 1).Select(a => SettingsStore.RetryDelay(a).TotalMilliseconds).ToList();

        Assert.Equal([25, 50, 100, 200, 400, 400, 400], delays);
        Assert.True(delays.Sum() < 2000);
    }
}
