using System.Text.RegularExpressions;
using App.Api.Http;
using App.Application.Public;
using App.Application.Reference;

namespace App.UnitTests;

public class SeoPagesTests
{
    private const string Base = "https://example.org";
    private const string Nasty = "</title></script><script>alert(1)</script>";

    private static EventDetailDto Event(string title, string? description = null, string? image = null, bool synthetic = false) =>
        new(Guid.NewGuid(), title, new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero), true, null, new CityDto("prizren", "Prizren"),
            null, null, null, null, null, image, "Burimi", "https://src.example/e", DateTimeOffset.UtcNow, "fresh", "active", synthetic,
            description, []);

    [Fact]
    public void Markup_in_the_title_cannot_break_out_of_title_meta_or_json_ld()
    {
        var html = SeoPages.EventPage(SeoPages.BuiltInShell, Event(Nasty, Nasty), Base);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Single(Regex.Matches(html, "</script>"));
        Assert.Single(Regex.Matches(html, "<title>"));
        Assert.Contains("&lt;/title&gt;", html);
    }

    [Fact]
    public void Event_page_replaces_shell_title_and_description_and_adds_tags()
    {
        var shell = "<html><head><title>Kosova Radar</title><meta name=\"description\" content=\"old\" /></head><body></body></html>";
        var html = SeoPages.EventPage(shell, Event("Koncert"), Base);

        Assert.Single(Regex.Matches(html, "<title>"));
        Assert.DoesNotContain("old", html);
        Assert.Contains("<title>Koncert · Kosova Radar</title>", System.Net.WebUtility.HtmlDecode(html));
        Assert.Contains("20.10.2026, 20:00 · Prizren", System.Net.WebUtility.HtmlDecode(html)); // Pristina time, no description
        Assert.DoesNotContain("og:image", html);
        Assert.DoesNotContain("noindex", html);
    }

    [Fact]
    public void Only_https_images_and_synthetic_items_are_noindex()
    {
        Assert.DoesNotContain("og:image", SeoPages.EventPage(SeoPages.BuiltInShell, Event("A", image: "http://x/y.png"), Base));
        Assert.Contains("og:image", SeoPages.EventPage(SeoPages.BuiltInShell, Event("A", image: "https://x/y.png"), Base));
        Assert.Contains("noindex", SeoPages.EventPage(SeoPages.BuiltInShell, Event("A", synthetic: true), Base));
    }

    [Fact]
    public void Description_is_truncated_to_200_chars()
    {
        var html = SeoPages.EventPage(SeoPages.BuiltInShell, Event("A", new string('x', 500)), Base);
        Assert.Contains($"content=\"{new string('x', 200)}\"", html);
        Assert.DoesNotContain(new string('x', 201), html);
    }

    [Fact]
    public void Sitemap_and_robots_use_the_base_url()
    {
        var id = Guid.NewGuid();
        var xml = SeoPages.Sitemap(Base, [id], []);
        Assert.Contains($"<loc>{Base}/events/{id}</loc>", xml);
        Assert.Contains($"<loc>{Base}/</loc>", xml);
        Assert.Contains($"Sitemap: {Base}/sitemap.xml", SeoPages.Robots(Base));
    }
}
