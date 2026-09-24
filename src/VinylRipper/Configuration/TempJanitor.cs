using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace VinylRipper.Configuration;

/// <summary>
/// Vigilante que vacía <see cref="AppPaths.TempDirectory"/> en cuanto termina la app, por la causa que
/// sea: cierre normal, fallo, "Finalizar tarea"... La app lo lanza al arrancar como una segunda copia de
/// sí misma, sin ventana ni interfaz, que sólo espera a que el proceso principal acabe, limpia y sale.
/// (Dentro del propio proceso no se puede: un fallo nativo o una muerte forzada no dejan ejecutar nada).
/// </summary>
public static class TempJanitor
{
    /// <summary>Argumento con el que la app se lanza a sí misma en modo vigilante.</summary>
    public const string Argument = "--vaciar-temp-al-terminar";

    /// <summary>Argumentos para vigilar un proceso: su PID y su hora de inicio (por si el PID se reutiliza).</summary>
    public static IReadOnlyList<string> BuildArguments(int processId, DateTime startTime) =>
        [Argument, processId.ToString(CultureInfo.InvariantCulture), startTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)];

    /// <summary>¿Es una invocación en modo vigilante? Si lo es, devuelve a quién vigilar.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out int processId, out long startTicks)
    {
        processId = 0;
        startTicks = 0;
        return args.Count == 3 && args[0] == Argument &&
               int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out processId) &&
               long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out startTicks);
    }

    /// <summary>Espera a que termine el proceso vigilado y vacía temp.</summary>
    /// <param name="retryDelay">Pausa entre intentos (Windows tarda un instante en soltar los archivos de un proceso muerto).</param>
    /// <returns>Código de salida del vigilante (siempre 0: no hay nadie que lo lea).</returns>
    public static int Run(int processId, long startTicks, AppPaths paths, TimeSpan? retryDelay = null)
    {
        WaitForExit(processId, startTicks);

        var delay = retryDelay ?? TimeSpan.FromMilliseconds(500);
        for (var attempt = 0; attempt < 6; attempt++)
        {
            Thread.Sleep(delay);
            paths.ClearTemp();
            if (IsEmpty(paths.TempDirectory)) break;
            // Algo sigue bloqueado (p. ej. un yt-dlp que no ha muerto aún): se reintenta; si no, al abrir.
        }
        return 0;
    }

    /// <summary>
    /// Bloquea hasta que el proceso termina. Si ya no existe, o el PID es ahora de otro proceso (distinta
    /// hora de inicio), es que la app ya terminó y vuelve enseguida.
    /// </summary>
    internal static void WaitForExit(int processId, long startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.StartTime.ToUniversalTime().Ticks != startTicks) return;
            process.WaitForExit();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // No existe o no se puede consultar: se da por terminado.
        }
    }

    private static bool IsEmpty(string directory)
    {
        try { return !Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
