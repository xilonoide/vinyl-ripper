using System.Runtime.InteropServices;
using VinylRipper.Configuration;

namespace VinylRipper.YouTube;

/// <summary>
/// Localiza ejecutables externos (yt-dlp, ffmpeg): primero la ruta configurada, luego la carpeta
/// <c>tools/</c> de la aplicación y por último el PATH del sistema.
/// </summary>
public sealed class ToolLocator
{
    private readonly AppPaths _paths;

    public ToolLocator(AppPaths paths)
    {
        _paths = paths;
    }

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string YtDlpFileName => IsWindows ? "yt-dlp.exe" : "yt-dlp";
    public static string FfmpegFileName => IsWindows ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>Ruta donde se descarga yt-dlp cuando no está instalado.</summary>
    public string BundledYtDlpPath => Path.Combine(_paths.ToolsDirectory, YtDlpFileName);

    public string? FindYtDlp(string? configuredPath) => Find(configuredPath, YtDlpFileName, BundledYtDlpPath);

    public string? FindFfmpeg(string? configuredPath) => Find(configuredPath, FfmpegFileName, Path.Combine(_paths.ToolsDirectory, FfmpegFileName));

    private static string? Find(string? configuredPath, string fileName, string bundledPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (File.Exists(configuredPath)) return configuredPath;
            var inDir = Path.Combine(configuredPath, fileName);
            if (Directory.Exists(configuredPath) && File.Exists(inDir)) return inDir;
        }

        if (File.Exists(bundledPath)) return bundledPath;

        return FindOnPath(fileName);
    }

    internal static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* entradas del PATH con caracteres inválidos */ }
        }
        return null;
    }
}
