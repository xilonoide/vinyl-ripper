using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using VinylRipper.Discogs;
using VinylRipper.Ripping;
using VinylRipper.YouTube;

namespace VinylRipper.Tests;

public class CoverUrlParsingTests
{
    private static string? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return DiscogsClient.ParseCoverUrl(doc.RootElement);
    }

    [Fact]
    public void Prefers_primary_image()
    {
        var url = Parse("""
            { "images": [
                { "type": "secondary", "uri": "https://i.discogs.com/back.jpg" },
                { "type": "primary",   "uri": "https://i.discogs.com/front.jpg" }
            ] }
            """);
        Assert.Equal("https://i.discogs.com/front.jpg", url);
    }

    [Fact]
    public void Falls_back_to_first_image_with_uri()
    {
        var url = Parse("""
            { "images": [
                { "type": "secondary", "uri": "" },
                { "type": "secondary", "resource_url": "https://i.discogs.com/a.jpg" },
                { "type": "secondary", "uri": "https://i.discogs.com/b.jpg" }
            ] }
            """);
        Assert.Equal("https://i.discogs.com/a.jpg", url);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "images": [] }""")]
    [InlineData("""{ "images": null }""")]
    [InlineData("""{ "images": [ { "type": "primary", "uri": "" } ] }""")]
    public void Null_when_there_is_no_image(string json) => Assert.Null(Parse(json));
}

public class CoverDownloadTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static (DiscogsClient, FakeHandler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri(DiscogsClient.BaseUrl) };
        return (new DiscogsClient(http, "secreto"), handler);
    }

    [Fact]
    public async Task GetRelease_exposes_cover_url()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                { "id": 7, "title": "Animals", "artists": [{ "name": "Pink Floyd" }],
                  "images": [ { "type": "primary", "uri": "https://i.discogs.com/animals.jpg" } ] }
                """, Encoding.UTF8, "application/json"),
        });

        var details = await client.GetReleaseAsync(7);

        Assert.Equal("https://i.discogs.com/animals.jpg", details.CoverUrl);
    }

    [Fact]
    public async Task Downloads_image_without_sending_the_token()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(jpeg) });

        var bytes = await client.DownloadImageAsync("https://i.discogs.com/animals.jpg");

        Assert.Equal(jpeg, bytes);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://i.discogs.com/animals.jpg", request.RequestUri!.AbsoluteUri);
        Assert.Null(request.Headers.Authorization);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "image/jpeg");
    }

    [Fact]
    public async Task Http_error_becomes_DiscogsException()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<DiscogsException>(() => client.DownloadImageAsync("https://i.discogs.com/x.jpg"));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Empty_body_becomes_DiscogsException()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        await Assert.ThrowsAsync<DiscogsException>(() => client.DownloadImageAsync("https://i.discogs.com/x.jpg"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("releases/1")]
    [InlineData("file:///C:/Windows/win.ini")]
    public async Task Rejects_non_http_urls(string url)
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await Assert.ThrowsAsync<DiscogsException>(() => client.DownloadImageAsync(url));
        Assert.Empty(handler.Requests);
    }
}

public class CoverArtEmbedderTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ".jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A }, ".png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, ".gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, ".webp")]
    [InlineData(new byte[] { 1, 2, 3 }, ".jpg")]
    public void Detects_image_format(byte[] bytes, string expected) =>
        Assert.Equal(expected, CoverArtEmbedder.DetectExtension(bytes));

    [Fact]
    public void Arguments_copy_audio_and_write_id3v23_front_cover()
    {
        var args = CoverArtEmbedder.BuildArguments(@"C:\a\Dogs.mp3", @"C:\t\cover-1.jpg", @"C:\t\out.mp3").ToList();

        Assert.Equal(@"C:\t\out.mp3", args[^1]);
        AssertPair(args, "-map", "0:a");
        AssertPair(args, "-map", "1:v");
        AssertPair(args, "-c:a", "copy");
        AssertPair(args, "-c:v", "copy");
        AssertPair(args, "-id3v2_version", "3");
        AssertPair(args, "-metadata:s:v", "comment=Cover (front)");
        AssertPair(args, "-disposition:v", "attached_pic");
        AssertPair(args, "-f", "mp3");
        Assert.Equal(@"C:\a\Dogs.mp3", args[args.IndexOf("-i") + 1]);
    }

    [Theory]
    [InlineData(".png", "copy")]
    [InlineData(".JPEG", "copy")]
    [InlineData(".webp", "mjpeg")]
    [InlineData(".gif", "mjpeg")]
    public void Only_jpeg_and_png_are_copied_as_is(string ext, string codec)
    {
        var args = CoverArtEmbedder.BuildArguments("in.mp3", "cover" + ext, "out.mp3").ToList();
        AssertPair(args, "-c:v", codec);
    }

    [Fact]
    public void SaveCover_uses_extension_from_content()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vr-cover-" + Guid.NewGuid().ToString("N"));
        try
        {
            var embedder = new CoverArtEmbedder("ffmpeg", dir);
            var path = embedder.SaveCover([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A], "cover-42");
            Assert.Equal(Path.Combine(dir, "cover-42.png"), path);
            Assert.True(File.Exists(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Missing_ffmpeg_becomes_CoverArtException_and_keeps_the_mp3()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vr-cover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mp3 = Path.Combine(dir, "Dogs.mp3");
            File.WriteAllBytes(mp3, [1, 2, 3]);
            var embedder = new CoverArtEmbedder(Path.Combine(dir, "no-existe.exe"), Path.Combine(dir, "temp"));

            await Assert.ThrowsAsync<CoverArtException>(() => embedder.EmbedAsync(mp3, Path.Combine(dir, "c.jpg")));

            Assert.Equal([1, 2, 3], File.ReadAllBytes(mp3));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static void AssertPair(List<string> args, string name, string value)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i] == name && args[i + 1] == value) return;
        Assert.Fail($"Falta '{name} {value}' en: {string.Join(' ', args)}");
    }
}

