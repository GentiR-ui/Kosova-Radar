using App.Application.Ingestion;
using App.Domain.Reference;

namespace App.UnitTests;

public class RssFeedParserTests
{
    private static readonly Guid Source = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static FeedParseResult Parse(string xml, ItemKind kind = ItemKind.Event) => RssFeedParser.Parse(Source, kind, xml);

    [Fact]
    public void Rss2_items_become_candidates_with_stripped_text_and_outer_xml_evidence()
    {
        var result = Parse(Fixture("rss2.xml"));

        Assert.Null(result.ErrorCode);
        Assert.Equal(4, result.Received);
        Assert.Equal(2, result.Items.Count);

        var first = result.Items[0];
        Assert.Equal(ItemKind.Event, first.Kind);
        Assert.Equal("evt-1", first.ExternalId);
        Assert.Equal("Koncert & Xhaz", first.Fields.Title);
        Assert.Equal("https://events.example/e/1", first.SourceUrl);
        Assert.Equal("https://events.example/e/1", first.Fields.Url);
        Assert.Equal("Koncert me hyrje 5 € & muzikë live.", first.Text);
        Assert.StartsWith("<item>", first.Evidence);
        Assert.Contains("<guid isPermaLink=\"false\">evt-1</guid>", first.Evidence);
        Assert.Null(first.Fields.StartsAt);
    }

    [Fact]
    public void Content_encoded_wins_over_description_and_missing_guid_falls_back_to_the_link()
    {
        var second = Parse(Fixture("rss2.xml")).Items[1];

        Assert.Equal("https://events.example/e/2", second.ExternalId);
        Assert.Equal("Teksti i plotë i shfaqjes", second.Text);
    }

    [Fact]
    public void Items_without_identity_or_with_a_non_http_link_fail_individually()
    {
        var result = Parse(Fixture("rss2.xml"));

        Assert.Collection(result.Failures,
            f => Assert.Equal(new ItemFailure(2, "MissingIdentity"), f),
            f => Assert.Equal(new ItemFailure(3, "InvalidField:url"), f));
    }

    [Fact]
    public void Atom_entries_use_the_alternate_link_and_the_source_default_kind()
    {
        var result = Parse(Fixture("atom.xml"), ItemKind.Deal);

        Assert.Null(result.ErrorCode);
        Assert.Equal(3, result.Received);
        Assert.Equal(2, result.Items.Count);

        var first = result.Items[0];
        Assert.Equal(ItemKind.Deal, first.Kind);
        Assert.Equal("urn:uuid:1225c695-cfb8-4ebb-aaaa-80da344efa6a", first.ExternalId);
        Assert.Equal("https://deals.example/d/1", first.SourceUrl);
        Assert.Equal("Laptop -20%", first.Fields.Title);
        Assert.Equal("Laptop me ulje 20%", first.Text);

        Assert.Equal("https://deals.example/d/2", result.Items[1].ExternalId);
        Assert.Equal("Vetëm lidhje", result.Items[1].Text);
        Assert.Equal("MissingIdentity", Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void Malformed_xml_is_a_malformed_payload()
    {
        var result = Parse(Fixture("malformed.xml"));

        Assert.Equal("MalformedPayload", result.ErrorCode);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"items\":[]}")]
    [InlineData("<html><body>Not a feed</body></html>")]
    public void Anything_that_is_not_a_feed_is_malformed(string payload) =>
        Assert.Equal("MalformedPayload", Parse(payload).ErrorCode);

    [Fact]
    public void A_feed_without_items_is_a_valid_empty_feed()
    {
        var result = Parse("<rss version=\"2.0\"><channel><title>x</title></channel></rss>");

        Assert.Null(result.ErrorCode);
        Assert.Equal(0, result.Received);
    }

    [Fact]
    public void A_doctype_is_refused_so_entities_are_never_expanded()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;">]>
            <rss version="2.0"><channel><item><title>&b;</title><link>https://x.example/1</link></item></channel></rss>
            """;

        Assert.Equal("MalformedPayload", Parse(xml).ErrorCode);
    }

    [Fact]
    public void An_external_entity_is_never_resolved()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY x SYSTEM "file:///etc/passwd">]>
            <rss version="2.0"><channel><item><title>&x;</title><link>https://x.example/1</link></item></channel></rss>
            """;

        Assert.Equal("MalformedPayload", Parse(xml).ErrorCode);
    }

