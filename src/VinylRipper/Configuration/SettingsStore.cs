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

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            _paths.EnsureCreated();
            var tmp = _paths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, _paths.SettingsFile, overwrite: true);
        }
    }
}
