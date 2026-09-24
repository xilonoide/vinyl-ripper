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
    private AppPaths? _paths;

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
        var paths = _paths = AppPaths.Default();
        paths.EnsureCreated();
        paths.ClearTemp(); // por si el vigilante no pudo (p. ej. se apagó el equipo)
        StartTempJanitor();
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

    /// <summary>
    /// Lanza el vigilante (<see cref="TempJanitor"/>): otra copia de este exe, sin ventana, que vacía temp
    /// cuando esta termine, aunque sea por un fallo o matándola. Si no se puede lanzar, temp se vacía
    /// igualmente al cerrar normal (<see cref="OnExit"/>) o en el siguiente arranque.
    /// </summary>
    private static void StartTempJanitor()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            using var self = Process.GetCurrentProcess();
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            // Lanzada como "dotnet VinylRipper.Windows.dll": hay que repetir la dll.
            if (System.IO.Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                psi.ArgumentList.Add(typeof(App).Assembly.Location);
            foreach (var arg in TempJanitor.BuildArguments(self.Id, self.StartTime))
                psi.ArgumentList.Add(arg);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>
    /// Al cerrar, vacía temp ya mismo: intermedios de yt-dlp y las pistas escuchadas (unos MB cada una).
    /// La ventana ya ha parado la escucha al cerrarse, así que el m4a que sonaba está libre. El vigilante
    /// hace lo mismo al ver que el proceso termina; aquí sólo se adelanta.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _paths?.ClearTemp();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var owner = MainWindow is { IsVisible: true } ? MainWindow : null;
        DarkMessageBox.Show(owner, "Error inesperado", e.Exception.Message, MessageKind.Error);
    }
}
