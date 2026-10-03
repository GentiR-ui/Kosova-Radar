using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace App.AcceptanceTests;

/// <summary>Time helpers. Europe/Pristina is not an IANA zone id; Kosovo uses Europe/Belgrade (same rules).</summary>
public static class Clock
{
    public static readonly TimeZoneInfo Pristina = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");

    /// <summary>Real UTC now truncated to whole seconds (exact round-trips through the X-Test-Now header).</summary>
    public static DateTimeOffset Now
    {
        get
        {
            var n = DateTimeOffset.UtcNow;
            return new DateTimeOffset(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second, TimeSpan.Zero);
        }
    }

    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Pristina).DateTime);

    /// <summary>A wall-clock time in Pristina with the correct offset (avoid 02:00-03:00 on DST days).</summary>
    public static DateTimeOffset At(DateOnly date, int hour, int minute = 0)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Pristina.GetUtcOffset(local));
    }

    /// <summary>Pristina date <paramref name="days"/> from today at 20:00 local.</summary>
    public static DateTimeOffset Future(int days, int hour = 20) => At(Today.AddDays(days), hour);

    /// <summary>ISO-8601 with offset, as feeds send it.</summary>
    public static string Iso(DateTimeOffset t) => t.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>ISO-8601 UTC, as the X-Test-Now header expects it.</summary>
    public static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string Date(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, Pristina).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public static class JsonExt
{
    public static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static JsonElement Parse(string text) => JsonDocument.Parse(text).RootElement.Clone();

    public static bool IsNull(this JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null;

    public static string? Str(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static Guid Id(this JsonElement e, string name = "id") => Guid.Parse(e.GetProperty(name).GetString()!);

    public static int Int(this JsonElement e, string name) => e.GetProperty(name).GetInt32();

    public static decimal? Dec(this JsonElement e, string name) => e.IsNull(name) ? null : e.GetProperty(name).GetDecimal();

    public static bool Bool(this JsonElement e, string name) => e.GetProperty(name).GetBoolean();

    public static DateTimeOffset Time(this JsonElement e, string name) =>
        DateTimeOffset.Parse(e.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

    public static DateTimeOffset? TimeOrNull(this JsonElement e, string name) => e.IsNull(name) ? null : e.Time(name);

    public static List<JsonElement> Items(this JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    public static List<JsonElement> Array(this JsonElement e) => e.EnumerateArray().ToList();

    public static List<string> IssueCodes(this JsonElement candidate) =>
        candidate.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()!).ToList();

    public static JsonNode? Node(this JsonElement e, string name) =>
        e.IsNull(name) ? null : JsonNode.Parse(e.GetProperty(name).GetRawText());

    /// <summary>Removes null-valued properties (feed items must not carry explicit nulls).</summary>
    public static JsonObject Compact(this JsonObject o)
    {
        foreach (var key in o.Where(p => p.Value is null).Select(p => p.Key).ToList()) o.Remove(key);
        return o;
    }

    public static string Raw(this JsonObject item) => item.ToJsonString(Relaxed);
}

public static class HttpExt
{
    public static async Task<HttpResponseMessage> Send(this HttpClient c, HttpMethod method, string url,
        object? body = null, DateTimeOffset? now = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is JsonNode node)
            req.Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
        else if (body is not null)
            req.Content = JsonContent.Create(body);
        if (now is { } t) req.Headers.Add("X-Test-Now", Clock.Utc(t));
        return await c.SendAsync(req);
    }

    public static Task<HttpResponseMessage> Get(this HttpClient c, string url, DateTimeOffset? now = null) =>
        c.Send(HttpMethod.Get, url, null, now);

    public static Task<HttpResponseMessage> Post(this HttpClient c, string url, object? body = null, DateTimeOffset? now = null) =>
        c.Send(HttpMethod.Post, url, body, now);

    public static Task<HttpResponseMessage> Put(this HttpClient c, string url, object? body = null, DateTimeOffset? now = null) =>
        c.Send(HttpMethod.Put, url, body, now);

    public static async Task<JsonElement> Json(this Task<HttpResponseMessage> response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var res = await response;
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"Expected {(int)expected} but got {(int)res.StatusCode}: {text}");
        return JsonExt.Parse(text);
    }

    public static async Task<HttpResponseMessage> Expect(this Task<HttpResponseMessage> response, HttpStatusCode expected)
    {
        var res = await response;
        if (res.StatusCode != expected)
        {
            var text = await res.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} but got {(int)res.StatusCode}: {text}");
        }
        return res;
    }

    /// <summary>Asserts a 400 problem-details response whose <c>errors</c> name every given field (case-insensitive).</summary>
    public static async Task<JsonElement> ExpectValidation(this Task<HttpResponseMessage> response, params string[] fields)
    {
        var res = await response;
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 but got {(int)res.StatusCode}: {text}");
        var json = JsonExt.Parse(text);
        Assert.True(json.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object,
            $"Expected problem details with an 'errors' object: {text}");
        var keys = errors.EnumerateObject().Select(p => p.Name).ToList();
        foreach (var f in fields)
            Assert.True(keys.Any(k => string.Equals(k, f, StringComparison.OrdinalIgnoreCase)),
                $"Expected a validation error on '{f}', got: {text}");
        return json;
    }
}

