using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VinylRipper.Windows.Controls;

namespace VinylRipper.Windows.Dialogs;

public enum MessageKind { Info, Success, Warning, Error }

/// <summary>
/// Sustituto oscuro de MessageBox. No tiene botón de cerrar ni de aceptar: se cierra con la X (o Esc).
/// Con <see cref="ShowWithAction"/> muestra además un botón para una acción concreta ("Reintentar").
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

    public static void Show(Window? owner, string title, string message, MessageKind kind = MessageKind.Info) =>
        Create(owner, title, message, kind).ShowDialog();

    /// <summary>Como <see cref="Show"/>, con un botón para <paramref name="actionText"/>.</summary>
    /// <returns>true si se ha pulsado el botón; false si se ha cerrado con la X o Esc.</returns>
    public static bool ShowWithAction(Window? owner, string title, string message, MessageKind kind, string actionText)
    {
        var box = Create(owner, title, message, kind);
        box.ActionButton.Content = actionText;
        box.ActionButton.Visibility = Visibility.Visible;
        box.ActionButton.IsDefault = true;
        return box.ShowDialog() == true;
    }

    private static DarkMessageBox Create(Window? owner, string title, string message, MessageKind kind)
    {
        var box = new DarkMessageBox(title, message, kind);
        if (owner is { IsLoaded: true })
            box.Owner = owner;
        else
            box.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return box;
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
