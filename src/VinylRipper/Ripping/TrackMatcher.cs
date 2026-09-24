using System.Globalization;
using System.Text;
using VinylRipper.Discogs;

namespace VinylRipper.Ripping;

/// <summary>
/// Empareja las pistas de un disco con los vídeos de YouTube que Discogs tiene asociados a la
/// edición. Cuando no hay vídeo razonable devuelve null y el llamador recurre a una búsqueda.
/// </summary>
public static class TrackMatcher
{
    public static Video? FindVideo(Track track, IReadOnlyList<Video> videos, ISet<string>? alreadyUsed = null)
    {
        if (videos.Count == 0) return null;

        var wanted = Normalize(track.Title);
        if (wanted.Length == 0) return null;

        Video? best = null;
        var bestScore = 0.0;

        foreach (var v in videos)
        {
            if (alreadyUsed is not null && alreadyUsed.Contains(v.Uri)) continue;

            var score = Score(wanted, Normalize(v.Title));
            if (score > bestScore)
            {
                bestScore = score;
                best = v;
            }
        }

        // Umbral: exige que casi todo el título de la pista aparezca en el título del vídeo.
        return bestScore >= 0.85 ? best : null;
    }

    /// <summary>Consulta para buscar la pista en YouTube cuando no hay vídeo asociado.</summary>
    public static string BuildSearchQuery(string releaseArtist, Track track)
    {
        var artist = track.Artist ?? releaseArtist;
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(artist) && !artist.Equals("Various", StringComparison.OrdinalIgnoreCase))
            sb.Append(artist).Append(' ');
        sb.Append(track.Title);
        return sb.ToString().Trim();
    }

    private static double Score(string wanted, string candidate)
    {
        if (candidate.Length == 0) return 0;
        if (candidate.Contains(wanted, StringComparison.Ordinal)) return 1.0;

        // Proporción de palabras de la pista presentes en el título del vídeo.
        var wantedWords = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (wantedWords.Length == 0) return 0;
        var candidateWords = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var hits = wantedWords.Count(candidateWords.Contains);
        return (double)hits / wantedWords.Length;
    }

    /// <summary>Minúsculas, sin acentos ni signos de puntuación, espacios simples.</summary>
    internal static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastSpace = true;
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                sb.Append(' ');
                lastSpace = true;
            }
        }
        return sb.ToString().Trim();
    }
}
