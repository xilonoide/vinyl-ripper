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
        var services = new AppServices(
            paths,
            new SettingsStore(paths),
            new TokenProtector(new MachineKeyMaterialProvider()),
            new ToolLocator(paths));

        var window = new MainWindow(new MainViewModel(services), services);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        DarkMessageBox.Show(MainWindow, "Error inesperado", e.Exception.Message, MessageKind.Error);
    }
}
