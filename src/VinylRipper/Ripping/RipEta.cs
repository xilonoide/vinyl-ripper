using System.Diagnostics;

namespace VinylRipper.Ripping;

/// <summary>
/// Tiempo estimado para terminar una pasada de descarga: lo que ha costado lo hecho, extrapolado a lo que
/// falta. Con varias pistas a la vez el ritmo es bastante estable, así que basta con la media desde el
/// principio de la pasada. Hasta tener algo de historia (<see cref="MinElapsed"/>) no se estima nada.
/// </summary>
public sealed class RipEta
{
    /// <summary>Antes de esto la estimación salta demasiado para ser útil.</summary>
    public static readonly TimeSpan MinElapsed = TimeSpan.FromSeconds(20);

    private readonly Func<TimeSpan> _clock;
    private TimeSpan _start;

    /// <param name="clock">Reloj con el tiempo transcurrido (para tests); por defecto, uno real.</param>
    public RipEta(Func<TimeSpan>? clock = null)
    {
        if (clock is null)
        {
            var sw = Stopwatch.StartNew();
            clock = () => sw.Elapsed;
        }
        _clock = clock;
        _start = _clock();
    }

    /// <summary>Empieza a contar de nuevo (p. ej. al pasar a reintentar las que fallaron).</summary>
    public void Restart() => _start = _clock();

    /// <summary>Lo que falta, o null si aún no se puede estimar.</summary>
    /// <param name="fractionDone">Parte hecha de la pasada, de 0 a 1.</param>
    public TimeSpan? Remaining(double fractionDone)
    {
        var elapsed = _clock() - _start;
        if (elapsed < MinElapsed || fractionDone <= 0) return null;
        if (fractionDone >= 1) return TimeSpan.Zero;
        return TimeSpan.FromTicks((long)(elapsed.Ticks * (1 - fractionDone) / fractionDone));
    }

    /// <summary>"quedan ~1 h 05 min", "quedan ~12 min" o "queda menos de 1 min" (se redondea hacia arriba).</summary>
    public static string Format(TimeSpan remaining)
    {
        var minutes = (long)Math.Ceiling(remaining.TotalMinutes);
        if (minutes <= 1) return remaining.TotalSeconds < 60 ? "queda menos de 1 min" : "quedan ~1 min";
        return minutes >= 60 ? $"quedan ~{minutes / 60} h {minutes % 60:00} min" : $"quedan ~{minutes} min";
    }
}
