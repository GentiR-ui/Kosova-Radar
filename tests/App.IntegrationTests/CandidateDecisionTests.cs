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
public class CandidateDecisionTests(AppFactory app)
{
    private async Task<HttpClient> LoginAdminAsync()
    {
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" })).StatusCode);
        return client;
    }

    private async Task<Guid> SeedAsync(Action<Candidate> configure, bool unreliableSource = false)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = new App.Domain.Sources.Source { Name = $"Burim {Guid.NewGuid():N}"[..14], CreatedAt = DateTimeOffset.UtcNow, IsUnreliable = unreliableSource };
        db.Sources.Add(source);
        var candidate = new Candidate
        {
            SourceId = source.Id,
            Kind = ItemKind.Event,
            SourceUrl = "https://example.test/x",
            Evidence = "{}",
            Fingerprint = Guid.NewGuid().ToString("N"),
            ContentHash = "h",
            FetchedAt = DateTimeOffset.UtcNow,
            Status = CandidateStatus.Pending,
            Validation = ValidationOutcome.Valid,
            Fields = new CandidateFields { Title = "Koncert", Url = "https://example.test/x", CitySlug = "prishtine", StartsAt = DateTimeOffset.UtcNow.AddDays(10) },
        };
        configure(candidate);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    private static object Body(string title = "Titulli i ri", string? citySlug = "prishtine", string? categorySlug = null) => new
    {
        title,
        url = "https://example.test/new",
        citySlug,
        categorySlug,
        startsAt = DateTimeOffset.UtcNow.AddDays(10),
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Edit_is_audited_with_before_and_after_and_keeps_the_unreliable_source_issue()
    {
        var admin = await LoginAdminAsync();
        var id = await SeedAsync(_ => { }, unreliableSource: true);

        var res = await admin.PutAsJsonAsync($"/api/admin/candidates/{id}", Body());
        var body = await JsonAsync(res);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("needsReview", body.GetProperty("validation").GetString());
        Assert.Contains(body.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "SourceUnreliable");
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"));
        var changes = audit.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("action").GetString() == "candidate.edit").GetProperty("changes");
        Assert.Equal("Koncert", changes.GetProperty("before").GetProperty("fields").GetProperty("title").GetString());
        Assert.Equal("Titulli i ri", changes.GetProperty("after").GetProperty("fields").GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_failed_edit_writes_no_audit_entry()
    {
        var admin = await LoginAdminAsync();
        var id = await SeedAsync(_ => { });

        var res = await admin.PutAsJsonAsync($"/api/admin/candidates/{id}", Body(citySlug: "atlantis"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"));
        Assert.Equal(0, audit.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_category_of_the_other_kind_is_refused()
    {
        var admin = await LoginAdminAsync();
        string dealCategory;
        await using (var scope = app.Services.CreateAsyncScope())
            dealCategory = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Categories
                .Where(c => c.Kind == ItemKind.Deal).Select(c => c.Slug).FirstAsync();
        var id = await SeedAsync(_ => { });

        var res = await admin.PutAsJsonAsync($"/api/admin/candidates/{id}", Body(categorySlug: dealCategory));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await JsonAsync(res)).GetProperty("errors").TryGetProperty("categorySlug", out _));
    }

    [Fact]
    public async Task Rejecting_an_auto_rejected_candidate_makes_it_final_and_audits_the_reason()
    {
        var admin = await LoginAdminAsync();
        var id = await SeedAsync(c => { c.Status = CandidateStatus.Rejected; c.Validation = ValidationOutcome.Rejected; });

        var res = await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/reject", new { reason = "  Spam  " });
        var again = await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/reject", new { reason = "Again" });

        Assert.Equal("Spam", (await JsonAsync(res)).GetProperty("rejectionReason").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"));
        Assert.Single(audit.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "candidate.reject");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("501")]
    public async Task Invalid_reasons_are_400_on_reason_and_leave_the_candidate_pending(string? reason)
    {
        var admin = await LoginAdminAsync();
        var id = await SeedAsync(_ => { });
        if (reason == "501") reason = new string('x', 501);

        var res = await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/reject", new { reason });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await JsonAsync(res)).GetProperty("errors").TryGetProperty("reason", out _));
        Assert.Equal("pending", (await JsonAsync(await admin.GetAsync($"/api/admin/candidates/{id}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unknown_and_malformed_ids_are_404_and_anonymous_is_401()
    {
        var admin = await LoginAdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/admin/candidates/{Guid.NewGuid()}", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/admin/candidates/nope/reject", new { reason = "x" })).StatusCode);
        var anon = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsJsonAsync($"/api/admin/candidates/{Guid.NewGuid()}", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync($"/api/admin/candidates/{Guid.NewGuid()}/reject", new { reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task Editing_a_deal_clears_event_only_fields()
    {
        var admin = await LoginAdminAsync();
        var id = await SeedAsync(c => c.Kind = ItemKind.Deal);

        var res = await admin.PutAsJsonAsync($"/api/admin/candidates/{id}",
            new { title = "Deal", url = "https://example.test/o", venueName = "Hall", startsAt = DateTimeOffset.UtcNow.AddDays(3), oldPrice = 100, newPrice = 80 });
        var fields = (await JsonAsync(res)).GetProperty("fields");

        Assert.Equal(JsonValueKind.Null, fields.GetProperty("venueName").ValueKind);
        Assert.Equal(JsonValueKind.Null, fields.GetProperty("startsAt").ValueKind);
        Assert.Equal(20, fields.GetProperty("discountPercent").GetInt32());
    }
}
