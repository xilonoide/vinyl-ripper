namespace VinylRipper.Configuration;

/// <summary>
/// Rutas base de la aplicación. Por defecto todo vive en <c>Documentos/vinyl-ripper</c>,
/// pero se puede inyectar otra raíz (tests, otras plataformas).
/// </summary>
public sealed class AppPaths
{
    public const string FolderName = "vinyl-ripper";

    public AppPaths(string root)
    {
        Root = root;
    }

    public static AppPaths Default()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(docs))
            docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new AppPaths(Path.Combine(docs, FolderName));
    }

    /// <summary>Documentos/vinyl-ripper</summary>
    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>Carpeta donde se descargan herramientas externas (yt-dlp).</summary>
    public string ToolsDirectory => Path.Combine(Root, "tools");

    /// <summary>Intermedios de yt-dlp (.webm, .part, .ytdl…) y pistas escuchadas. Se vacía al abrir y al cerrar la app.</summary>
    public string TempDirectory => Path.Combine(Root, "temp");

    /// <summary>Detalle de discos ya pedidos a Discogs, para no volver a pedirlos. No se vacía.</summary>
    public string ReleaseCacheDirectory => Path.Combine(Root, "cache", "releases");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(TempDirectory);
    }

    /// <summary>
    /// Borra el contenido de <see cref="TempDirectory"/>. Un archivo bloqueado (otra instancia
    /// descargando) no debe impedir abrir ni cerrar la app: se ignora y se limpiará la próxima vez.
    /// </summary>
    /// <returns>Número de entradas eliminadas.</returns>
    public int ClearTemp()
    {
        if (!Directory.Exists(TempDirectory)) return 0;
        var removed = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(TempDirectory))
        {
            try
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
                removed++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }
}
