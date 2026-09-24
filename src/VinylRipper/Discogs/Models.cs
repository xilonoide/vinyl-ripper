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

/// <summary>Una de las listas que el usuario puede elegir en el árbol de fuentes.</summary>
public sealed record DiscogsListDescriptor(DiscogsListKind Kind, long Id, string Name, int? Count)
{
    /// <summary>Clave estable para guardar en configuración (p. ej. <c>CollectionFolder:0</c>).</summary>
    public string Key => $"{Kind}:{Id}";
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

/// <summary>
/// Una pista concreta de un disco, elegida para descargar. <paramref name="Index"/> es su posición
/// (1..N) dentro del tracklist completo y <paramref name="TotalTracks"/> el total, para numerar archivos.
/// </summary>
public sealed record TrackSelection(ReleaseSummary Release, Track Track, int Index, int TotalTracks)
{
    public string Key => $"{Release.ReleaseId}:{Index}";
    public string DisplayPosition => string.IsNullOrEmpty(Track.Position) ? Index.ToString() : Track.Position;
}

public sealed record DiscogsIdentity(long Id, string Username);

public sealed class DiscogsException : Exception
{
    public DiscogsException(string message, Exception? inner = null) : base(message, inner) { }
    public int? StatusCode { get; init; }
}