public sealed record TestUser(Guid Id, string Email, string Password, string Role);

public sealed record Ingested(Guid SourceId, string SourceName, JsonObject SourceBody, JsonElement Run, List<JsonElement> Candidates)
{
    public JsonElement Candidate(string externalId) =>
        Candidates.Single(c => c.Str("externalId") == externalId);

    public JsonElement Single => Assert.Single(Candidates);
}

public sealed record Published(Guid SourceId, string SourceName, JsonObject SourceBody, Guid CandidateId, Guid ItemId, string Kind)
{
    public string PublicPath => Kind == "event" ? $"/api/events/{ItemId}" : $"/api/deals/{ItemId}";
    public string AdminPath => Kind == "event" ? $"/api/admin/events/{ItemId}" : $"/api/admin/deals/{ItemId}";
}

/// <summary>Black-box helpers over the Kosova Radar HTTP API (design.md §5, §6).</summary>
public static class Radar
{
    public const string AdminEmail = "admin@kosovaradar.local";
    public const string AdminPassword = "Radar-admin-dev-2026";
    public const string UserPassword = "Strong-pass-2026!";

    /// <summary>Unique token: "t" + 8 hex chars.</summary>
    public static string Token() => "t" + Guid.NewGuid().ToString("N")[..8];

    public static string Email() => $"u{Guid.NewGuid():N}@test.local";

    // ---------- auth & users ----------

    public static Task<HttpResponseMessage> Login(HttpClient c, string email, string password) =>
        c.Post("/api/auth/login", new { email, password });

    public static async Task<HttpClient> LoginAsync(AppFactory app, string email, string password)
    {
        var c = app.CreateClient();
        await Login(c, email, password).Expect(HttpStatusCode.OK);
        return c;
    }

    public static Task<HttpClient> AdminAsync(AppFactory app) => LoginAsync(app, AdminEmail, AdminPassword);

    public static async Task<TestUser> CreateUserAsync(HttpClient admin, string role, string? email = null, string? password = null)
    {
        email ??= Email();
        password ??= UserPassword;
        var dto = await admin.Post("/api/admin/users", new { email, password, role }).Json(HttpStatusCode.Created);
        return new TestUser(dto.Id(), email, password, role);
    }

    public static async Task<(HttpClient Client, TestUser User)> SignedInUserAsync(AppFactory app, HttpClient admin, string role)
    {
        var user = await CreateUserAsync(admin, role);
        return (await LoginAsync(app, user.Email, user.Password), user);
    }

    // ---------- feed items (Radar JSON feed, design.md §6) ----------

    /// <summary>Title with the group token plus its own token (keeps title similarity between items below 0.8).</summary>
    public static string Title(string group, string word = "Koncert") => $"{word} {group} {Token()}";

    /// <summary>A valid event item: known city, future start, https url, no text (no AI call).</summary>
    public static JsonObject EventItem(string title, DateTimeOffset? startsAt = null, string? city = "Prishtinë",
        DateTimeOffset? endsAt = null, string? externalId = null) => new JsonObject
        {
            ["externalId"] = externalId ?? "ev-" + Guid.NewGuid().ToString("N"),
            ["kind"] = "event",
            ["title"] = title,
            ["url"] = $"https://example.com/events/{Guid.NewGuid():N}",
            ["city"] = city,
            ["startsAt"] = Clock.Iso(startsAt ?? Clock.Future(5)),
            ["endsAt"] = endsAt is { } e ? Clock.Iso(e) : null,
        }.Compact();

