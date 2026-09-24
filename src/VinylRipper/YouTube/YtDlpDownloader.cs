using System.Diagnostics;
using System.Text;

namespace VinylRipper.YouTube;

/// <param name="TempDirectory">Carpeta para los intermedios de yt-dlp; null = junto al MP3 final.</param>
public sealed record YtDlpOptions(string YtDlpPath, string? FfmpegPath, int AudioQuality = 0, string? TempDirectory = null);

public sealed class YtDlpException : Exception
{
    public YtDlpException(string message) : base(message) { }
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
}

/// <summary>
/// Envuelve el proceso yt-dlp para extraer el audio de un vídeo (o del primer resultado de una
/// búsqueda) a MP3. Reporta progreso por porcentaje y devuelve la ruta del archivo generado.
/// </summary>
public sealed class YtDlpDownloader
{
    private readonly YtDlpOptions _options;

    public YtDlpDownloader(YtDlpOptions options)
    {
        _options = options;
    }

    /// <summary>Construye la URL de búsqueda que hace que yt-dlp descargue el primer resultado.</summary>
    public static string SearchUrl(string query) =>
        "ytsearch1:" + string.Join(' ', query.Replace('"', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Descarga <paramref name="urlOrSearch"/> como MP3 en <paramref name="outputDirectory"/> con el
    /// nombre <paramref name="fileNameWithoutExtension"/>.mp3.
    /// </summary>
    public async Task<string> DownloadMp3Async(string urlOrSearch, string outputDirectory, string fileNameWithoutExtension,
        IProgress<YtDlpProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        if (_options.TempDirectory is not null) Directory.CreateDirectory(_options.TempDirectory);
        var expected = Path.Combine(outputDirectory, fileNameWithoutExtension + ".mp3");

        var psi = new ProcessStartInfo
        {
            FileName = _options.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in BuildArguments(urlOrSearch, outputDirectory, fileNameWithoutExtension))
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var log = new StringBuilder();
        string? destination = null;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (log) log.AppendLine(e.Data);
            if (YtDlpProgressParser.TryParse(e.Data, out var p)) progress?.Report(p);
            destination ??= YtDlpProgressParser.TryParseDestination(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (log) log.AppendLine(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new YtDlpException($"No se pudo ejecutar yt-dlp en '{_options.YtDlpPath}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ya terminó */ }
            throw;
        }

        string output;
        lock (log) output = log.ToString();

        if (process.ExitCode != 0)
            throw new YtDlpException(SummarizeError(output)) { ExitCode = process.ExitCode, Output = output };

        if (File.Exists(expected)) return expected;
        if (destination is not null && File.Exists(destination)) return destination;

        throw new YtDlpException("yt-dlp terminó sin generar el MP3.") { ExitCode = 0, Output = output };
    }

    internal IEnumerable<string> BuildArguments(string urlOrSearch, string outputDirectory, string fileNameWithoutExtension)
    {
        yield return "--no-playlist";
        yield return "--newline";
        yield return "--no-colors";
        yield return "--no-mtime";
        yield return "--extract-audio";
        yield return "--audio-format"; yield return "mp3";
        yield return "--audio-quality"; yield return Math.Clamp(_options.AudioQuality, 0, 9).ToString();
        yield return "--embed-metadata";
        yield return "--default-search"; yield return "ytsearch1";

        // La plantilla debe ser relativa para que yt-dlp respete --paths: los intermedios van a
        // "temp" y sólo el MP3 final se mueve a "home". Nombre fijo; yt-dlp pone la extensión.
        yield return "--paths"; yield return "home:" + outputDirectory;
        if (!string.IsNullOrWhiteSpace(_options.TempDirectory))
        {
            yield return "--paths"; yield return "temp:" + _options.TempDirectory;
        }
        yield return "--output"; yield return fileNameWithoutExtension + ".%(ext)s";

        if (!string.IsNullOrWhiteSpace(_options.FfmpegPath))
        {
            yield return "--ffmpeg-location";
            yield return _options.FfmpegPath;
        }

        yield return "--";
        yield return urlOrSearch;
    }

    private static string SummarizeError(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var error = lines.LastOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase));
        if (error is not null)
        {
            if (error.Contains("ffprobe and ffmpeg not found", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("ffmpeg not found", StringComparison.OrdinalIgnoreCase))
                return "yt-dlp necesita ffmpeg para convertir a MP3 y no lo encuentra. Instálalo o indica su ruta en la configuración.";
            return error;
        }
        return lines.LastOrDefault() ?? "yt-dlp falló sin mensaje.";
    }
}
