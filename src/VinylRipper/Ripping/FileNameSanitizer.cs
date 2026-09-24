using System.Text;

namespace VinylRipper.Ripping;

/// <summary>Convierte títulos arbitrarios en nombres de archivo/carpeta válidos en Windows y Linux.</summary>
public static class FileNameSanitizer
{
    // Unión de los caracteres inválidos de Windows (superconjunto de los de Linux) más controles.
    private static readonly HashSet<char> Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Sanitize(string name, int maxLength = 120, string fallback = "sin_titulo")
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsControl(ch)) continue;
            sb.Append(Invalid.Contains(ch) ? '_' : ch);
        }

        // Espacios repetidos → uno; sin espacios/puntos en los extremos (Windows los recorta y confunde).
        var cleaned = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');

        if (cleaned.Length > maxLength)
            cleaned = cleaned[..maxLength].TrimEnd(' ', '.');

        if (cleaned.Length == 0 || ReservedNames.Contains(cleaned))
            cleaned = cleaned.Length == 0 ? fallback : "_" + cleaned;

        return cleaned;
    }
}
