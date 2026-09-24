using VinylRipper.Configuration;
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

    public void Save() => Store.Save(Settings);

    /// <summary>Token de Discogs en claro, o null si no hay o no se puede descifrar (otra máquina/usuario).</summary>
    public string? DiscogsToken => Protector.TryUnprotect(Settings.EncryptedDiscogsToken);

    /// <summary>Raíz donde se crean las carpetas de descarga numeradas.</summary>
    public string OutputRoot => string.IsNullOrWhiteSpace(Settings.OutputRoot) ? Paths.Root : Settings.OutputRoot;
}
