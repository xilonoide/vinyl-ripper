using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VinylRipper.Windows.Controls;

namespace VinylRipper.Windows.Dialogs;

public enum MessageKind { Info, Success, Warning, Error }

/// <summary>
/// Sustituto oscuro de MessageBox. No tiene botones: se cierra con la X (o Esc).
/// </summary>
public partial class DarkMessageBox : Window
{
    private DarkMessageBox(string title, string message, MessageKind kind)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Title = title;
        MessageText.Text = message;

        var (glyph, brushKey) = kind switch
        {
            MessageKind.Success => ("✔", "Brush.Success"),
            MessageKind.Warning => ("⚠", "Brush.Accent"),
            MessageKind.Error => ("✖", "Brush.Error"),
            _ => ("ℹ", "Brush.Accent"),
        };
        IconText.Text = glyph;
        IconText.Foreground = (Brush)FindResource(brushKey);

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public static void Show(Window? owner, string title, string message, MessageKind kind = MessageKind.Info)
    {
        var box = new DarkMessageBox(title, message, kind);
        if (owner is { IsLoaded: true })
            box.Owner = owner;
        else
            box.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        box.ShowDialog();
    }
}
