using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinylRipper.Configuration;
using VinylRipper.Discogs;
using VinylRipper.Ripping;
using VinylRipper.Windows.Dialogs;
using VinylRipper.YouTube;

namespace VinylRipper.Windows.ViewModels;

/// <summary>
/// Ventana principal. Tres niveles de navegación (fuente → discos → pistas) y una lista de
/// pistas seleccionadas, agrupadas por disco, que se descargan a MP3.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly HttpClient _http = DiscogsClient.CreateHttpClient();
    private readonly Dictionary<long, ReleaseDetails> _detailsCache = [];

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _tracksCts;
    private CancellationTokenSource? _downloadCts;
    private bool _restoringState;
    /// <summary>Mientras se añaden o quitan pistas en bloque, se guarda una sola vez al final.</summary>
    private bool _batchingSelection;

    public MainViewModel(AppServices services)
    {
        _services = services;

        ReleasesView = CollectionViewSource.GetDefaultView(Releases);
        ReleasesView.Filter = FilterRelease;

        var selectedView = new ListCollectionView(Selected);
        selectedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TrackSelection.Release) + "." + nameof(ReleaseSummary.DisplayName)));
        SelectedView = selectedView;

        _searchFilter = services.Settings.SearchFilter;
        _lastOutputFolder = services.Settings.LastOutputFolder;
        _hasToken = services.DiscogsToken is not null;

        foreach (var t in services.Settings.SelectedTracks)
        {
            var release = new ReleaseSummary(t.ReleaseId, t.Artist, t.ReleaseTitle, t.Year, t.Format, t.Thumb);
            Selected.Add(new TrackSelection(release, new Track(t.Position, t.TrackTitle, t.TrackArtist, t.Duration), t.Index, t.TotalTracks));
        }
        Selected.CollectionChanged += Selected_CollectionChanged;

        UpdateStatus();
    }

    // ------------------------------------------------------------------ callbacks de la ventana

    /// <summary>Abre la ventana de configuración (modal) y devuelve si cambió el token.</summary>
    public Func<bool>? RequestSettings { get; set; }

    public Action<string, string, MessageKind>? ShowMessage { get; set; }

    // ------------------------------------------------------------------ estado

    /// <summary>Nivel 1: árbol Colección/carpetas, Deseados, Inventario, Listas.</summary>
    public ObservableCollection<SourceNode> Sources { get; } = [];

    /// <summary>Nivel 2: discos de la fuente elegida.</summary>
    public ObservableCollection<ReleaseSummary> Releases { get; } = [];
    public ICollectionView ReleasesView { get; }

    /// <summary>Nivel 3: pistas del disco enfocado.</summary>
    public ObservableCollection<TrackSelection> Tracks { get; } = [];

    /// <summary>Pistas acumuladas para descargar.</summary>
    public ObservableCollection<TrackSelection> Selected { get; } = [];
    public ICollectionView SelectedView { get; }

    [ObservableProperty] private SourceNode? _selectedSource;
    [ObservableProperty] private ReleaseSummary? _focusedRelease;
    [ObservableProperty] private bool _isLoadingTracks;
    [ObservableProperty] private string _searchFilter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSourcesCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddReleasesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddTracksCommand))]
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

    public string TracksHeader => FocusedRelease is null ? "Pistas" : $"Pistas · {FocusedRelease.Artist} – {FocusedRelease.Title}";

    partial void OnBusyPercentChanged(double? value) => OnPropertyChanged(nameof(IsProgressIndeterminate));

    partial void OnFocusedReleaseChanged(ReleaseSummary? value)
    {
        OnPropertyChanged(nameof(TracksHeader));
        _ = LoadTracksAsync(value);
    }

    partial void OnSearchFilterChanged(string value)
    {
        _services.Settings.SearchFilter = value;
        _services.Save();
        ReleasesView.Refresh();
        UpdateStatus();
    }

    partial void OnSelectedSourceChanged(SourceNode? value)
    {
        if (value is null) return;
        if (!value.IsLeaf)
        {
            // Los grupos sólo se expanden; el contenido lo cargan las hojas.
            value.IsExpanded = true;
            return;
        }
        if (!_restoringState)
        {
            _services.Settings.SelectedListKey = value.List!.Key;
            _services.Save();
        }
        _ = LoadReleasesAsync(value.List!);
    }

    private void Selected_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_batchingSelection) OnSelectionChanged();
    }

    /// <summary>Persiste la selección entera y refresca botones y estado.</summary>
    private void OnSelectionChanged()
    {
        _services.Settings.SelectedTracks = Selected.Select(s => new SavedTrack
        {
            ReleaseId = s.Release.ReleaseId, Artist = s.Release.Artist, ReleaseTitle = s.Release.Title,
            Year = s.Release.Year, Format = s.Release.Format, Thumb = s.Release.Thumb,
            Index = s.Index, TotalTracks = s.TotalTracks, Position = s.Track.Position,
            TrackTitle = s.Track.Title, TrackArtist = s.Track.Artist, Duration = s.Track.Duration,
        }).ToList();
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
        var releases = Selected.Select(s => s.Release.ReleaseId).Distinct().Count();
        StatusText = $"{discos} · {Selected.Count} pistas de {releases} discos seleccionadas";
    }

    // ------------------------------------------------------------------ arranque

    public async Task InitializeAsync()
    {
        if (!HasToken)
        {
            StatusText = "Configura tu token de Discogs en ⚙ para empezar.";
            return;
        }
        await RefreshSourcesAsync();
    }

    // ------------------------------------------------------------------ nivel 1: fuentes

    private bool CanRefreshSources() => !IsBusy && HasToken;

    [RelayCommand(CanExecute = nameof(CanRefreshSources))]
    private async Task RefreshSourcesAsync()
    {
        var token = _services.DiscogsToken;
        if (token is null) { HasToken = false; return; }

        var wantedKey = SelectedSource?.List?.Key ?? _services.Settings.SelectedListKey;

        IsBusy = true; BusyText = "Cargando listas de Discogs…"; BusyPercent = null;
        try
        {
            var client = new DiscogsClient(_http, token);
            var lists = await client.GetAvailableListsAsync();

            _restoringState = true;
            Sources.Clear();
            foreach (var node in BuildTree(lists)) Sources.Add(node);

            var leaves = Sources.SelectMany(n => n.Flatten()).Where(n => n.IsLeaf).ToList();
            var wanted = leaves.FirstOrDefault(n => n.List!.Key == wantedKey) ?? leaves.FirstOrDefault();
            if (wanted is not null)
            {
                ExpandAncestors(wanted);
                wanted.IsSelected = true;
                SelectedSource = wanted;
            }
            _restoringState = false;

            if (wanted is not null && wanted.List!.Key != _services.Settings.SelectedListKey)
            {
                _services.Settings.SelectedListKey = wanted.List.Key;
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

    private static IEnumerable<SourceNode> BuildTree(IReadOnlyList<DiscogsListDescriptor> lists)
    {
        var collection = new SourceNode("Colección", "📚") { IsExpanded = true };
        foreach (var l in lists.Where(l => l.Kind == DiscogsListKind.CollectionFolder))
            collection.Children.Add(new SourceNode(l.Name, l.Id == 0 ? "🗂" : "📁", l));
        yield return collection;

        foreach (var l in lists.Where(l => l.Kind == DiscogsListKind.Wantlist))
            yield return new SourceNode(l.Name, "♥", l);

        foreach (var l in lists.Where(l => l.Kind == DiscogsListKind.Inventory))
            yield return new SourceNode(l.Name, "🏷", l);

        var userLists = lists.Where(l => l.Kind == DiscogsListKind.UserList).ToList();
        if (userLists.Count > 0)
        {
            var group = new SourceNode("Listas", "📝") { IsExpanded = true };
            foreach (var l in userLists) group.Children.Add(new SourceNode(l.Name, "📄", l));
            yield return group;
        }
    }

    private void ExpandAncestors(SourceNode target)
    {
        foreach (var root in Sources)
            if (root != target && root.Flatten().Contains(target))
                root.IsExpanded = true;
    }

    // ------------------------------------------------------------------ nivel 2: discos

    private async Task LoadReleasesAsync(DiscogsListDescriptor list)
    {
        _loadCts?.Cancel();
        Releases.Clear();
        FocusedRelease = null;
        UpdateStatus();

        var token = _services.DiscogsToken;
        if (token is null) return;

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

    // ------------------------------------------------------------------ nivel 3: pistas

    private async Task LoadTracksAsync(ReleaseSummary? release)
    {
        _tracksCts?.Cancel();
        Tracks.Clear();
        if (release is null) { IsLoadingTracks = false; return; }

        var cts = _tracksCts = new CancellationTokenSource();
        IsLoadingTracks = true;
        try
        {
            var details = await GetDetailsAsync(release.ReleaseId, cts.Token);
            if (cts.IsCancellationRequested) return;
            foreach (var t in ToSelections(release, details)) Tracks.Add(t);
        }
        catch (OperationCanceledException) { }
        catch (DiscogsException ex)
        {
            if (!cts.IsCancellationRequested)
                ShowMessage?.Invoke("Discogs", ex.Message, MessageKind.Error);
        }
        finally
        {
            if (ReferenceEquals(_tracksCts, cts)) IsLoadingTracks = false;
        }
    }

    private async Task<ReleaseDetails> GetDetailsAsync(long releaseId, CancellationToken ct)
    {
        if (_detailsCache.TryGetValue(releaseId, out var cached)) return cached;
        var token = _services.DiscogsToken ?? throw new DiscogsException("Falta el token de Discogs.");
        var details = await new DiscogsClient(_http, token).GetReleaseAsync(releaseId, ct);
        _detailsCache[releaseId] = details;
        return details;
    }

    private static IEnumerable<TrackSelection> ToSelections(ReleaseSummary release, ReleaseDetails details)
    {
        // Completamos año/artista si la lista no los traía (p. ej. listas personalizadas).
        var full = release with
        {
            Artist = string.IsNullOrEmpty(release.Artist) ? details.Artist : release.Artist,
            Year = release.Year ?? details.Year,
        };
        var total = details.Tracks.Count;
        return details.Tracks.Select((t, i) => new TrackSelection(full, t, i + 1, total));
    }

    // ------------------------------------------------------------------ selección

    private bool CanAct() => !IsBusy;

    /// <summary>Añade todas las pistas de los discos marcados (descarga el tracklist si hace falta).</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task AddReleasesAsync(IList? items)
    {
        if (items is null) return;
        var releases = items.OfType<ReleaseSummary>().ToList();
        if (releases.Count == 0) return;

        IsBusy = true; BusyPercent = releases.Count > 1 ? 0 : null;
        try
        {
            for (var i = 0; i < releases.Count; i++)
            {
                var r = releases[i];
                BusyText = $"Leyendo pistas {i + 1}/{releases.Count} · {r.DisplayName}";
                BusyPercent = releases.Count > 1 ? 100.0 * i / releases.Count : null;
                try
                {
                    var details = await GetDetailsAsync(r.ReleaseId, CancellationToken.None);
                    AddUnique(ToSelections(r, details));
                }
                catch (DiscogsException ex)
                {
                    ShowMessage?.Invoke("Discogs", $"{r.DisplayName}: {ex.Message}", MessageKind.Error);
                }
            }
        }
        finally
        {
            IsBusy = false; BusyText = null; BusyPercent = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void AddTracks(IList? items)
    {
        if (items is null) return;
        AddUnique(items.OfType<TrackSelection>().ToList());
    }

    private void AddUnique(IEnumerable<TrackSelection> tracks)
    {
        var known = Selected.Select(s => s.Key).ToHashSet();
        var changed = false;
        _batchingSelection = true;
        try
        {
            foreach (var t in tracks)
                if (known.Add(t.Key))
                {
                    Selected.Add(t);
                    changed = true;
                }
        }
        finally { _batchingSelection = false; }
        if (changed) OnSelectionChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void RemoveFromSelected(IList? items)
    {
        if (items is null) return;
        var changed = false;
        _batchingSelection = true;
        try
        {
            foreach (var t in items.OfType<TrackSelection>().ToList())
                changed |= Selected.Remove(t);
        }
        finally { _batchingSelection = false; }
        if (changed) OnSelectionChanged();
    }

    private bool CanClearSelected() => !IsBusy && Selected.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClearSelected))]
    private void ClearSelected() => Selected.Clear();

    // ------------------------------------------------------------------ configuración

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task OpenSettingsAsync()
    {
        var tokenChanged = RequestSettings?.Invoke() ?? false;
        var nowHasToken = _services.DiscogsToken is not null;
        HasToken = nowHasToken;
        RefreshSourcesCommand.NotifyCanExecuteChanged();

        if (tokenChanged)
        {
            _detailsCache.Clear();
            if (nowHasToken)
                await RefreshSourcesAsync();
            else
            {
                Sources.Clear();
                Releases.Clear();
                Tracks.Clear();
                SelectedSource = null;
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
                new YtDlpDownloader(new YtDlpOptions(ytDlp, ffmpeg, _services.Settings.AudioQuality, _services.Paths.TempDirectory)),
                new CoverArtEmbedder(ffmpeg, _services.Paths.TempDirectory));

            var progress = new Progress<RipProgress>(p =>
            {
                BusyPercent = p.OverallPercent;
                BusyText = p.Phase switch
                {
                    RipPhase.Resolving => $"Buscando vídeos · {p.CurrentRelease}",
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
            if (result.ReleasesWithoutCover.Count > 0)
            {
                summary += "\n\nSin portada (Discogs no la tiene o no se pudo incrustar):\n" +
                    string.Join("\n", result.ReleasesWithoutCover.Take(10).Select(r => $"• {r}"));
                if (result.ReleasesWithoutCover.Count > 10) summary += $"\n… y {result.ReleasesWithoutCover.Count - 10} más.";
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
