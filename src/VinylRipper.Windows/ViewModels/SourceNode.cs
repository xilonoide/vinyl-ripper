using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VinylRipper.Discogs;

namespace VinylRipper.Windows.ViewModels;

/// <summary>
/// Nodo del árbol de fuentes: un grupo (Colección, Listas…) o una hoja que apunta a una lista
/// concreta de Discogs (<see cref="List"/> no nulo). Sólo las hojas cargan discos.
/// </summary>
public sealed partial class SourceNode : ObservableObject
{
    public SourceNode(string name, string glyph, DiscogsListDescriptor? list = null)
    {
        Name = name;
        Glyph = glyph;
        List = list;
    }

    public string Name { get; }

    /// <summary>Icono de texto que precede al nombre (📚, 📁, ♥…).</summary>
    public string Glyph { get; }

    public DiscogsListDescriptor? List { get; }

    public ObservableCollection<SourceNode> Children { get; } = [];

    public bool IsLeaf => List is not null;

    public string CountText => List?.Count is { } c ? c.ToString() : string.Empty;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    /// <summary>Recorre el árbol en profundidad devolviendo todos los nodos.</summary>
    public IEnumerable<SourceNode> Flatten()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var n in child.Flatten())
                yield return n;
    }
}