    /// <summary>A valid deal item: both prices, currency, future validUntil, no text.</summary>
    public static JsonObject DealItem(string title, decimal? oldPrice = 25m, decimal? newPrice = 20m,
        DateTimeOffset? validUntil = null, string? city = "Prishtinë", string? externalId = null, bool noValidUntil = false) => new JsonObject
        {
            ["externalId"] = externalId ?? "dl-" + Guid.NewGuid().ToString("N"),
            ["kind"] = "deal",
            ["title"] = title,
            ["url"] = $"https://example.com/deals/{Guid.NewGuid():N}",
            ["city"] = city,
            ["merchant"] = "Dyqani Test",
            ["oldPrice"] = oldPrice,
            ["newPrice"] = newPrice,
            ["currency"] = "EUR",
            ["validUntil"] = noValidUntil ? null : Clock.Iso(validUntil ?? Clock.Future(10)),
        }.Compact();

    public static string Payload(params JsonObject[] items) =>
        "{\"items\":[" + string.Join(",", items.Select(i => i.Raw())) + "]}";

    // ---------- sources ----------

    public static JsonObject FixtureSource(string payload, string? name = null, bool isActive = false,
        bool isUnreliable = false, int pollIntervalMinutes = 1440) => new()
        {
            ["name"] = name ?? $"Burim {Token()}",
            ["type"] = "fixture",
            ["baseUrl"] = null,
            ["fixturePayload"] = payload,
            ["defaultKind"] = "event",
            ["pollIntervalMinutes"] = pollIntervalMinutes,
            ["isActive"] = isActive,
            ["isUnreliable"] = isUnreliable,
        };

    public static JsonObject UrlSource(string type, string baseUrl, string? name = null, bool isActive = false,
        int pollIntervalMinutes = 1440, string defaultKind = "event") => new()
        {
            ["name"] = name ?? $"Burim {Token()}",
            ["type"] = type,
            ["baseUrl"] = baseUrl,
            ["fixturePayload"] = null,
            ["defaultKind"] = defaultKind,
            ["pollIntervalMinutes"] = pollIntervalMinutes,
            ["isActive"] = isActive,
            ["isUnreliable"] = false,
        };

    public static Task<JsonElement> CreateSourceAsync(HttpClient admin, JsonObject body) =>
        admin.Post("/api/admin/sources", body).Json(HttpStatusCode.Created);

    public static Task<JsonElement> UpdateSourceAsync(HttpClient admin, Guid id, JsonObject body) =>
        admin.Put($"/api/admin/sources/{id}", body).Json();

    public static Task<JsonElement> SourceAsync(HttpClient admin, Guid id) =>
        admin.Get($"/api/admin/sources/{id}").Json();

    public static Task<JsonElement> FetchAsync(HttpClient admin, Guid sourceId, DateTimeOffset? now = null) =>
        admin.Post($"/api/admin/sources/{sourceId}/fetch", null, now).Json();

    // ---------- candidates ----------

    public static Task<JsonElement> CandidatePageAsync(HttpClient c, Guid sourceId, string extraQuery = "") =>
        c.Get($"/api/admin/candidates?sourceId={sourceId}&pageSize=100{extraQuery}").Json();

    public static Task<JsonElement> CandidateAsync(HttpClient c, Guid id) =>
        c.Get($"/api/admin/candidates/{id}").Json();

    public static async Task<List<JsonElement>> CandidatesOfSourceAsync(HttpClient c, Guid sourceId)
    {
        var page = await CandidatePageAsync(c, sourceId);
        var result = new List<JsonElement>();
        foreach (var s in page.Items()) result.Add(await CandidateAsync(c, s.Id()));
        return result;
    }

    /// <summary>Creates an inactive fixture source with the items, fetches it once and returns the candidates.</summary>
    public static async Task<Ingested> IngestAsync(HttpClient admin, JsonObject[] items, DateTimeOffset? now = null,
        bool isUnreliable = false, string? sourceName = null)
    {
        var body = FixtureSource(Payload(items), sourceName, isUnreliable: isUnreliable);
        var source = await CreateSourceAsync(admin, body);
        var run = await FetchAsync(admin, source.Id(), now);
        Assert.Equal("succeeded", run.Str("status"));
        var candidates = await CandidatesOfSourceAsync(admin, source.Id());
        return new Ingested(source.Id(), source.Str("name")!, body, run, candidates);
    }

    public static async Task<JsonElement> IngestOneAsync(HttpClient admin, JsonObject item, DateTimeOffset? now = null,
        bool isUnreliable = false) => (await IngestAsync(admin, [item], now, isUnreliable)).Single;

    public static async Task<Guid> ApproveAsync(HttpClient c, Guid candidateId)
    {
        var res = await c.Post($"/api/admin/candidates/{candidateId}/approve").Json();
        return res.Id("itemId");
    }