/// <summary>
/// Pruebas contra el ffmpeg real (el que usa la app para convertir a MP3). Si no está en el PATH,
/// no hay nada que comprobar y se dan por buenas.
/// </summary>
public class CoverArtEmbedderFfmpegTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vr-ffmpeg-" + Guid.NewGuid().ToString("N"));
    private readonly string? _ffmpeg = ToolLocator.FindOnPath(ToolLocator.FfmpegFileName);

    public CoverArtEmbedderFfmpegTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Embeds_front_cover_in_id3v23_and_keeps_existing_tags()
    {
        if (_ffmpeg is null) return;
        var mp3 = await MakeMp3Async("Dogs.mp3", title: "Dogs", artist: "Pink Floyd");
        var cover = await MakeJpegAsync("cover.jpg");
        var temp = Path.Combine(_dir, "temp");
        var originalLength = new FileInfo(mp3).Length;

        await new CoverArtEmbedder(_ffmpeg, temp).EmbedAsync(mp3, cover);

        var bytes = File.ReadAllBytes(mp3);
        Assert.Equal("ID3"u8.ToArray(), bytes[..3]);
        Assert.Equal(3, bytes[3]);                                   // ID3v2.3
        Assert.Equal(1, Count(bytes, "APIC"u8));                     // una sola portada
        Assert.True(Count(bytes, "image/jpeg"u8) >= 1);
        Assert.True(Count(bytes, "Dogs"u8) >= 1);                    // TIT2 conservado
        Assert.True(Count(bytes, "Pink Floyd"u8) >= 1);              // TPE1 conservado
        Assert.True(bytes.Length > originalLength);
        Assert.Empty(Directory.GetFiles(temp));                      // sin intermedios
    }

    [Fact]
    public async Task Embedding_twice_replaces_the_cover_instead_of_adding_another()
    {
        if (_ffmpeg is null) return;
        var mp3 = await MakeMp3Async("Sheep.mp3", title: "Sheep", artist: "Pink Floyd");
        var cover = await MakeJpegAsync("cover.jpg");
        var embedder = new CoverArtEmbedder(_ffmpeg, Path.Combine(_dir, "temp"));

        await embedder.EmbedAsync(mp3, cover);
        await embedder.EmbedAsync(mp3, cover);

        Assert.Equal(1, Count(File.ReadAllBytes(mp3), "APIC"u8));
    }

    [Fact]
    public async Task Converts_non_jpeg_covers()
    {
        if (_ffmpeg is null) return;
        var mp3 = await MakeMp3Async("Pigs.mp3", title: "Pigs", artist: "Pink Floyd");
        var cover = Path.Combine(_dir, "cover.webp");
        await RunFfmpegAsync("-f", "lavfi", "-i", "color=c=blue:s=32x32", "-frames:v", "1", cover);
        if (!File.Exists(cover)) return;                              // ffmpeg sin codificador WebP

        await new CoverArtEmbedder(_ffmpeg, Path.Combine(_dir, "temp")).EmbedAsync(mp3, cover);

        var bytes = File.ReadAllBytes(mp3);
        Assert.Equal(1, Count(bytes, "APIC"u8));
        Assert.True(Count(bytes, "image/jpeg"u8) >= 1);
    }

    [Fact]
    public async Task Broken_cover_throws_and_leaves_the_mp3_untouched()
    {
        if (_ffmpeg is null) return;
        var mp3 = await MakeMp3Async("Dogs.mp3", title: "Dogs", artist: "Pink Floyd");
        var before = File.ReadAllBytes(mp3);
        var cover = Path.Combine(_dir, "roto.jpg");
        File.WriteAllText(cover, "esto no es una imagen");

        await Assert.ThrowsAsync<CoverArtException>(() =>
            new CoverArtEmbedder(_ffmpeg, Path.Combine(_dir, "temp")).EmbedAsync(mp3, cover));

        Assert.Equal(before, File.ReadAllBytes(mp3));
    }

    private async Task<string> MakeMp3Async(string name, string title, string artist)
    {
        var path = Path.Combine(_dir, name);
        await RunFfmpegAsync("-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo", "-t", "1",
            "-c:a", "libmp3lame", "-b:a", "128k", "-metadata", $"title={title}", "-metadata", $"artist={artist}", path);
        Assert.True(File.Exists(path), "ffmpeg no generó el MP3 de prueba");
        return path;
    }

    private async Task<string> MakeJpegAsync(string name)
    {
        var path = Path.Combine(_dir, name);
        await RunFfmpegAsync("-f", "lavfi", "-i", "color=c=red:s=32x32", "-frames:v", "1", path);
        Assert.True(File.Exists(path), "ffmpeg no generó la portada de prueba");
        return path;
    }

    private async Task RunFfmpegAsync(params string[] args)
    {
        var psi = new ProcessStartInfo(_ffmpeg!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        await stderr;
    }

    private static int Count(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        var count = 0;
        var span = haystack.AsSpan();
        for (int i; (i = span.IndexOf(needle)) >= 0; span = span[(i + needle.Length)..]) count++;
        return count;
    }
}
