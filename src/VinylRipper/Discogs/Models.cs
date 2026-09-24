namespace VinylRipper.Discogs;

public enum DiscogsListKind
{
    /// <summary>Carpeta de la colección (0 = todas, 1 = sin categoría, resto = carpetas del usuario).</summary>
    CollectionFolder,
    /// <summary>Lista de deseados.</summary>
    Wantlist,
    /// <summary>Inventario (discos a la venta).</summary>
    Inventory,
    /// <summary>Lista personalizada del usuario.</summary>
    UserList,
}

/// <summary>Una de las listas que el usuario puede elegir en el desplegable.</summary>
public sealed record DiscogsListDescriptor(DiscogsListKind Kind, long Id, string Name, int? Count)
{
    /// <summary>Clave estable para guardar en configuración (p. ej. <c>CollectionFolder:0</c>).</summary>
    public string Key => $"{Kind}:{Id}";

    public string DisplayName => Count is { } c ? $"{Name} ({c})" : Name;

    public static bool TryParseKey(string? key, out DiscogsListKind kind, out long id)
    {
        kind = default; id = 0;
        if (string.IsNullOrWhiteSpace(key)) return false;
        var parts = key.Split(':', 2);
        return parts.Length == 2 && Enum.TryParse(parts[0], out kind) && long.TryParse(parts[1], out id);
    }
}

/// <summary>Disco tal y como aparece en una lista (información básica).</summary>
public sealed record ReleaseSummary(
    long ReleaseId,
    string Artist,
    string Title,
    int? Year,
    string? Format,
    string? Thumb)
{
    public string DisplayName => Year is { } y and > 0 ? $"{Artist} – {Title} ({y})" : $"{Artist} – {Title}";
}

public sealed record Track(string Position, string Title, string? Artist, string? Duration);

public sealed record Video(string Uri, string Title, int? DurationSeconds);

/// <summary>Detalle de un disco: tracklist y vídeos asociados en Discogs.</summary>
public sealed record ReleaseDetails(
    long ReleaseId,
    string Artist,
    string Title,
    int? Year,
    IReadOnlyList<Track> Tracks,
    IReadOnlyList<Video> Videos);

public sealed record DiscogsIdentity(long Id, string Username);

public sealed class DiscogsException : Exception
{
    public DiscogsException(string message, Exception? inner = null) : base(message, inner) { }
    public int? StatusCode { get; init; }
}
