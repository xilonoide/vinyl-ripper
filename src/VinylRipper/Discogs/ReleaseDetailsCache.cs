using System.Collections.Concurrent;
using System.Text.Json;

namespace VinylRipper.Discogs;

/// <summary>
/// Caché persistente del detalle de cada disco (tracklist, vídeos y portada) para no volver a pedirlo
/// a la API de Discogs: ni al volver a abrir un disco, ni al añadirlo, escucharlo o descargarlo, ni en
/// siguientes arranques. Un archivo JSON pequeño por disco en <c>cache/releases/{id}.json</c>, así cada
/// disco nuevo es una escritura corta e independiente (nada de reescribir un archivo que crece).
/// Si la caché no se puede leer o escribir, simplemente se vuelve a pedir a la API.
/// </summary>
public sealed class ReleaseDetailsCache
{
    /// <summary>Sube si cambia <see cref="ReleaseDetails"/> y lo guardado deja de valer.</summary>
    internal const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly ConcurrentDictionary<long, ReleaseDetails> _memory = new();
    private readonly ConcurrentDictionary<long, Task<ReleaseDetails>> _inFlight = new();

    public ReleaseDetailsCache(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    internal string PathFor(long releaseId) => System.IO.Path.Combine(Directory, $"{releaseId}.json");

    /// <summary>Detalle guardado (en memoria o en disco), o false si hay que pedirlo.</summary>
    public bool TryGet(long releaseId, out ReleaseDetails details)
    {
        if (_memory.TryGetValue(releaseId, out details!)) return true;

        var loaded = Load(releaseId);
        if (loaded is null) return false;
        details = _memory.GetOrAdd(releaseId, loaded);
        return true;
    }

    public void Store(ReleaseDetails details)
    {
        _memory[details.ReleaseId] = details;
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = PathFor(details.ReleaseId);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Entry(FormatVersion, DateTimeOffset.UtcNow, details), JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin persistir sólo se pierde el ahorro en el próximo arranque; en memoria sigue.
        }
    }

    /// <summary>Discos guardados en disco.</summary>
    public int Count
    {
        get
        {
            try { return System.IO.Directory.Exists(Directory) ? System.IO.Directory.EnumerateFiles(Directory, "*.json").Count() : 0; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        }
    }

    /// <summary>
    /// Olvida todo lo guardado (en memoria y en disco), para que cada disco se vuelva a pedir a Discogs
    /// la próxima vez: útil si alguno ha cambiado allí. Un archivo bloqueado se deja; se sobrescribirá.
    /// </summary>
    /// <returns>Discos borrados del disco.</returns>
    public int Clear()
    {
        _memory.Clear();

        List<string> files;
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return 0;
            files = System.IO.Directory.EnumerateFiles(Directory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }

        var removed = 0;
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
                if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }

    /// <summary>
    /// El detalle del disco: de la caché si está y, si no, de <paramref name="fetch"/> (la API), que se
    /// guarda. Varias peticiones simultáneas del mismo disco comparten una sola llamada.
    /// </summary>
    public async Task<ReleaseDetails> GetOrFetchAsync(long releaseId,
        Func<CancellationToken, Task<ReleaseDetails>> fetch, CancellationToken ct = default)
    {
        if (TryGet(releaseId, out var cached)) return cached;

        var task = _inFlight.GetOrAdd(releaseId, id =>
        {
            var started = FetchAndStoreAsync(fetch);
            // Por si quien la pidió cancela antes de que acabe: al terminar deja de estar "en curso".
            _ = started.ContinueWith(done => _inFlight.TryRemove(new KeyValuePair<long, Task<ReleaseDetails>>(id, done)),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return started;
        });
        try
        {
            return await task.WaitAsync(ct);
        }
        finally
        {
            // Sólo si sigue siendo la misma petición (y ya ha acabado): un fallo no se queda "en curso".
            if (task.IsCompleted) _inFlight.TryRemove(new KeyValuePair<long, Task<ReleaseDetails>>(releaseId, task));
        }
    }

    private async Task<ReleaseDetails> FetchAndStoreAsync(Func<CancellationToken, Task<ReleaseDetails>> fetch)
    {
        // Sin el token del llamador: si uno cancela, los demás que esperan el mismo disco siguen.
        var details = await fetch(CancellationToken.None).ConfigureAwait(false);
        Store(details);
        return details;
    }

    private ReleaseDetails? Load(long releaseId)
    {
        try
        {
            var path = PathFor(releaseId);
            if (!File.Exists(path)) return null;
            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path), JsonOptions);
            return entry is { Version: FormatVersion, Release: { } r } && r.ReleaseId == releaseId ? Normalize(r) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null; // archivo roto o de otra versión: se vuelve a pedir y se sobrescribe
        }
    }

    /// <summary>Un JSON editado a mano podría traer listas null; nunca se devuelven así.</summary>
    private static ReleaseDetails Normalize(ReleaseDetails r) =>
        r.Tracks is null || r.Videos is null ? r with { Tracks = r.Tracks ?? [], Videos = r.Videos ?? [] } : r;

    private sealed record Entry(int Version, DateTimeOffset FetchedUtc, ReleaseDetails Release);
}
