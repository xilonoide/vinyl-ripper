using System.Globalization;
using System.Windows.Data;
using VinylRipper.Discogs;
using VinylRipper.Windows.ViewModels;

namespace VinylRipper.Windows.Controls;

/// <summary>
/// Estado de escucha de UNA fila: el de <see cref="PreviewPlayer.State"/> si la fila es la pista actual,
/// y <see cref="PreviewState.Idle"/> si no. Valores: la pista de la fila, <see cref="PreviewPlayer.CurrentKey"/>
/// y <see cref="PreviewPlayer.State"/>. El estilo <c>PreviewRowButton</c> elige ▶ / spinner / ■ a partir de él.
/// </summary>
public sealed class PreviewStateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isCurrent = values.Length >= 3 && values[0] is TrackSelection track && values[1] is string key && key == track.Key;
        return isCurrent && values[2] is PreviewState state ? state : PreviewState.Idle;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
