using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinylRipper.Discogs;
using VinylRipper.YouTube;

namespace VinylRipper.Windows.ViewModels;

public sealed record AudioQualityOption(int Value, string Label);

/// <summary>
/// Ventana de configuración. Cada propiedad se guarda en cuanto cambia; no hay botón "Guardar".
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly HttpClient _http;

    public SettingsViewModel(AppServices services, HttpClient http)
    {
        _services = services;
        _http = http;

        var s = services.Settings;
        _discogsToken = services.DiscogsToken ?? string.Empty;
        _outputRoot = s.OutputRoot ?? string.Empty;
        _ytDlpPath = s.YtDlpPath ?? string.Empty;
        _ffmpegPath = s.FfmpegPath ?? string.Empty;
        _audioQuality = AudioQualities.FirstOrDefault(q => q.Value == s.AudioQuality) ?? AudioQualities[0];

        RefreshDetected();
    }

    public static IReadOnlyList<AudioQualityOption> AudioQualities { get; } =
    [
        new(0, "0 · Máxima (~245 kbps VBR)"),
        new(2, "2 · Muy alta (~190 kbps VBR)"),
        new(4, "4 · Alta (~165 kbps VBR)"),
        new(5, "5 · Media (~130 kbps VBR)"),
        new(7, "7 · Baja (~100 kbps VBR)"),
        new(9, "9 · Mínima (~65 kbps VBR)"),
    ];

    /// <summary>Se activa cuando el token cambia, para que la ventana principal recargue las listas.</summary>
    public bool TokenChanged { get; private set; }

    public string DefaultOutputRoot => _services.Paths.Root;

    [ObservableProperty] private string _discogsToken;
    [ObservableProperty] private bool _showToken;
    [ObservableProperty] private string _outputRoot;
    [ObservableProperty] private string _ytDlpPath;
    [ObservableProperty] private string _ffmpegPath;
    [ObservableProperty] private AudioQualityOption _audioQuality;

    [ObservableProperty] private string? _detectedYtDlp;
    [ObservableProperty] private string? _detectedFfmpeg;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _busyText;
    [ObservableProperty] private double? _busyPercent;
    [ObservableProperty] private string? _tokenStatus;
    [ObservableProperty] private bool _tokenStatusIsError;

    partial void OnDiscogsTokenChanged(string value)
    {
        var trimmed = value.Trim();
        _services.Settings.EncryptedDiscogsToken = trimmed.Length == 0 ? null : _services.Protector.Protect(trimmed);
        _services.Save();
        TokenChanged = true;
        TokenStatus = null;
    }

    partial void OnOutputRootChanged(string value)
    {
        _services.Settings.OutputRoot = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _services.Save();
    }

    partial void OnYtDlpPathChanged(string value)
    {
        _services.Settings.YtDlpPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _services.Save();
        RefreshDetected();
    }

    partial void OnFfmpegPathChanged(string value)
    {
        _services.Settings.FfmpegPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _services.Save();
        RefreshDetected();
    }

    partial void OnAudioQualityChanged(AudioQualityOption value)
    {
        _services.Settings.AudioQuality = value.Value;
        _services.Save();
    }

    private void RefreshDetected()
    {
        DetectedYtDlp = _services.Locator.FindYtDlp(_services.Settings.YtDlpPath);
        DetectedFfmpeg = _services.Locator.FindFfmpeg(_services.Settings.FfmpegPath);
    }

    [RelayCommand]
    private async Task TestTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(DiscogsToken))
        {
            TokenStatus = "Introduce un token.";
            TokenStatusIsError = true;
            return;
        }

        IsBusy = true; BusyText = "Comprobando token…"; BusyPercent = null;
        try
        {
            var client = new DiscogsClient(_http, DiscogsToken);
            var me = await client.GetIdentityAsync();
            TokenStatus = $"✔ Conectado como {me.Username}";
            TokenStatusIsError = false;
        }
        catch (DiscogsException ex)
        {
            TokenStatus = "✖ " + ex.Message;
            TokenStatusIsError = true;
        }
        finally
        {
            IsBusy = false; BusyText = null;
        }
    }

    [RelayCommand]
    private async Task InstallYtDlpAsync()
    {
        IsBusy = true; BusyText = "Descargando yt-dlp…"; BusyPercent = null;
        try
        {
            var installer = new YtDlpInstaller(_http, _services.Locator);
            var progress = new Progress<DownloadProgress>(p => BusyPercent = p.Percent);
            var path = await installer.InstallAsync(progress);
            // Si el usuario tenía una ruta manual, la vaciamos para que use la recién descargada.
            if (!string.IsNullOrWhiteSpace(YtDlpPath) && !string.Equals(YtDlpPath, path, StringComparison.OrdinalIgnoreCase))
                YtDlpPath = string.Empty;
            RefreshDetected();
        }
        finally
        {
            IsBusy = false; BusyText = null; BusyPercent = null;
        }
    }
}
