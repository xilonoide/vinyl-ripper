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

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ToolsDirectory);
    }
}
