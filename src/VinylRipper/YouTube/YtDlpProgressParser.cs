using System.Globalization;
using System.Text.RegularExpressions;

namespace VinylRipper.YouTube;

/// <summary>Una línea de progreso de yt-dlp ya interpretada.</summary>
public readonly record struct YtDlpProgress(double Percent, string? Speed, string? Eta);

/// <summary>
/// Interpreta la salida de <c>yt-dlp --newline</c>. Las líneas útiles tienen la forma
/// <c>[download]  45.2% of 3.40MiB at 1.20MiB/s ETA 00:02</c>.
/// </summary>
public static partial class YtDlpProgressParser
{
    public static bool TryParse(string? line, out YtDlpProgress progress)
    {
        progress = default;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var m = ProgressLine().Match(line);
        if (!m.Success) return false;

        if (!double.TryParse(m.Groups["pct"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            return false;

        progress = new YtDlpProgress(
            Math.Clamp(pct, 0, 100),
            m.Groups["speed"].Success ? m.Groups["speed"].Value : null,
            m.Groups["eta"].Success ? m.Groups["eta"].Value : null);
        return true;
    }

    /// <summary>Devuelve la ruta del archivo final si la línea es el aviso de destino de ExtractAudio.</summary>
    public static string? TryParseDestination(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var m = DestinationLine().Match(line);
        return m.Success ? m.Groups["path"].Value.Trim() : null;
    }

    [GeneratedRegex(@"^\[download\]\s+(?<pct>\d{1,3}(?:\.\d+)?)%(?:\s+of\s+~?\s*\S+)?(?:\s+at\s+(?<speed>.+?))?(?:\s+ETA\s+(?<eta>\S+))?(?:\s+in\s+\S+)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressLine();

    [GeneratedRegex(@"^\[ExtractAudio\]\s+Destination:\s+(?<path>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DestinationLine();
}
