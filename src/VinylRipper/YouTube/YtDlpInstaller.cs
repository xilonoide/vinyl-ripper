using System.Runtime.InteropServices;

namespace VinylRipper.YouTube;

/// <summary>Progreso de una descarga de archivo: bytes recibidos y total (si se conoce).</summary>
public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Percent => TotalBytes is { } t and > 0 ? 100.0 * BytesReceived / t : null;
}

/// <summary>
/// Descarga la última release de yt-dlp desde GitHub a la carpeta <c>tools/</c> de la aplicación.
/// </summary>
public sealed class YtDlpInstaller
{
    private const string ReleaseBase = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";

    private readonly HttpClient _http;
    private readonly ToolLocator _locator;

    public YtDlpInstaller(HttpClient http, ToolLocator locator)
    {
        _http = http;
        _locator = locator;
    }

    /// <summary>Nombre del asset de GitHub adecuado a la plataforma actual.</summary>
    public static string AssetName
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "yt-dlp.exe";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "yt-dlp_macos";
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "yt-dlp_linux_aarch64" : "yt-dlp_linux";
        }
    }

    public async Task<string> InstallAsync(IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var target = _locator.BundledYtDlpPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".download";

        using var response = await _http.GetAsync(ReleaseBase + AssetName, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = File.Create(tmp))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                progress?.Report(new DownloadProgress(received, total));
            }
        }

        File.Move(tmp, target, overwrite: true);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                         UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        return target;
    }
}
