using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VinylRipper.Configuration;
using VinylRipper.Discogs;
using VinylRipper.Windows.Controls;
using VinylRipper.Windows.Dialogs;
using VinylRipper.Windows.ViewModels;

namespace VinylRipper.Windows;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppServices _services;
    private readonly DispatcherTimer _placementSaveTimer;

    public MainWindow(MainViewModel vm, AppServices services)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        _vm = vm;
        _services = services;
        DataContext = vm;

        vm.ShowMessage = (title, message, kind) => DarkMessageBox.Show(this, title, message, kind);
        vm.RequestSettings = OpenSettingsDialog;

        RestorePlacement(services.Settings.Window);
        SourceColumn.Width = new GridLength(Math.Clamp(services.Settings.SourcePaneWidth, 0.2, 3), GridUnitType.Star);

        // Guardamos posición/tamaño con un pequeño retardo para no escribir el JSON en cada píxel.
        _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _placementSaveTimer.Tick += (_, _) => { _placementSaveTimer.Stop(); SavePlacement(); };
        SizeChanged += (_, _) => _placementSaveTimer.Start();
        LocationChanged += (_, _) => _placementSaveTimer.Start();
        StateChanged += (_, _) => _placementSaveTimer.Start();
        Closing += (_, _) => { _placementSaveTimer.Stop(); SavePlacement(); };

        Loaded += async (_, _) => await _vm.InitializeAsync();
    }

    private bool OpenSettingsDialog()
    {
        var http = DiscogsClient.CreateHttpClient();
        var svm = new SettingsViewModel(_services, http);
        var window = new SettingsWindow(svm) { Owner = this };
        window.ShowDialog();
        return svm.TokenChanged;
    }

    // ------------------------------------------------------------------ nivel 1: árbol

    private void SourceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SourceNode node)
            _vm.SelectedSource = node;
    }

    private void SourceSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Guardamos el ancho del panel de fuente en estrellas relativas a la columna de discos.
        var releasesWidth = ContentGrid.ColumnDefinitions[2].ActualWidth;
        if (releasesWidth > 0)
        {
            _services.Settings.SourcePaneWidth = Math.Round(SourceColumn.ActualWidth / releasesWidth, 3);
            _services.Save();
        }
    }

    // ------------------------------------------------------------------ nivel 2: discos

    private void ReleasesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // El disco "enfocado" (cuyas pistas se muestran) es el último que se ha marcado.
        var focused = e.AddedItems.OfType<ReleaseSummary>().LastOrDefault()
                      ?? ReleasesList.SelectedItems.OfType<ReleaseSummary>().LastOrDefault();
        _vm.FocusedRelease = focused;
    }

    private void ReleasesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnderMouse<ReleaseSummary>(ReleasesList, e) is { } item && _vm.AddReleasesCommand.CanExecute(null))
            _vm.AddReleasesCommand.Execute(new[] { item });
    }

    // ------------------------------------------------------------------ nivel 3: pistas

    private void TracksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnderMouse<TrackSelection>(TracksList, e) is { } item && _vm.AddTracksCommand.CanExecute(null))
            _vm.AddTracksCommand.Execute(new[] { item });
    }

    private void SelectedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnderMouse<TrackSelection>(SelectedList, e) is { } item && _vm.RemoveFromSelectedCommand.CanExecute(null))
            _vm.RemoveFromSelectedCommand.Execute(new[] { item });
    }

    private static T? ItemUnderMouse<T>(ListBox list, MouseButtonEventArgs e) where T : class
    {
        // Sólo reaccionamos al doble clic sobre un elemento, no sobre el hueco vacío ni las cabeceras de grupo.
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not ListBoxItem && source != list)
            source = VisualTreeHelper.GetParent(source);
        return source is ListBoxItem lbi ? lbi.DataContext as T : null;
    }

    // ------------------------------------------------------------------ posición y tamaño

    private void RestorePlacement(WindowPlacement p)
    {
        Width = Math.Max(MinWidth, p.Width);
        Height = Math.Max(MinHeight, p.Height);

        if (p.HasPosition && IsOnScreen(p.Left!.Value, p.Top!.Value, Width, Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = p.Left.Value;
            Top = p.Top.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vr = vl + SystemParameters.VirtualScreenWidth;
        var vb = vt + SystemParameters.VirtualScreenHeight;
        // Basta con que una parte razonable de la ventana quede visible.
        return left + width > vl + 100 && left < vr - 100 && top + height > vt + 50 && top < vb - 50;
    }

    private void SavePlacement()
    {
        var p = _services.Settings.Window;
        p.Maximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0 && !double.IsNaN(bounds.Left))
        {
            p.Left = bounds.Left;
            p.Top = bounds.Top;
            p.Width = bounds.Width;
            p.Height = bounds.Height;
        }
        _services.Save();
    }
}
