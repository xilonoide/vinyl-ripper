using System.Net;
using System.Text;
using System.Text.Json;
using VinylRipper.Discogs;

namespace VinylRipper.Tests;

public class DiscogsParsingTests
{
    [Fact]
    public void ParseBasicInformation_maps_fields_and_joins_artists()
    {
        var json = """
        {
          "id": 1234,
          "title": "Animals",
          "year": 1977,
          "thumb": "https://i.discogs.com/thumb.jpg",
          "formats": [ { "name": "Vinyl", "descriptions": ["LP", "Album"] }, { "name": "Vinyl" } ],
          "artists": [ { "name": "Pink Floyd (2)", "anv": "", "join": "" } ]
        }
        """;
        using var doc = JsonDocument.Parse(json);

        var r = DiscogsClient.ParseBasicInformation(doc.RootElement);

        Assert.Equal(1234, r.ReleaseId);
        Assert.Equal("Pink Floyd", r.Artist);
        Assert.Equal("Animals", r.Title);
        Assert.Equal(1977, r.Year);
        Assert.Equal("Vinyl", r.Format);
        Assert.Equal("https://i.discogs.com/thumb.jpg", r.Thumb);
        Assert.Equal("Pink Floyd – Animals (1977)", r.DisplayName);
    }

    [Fact]
    public void ParseBasicInformation_treats_year_zero_as_unknown()
    {
        using var doc = JsonDocument.Parse("""{ "id": 1, "title": "X", "year": 0, "artists": [] }""");
        var r = DiscogsClient.ParseBasicInformation(doc.RootElement);
        Assert.Null(r.Year);
        Assert.Null(r.Format);
        Assert.Equal(" – X", r.DisplayName);
    }

    [Fact]
    public void JoinArtists_respects_join_field()
    {
        var json = """
        { "artists": [
            { "name": "Simon", "join": "&" },
            { "name": "Garfunkel (3)", "join": "Feat." },
            { "name": "Alguien", "anv": "Someone", "join": "" }
        ] }
        """;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Simon & Garfunkel Feat. Someone", DiscogsClient.JoinArtists(doc.RootElement));
    }

    [Theory]
    [InlineData("Nirvana (2)", "Nirvana")]
    [InlineData("Nirvana", "Nirvana")]
    [InlineData("Blink (182)", "Blink")]
    [InlineData("  Tool (2)  ", "Tool")]
    public void CleanArtist_removes_disambiguation_suffix(string input, string expected) =>
        Assert.Equal(expected, DiscogsClient.CleanArtist(input));

    [Fact]
    public void SplitDisplayTitle()
    {
        Assert.Equal(("Pink Floyd", "Animals"), DiscogsClient.SplitDisplayTitle("Pink Floyd (2) - Animals"));
        Assert.Equal(("", "Sin guion"), DiscogsClient.SplitDisplayTitle("Sin guion"));
        Assert.Equal(("A", "B - C"), DiscogsClient.SplitDisplayTitle("A - B - C"));
    }

    [Fact]
    public void ListDescriptor_key_roundtrips()
    {
        var d = new DiscogsListDescriptor(DiscogsListKind.CollectionFolder, 123, "Colección · Rock", 40);

        Assert.Equal("CollectionFolder:123", d.Key);
        Assert.Equal("Colección · Rock (40)", d.DisplayName);
        Assert.True(DiscogsListDescriptor.TryParseKey(d.Key, out var kind, out var id));
        Assert.Equal(DiscogsListKind.CollectionFolder, kind);
        Assert.Equal(123, id);

        Assert.False(DiscogsListDescriptor.TryParseKey(null, out _, out _));
        Assert.False(DiscogsListDescriptor.TryParseKey("Basura", out _, out _));
        Assert.False(DiscogsListDescriptor.TryParseKey("Wantlist:abc", out _, out _));
    }
}

