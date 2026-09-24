using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VinylRipper.Discogs;

/// <summary>Progreso de una carga paginada: página actual y total de páginas.</summary>
public readonly record struct PageProgress(int Page, int Pages);

/// <summary>
/// Cliente mínimo de la API de Discogs autenticado con token personal.
/// Sólo cubre lo que necesita la aplicación: identidad, listas del usuario y detalle de discos.
/// </summary>
public sealed partial class DiscogsClient
{
    public const string BaseUrl = "https://api.discogs.com/";
    public const string UserAgent = "VinylRipper/1.0 (+https://github.com/xilonoide/vinyl-ripper)";
    private const int PerPage = 100;

    private readonly HttpClient _http;
    private readonly string _token;
    private DiscogsIdentity? _identity;

    public DiscogsClient(HttpClient http, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _http = http;
        _token = token.Trim();
    }

    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.discogs.v2.plaintext+json"));
        return http;
    }

    // ---------------------------------------------------------------- identidad

    public async Task<DiscogsIdentity> GetIdentityAsync(CancellationToken ct = default)
    {
        if (_identity is not null) return _identity;
        using var doc = await GetJsonAsync("oauth/identity", ct);
        var root = doc.RootElement;
        _identity = new DiscogsIdentity(root.GetProperty("id").GetInt64(), root.GetProperty("username").GetString()!);
        return _identity;
    }

    // ---------------------------------------------------------------- listas disponibles

    /// <summary>Todas las listas que se pueden mostrar en el desplegable.</summary>
    public async Task<IReadOnlyList<DiscogsListDescriptor>> GetAvailableListsAsync(CancellationToken ct = default)
    {
        var me = await GetIdentityAsync(ct);
        var result = new List<DiscogsListDescriptor>();

        using (var doc = await GetJsonAsync($"users/{Uri.EscapeDataString(me.Username)}/collection/folders", ct))
        {
            foreach (var f in doc.RootElement.GetProperty("folders").EnumerateArray())
            {
                var id = f.GetProperty("id").GetInt64();
                var name = f.GetProperty("name").GetString() ?? $"Carpeta {id}";
                var label = id switch { 0 => "Todo", 1 => "Sin categoría", _ => name };
                result.Add(new DiscogsListDescriptor(DiscogsListKind.CollectionFolder, id, label, TryInt(f, "count")));
            }
        }

        result.Add(new DiscogsListDescriptor(DiscogsListKind.Wantlist, 0, "Deseados", null));
        result.Add(new DiscogsListDescriptor(DiscogsListKind.Inventory, 0, "Inventario (en venta)", null));

        using (var doc = await GetJsonAsync($"users/{Uri.EscapeDataString(me.Username)}/lists?per_page={PerPage}", ct))
        {
            if (doc.RootElement.TryGetProperty("lists", out var lists))
            {
                foreach (var l in lists.EnumerateArray())
                {
                    var id = l.GetProperty("id").GetInt64();
                    var name = l.GetProperty("name").GetString() ?? $"Lista {id}";
                    result.Add(new DiscogsListDescriptor(DiscogsListKind.UserList, id, name, TryInt(l, "item_count")));
                }
            }
        }

        return result;
    }

    // ---------------------------------------------------------------- contenido de una lista

    public Task<IReadOnlyList<ReleaseSummary>> GetListReleasesAsync(
        DiscogsListDescriptor list, IProgress<PageProgress>? progress = null, CancellationToken ct = default) =>
        list.Kind switch
        {
            DiscogsListKind.CollectionFolder => GetCollectionFolderAsync(list.Id, progress, ct),
            DiscogsListKind.Wantlist => GetWantlistAsync(progress, ct),
            DiscogsListKind.Inventory => GetInventoryAsync(progress, ct),
            DiscogsListKind.UserList => GetUserListAsync(list.Id, progress, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(list)),
        };

    private async Task<IReadOnlyList<ReleaseSummary>> GetCollectionFolderAsync(long folderId, IProgress<PageProgress>? progress, CancellationToken ct)
    {
        var me = await GetIdentityAsync(ct);
        var path = $"users/{Uri.EscapeDataString(me.Username)}/collection/folders/{folderId}/releases?sort=artist&sort_order=asc";
        return await GetAllPagesAsync(path, "releases", e => ParseBasicInformation(e.GetProperty("basic_information")), progress, ct);
    }

    private async Task<IReadOnlyList<ReleaseSummary>> GetWantlistAsync(IProgress<PageProgress>? progress, CancellationToken ct)
    {
        var me = await GetIdentityAsync(ct);
        var path = $"users/{Uri.EscapeDataString(me.Username)}/wants?sort=artist&sort_order=asc";
        return await GetAllPagesAsync(path, "wants", e => ParseBasicInformation(e.GetProperty("basic_information")), progress, ct);
    }

    private async Task<IReadOnlyList<ReleaseSummary>> GetInventoryAsync(IProgress<PageProgress>? progress, CancellationToken ct)
    {
        var me = await GetIdentityAsync(ct);
        var path = $"users/{Uri.EscapeDataString(me.Username)}/inventory?status=All&sort=artist&sort_order=asc";
        var all = await GetAllPagesAsync(path, "listings", e =>
        {
            var r = e.GetProperty("release");
            var artist = CleanArtist(TryString(r, "artist") ?? string.Empty);
            var title = TryString(r, "title") ?? string.Empty;
            // En inventario el título viene como "Artista - Título"; lo separamos si podemos.
            if (!string.IsNullOrEmpty(artist) && title.StartsWith(artist + " - ", StringComparison.OrdinalIgnoreCase))
                title = title[(artist.Length + 3)..];
            return new ReleaseSummary(r.GetProperty("id").GetInt64(), artist, title, TryInt(r, "year"), TryString(r, "format"), TryString(r, "thumbnail"));
        }, progress, ct);

        // Varias copias del mismo disco a la venta → una sola entrada.
        return all.DistinctBy(r => r.ReleaseId).ToList();
    }

    private async Task<IReadOnlyList<ReleaseSummary>> GetUserListAsync(long listId, IProgress<PageProgress>? progress, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"lists/{listId}", ct);
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        var result = new List<ReleaseSummary>();
        var i = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new PageProgress(++i, items.Count));

            var type = TryString(item, "type");
            var id = item.GetProperty("id").GetInt64();
            var display = TryString(item, "display_title") ?? string.Empty;
            var (artist, title) = SplitDisplayTitle(display);
            var thumb = TryString(item, "image_url");

            switch (type)
            {
                case "release":
                    result.Add(new ReleaseSummary(id, artist, title, null, null, thumb));
                    break;
                case "master":
                    // Un master no se puede descargar directamente: usamos su edición principal.
                    var mainRelease = await GetMasterMainReleaseAsync(id, ct);
                    if (mainRelease is { } mr) result.Add(new ReleaseSummary(mr, artist, title, null, "Master", thumb));
                    break;
                // artistas y sellos en listas: no son discos, se ignoran
            }
        }
        return result;
    }

    private async Task<long?> GetMasterMainReleaseAsync(long masterId, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"masters/{masterId}", ct);
        return doc.RootElement.TryGetProperty("main_release", out var mr) && mr.ValueKind == JsonValueKind.Number ? mr.GetInt64() : null;
    }

    // ---------------------------------------------------------------- detalle de disco

    public async Task<ReleaseDetails> GetReleaseAsync(long releaseId, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"releases/{releaseId}", ct);
        var root = doc.RootElement;

        var artist = JoinArtists(root);
        var title = TryString(root, "title") ?? string.Empty;
        var year = TryInt(root, "year");

        var tracks = new List<Track>();
        if (root.TryGetProperty("tracklist", out var tl))
        {
            foreach (var t in tl.EnumerateArray())
            {
                // type_ = "track" | "heading" | "index"; cabeceras de cara no son pistas.
                var type = TryString(t, "type_");
                if (type is not null && type != "track") continue;
                var ttitle = TryString(t, "title");
                if (string.IsNullOrWhiteSpace(ttitle)) continue;
                var tartist = t.TryGetProperty("artists", out _) ? JoinArtists(t) : null;
                tracks.Add(new Track(TryString(t, "position") ?? string.Empty, ttitle, string.IsNullOrEmpty(tartist) ? null : tartist, TryString(t, "duration")));
            }
        }

        var videos = new List<Video>();
        if (root.TryGetProperty("videos", out var vs))
        {
            foreach (var v in vs.EnumerateArray())
            {
                var uri = TryString(v, "uri");
                if (string.IsNullOrWhiteSpace(uri)) continue;
                videos.Add(new Video(uri, TryString(v, "title") ?? string.Empty, TryInt(v, "duration")));
            }
        }

        return new ReleaseDetails(releaseId, artist, title, year, tracks, videos, ParseCoverUrl(root));
    }

    /// <summary>URL de la portada: la imagen <c>primary</c> y, si no hay, la primera de la lista.</summary>
    internal static string? ParseCoverUrl(JsonElement release)
    {
        if (!release.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
            return null;

        string? first = null;
        foreach (var img in images.EnumerateArray())
        {
            var uri = TryString(img, "uri");
            if (string.IsNullOrWhiteSpace(uri)) uri = TryString(img, "resource_url");
            if (string.IsNullOrWhiteSpace(uri)) continue;
            if (string.Equals(TryString(img, "type"), "primary", StringComparison.OrdinalIgnoreCase)) return uri;
            first ??= uri;
        }
        return first;
    }

    // ---------------------------------------------------------------- imágenes

    /// <summary>
    /// Descarga una imagen de Discogs (portada o miniatura). Las URLs de <c>i.discogs.com</c> ya van
    /// firmadas, así que no se envía el token. Reintenta un par de veces si el CDN limita.
    /// </summary>
    public async Task<byte[]> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new DiscogsException($"URL de imagen no válida: {url}");

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png", 0.9));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*", 0.5));

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException ex) { throw new DiscogsException("No se pudo descargar la portada: " + ex.Message, ex); }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt <= 3)
                {
                    await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt), ct);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new DiscogsException($"La portada devolvió {(int)response.StatusCode} {response.ReasonPhrase}.") { StatusCode = (int)response.StatusCode };

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length == 0) throw new DiscogsException("La portada está vacía.");
                return bytes;
            }
        }
    }

    // ---------------------------------------------------------------- paginación / HTTP

    private async Task<List<T>> GetAllPagesAsync<T>(string path, string arrayName, Func<JsonElement, T> map,
        IProgress<PageProgress>? progress, CancellationToken ct)
    {
        var result = new List<T>();
        var page = 1;
        var pages = 1;
        var sep = path.Contains('?') ? '&' : '?';

        do
        {
            using var doc = await GetJsonAsync($"{path}{sep}page={page}&per_page={PerPage}", ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("pagination", out var pg))
                pages = Math.Max(1, TryInt(pg, "pages") ?? 1);

            progress?.Report(new PageProgress(page, pages));

            if (root.TryGetProperty(arrayName, out var arr))
                foreach (var e in arr.EnumerateArray())
                    result.Add(map(e));

            page++;
        } while (page <= pages);

        return result;
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Discogs", $"token={_token}");

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException ex) { throw new DiscogsException("No se pudo conectar con Discogs: " + ex.Message, ex); }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt <= 5)
                {
                    // Límite: 60 peticiones/min autenticado. Esperamos lo que diga Discogs o un poco.
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(3 * attempt);
                    await Task.Delay(wait, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var msg = response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized => "Token de Discogs no válido o caducado.",
                        HttpStatusCode.Forbidden => "Discogs ha denegado el acceso (¿token sin permisos?).",
                        HttpStatusCode.NotFound => "Recurso no encontrado en Discogs.",
                        HttpStatusCode.TooManyRequests => "Discogs ha limitado las peticiones; espera un minuto e inténtalo de nuevo.",
                        _ => $"Discogs devolvió {(int)response.StatusCode} {response.ReasonPhrase}.",
                    };
                    throw new DiscogsException(msg) { StatusCode = (int)response.StatusCode };
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
        }
    }

    // ---------------------------------------------------------------- parsing helpers

    internal static ReleaseSummary ParseBasicInformation(JsonElement bi)
    {
        string? format = null;
        if (bi.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            var names = formats.EnumerateArray().Select(f => TryString(f, "name")).Where(n => !string.IsNullOrEmpty(n)).Distinct();
            format = string.Join(", ", names);
            if (format.Length == 0) format = null;
        }

        return new ReleaseSummary(
            bi.GetProperty("id").GetInt64(),
            JoinArtists(bi),
            TryString(bi, "title") ?? string.Empty,
            TryInt(bi, "year") is { } y and > 0 ? y : null,
            format,
            TryString(bi, "thumb"));
    }

    /// <summary>Une los artistas respetando el campo <c>join</c> ("&amp;", "Feat.", ",") de Discogs.</summary>
    internal static string JoinArtists(JsonElement parent)
    {
        if (!parent.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var a in artists.EnumerateArray())
        {
            var name = TryString(a, "anv");
            if (string.IsNullOrWhiteSpace(name)) name = TryString(a, "name");
            sb.Append(CleanArtist(name ?? string.Empty));
            var join = TryString(a, "join")?.Trim();
            if (!string.IsNullOrEmpty(join))
                sb.Append(join == "," ? ", " : $" {join} ");
        }
        return sb.ToString().Trim();
    }

    /// <summary>Quita el sufijo de desambiguación de Discogs: "Nirvana (2)" → "Nirvana".</summary>
    internal static string CleanArtist(string name) => DisambiguationSuffix().Replace(name, string.Empty).Trim();

    internal static (string Artist, string Title) SplitDisplayTitle(string display)
    {
        var idx = display.IndexOf(" - ", StringComparison.Ordinal);
        return idx < 0
            ? (string.Empty, display.Trim())
            : (CleanArtist(display[..idx]), display[(idx + 3)..].Trim());
    }

    private static string? TryString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? TryInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : null;

    [GeneratedRegex(@"\s*\(\d+\)\s*$")]
    private static partial Regex DisambiguationSuffix();
}
