using System.Windows;
using System.Windows.Threading;
using VinylRipper.Configuration;
using VinylRipper.Security;
using VinylRipper.Windows.Dialogs;
using VinylRipper.Windows.ViewModels;
using VinylRipper.YouTube;

namespace VinylRipper.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        // Composición manual: la app es pequeña y así queda claro qué depende de qué.
        var paths = AppPaths.Default();
        paths.EnsureCreated();
        var store = new SettingsStore(paths);
        var protector = new TokenProtector(new MachineKeyMaterialProvider());
        var locator = new ToolLocator(paths);
        var services = new AppServices(paths, store, protector, locator);

        var vm = new MainViewModel(services);
        var window = new MainWindow(vm, services);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        DarkMessageBox.Show(MainWindow, "Error inesperado", e.Exception.Message, MessageKind.Error);
    }
}

/// <summary>Servicios compartidos por las ventanas.</summary>
public sealed record AppServices(AppPaths Paths, SettingsStore Store, TokenProtector Protector, ToolLocator Locator)
{
    public AppSettings Settings { get; } = Store.Load();

    public void Save() => Store.Save(Settings);

    public string? DiscogsToken => Protector.TryUnprotect(Settings.EncryptedDiscogsToken);

    public string OutputRoot => string.IsNullOrWhiteSpace(Settings.OutputRoot) ? Paths.Root : Settings.OutputRoot;
}