public class DiscogsClientHttpTests
{
    /// <summary>Handler falso: responde según la ruta y registra las peticiones.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var key = request.RequestUri!.PathAndQuery;
            if (Routes.TryGetValue(key, out var factory)) return Task.FromResult(factory());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (DiscogsClient Client, FakeHandler Handler) Create(string token = "tok")
    {
        var handler = new FakeHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(DiscogsClient.BaseUrl) };
        return (new DiscogsClient(http, token), handler);
    }

    [Fact]
    public async Task Sends_token_header_and_caches_identity()
    {
        var (client, handler) = Create("mi-token");
        handler.Routes["/oauth/identity"] = () => Json("""{ "id": 7, "username": "sergi" }""");

        var me = await client.GetIdentityAsync();
        await client.GetIdentityAsync();

        Assert.Equal("sergi", me.Username);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("Discogs", req.Headers.Authorization!.Scheme);
        Assert.Equal("token=mi-token", req.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Unauthorized_becomes_DiscogsException()
    {
        var (client, handler) = Create();
        handler.Routes["/oauth/identity"] = () => Json("""{ "message": "bad" }""", HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<DiscogsException>(() => client.GetIdentityAsync());
        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("Token", ex.Message);
    }

    [Fact]
    public async Task Wantlist_follows_pagination_and_reports_progress()
    {
        var (client, handler) = Create();
        handler.Routes["/oauth/identity"] = () => Json("""{ "id": 7, "username": "sergi" }""");
        handler.Routes["/users/sergi/wants?sort=artist&sort_order=asc&page=1&per_page=100"] = () => Json("""
            { "pagination": { "pages": 2 }, "wants": [
                { "basic_information": { "id": 1, "title": "Uno", "year": 1990, "artists": [{ "name": "A" }] } }
            ] }
            """);
        handler.Routes["/users/sergi/wants?sort=artist&sort_order=asc&page=2&per_page=100"] = () => Json("""
            { "pagination": { "pages": 2 }, "wants": [
                { "basic_information": { "id": 2, "title": "Dos", "year": 1991, "artists": [{ "name": "B" }] } }
            ] }
            """);

        var pages = new List<PageProgress>();
        var progress = new SynchronousProgress<PageProgress>(pages.Add);
        var releases = await client.GetListReleasesAsync(new DiscogsListDescriptor(DiscogsListKind.Wantlist, 0, "Deseados", null), progress);

        Assert.Equal([1L, 2L], releases.Select(r => r.ReleaseId));
        Assert.Equal([new PageProgress(1, 2), new PageProgress(2, 2)], pages);
    }

    [Fact]
    public async Task Inventory_dedups_and_splits_title()
    {
        var (client, handler) = Create();
        handler.Routes["/oauth/identity"] = () => Json("""{ "id": 7, "username": "sergi" }""");
        handler.Routes["/users/sergi/inventory?status=All&sort=artist&sort_order=asc&page=1&per_page=100"] = () => Json("""
            { "pagination": { "pages": 1 }, "listings": [
                { "release": { "id": 5, "title": "Pink Floyd - Animals", "artist": "Pink Floyd (2)", "year": 1977, "format": "LP", "thumbnail": "t" } },
                { "release": { "id": 5, "title": "Pink Floyd - Animals", "artist": "Pink Floyd (2)", "year": 1977, "format": "LP", "thumbnail": "t" } }
            ] }
            """);

        var releases = await client.GetListReleasesAsync(new DiscogsListDescriptor(DiscogsListKind.Inventory, 0, "Inventario", null));

        var r = Assert.Single(releases);
        Assert.Equal("Pink Floyd", r.Artist);
        Assert.Equal("Animals", r.Title);
        Assert.Equal("LP", r.Format);
    }

    [Fact]
    public async Task GetRelease_parses_tracks_and_videos_skipping_headings()
    {
        var (client, handler) = Create();
        handler.Routes["/releases/99"] = () => Json("""
            {
              "id": 99, "title": "Animals", "year": 1977,
              "artists": [{ "name": "Pink Floyd (2)" }],
              "tracklist": [
                { "position": "", "type_": "heading", "title": "Side A" },
                { "position": "A1", "type_": "track", "title": "Pigs On The Wing (Part One)", "duration": "1:25" },
                { "position": "A2", "type_": "track", "title": "Dogs", "duration": "17:06", "artists": [{ "name": "Waters" }, { "name": "Gilmour" }] }
              ],
              "videos": [
                { "uri": "https://www.youtube.com/watch?v=x", "title": "Pink Floyd - Dogs", "duration": 1026 },
                { "uri": "", "title": "vacío" }
              ]
            }
            """);

        var details = await client.GetReleaseAsync(99);

        Assert.Equal("Pink Floyd", details.Artist);
        Assert.Equal(1977, details.Year);
        Assert.Equal(2, details.Tracks.Count);
        Assert.Equal("Dogs", details.Tracks[1].Title);
        Assert.Equal("WatersGilmour", details.Tracks[1].Artist);
        Assert.Null(details.Tracks[0].Artist);
        var video = Assert.Single(details.Videos);
        Assert.Equal(1026, video.DurationSeconds);
    }

    [Fact]
    public async Task Retries_on_429()
    {
        var (client, handler) = Create();
        var calls = 0;
        handler.Routes["/oauth/identity"] = () =>
        {
            calls++;
            if (calls == 1)
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return r;
            }
            return Json("""{ "id": 1, "username": "u" }""");
        };

        var me = await client.GetIdentityAsync();

        Assert.Equal("u", me.Username);
        Assert.Equal(2, calls);
    }

    /// <summary>IProgress que invoca en el mismo hilo (Progress&lt;T&gt; usa el SynchronizationContext y es asíncrono).</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