    /// <summary>Ingests one item through its own inactive fixture source and approves it.</summary>
    public static async Task<Published> PublishAsync(HttpClient admin, JsonObject item, DateTimeOffset? fetchNow = null,
        string? sourceName = null)
    {
        var ing = await IngestAsync(admin, [item], fetchNow, sourceName: sourceName);
        var candidate = ing.Single;
        var itemId = await ApproveAsync(admin, candidate.Id());
        return new Published(ing.SourceId, ing.SourceName, ing.SourceBody, candidate.Id(), itemId, candidate.Str("kind")!);
    }

    /// <summary>Re-fetches a source after replacing its fixture payload.</summary>
    public static async Task<JsonElement> RefetchWithAsync(HttpClient admin, Guid sourceId, JsonObject body,
        JsonObject[] items, DateTimeOffset? now = null)
    {
        body["fixturePayload"] = Payload(items);
        await UpdateSourceAsync(admin, sourceId, body);
        return await FetchAsync(admin, sourceId, now);
    }

    /// <summary>CandidateFieldsRequest built from a CandidateDto's fields.</summary>
    public static JsonObject FieldsRequest(JsonElement candidate)
    {
        var f = JsonNode.Parse(candidate.GetProperty("fields").GetRawText())!.AsObject();
        f.Remove("discountPercent");
        f.Remove("cityText");
        f.Remove("categoryText");
        return f;
    }

    /// <summary>CandidateFieldsRequest built from a public/admin EventDetailDto.</summary>
    public static JsonObject EventEditRequest(JsonElement ev) => new()
    {
        ["title"] = ev.Node("title"),
        ["description"] = ev.Node("description"),
        ["url"] = ev.Node("sourceUrl"),
        ["imageUrl"] = ev.Node("imageUrl"),
        ["citySlug"] = ev.GetProperty("city").Node("slug"),
        ["venueName"] = ev.Node("venueName"),
        ["categorySlug"] = ev.IsNull("category") ? null : ev.GetProperty("category").Node("slug"),
        ["startsAt"] = ev.Node("startsAt"),
        ["endsAt"] = ev.Node("endsAt"),
        ["priceText"] = ev.Node("priceText"),
        ["priceAmount"] = ev.Node("priceAmount"),
        ["currency"] = ev.Node("currency"),
        ["merchantName"] = null,
        ["oldPrice"] = null,
        ["newPrice"] = null,
        ["statedDiscountPercent"] = null,
        ["validFrom"] = null,
        ["validUntil"] = null,
    };

    /// <summary>CandidateFieldsRequest built from a public/admin DealDetailDto.</summary>
    public static JsonObject DealEditRequest(JsonElement deal) => new()
    {
        ["title"] = deal.Node("title"),
        ["description"] = deal.Node("description"),
        ["url"] = deal.Node("sourceUrl"),
        ["imageUrl"] = deal.Node("imageUrl"),
        ["citySlug"] = deal.IsNull("city") ? null : deal.GetProperty("city").Node("slug"),
        ["venueName"] = null,
        ["categorySlug"] = deal.IsNull("category") ? null : deal.GetProperty("category").Node("slug"),
        ["startsAt"] = null,
        ["endsAt"] = null,
        ["priceText"] = deal.Node("priceText"),
        ["priceAmount"] = null,
        ["currency"] = deal.Node("currency"),
        ["merchantName"] = deal.Node("merchantName"),
        ["oldPrice"] = deal.Node("oldPrice"),
        ["newPrice"] = deal.Node("newPrice"),
        ["statedDiscountPercent"] = null,
        ["validFrom"] = deal.Node("validFrom"),
        ["validUntil"] = deal.Node("validUntil"),
    };

    // ---------- public ----------

    public static async Task<List<JsonElement>> PublicListAsync(HttpClient c, string path, DateTimeOffset? now = null) =>
        (await c.Get(path, now).Json()).Items();

    public static async Task<List<Guid>> PublicIdsAsync(HttpClient c, string path, DateTimeOffset? now = null) =>
        (await PublicListAsync(c, path, now)).Select(i => i.Id()).ToList();

    public static async Task<List<JsonElement>> AuditAsync(HttpClient c, Guid entityId) =>
        (await c.Get($"/api/admin/audit?entityId={entityId}&pageSize=100").Json()).Items();

    // ---------- polling ----------

    public static async Task<T> PollAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan timeout, string what)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            var value = await read();
            if (done(value)) return value;
            if (DateTime.UtcNow > until) Assert.Fail($"Timed out after {timeout.TotalSeconds}s waiting for {what}.");
            await Task.Delay(500);
        }
    }
}
