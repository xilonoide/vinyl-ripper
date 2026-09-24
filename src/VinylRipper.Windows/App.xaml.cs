using System.Diagnostics;
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
    /// <summary>Tiempo mínimo que se ve la pantalla de inicio, aunque la app arranque antes.</summary>
    private static readonly TimeSpan MinSplashTime = TimeSpan.FromSeconds(3);

    private readonly SplashScreen _splash;
    private readonly Stopwatch _splashClock;

    public App()
    {
        // Lo primerísimo: la pantalla de inicio es una ventana nativa que aparece antes de cargar el
        // tema, los servicios y la ventana principal. Se cierra a mano (ShowWindowWhenReady).
        _splash = new SplashScreen("Assets/splash.jpg");
        _splash.Show(autoClose: false, topMost: true);
        _splashClock = Stopwatch.StartNew();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        // Composición manual: la app es pequeña y así queda claro qué depende de qué.
        var paths = AppPaths.Default();
        paths.EnsureCreated();
        paths.ClearTemp(); // restos de yt-dlp de una descarga cancelada o de un cierre brusco
        var services = new AppServices(
            paths,
            new SettingsStore(paths),
            new TokenProtector(new MachineKeyMaterialProvider()),
            new ToolLocator(paths));

        var window = new MainWindow(new MainViewModel(services), services);
        MainWindow = window;
        _ = ShowWindowWhenReadyAsync(window);
    }

    /// <summary>
    /// Enseña la ventana principal cuando la pantalla de inicio lleva al menos <see cref="MinSplashTime"/>.
    /// La pantalla de inicio sigue encima hasta que la ventana ha pintado su primer fotograma, así que
    /// si arrancar tarda más de eso, se sigue viendo hasta que la ventana está lista.
    /// </summary>
    private async Task ShowWindowWhenReadyAsync(Window window)
    {
        var remaining = MinSplashTime - _splashClock.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);

        window.ContentRendered += (_, _) => _splash.Close(TimeSpan.FromMilliseconds(300));
        window.Show();
        window.Activate();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var owner = MainWindow is { IsVisible: true } ? MainWindow : null;
        DarkMessageBox.Show(owner, "Error inesperado", e.Exception.Message, MessageKind.Error);
    }
}