    [Fact]
    public void An_item_over_64_KB_or_with_a_long_id_fails_alone()
    {
        var big = new string('a', RadarFeedParser.MaxItemBytes);
        var longId = new string('i', RadarFeedParser.MaxExternalIdLength + 1);
        var xml = $"""
            <rss version="2.0"><channel>
              <item><title>big</title><link>https://x.example/1</link><description>{big}</description></item>
              <item><title>id</title><guid>{longId}</guid></item>
              <item><title>ok</title><link>https://x.example/3</link></item>
            </channel></rss>
            """;

        var result = Parse(xml);

        Assert.Equal(3, result.Received);
        Assert.Equal("ok", Assert.Single(result.Items).Fields.Title);
        Assert.Equal(["TooLarge", "InvalidField:externalId"], result.Failures.Select(f => f.Reason));
    }

    [Fact]
    public void The_same_item_keeps_its_fingerprint_and_a_changed_text_changes_the_hash()
    {
        const string a = "<rss version=\"2.0\"><channel><item><title>t</title><link>https://x.example/1</link><description>one</description></item></channel></rss>";
        var b = a.Replace("one", "two");

        var first = Parse(a).Items[0];
        Assert.Equal(first.ContentHash, Parse(a).Items[0].ContentHash);
        Assert.Equal(first.Fingerprint, Parse(b).Items[0].Fingerprint);
        Assert.NotEqual(first.ContentHash, Parse(b).Items[0].ContentHash);
    }

    [Theory]
    [InlineData("<title>x&amp;#0;y</title>", "InvalidField:title")]
    [InlineData("<title>ok</title><description>a&amp;#0;b</description>", "InvalidField:text")]
    public void Entities_that_decode_to_unstorable_characters_fail_the_item_not_the_run(string inner, string reason)
    {
        var result = Parse($"<rss version=\"2.0\"><channel><item>{inner}<link>https://x.example/1</link></item></channel></rss>");

        Assert.Empty(result.Items);
        Assert.Equal(reason, Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void An_empty_content_element_does_not_hide_the_summary()
    {
        var result = Parse("<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><id>1</id><content/><summary>Përmbledhje</summary></entry></feed>");

        Assert.Equal("Përmbledhje", Assert.Single(result.Items).Text);
    }

    [Fact]
    public void A_payload_over_5_MB_is_malformed()
    {
        var xml = "<rss version=\"2.0\"><channel>" + new string(' ', RssFeedParser.MaxPayloadChars) + "</channel></rss>";

        Assert.Equal("MalformedPayload", Parse(xml).ErrorCode);
    }

    [Fact]
    public void A_deeply_nested_document_is_malformed()
    {
        var xml = "<rss version=\"2.0\"><channel><item><title>" + string.Concat(Enumerable.Repeat("<a>", 5000)) + "</title></item></channel></rss>";

        Assert.Equal("MalformedPayload", Parse(xml).ErrorCode);
    }

    [Fact]
    public void Items_beyond_the_cap_are_reported_as_one_failure()
    {
        var items = string.Concat(Enumerable.Range(0, RssFeedParser.MaxItems + 5).Select(i => $"<item><guid>{i}</guid></item>"));

        var result = Parse($"<rss version=\"2.0\"><channel>{items}</channel></rss>");

        Assert.Equal(RssFeedParser.MaxItems, result.Items.Count);
        Assert.Equal(RssFeedParser.MaxItems + 5, result.Received);
        Assert.Equal("TooManyItems", Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void Hostile_markup_in_a_body_is_handled_quickly_without_crashing()
    {
        var body = string.Concat(Enumerable.Repeat("&lt;script&gt;", 6000)) + string.Concat(Enumerable.Repeat("&lt;", 5000));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = Parse($"<rss version=\"2.0\"><channel><item><guid>1</guid><description>{body}</description></item></channel></rss>");

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(1, result.Received);
    }
}
