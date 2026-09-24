using System.Text.Json;
using System.Text.Json.Serialization;

namespace VinylRipper.Configuration;

/// <summary>
/// Carga y guarda <see cref="AppSettings"/> en JSON. El guardado es atómico (escribe a un
/// temporal y renombra) y se serializa con un lock para que los guardados "a cada cambio"
/// desde la UI no se pisen entre sí.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppPaths _paths;
    private readonly object _gate = new();

    public SettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public string FilePath => _paths.SettingsFile;

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_paths.SettingsFile))
                return new AppSettings();

            try
            {
                var json = File.ReadAllText(_paths.SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch (JsonException)
            {
                // Archivo corrupto: lo apartamos y empezamos de cero en vez de reventar al arrancar.
                File.Move(_paths.SettingsFile, _paths.SettingsFile + ".corrupt", overwrite: true);
                return new AppSettings();
            }
        }
    }

    /// <summary>Intentos de guardado antes de rendirse (~1,5 s en total en el peor caso).</summary>
    internal const int SaveAttempts = 8;

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            _paths.EnsureCreated();
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tmp = _paths.SettingsFile + ".tmp";

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, _paths.SettingsFile, overwrite: true);
                    return;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < SaveAttempts)
                {
                    // Un antivirus o el indexador de Windows abre un instante el archivo recién escrito
                    // para analizarlo; mientras lo tiene abierto, renombrar encima da "Access denied".
                    Thread.Sleep(RetryDelay(attempt));
                }
            }
        }
    }

    /// <summary>25, 50, 100, 200, 400, 400… ms.</summary>
    internal static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(400, 25 * (1 << Math.Min(attempt - 1, 4))));
}
