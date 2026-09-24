using System.Diagnostics;
using VinylRipper.Configuration;

namespace VinylRipper.Tests;

public sealed class TempJanitorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vr-janitor-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public TempJanitorTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void FillTemp()
    {
        File.WriteAllText(Path.Combine(_paths.TempDirectory, "Dogs.webm.part"), "x");
        Directory.CreateDirectory(Path.Combine(_paths.TempDirectory, "previews"));
        File.WriteAllText(Path.Combine(_paths.TempDirectory, "previews", "preview-1-1.m4a"), "x");
    }

    [Fact]
    public void Arguments_roundtrip()
    {
        var start = new DateTime(2026, 9, 25, 10, 30, 0, DateTimeKind.Utc);
        var args = TempJanitor.BuildArguments(4321, start);

        Assert.True(TempJanitor.TryParse(args, out var pid, out var ticks));
        Assert.Equal(4321, pid);
        Assert.Equal(start.Ticks, ticks);
    }

    [Theory]
    [InlineData()]
    [InlineData("--otra-cosa", "1", "2")]
    [InlineData(TempJanitor.Argument, "no-es-un-pid", "2")]
    [InlineData(TempJanitor.Argument, "1")]
    [InlineData(TempJanitor.Argument, "-1", "2")]
    public void Normal_launches_are_not_the_janitor(params string[] args) =>
        Assert.False(TempJanitor.TryParse(args, out _, out _));

    [Fact]
    public void Waits_for_the_process_to_end_and_then_empties_temp()
    {
        FillTemp();
        // Un proceso que dura ~1 s, como si fuera la app.
        using var app = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 2 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        var startTicks = app.StartTime.ToUniversalTime().Ticks;

        var sw = Stopwatch.StartNew();
        TempJanitor.Run(app.Id, startTicks, _paths, TimeSpan.FromMilliseconds(20));

        Assert.True(app.HasExited);
        Assert.True(sw.ElapsedMilliseconds >= 500, $"No esperó al proceso ({sw.ElapsedMilliseconds} ms)");
        Assert.Empty(Directory.EnumerateFileSystemEntries(_paths.TempDirectory));
        Assert.True(Directory.Exists(_paths.TempDirectory));
    }

    [Fact]
    public void A_reused_pid_is_not_waited_for()
    {
        FillTemp();
        using var self = Process.GetCurrentProcess();
        var otherStart = self.StartTime.ToUniversalTime().AddSeconds(-5).Ticks; // mismo PID, otra "vida"

        var sw = Stopwatch.StartNew();
        TempJanitor.Run(self.Id, otherStart, _paths, TimeSpan.FromMilliseconds(20));

        Assert.True(sw.ElapsedMilliseconds < 2000); // no se ha quedado esperando a este proceso de tests
        Assert.Empty(Directory.EnumerateFileSystemEntries(_paths.TempDirectory));
    }

    [Fact]
    public void A_process_that_no_longer_exists_just_empties_temp()
    {
        FillTemp();
        using var gone = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        gone.WaitForExit();
        var pid = gone.Id;

        TempJanitor.Run(pid, 0, _paths, TimeSpan.FromMilliseconds(20));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_paths.TempDirectory));
    }
}
