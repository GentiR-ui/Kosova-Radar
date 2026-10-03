using System.Text.Json;
using App.Application.Audit;

namespace App.UnitTests;

public class AuditChangesTests
{
    private enum Kind { NeedsReview }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Serializes_before_and_after_as_camelCase_with_nulls_kept()
    {
        var root = Parse(AuditChanges.Serialize(null, new { Title = "A", Kind = Kind.NeedsReview }));

        Assert.Equal(JsonValueKind.Null, root.GetProperty("before").ValueKind);
        Assert.Equal("A", root.GetProperty("after").GetProperty("title").GetString());
        Assert.Equal("needsReview", root.GetProperty("after").GetProperty("kind").GetString());
    }

    [Fact]
    public void Drops_password_properties_at_any_depth_and_case()
    {
        var json = AuditChanges.Serialize(
            new { Email = "a@b.c", Password = "p1" },
            new { Items = new[] { new { PasswordHash = "h", Keep = 1 } }, Inner = new { CurrentPASSWORD = "p2", Ok = true, ApiKey = "k", AccessToken = "t" } });

        Assert.DoesNotContain("\"k\"", json);
        Assert.DoesNotContain("\"t\"", json);

        Assert.DoesNotContain("assword", json, StringComparison.OrdinalIgnoreCase);
        var root = Parse(json);
        Assert.Equal("a@b.c", root.GetProperty("before").GetProperty("email").GetString());
        Assert.Equal(1, root.GetProperty("after").GetProperty("items")[0].GetProperty("keep").GetInt32());
        Assert.True(root.GetProperty("after").GetProperty("inner").GetProperty("ok").GetBoolean());
    }
}
