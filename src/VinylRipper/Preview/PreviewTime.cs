namespace VinylRipper.Preview;

public static class PreviewTime
{
    /// <summary>"0:05", "17:06" o, a partir de una hora, "1:02:03".</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        var totalSeconds = (long)time.TotalSeconds;
        var h = totalSeconds / 3600;
        var m = totalSeconds / 60 % 60;
        var s = totalSeconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{totalSeconds / 60}:{s:00}";
    }
}
