using System.IO;
using System.Net.Http;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinylRipper.Configuration;
using VinylRipper.Discogs;
using VinylRipper.Ripping;
using VinylRipper.Windows.Dialogs;
using VinylRipper.YouTube;

namespace VinylRipper.Windows.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly HttpClient _http = DiscogsClient.CreateHttpClient();

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _downloadCts;
    private bool _restoringState;

    public MainViewModel(AppServices services)
    {
        _services = services;

        ReleasesView = CollectionViewSource.GetDefaultView(Releases);
        ReleasesView.Filter = FilterRelease;

        _searchFilter = services.Settings.SearchFilter;
        _lastOutputFolder = services.Settings.LastOutputFolder;
        _hasToken = services.DiscogsToken is not null;

        foreach (var r in services.Settings.SelectedReleases)
            Selected.Add(new ReleaseSummary(r.ReleaseId, r.Artist, r.Title, r.Year, r.Format, r.Thumb));
        Selected.CollectionChanged += Selected_CollectionChanged;

        UpdateStatus();
    }

    // ------------------------------------------------------------------ callbacks de la ventana

    /// <summary>Abre la ventana de configuración (modal) y devuelve si cambió el token.</summary>
    public Func<bool>? RequestSettings { get; set; }

    public Action<string, string, MessageKind>? ShowMessage { get; set; }

    // ------------------------------------------------------------------ estado

    public ObservableCollection<DiscogsListDescriptor> Lists { get; } = [];
    public ObservableCollection<ReleaseSummary> Releases { get; } = [];
    public ICollectionView ReleasesView { get; }
    public ObservableCollection<ReleaseSummary> Selected { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshListsCommand))]
    private DiscogsListDescriptor? _selectedList;

    [ObservableProperty] private string _searchFilter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshListsCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddToSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveFromSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string? _busyText;
    [ObservableProperty] private double? _busyPercent;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasToken;
    [ObservableProperty] private string? _lastOutputFolder;
    [ObservableProperty] private int _visibleReleaseCount;

    public bool IsProgressIndeterminate => BusyPercent is null;

    partial void OnBusyPercentChanged(double? value) => OnPropertyChanged(nameof(IsProgressIndeterminate));

    partial void OnSearchFilterChanged(string value)
    {
        _services.Settings.SearchFilter = value;
        _services.Save();
        ReleasesView.Refresh();
        UpdateStatus();
    }

    partial void OnSelectedListChanged(DiscogsListDescriptor? value)
    {
        if (!_restoringState)
        {
            _services.Settings.SelectedListKey = value?.Key;
            _services.Save();
        }
        _ = LoadReleasesAsync();
    }

    private void Selected_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _services.Settings.SelectedReleases = Selected
            .Select(r => new SavedRelease { ReleaseId = r.ReleaseId, Artist = r.Artist, Title = r.Title, Year = r.Year, Format = r.Format, Thumb = r.Thumb })
            .ToList();
        _services.Save();
        DownloadCommand.NotifyCanExecuteChanged();
        ClearSelectedCommand.NotifyCanExecuteChanged();
        UpdateStatus();
    }

    private bool FilterRelease(object o)
    {
        if (string.IsNullOrWhiteSpace(SearchFilter)) return true;
        if (o is not ReleaseSummary r) return false;
        var f = SearchFilter.Trim();
        return r.Artist.Contains(f, StringComparison.CurrentCultureIgnoreCase)
            || r.Title.Contains(f, StringComparison.CurrentCultureIgnoreCase)
            || (r.Year?.ToString().Contains(f, StringComparison.Ordinal) ?? false)
            || (r.Format?.Contains(f, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private void UpdateStatus()
    {
        VisibleReleaseCount = ReleasesView.Cast<object>().Count();
        var total = Releases.Count;
        var discos = VisibleReleaseCount == total ? $"{total} discos" : $"{VisibleReleaseCount} de {total} discos";
        StatusText = $"{discos} · {Selected.Count} seleccionados";
    }

    // ------------------------------------------------------------------ arranque

    public async Task InitializeAsync()
    {
        if (!HasToken)
        {
            StatusText = "Configura tu token de Discogs en ⚙ para empezar.";
            return;
        }
        await RefreshListsAsync();
    }

    // ------------------------------------------------------------------ listas de Discogs

    private bool CanRefreshLists() => !IsBusy && HasToken;

    [RelayCommand(CanExecute = nameof(CanRefreshLists))]
    private async Task RefreshListsAsync()
    {
        var token = _services.DiscogsToken;
        if (token is null) { HasToken = false; return; }

        var wantedKey = SelectedList?.Key ?? _services.Settings.SelectedListKey;

        IsBusy = true; BusyText = "Cargando listas de Discogs…"; BusyPercent = null;
        try
        {
            var client = new DiscogsClient(_http, token);
            var lists = await client.GetAvailableListsAsync();

            _restoringState = true;
            Lists.Clear();
            foreach (var l in lists) Lists.Add(l);
            SelectedList = Lists.FirstOrDefault(l => l.Key == wantedKey) ?? Lists.FirstOrDefault();
            _restoringState = false;

            if (SelectedList is not null && SelectedList.Key != _services.Settings.SelectedListKey)
            {
                _services.Settings.SelectedListKey = SelectedList.Key;
                _services.Save();
            }
        }
        catch (DiscogsException ex)
        {
            ShowMessage?.Invoke("Discogs", ex.Message, MessageKind.Error);
        }
        finally
        {
            _restoringState = false;
            IsBusy = false; BusyText = null;
        }
    }

    private async Task LoadReleasesAsync()
    {
        _loadCts?.Cancel();
        Releases.Clear();
        UpdateStatus();

        var list = SelectedList;
        var token = _services.DiscogsToken;
        if (list is null || token is null) return;

        var cts = _loadCts = new CancellationTokenSource();
        IsBusy = true; BusyText = $"Cargando {list.Name}…"; BusyPercent = null;
        try
        {
            var client = new DiscogsClient(_http, token);
            var progress = new Progress<PageProgress>(p =>
            {
                if (cts.IsCancellationRequested) return;
                BusyText = $"Cargando {list.Name}… página {p.Page} de {p.Pages}";
                BusyPercent = p.Pages > 1 ? 100.0 * p.Page / p.Pages : null;
            });
            var releases = await client.GetListReleasesAsync(list, progress, cts.Token);
            if (cts.IsCancellationRequested) return;

            foreach (var r in releases) Releases.Add(r);
            UpdateStatus();
        }
        catch (OperationCanceledException) { }
        catch (DiscogsException ex)
        {
            if (!cts.IsCancellationRequested)
                ShowMessage?.Invoke("Discogs", ex.Message, MessageKind.Error);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsBusy = false; BusyText = null; BusyPercent = null;
            }
        }
    }

    // ------------------------------------------------------------------ selección

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void AddToSelected(IList? items)
    {
        if (items is null) return;
        var known = Selected.Select(r => r.ReleaseId).ToHashSet();
        foreach (var r in items.OfType<ReleaseSummary>().ToList())
        {
            if (known.Add(r.ReleaseId))
                Selected.Add(r);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void RemoveFromSelected(IList? items)
    {
        if (items is null) return;
        foreach (var r in items.OfType<ReleaseSummary>().ToList())
            Selected.Remove(r);
    }

    private bool CanClearSelected() => !IsBusy && Selected.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClearSelected))]
    private void ClearSelected() => Selected.Clear();

    private bool CanAct() => !IsBusy;

    // ------------------------------------------------------------------ configuración

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task OpenSettingsAsync()
    {
        var tokenChanged = RequestSettings?.Invoke() ?? false;
        var nowHasToken = _services.DiscogsToken is not null;
        HasToken = nowHasToken;
        RefreshListsCommand.NotifyCanExecuteChanged();

        if (tokenChanged)
        {
            if (nowHasToken)
                await RefreshListsAsync();
            else
            {
                Lists.Clear();
                SelectedList = null;
                StatusText = "Configura tu token de Discogs en ⚙ para empezar.";
            }
        }
    }

    // ------------------------------------------------------------------ descarga

    private bool CanDownload() => !IsBusy && Selected.Count > 0;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        var token = _services.DiscogsToken;
        if (token is null)
        {
            ShowMessage?.Invoke("Discogs", "Configura primero tu token de Discogs.", MessageKind.Warning);
            return;
        }

        var cts = _downloadCts = new CancellationTokenSource();
        IsBusy = true; IsDownloading = true; BusyPercent = null;
        try
        {
            // 1. yt-dlp: si no está, se descarga a Documentos/vinyl-ripper/tools.
            var ytDlp = _services.Locator.FindYtDlp(_services.Settings.YtDlpPath);
            if (ytDlp is null)
            {
                BusyText = "Descargando yt-dlp…";
                var installer = new YtDlpInstaller(_http, _services.Locator);
                ytDlp = await installer.InstallAsync(new Progress<DownloadProgress>(p => BusyPercent = p.Percent), cts.Token);
                BusyPercent = null;
            }

            // 2. ffmpeg: imprescindible para MP3; no lo descargamos nosotros.
            var ffmpeg = _services.Locator.FindFfmpeg(_services.Settings.FfmpegPath);
            if (ffmpeg is null)
            {
                ShowMessage?.Invoke("Falta ffmpeg",
                    "yt-dlp necesita ffmpeg para convertir a MP3 y no lo encuentro en el PATH.\n\n" +
                    "Instálalo con:\n    winget install Gyan.FFmpeg\n\n" +
                    "o indica su ruta en ⚙ Configuración.", MessageKind.Warning);
                return;
            }

            // 3. Carpeta de salida numerada.
            var folder = OutputFolders.CreateNext(_services.OutputRoot);
            LastOutputFolder = folder;
            _services.Settings.LastOutputFolder = folder;
            _services.Save();

            var rip = new RipService(
                new DiscogsClient(_http, token),
                new YtDlpDownloader(new YtDlpOptions(ytDlp, ffmpeg, _services.Settings.AudioQuality)));

            var progress = new Progress<RipProgress>(p =>
            {
                BusyPercent = p.OverallPercent;
                BusyText = p.Phase switch
                {
                    RipPhase.Resolving => $"Leyendo pistas {p.CompletedTracks + 1}/{p.TotalTracks} · {p.CurrentRelease}",
                    RipPhase.Downloading => $"Descargando {p.CompletedTracks + 1}/{p.TotalTracks} · {p.CurrentRelease} · {p.CurrentTrack} ({p.CurrentTrackPercent:0}%)",
                    _ => "Terminando…",
                };
            });

            var result = await rip.RipAsync(Selected.ToList(), folder, progress, cts.Token);

            var summary = $"{result.Downloaded} pistas descargadas en\n{result.OutputFolder}";
            if (result.Failures.Count > 0)
            {
                summary += $"\n\n{result.Failures.Count} fallos:\n" +
                    string.Join("\n", result.Failures.Take(25).Select(f => $"• {f.Release} — {f.Track}: {f.Error}"));
                if (result.Failures.Count > 25) summary += $"\n… y {result.Failures.Count - 25} más.";
            }
            ShowMessage?.Invoke("Descarga terminada", summary, result.Failures.Count == 0 ? MessageKind.Success : MessageKind.Warning);
        }
        catch (OperationCanceledException)
        {
            ShowMessage?.Invoke("Descarga cancelada", "Se ha detenido la descarga. Lo ya bajado sigue en la carpeta de salida.", MessageKind.Info);
        }
        catch (Exception ex) when (ex is DiscogsException or YtDlpException or HttpRequestException or IOException)
        {
            ShowMessage?.Invoke("Error en la descarga", ex.Message, MessageKind.Error);
        }
        finally
        {
            IsBusy = false; IsDownloading = false; BusyText = null; BusyPercent = null;
            _downloadCts = null;
            cts.Dispose();
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var folder = !string.IsNullOrEmpty(LastOutputFolder) && Directory.Exists(LastOutputFolder) ? LastOutputFolder : _services.OutputRoot;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
