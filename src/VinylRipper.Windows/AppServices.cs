using VinylRipper.Configuration;
using VinylRipper.Discogs;
using VinylRipper.Security;
using VinylRipper.YouTube;

namespace VinylRipper.Windows;

/// <summary>
/// Servicios compartidos por todas las ventanas. Hay una única instancia de <see cref="Settings"/>
/// en memoria; cada cambio desde la UI se persiste en el acto con <see cref="Save"/>.
/// </summary>
public sealed record AppServices(AppPaths Paths, SettingsStore Store, TokenProtector Protector, ToolLocator Locator)
{
    public AppSettings Settings { get; } = Store.Load();

    /// <summary>Detalle de discos ya pedidos a Discogs (tracklist, vídeos, portada), guardado en disco.</summary>
    public ReleaseDetailsCache Releases { get; } = new(Paths.ReleaseCacheDirectory);

    /// <summary>
    /// Guarda la configuración. Si el archivo sigue bloqueado tras los reintentos del almacén, no se
    /// interrumpe lo que estuviera haciendo el usuario: todo el estado vive en memoria y cada guardado
    /// lo escribe entero, así que el siguiente cambio (o el cierre de la ventana) lo vuelve a intentar.
    /// </summary>
    public void Save()
    {
        try
        {
            Store.Save(Settings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"No se pudo guardar {Store.FilePath}: {ex.Message}");
        }
    }

    /// <summary>Token de Discogs en claro, o null si no hay o no se puede descifrar (otra máquina/usuario).</summary>
    public string? DiscogsToken => Protector.TryUnprotect(Settings.EncryptedDiscogsToken);

    /// <summary>Raíz donde se crean las carpetas de descarga numeradas.</summary>
    public string OutputRoot => string.IsNullOrWhiteSpace(Settings.OutputRoot) ? Paths.Root : Settings.OutputRoot;
}
