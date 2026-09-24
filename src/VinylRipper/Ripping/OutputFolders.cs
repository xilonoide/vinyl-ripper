namespace VinylRipper.Ripping;

/// <summary>
/// Crea carpetas de salida numeradas con ticks de reloj (<see cref="DateTime.Ticks"/>), un número
/// que siempre crece y ordena cronológicamente sin recurrir a GUIDs.
/// </summary>
public static class OutputFolders
{
    public static string NextName(DateTime? now = null) =>
        (now ?? DateTime.Now).Ticks.ToString();

    /// <summary>Crea y devuelve <c>root/&lt;ticks&gt;</c>, garantizando que no existía previamente.</summary>
    public static string CreateNext(string root)
    {
        Directory.CreateDirectory(root);
        while (true)
        {
            var path = Path.Combine(root, NextName());
            if (Directory.Exists(path)) continue; // dos llamadas en el mismo tick: reintentar
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
