using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Application.Sources;
using App.Infrastructure.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.IntegrationTests;

/// <summary>
/// The whole fetch path (endpoint → run → adapter → named client "sources" with its retry handler) with a stub
/// instead of the network. The guard itself is covered by unit tests and the REQ-020 acceptance tests.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class SourceHttpTests(AppFactory app) : IDisposable
{
    private readonly List<string> _requests = [];
    private readonly Dictionary<string, Func<int, Task<HttpResponseMessage>>> _routes = [];
    private readonly List<IDisposable> _factories = [];

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            respond(request).WaitAsync(ct);
    }

    private HttpClient CreateClient(int retries = 0, int timeoutSeconds = 2, int perHostDelaySeconds = 0)
    {
        var factory = app.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IngestionOptions>();
            services.AddSingleton(new IngestionOptions { AllowFixtureSources = true, HttpRetries = retries, HttpTimeoutSeconds = timeoutSeconds, PerHostDelaySeconds = perHostDelaySeconds });
            services.AddHttpClient<SourceHttpFetcher>(SourceHttpFetcher.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(request =>
                {
                    var path = request.RequestUri!.AbsoluteUri;
                    lock (_requests) _requests.Add(path);
                    int attempt;
                    lock (_requests) attempt = _requests.Count(r => r == path);
                    // Sites without a stated robots.txt answer 404 (allow everything).
                    return _routes.TryGetValue(path, out var route) ? route(attempt)
                        : path.EndsWith("/robots.txt") ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
                        : throw new InvalidOperationException($"No route for {path}");
                }));
        }));
        _factories.Add(factory);
        return factory.CreateClient();
    }

    private static string Token() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<HttpClient> LoginAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return client;
    }

    private async Task<JsonElement> FetchAsync(HttpClient admin, string url, string type = "jsonFeed")
    {
        var created = await admin.PostAsJsonAsync("/api/admin/sources", new
        {
            name = $"Http {Token()}",
            type,
            baseUrl = url,
            fixturePayload = (string?)null,
            defaultKind = "event",
            pollIntervalMinutes = 1440,
            isActive = false,
            isUnreliable = false,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        var run = await admin.PostAsync($"/api/admin/sources/{id}/fetch", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        return JsonDocument.Parse(await run.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string NewUrl() => $"https://feeds-{Token()}.example/feed.json";

    private static Task<HttpResponseMessage> Json(string body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    [Fact]
    public async Task A_json_feed_over_http_creates_candidates()
    {
        var url = NewUrl();
        _routes[url] = _ => Json("{\"items\":[{\"externalId\":\"a1\",\"kind\":\"event\",\"title\":\"Koncert\",\"url\":\"https://example.test/a1\"}]}");
        var admin = await LoginAsync(CreateClient());

        var run = await FetchAsync(admin, url);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(1, run.GetProperty("created").GetInt32());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_non_2xx_answer_fails_the_run_with_HttpStatus(HttpStatusCode status)
    {
        var url = NewUrl();
        _routes[url] = _ => Task.FromResult(new HttpResponseMessage(status));
        var admin = await LoginAsync(CreateClient());

        var run = await FetchAsync(admin, url);

        Assert.Equal("failed", run.GetProperty("status").GetString());
        Assert.Equal("HttpStatus", run.GetProperty("errorCode").GetString());
        Assert.Contains(((int)status).ToString(), run.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_slow_source_fails_the_run_with_Timeout()
    {
        var url = NewUrl();
        _routes[url] = _ => new TaskCompletionSource<HttpResponseMessage>().Task;
        var admin = await LoginAsync(CreateClient(timeoutSeconds: 1));

        var run = await FetchAsync(admin, url);

        Assert.Equal("failed", run.GetProperty("status").GetString());
        Assert.Equal("Timeout", run.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task An_answer_over_5_MB_fails_the_run_with_TooLarge()
    {
        var url = NewUrl();
        _routes[url] = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[5 * 1024 * 1024 + 1]),
        });
        var admin = await LoginAsync(CreateClient());

        var run = await FetchAsync(admin, url);

        Assert.Equal("failed", run.GetProperty("status").GetString());
        Assert.Equal("TooLarge", run.GetProperty("errorCode").GetString());
        Assert.Equal(0, run.GetProperty("created").GetInt32());
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_the_run_succeeds()
    {
        var url = NewUrl();
        _routes[url] = attempt => attempt == 1
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            : Json("{\"items\":[]}");
        var admin = await LoginAsync(CreateClient(retries: 2, timeoutSeconds: 20));

        var run = await FetchAsync(admin, url);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(2, _requests.Count(r => r == url));
    }

    [Fact]
    public async Task A_network_error_is_retried()
    {
        var url = NewUrl();
        _routes[url] = attempt => attempt == 1 ? throw new HttpRequestException("reset") : Json("{\"items\":[]}");
        var admin = await LoginAsync(CreateClient(retries: 2, timeoutSeconds: 20));

        var run = await FetchAsync(admin, url);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(2, _requests.Count(r => r == url));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Request_timeout_and_too_many_requests_are_retried(HttpStatusCode status)
    {
        var url = NewUrl();
        _routes[url] = attempt => attempt == 1 ? Task.FromResult(new HttpResponseMessage(status)) : Json("{\"items\":[]}");
        var admin = await LoginAsync(CreateClient(retries: 1, timeoutSeconds: 20));

        Assert.Equal("succeeded", (await FetchAsync(admin, url)).GetProperty("status").GetString());
        Assert.Equal(2, _requests.Count(r => r == url));
    }

    [Fact]
    public async Task A_persistent_server_error_uses_all_retries_and_ends_as_HttpStatus()
    {
        var url = NewUrl();
        _routes[url] = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var admin = await LoginAsync(CreateClient(retries: 2, timeoutSeconds: 30));

        var run = await FetchAsync(admin, url);

        Assert.Equal("HttpStatus", run.GetProperty("errorCode").GetString());
        Assert.Equal(3, _requests.Count(r => r == url));
    }

    [Fact]
    public async Task A_blocked_address_is_never_retried()
    {
        var url = NewUrl();
        _routes[url] = _ => throw new HttpRequestException("x", new BlockedAddressException());
        var admin = await LoginAsync(CreateClient(retries: 2, timeoutSeconds: 20));

        var run = await FetchAsync(admin, url);

        Assert.Equal("BlockedAddress", run.GetProperty("errorCode").GetString());
        Assert.Single(_requests, r => r == url);
    }

    [Fact]
    public async Task An_unknown_host_is_not_retried()
    {
        var url = NewUrl();
        _routes[url] = _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "x");
        var admin = await LoginAsync(CreateClient(retries: 2, timeoutSeconds: 20));

        var run = await FetchAsync(admin, url);

        Assert.Equal("Unreachable", run.GetProperty("errorCode").GetString());
        Assert.Single(_requests, r => r == url);
    }

    [Fact]
    public async Task A_client_error_is_not_retried()
    {
        var url = NewUrl();
        _routes[url] = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var admin = await LoginAsync(CreateClient(retries: 2, timeoutSeconds: 20));

        var run = await FetchAsync(admin, url);

        Assert.Equal("HttpStatus", run.GetProperty("errorCode").GetString());
        Assert.Single(_requests, r => r == url);
    }

    [Fact]
    public async Task A_network_error_is_Unreachable_and_the_message_hides_the_cause()
    {
        var url = NewUrl();
        _routes[url] = _ => throw new HttpRequestException("connect to 10.9.8.7 refused");
        var admin = await LoginAsync(CreateClient());

        var run = await FetchAsync(admin, url);

        Assert.Equal("Unreachable", run.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("10.9.8.7", run.GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_rss_source_is_fetched_through_the_same_client()
    {
        var url = $"https://rss-{Token()}.example/rss.xml";
        _routes[url] = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var admin = await LoginAsync(CreateClient());

        var run = await FetchAsync(admin, url, "rss");

        Assert.Equal("HttpStatus", run.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task An_rss_feed_creates_candidates_and_counts_a_bad_item_as_failed()
    {
        var url = $"https://rss-{Token()}.example/rss.xml";
        _routes[url] = _ => Json("<rss version=\"2.0\"><channel>"
            + "<item><title>Koncert</title><link>https://example.test/r1</link><description>&lt;p&gt;Live&lt;/p&gt;</description></item>"
            + "<item><title>Pa lidhje</title></item></channel></rss>");
        var admin = await LoginAsync(CreateClient());

        var run = await FetchAsync(admin, url, "rss");

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(1, run.GetProperty("created").GetInt32());
        Assert.Equal(1, run.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task An_rss_source_that_serves_something_else_fails_with_MalformedPayload()
    {
        var url = $"https://rss-{Token()}.example/rss.xml";
        _routes[url] = _ => Json("{\"items\":[]}");
        var admin = await LoginAsync(CreateClient());

        var run = await FetchAsync(admin, url, "rss");

        Assert.Equal("MalformedPayload", run.GetProperty("errorCode").GetString());
    }

    private static string Host() => $"https://polite-{Token()}.example";

    private static Task<HttpResponseMessage> Text(string body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    [Fact]
    public async Task A_robots_txt_that_disallows_the_feed_fails_the_run_with_RobotsDisallowed_and_the_feed_is_not_requested()
    {
        var host = Host();
        var url = $"{host}/feed.json";
        _routes[$"{host}/robots.txt"] = _ => Text("User-agent: *\nDisallow: /feed");
        _routes[url] = _ => Json("{\"items\":[]}");
        var admin = await LoginAsync(CreateClient());

        var run = await FetchAsync(admin, url);

        Assert.Equal("failed", run.GetProperty("status").GetString());
        Assert.Equal("RobotsDisallowed", run.GetProperty("errorCode").GetString());
        Assert.DoesNotContain(url, _requests);
    }

    [Fact]
    public async Task A_robots_txt_that_allows_the_feed_lets_the_run_through()
    {
        var host = Host();
        var url = $"{host}/feed.json";
        _routes[$"{host}/robots.txt"] = _ => Text("User-agent: *\nDisallow: /admin\n");
        _routes[url] = _ => Json("{\"items\":[]}");
        var admin = await LoginAsync(CreateClient());

        Assert.Equal("succeeded", (await FetchAsync(admin, url)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_unavailable_robots_txt_fails_the_run_and_is_asked_again_next_time()
    {
        var host = Host();
        var url = $"{host}/feed.json";
        _routes[$"{host}/robots.txt"] = attempt => attempt == 1
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            : Text("User-agent: *\nDisallow:\n");
        _routes[url] = _ => Json("{\"items\":[]}");
        var admin = await LoginAsync(CreateClient());

        var first = await FetchAsync(admin, url);
        var second = await FetchAsync(admin, url);

        Assert.Equal("RobotsDisallowed", first.GetProperty("errorCode").GetString());
        Assert.Equal("succeeded", second.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_robots_txt_that_cannot_be_reached_keeps_the_network_error_code()
    {
        var host = Host();
        _routes[$"{host}/robots.txt"] = _ => throw new HttpRequestException("reset");
        var admin = await LoginAsync(CreateClient());

        var run = await FetchAsync(admin, $"{host}/feed.json");

        Assert.Equal("Unreachable", run.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_robots_txt_that_names_a_blocked_address_is_BlockedAddress_before_anything_else()
    {
        var host = Host();
        _routes[$"{host}/robots.txt"] = _ => throw new HttpRequestException("x", new BlockedAddressException());
        var admin = await LoginAsync(CreateClient());

        Assert.Equal("BlockedAddress", (await FetchAsync(admin, $"{host}/feed.json")).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task The_robots_txt_is_fetched_once_per_host_and_reused()
    {
        var host = Host();
        var url = $"{host}/feed.json";
        _routes[$"{host}/robots.txt"] = _ => Text("User-agent: *\nDisallow:\n");
        _routes[url] = _ => Json("{\"items\":[]}");
        var admin = await LoginAsync(CreateClient());

        await FetchAsync(admin, url);
        await FetchAsync(admin, url);

        Assert.Single(_requests, r => r == $"{host}/robots.txt");
        Assert.Equal(2, _requests.Count(r => r == url));
    }

    [Fact]
    public async Task Requests_to_one_host_are_spaced_and_the_wait_does_not_use_up_the_timeout()
    {
        var host = Host();
        var url = $"{host}/feed.json";
        _routes[url] = _ => Json("{\"items\":[]}");
        var admin = await LoginAsync(CreateClient(timeoutSeconds: 1, perHostDelaySeconds: 2));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var run = await FetchAsync(admin, url); // robots.txt now, the feed 2 s later
        watch.Stop();

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(1.8), $"took only {watch.Elapsed}");
    }

    public void Dispose() => _factories.ForEach(f => f.Dispose());
}
