using System.Diagnostics;
using System.Text;

namespace VinylRipper.Ripping;

public sealed class CoverArtException : Exception
{
    public CoverArtException(string message) : base(message) { }
}

/// <summary>
/// Incrusta una portada en un MP3 ya generado usando ffmpeg (el mismo que usa yt-dlp para convertir).
/// El audio se copia tal cual, sin recodificar; se conservan las etiquetas que ya tuviera el archivo y
/// la imagen entra como frame <c>APIC</c> de tipo "Cover (front)". La etiqueta se escribe en ID3v2.3,
/// la versión que entienden todos los reproductores y el Explorador de Windows.
/// </summary>
/// <param name="ffmpegPath">Ruta al ejecutable de ffmpeg.</param>
/// <param name="tempDirectory">Dónde dejar el MP3 intermedio y las portadas descargadas.</param>
public sealed class CoverArtEmbedder(string ffmpegPath, string tempDirectory)
{
    public string TempDirectory { get; } = tempDirectory;

    /// <summary>
    /// Guarda la imagen en la carpeta temporal con la extensión que corresponde a su contenido
    /// (JPEG, PNG…) y devuelve la ruta. ffmpeg deduce el formato de la extensión.
    /// </summary>
    public string SaveCover(byte[] image, string name)
    {
        Directory.CreateDirectory(TempDirectory);
        var path = Path.Combine(TempDirectory, name + DetectExtension(image));
        File.WriteAllBytes(path, image);
        return path;
    }

    /// <summary>Sustituye <paramref name="mp3Path"/> por una copia con la portada incrustada.</summary>
    public async Task EmbedAsync(string mp3Path, string coverPath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(TempDirectory);
        var tempOutput = Path.Combine(TempDirectory, $"cover-{Guid.NewGuid():N}.mp3");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in BuildArguments(mp3Path, coverPath, tempOutput))
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        var log = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };

        try
        {
            try
            {
                process.Start();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                throw new CoverArtException($"No se pudo ejecutar ffmpeg en '{ffmpegPath}': {ex.Message}");
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

            if (process.ExitCode != 0 || !File.Exists(tempOutput) || new FileInfo(tempOutput).Length == 0)
            {
                string output;
                lock (log) output = log.ToString();
                var last = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                throw new CoverArtException("ffmpeg no pudo incrustar la portada" + (last is null ? "." : ": " + last));
            }

            File.Move(tempOutput, mp3Path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch (IOException) { /* se limpia al arrancar */ }
        }
    }

    internal static IEnumerable<string> BuildArguments(string mp3Path, string coverPath, string outputPath)
    {
        yield return "-hide_banner";
        yield return "-nostdin";
        yield return "-loglevel"; yield return "error";
        yield return "-y";
        yield return "-i"; yield return mp3Path;
        yield return "-i"; yield return coverPath;
        // Sólo el audio del MP3 (si ya traía una imagen, se sustituye) y la portada nueva.
        yield return "-map"; yield return "0:a";
        yield return "-map"; yield return "1:v";
        yield return "-c:a"; yield return "copy";
        // JPEG y PNG van tal cual; cualquier otro formato se convierte a JPEG, que es lo que admite ID3.
        var ext = Path.GetExtension(coverPath);
        var copyImage = ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
        yield return "-c:v"; yield return copyImage ? "copy" : "mjpeg";
        yield return "-map_metadata"; yield return "0";
        yield return "-id3v2_version"; yield return "3";
        yield return "-metadata:s:v"; yield return "title=Album cover";
        yield return "-metadata:s:v"; yield return "comment=Cover (front)";
        yield return "-disposition:v"; yield return "attached_pic";
        yield return "-f"; yield return "mp3";
        yield return outputPath;
    }

    /// <summary>Extensión según la firma del archivo; Discogs sirve casi siempre JPEG.</summary>
    internal static string DetectExtension(byte[] image) => image switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => ".png",
        [0x47, 0x49, 0x46, ..] => ".gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => ".webp",
        _ => ".jpg",
    };
}
