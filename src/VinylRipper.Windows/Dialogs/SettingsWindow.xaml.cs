using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VinylRipper.Windows.Controls;
using VinylRipper.Windows.ViewModels;

namespace VinylRipper.Windows.Dialogs;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private bool _syncingToken;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _vm = vm;
        DataContext = vm;

        // PasswordBox.Password no es bindable: lo sincronizamos a mano en ambos sentidos.
        TokenBox.Password = vm.DiscogsToken;
        vm.PropertyChanged += Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.DiscogsToken) || _syncingToken) return;
        if (TokenBox.Password != _vm.DiscogsToken)
        {
            _syncingToken = true;
            TokenBox.Password = _vm.DiscogsToken;
            _syncingToken = false;
        }
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingToken) return;
        _syncingToken = true;
        _vm.DiscogsToken = TokenBox.Password;
        _syncingToken = false;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Carpeta raíz de descargas",
            InitialDirectory = string.IsNullOrWhiteSpace(_vm.OutputRoot) ? _vm.DefaultOutputRoot : _vm.OutputRoot,
        };
        if (dlg.ShowDialog(this) == true)
            _vm.OutputRoot = dlg.FolderName;
    }

    private void BrowseYtDlp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Ejecutable de yt-dlp", Filter = "yt-dlp|yt-dlp.exe;yt-dlp|Ejecutables|*.exe|Todos|*.*" };
        if (dlg.ShowDialog(this) == true)
            _vm.YtDlpPath = dlg.FileName;
    }

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Ejecutable de ffmpeg", Filter = "ffmpeg|ffmpeg.exe;ffmpeg|Ejecutables|*.exe|Todos|*.*" };
        if (dlg.ShowDialog(this) == true)
            _vm.FfmpegPath = dlg.FileName;
    }
}
