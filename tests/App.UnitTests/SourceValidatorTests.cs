using App.Application.Sources;
using App.Domain.Reference;
using App.Domain.Sources;

namespace App.UnitTests;

public class SourceValidatorTests
{
    private static SourceRequest Fixture(string? name = "Burim", string? payload = "{\"items\":[]}", int? poll = 60) =>
        new(name, "fixture", null, null, payload, null, poll, false, false);

    private static SourceRequest Url(string type, string? url, int? poll = 60, string? kind = null) =>
        new("Burim", type, null, url, null, kind, poll, true, false);

    private static string[] Fields(SourceRequest req, bool allowFixture = true) =>
        SourceValidator.Validate(req, allowFixture).Errors.Keys.Order().ToArray();

    [Fact]
    public void Valid_fixture_keeps_the_payload_and_drops_any_url()
    {
        var (source, errors) = SourceValidator.Validate(Fixture() with { BaseUrl = "https://ignored.example/" }, true);

        Assert.Empty(errors);
        Assert.Equal(SourceType.Fixture, source!.Type);
        Assert.Null(source.BaseUrl);
        Assert.Equal("{\"items\":[]}", source.FixturePayload);
        Assert.Equal(ItemKind.Event, source.DefaultKind);
    }

    [Fact]
    public void Valid_url_source_drops_the_fixture_payload_and_trims_the_name()
    {
        var req = Url("rss", " https://example.com/rss ", kind: "deal") with { Name = "  Burim  ", FixturePayload = "x" };

        var (source, errors) = SourceValidator.Validate(req, false);

        Assert.Empty(errors);
        Assert.Equal("Burim", source!.Name);
        Assert.Equal("https://example.com/rss", source.BaseUrl);
        Assert.Null(source.FixturePayload);
        Assert.Equal(ItemKind.Deal, source.DefaultKind);
    }

    [Theory]
    [InlineData("fixture", 0, false)]
    [InlineData("fixture", 1, true)]
    [InlineData("fixture", 1440, true)]
    [InlineData("fixture", 1441, false)]
    [InlineData("jsonFeed", 14, false)]
    [InlineData("jsonFeed", 15, true)]
    [InlineData("rss", 1440, true)]
    [InlineData("rss", 1441, false)]
    public void Poll_interval_range_depends_on_the_type(string type, int poll, bool valid)
    {
        var req = type == "fixture" ? Fixture(poll: poll) : Url(type, "https://example.com/feed", poll);

        Assert.Equal(valid, SourceValidator.Validate(req, true).Source is not null);
    }

