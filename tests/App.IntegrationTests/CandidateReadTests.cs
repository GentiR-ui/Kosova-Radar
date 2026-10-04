using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Domain.Content;
using App.Domain.Reference;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class CandidateReadTests(AppFactory app)
{
    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
        return client;
    }

    private Task<HttpClient> LoginAdminAsync() => LoginAsync("admin@kosovaradar.local", "Radar-admin-dev-2026");

    private async Task<(Guid SourceId, Guid CandidateId)> SeedAsync(Action<Candidate> configure)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = new App.Domain.Sources.Source { Name = $"Burim {Guid.NewGuid():N}"[..14], CreatedAt = DateTimeOffset.UtcNow };
        db.Sources.Add(source);
        var candidate = new Candidate
        {
            SourceId = source.Id, Kind = ItemKind.Event, SourceUrl = "https://example.test/x", Evidence = "{\"raw\":true}",
            Fingerprint = Guid.NewGuid().ToString("N"), ContentHash = "h", FetchedAt = DateTimeOffset.UtcNow,
        };
        configure(candidate);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return (source.Id, candidate.Id);
    }

    [Fact]
    public async Task Detail_resolves_suggested_category_ai_block_and_duplicate_title()
    {
        var admin = await LoginAdminAsync();
        Guid categoryId, otherId;
        await using (var scope = app.Services.CreateAsyncScope())
            categoryId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Categories
                .Where(c => c.Slug == "muzike").Select(c => c.Id).SingleAsync();
        var (_, other) = await SeedAsync(c => c.Fields = new CandidateFields { Title = "Titulli i dyfishuar" });
        otherId = other;
        var (_, id) = await SeedAsync(c =>
        {
            c.SuggestedCategoryId = categoryId;
            c.AiModel = "fake"; c.AiConfidence = 0.5; c.AiProcessedAt = DateTimeOffset.UtcNow; c.AiSuggestedFields = ["categorySlug"];
            c.DuplicateOfKind = ItemKind.Event; c.DuplicateOfId = otherId; c.DuplicateOfIsCandidate = true;
            c.Fields = new CandidateFields { Title = "T", OldPrice = 100m, NewPrice = 75m };
            c.Issues = [new Issue("PossibleDuplicate", IssueSeverity.Review, null, "m")];
        });

        var res = await admin.GetAsync($"/api/admin/candidates/{id}");
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("muzike", body.GetProperty("suggestedCategory").GetProperty("slug").GetString());
        Assert.Equal("fake", body.GetProperty("ai").GetProperty("model").GetString());
        Assert.Equal("categorySlug", body.GetProperty("ai").GetProperty("suggestedFields")[0].GetString());
        var dup = body.GetProperty("duplicateOf");
        Assert.Equal("Titulli i dyfishuar", dup.GetProperty("title").GetString());
        Assert.True(dup.GetProperty("isCandidate").GetBoolean());
        Assert.Equal(25, body.GetProperty("fields").GetProperty("discountPercent").GetInt32());
        var issue = body.GetProperty("issues")[0];
        Assert.Equal("PossibleDuplicate", issue.GetProperty("code").GetString());
        Assert.Equal("review", issue.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Detail_without_ai_or_duplicate_has_nulls_and_a_vanished_duplicate_target_has_no_title()
    {
        var admin = await LoginAdminAsync();
        var (_, plain) = await SeedAsync(_ => { });
        var (_, orphan) = await SeedAsync(c =>
        {
            c.DuplicateOfKind = ItemKind.Deal; c.DuplicateOfId = Guid.NewGuid(); c.DuplicateOfIsCandidate = false;
        });

        var a = JsonDocument.Parse(await admin.GetStringAsync($"/api/admin/candidates/{plain}")).RootElement;
        var b = JsonDocument.Parse(await admin.GetStringAsync($"/api/admin/candidates/{orphan}")).RootElement;

        Assert.Equal(JsonValueKind.Null, a.GetProperty("ai").ValueKind);
        Assert.Equal(JsonValueKind.Null, a.GetProperty("duplicateOf").ValueKind);
        Assert.Equal(JsonValueKind.Null, a.GetProperty("suggestedCategory").ValueKind);
        Assert.Equal(JsonValueKind.Null, b.GetProperty("duplicateOf").GetProperty("title").ValueKind);
    }

    [Fact]
    public async Task List_is_newest_first_and_leaves_out_the_evidence()
    {
        var admin = await LoginAdminAsync();
        var (source, _) = await SeedAsync(c => c.FetchedAt = DateTimeOffset.UtcNow.AddHours(-2));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Candidates.Add(new Candidate
            {
                SourceId = source, Kind = ItemKind.Deal, SourceUrl = "https://example.test/y", Evidence = "e",
                Fingerprint = Guid.NewGuid().ToString("N"), ContentHash = "h", FetchedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var page = JsonDocument.Parse(await admin.GetStringAsync($"/api/admin/candidates?sourceId={source}")).RootElement;

        var items = page.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("deal", items[0].GetProperty("kind").GetString());
        Assert.False(items[0].TryGetProperty("evidence", out _));
    }

    [Fact]
    public async Task Several_invalid_parameters_are_all_reported()
    {
        var admin = await LoginAdminAsync();

        var res = await admin.GetAsync("/api/admin/candidates?status=1&sourceId=x&page=0");
        var errors = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True(errors.TryGetProperty("status", out _));
        Assert.True(errors.TryGetProperty("sourceId", out _));
        Assert.True(errors.TryGetProperty("page", out _));
    }

    [Fact]
    public async Task Anonymous_gets_401_on_list_and_detail()
    {
        var anon = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/admin/candidates")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/admin/candidates/{Guid.NewGuid()}")).StatusCode);
    }
}
