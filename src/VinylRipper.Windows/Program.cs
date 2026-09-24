using VinylRipper.Configuration;

namespace VinylRipper.Windows;

/// <summary>
/// Punto de entrada propio (App.xaml es Page, no ApplicationDefinition) para decidir qué se arranca antes
/// de tocar WPF: la app normal o, si viene con <see cref="TempJanitor.Argument"/>, sólo el vigilante que
/// vacía temp cuando termina la app, sin cargar interfaz.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (TempJanitor.TryParse(args, out var processId, out var startTicks))
            return TempJanitor.Run(processId, startTicks, AppPaths.Default());

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