    [Fact]
    public void Missing_poll_interval_is_reported() => Assert.Equal(["pollIntervalMinutes"], Fields(Fixture(poll: null)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string? name) => Assert.Equal(["name"], Fields(Fixture(name)));

    [Fact]
    public void Name_longer_than_120_characters_is_rejected()
    {
        Assert.Empty(Fields(Fixture(new string('a', 120))));
        Assert.Equal(["name"], Fields(Fixture(new string('a', 121))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ical")]
    [InlineData("")]
    public void Unknown_type_is_reported_on_type_only(string? type) =>
        Assert.Equal(["type"], Fields(new SourceRequest("X", type, null, "https://example.com/", "{}", null, 60, false, false)));

    private static SourceRequest Html(string? adapter, string? url = "https://example.com/events", int? poll = 180) =>
        new("Faqe", "html", adapter, url, null, null, poll, false, false);

    [Theory]
    [InlineData("prishtinaEvents", "prishtinaEvents")]
    [InlineData("KAPODALIM", "kapodalim")]
    [InlineData(" kapodalim ", "kapodalim")]
    public void Html_source_keeps_a_known_adapter_in_its_canonical_spelling(string given, string stored)
    {
        var (source, errors) = SourceValidator.Validate(Html(given), false);

        Assert.Empty(errors);
        Assert.Equal(SourceType.Html, source!.Type);
        Assert.Equal(stored, source.Adapter);
        Assert.Equal("https://example.com/events", source.BaseUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("facebookEvents")]
    public void Html_source_needs_a_known_adapter(string? adapter) => Assert.Equal(["adapter"], Fields(Html(adapter)));

    [Fact]
    public void Html_source_needs_an_absolute_url_and_a_poll_interval_of_at_least_15()
    {
        Assert.Equal(["baseUrl"], Fields(Html("kapodalim", url: "/events")));
        Assert.Equal(["baseUrl"], Fields(Html("kapodalim", url: null)));
        Assert.Equal(["pollIntervalMinutes"], Fields(Html("kapodalim", poll: 14)));
        Assert.Empty(Fields(Html("kapodalim", poll: 15)));
    }

    [Fact]
    public void The_adapter_is_ignored_and_stored_null_for_other_types()
    {
        var (source, errors) = SourceValidator.Validate(Url("rss", "https://example.com/rss") with { Adapter = "kapodalim" }, false);

        Assert.Empty(errors);
        Assert.Null(source!.Adapter);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Fixture, Rss")]
    public void Type_and_kind_accept_names_only(string value)
    {
        Assert.Equal(["type"], Fields(new SourceRequest("X", value, null, "https://example.com/", "{}", null, 60, false, false)));
        Assert.Equal(["defaultKind"], Fields(Url("rss", "https://example.com/r", kind: value)));
    }

    [Fact]
    public void Name_with_control_characters_is_rejected() => Assert.Equal(["name"], Fields(Fixture("a\nb")));

    [Fact]
    public void Type_is_accepted_in_any_letter_case() =>
        Assert.NotNull(SourceValidator.Validate(Url("JSONFEED", "https://example.com/f", 15), true).Source);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative/feed")]
    [InlineData("ftp://example.com/feed")]
    [InlineData("not a url")]
    [InlineData("https://user:secret@example.com/feed")]
    public void Url_sources_need_an_absolute_http_url_without_credentials(string? url) =>
        Assert.Equal(["baseUrl"], Fields(Url("jsonFeed", url)));

    [Fact]
    public void Url_longer_than_2000_characters_is_rejected()
    {
        var prefix = "https://example.com/";
        Assert.Empty(Fields(Url("rss", prefix + new string('a', 2000 - prefix.Length))));
        Assert.Equal(["baseUrl"], Fields(Url("rss", prefix + new string('a', 2001 - prefix.Length))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Fixture_needs_a_payload(string? payload) => Assert.Equal(["fixturePayload"], Fields(Fixture(payload: payload)));

    [Fact]
    public void Fixture_payload_with_a_NUL_character_is_refused_instead_of_failing_in_the_database() =>
        Assert.Equal(["fixturePayload"], Fields(Fixture(payload: "{\"items\":[]}" + (char)0)));

    [Fact]
    public void Fixture_payload_is_not_required_to_be_json_but_is_capped_at_1_MB()
    {
        Assert.Empty(Fields(Fixture(payload: "not json")));
        Assert.Empty(Fields(Fixture(payload: new string('x', SourceValidator.MaxFixtureChars))));
        Assert.Equal(["fixturePayload"], Fields(Fixture(payload: new string('x', SourceValidator.MaxFixtureChars + 1))));
    }

    [Fact]
    public void Fixture_type_is_refused_when_fixtures_are_not_allowed() =>
        Assert.Equal(["type"], Fields(Fixture(), allowFixture: false));

    [Fact]
    public void Unknown_default_kind_is_reported() => Assert.Equal(["defaultKind"], Fields(Url("rss", "https://example.com/r", kind: "movie")));

    [Fact]
    public void Every_invalid_field_is_named_at_once() =>
        Assert.Equal(["baseUrl", "name", "pollIntervalMinutes"],
            Fields(new SourceRequest("", "jsonFeed", null, "not a url", null, null, 5, false, false)));

    [Theory]
    [InlineData(false, 0, 0, SourceHealth.Disabled)]
    [InlineData(false, 5, 0, SourceHealth.Disabled)]
    [InlineData(true, -1, 0, SourceHealth.Never)]
    [InlineData(true, 0, 0, SourceHealth.Healthy)]
    [InlineData(true, 0, 1, SourceHealth.Degraded)]
    [InlineData(true, 0, 2, SourceHealth.Degraded)]
    [InlineData(true, 0, 3, SourceHealth.Failing)]
    [InlineData(true, 0, 10, SourceHealth.Failing)]
    public void Health_is_derived_from_activity_and_failures(bool active, int fetchedDaysAgo, int failures, SourceHealth expected)
    {
        var source = new Source
        {
            Name = "X",
            IsActive = active,
            ConsecutiveFailures = failures,
            LastFetchedAt = fetchedDaysAgo < 0 ? null : DateTimeOffset.UtcNow.AddDays(-fetchedDaysAgo),
        };

        Assert.Equal(expected, source.Health);
    }
}
