using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Reference;

namespace App.UnitTests;

public class SourceRunLockTests
{
    [Fact]
    public async Task A_held_lock_refuses_the_same_source_but_not_another_and_release_is_idempotent()
    {
        var locks = new SourceRunLock();
        var a = Guid.NewGuid();

        var first = await locks.TryAcquireAsync(a, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(await locks.TryAcquireAsync(a, TimeSpan.FromMilliseconds(20), CancellationToken.None));
        using (await locks.TryAcquireAsync(Guid.NewGuid(), TimeSpan.Zero, CancellationToken.None) ?? throw new Xunit.Sdk.XunitException("other source locked")) { }

        first.Dispose();
        first.Dispose(); // a second dispose must not free a lock someone else holds
        var second = await locks.TryAcquireAsync(a, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(second);
        first.Dispose();
        Assert.Null(await locks.TryAcquireAsync(a, TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task A_waiting_run_gets_the_lock_when_the_holder_finishes()
    {
        var locks = new SourceRunLock();
        var id = Guid.NewGuid();
        var holder = await locks.TryAcquireAsync(id, TimeSpan.Zero, CancellationToken.None);
        var waiting = locks.TryAcquireAsync(id, TimeSpan.FromSeconds(5), CancellationToken.None);

        holder!.Dispose();

        Assert.NotNull(await waiting);
    }
}

public class ReferenceResolverTests
{
    private static readonly ReferenceResolver Resolver = new(
        new CityMatcher([new City { Slug = "prishtine", Name = "Prishtinë", Aliases = ["Pristina"] }]),
        [
            new Category { Slug = "muzike", Name = "Muzikë & koncerte", Kind = ItemKind.Event },
            new Category { Slug = "ushqim", Name = "Ushqim & pije", Kind = ItemKind.Event },
            new Category { Slug = "teknologji", Name = "Teknologji", Kind = ItemKind.Deal },
        ]);

    [Theory]
    [InlineData("Pristina", "prishtine")]
    [InlineData("PRISHTINË", "prishtine")]
    [InlineData("Nowhere", null)]
    [InlineData(null, null)]
    public void City_text_resolves_to_a_slug_or_stays_unresolved(string? text, string? slug)
    {
        var resolved = Resolver.Resolve(ItemKind.Event, new CandidateFields { CityText = text });

        Assert.Equal(slug, resolved.CitySlug);
        Assert.Equal(text, resolved.CityText);
    }

    [Theory]
    [InlineData(ItemKind.Event, "muzike", "muzike")]
    [InlineData(ItemKind.Event, "Muzikë & koncerte", "muzike")]
    [InlineData(ItemKind.Event, "teknologji", null)]
    [InlineData(ItemKind.Deal, "teknologji", "teknologji")]
    [InlineData(ItemKind.Deal, "muzike", null)]
    [InlineData(ItemKind.Event, "  ", null)]
    public void Category_text_resolves_only_within_the_kind(ItemKind kind, string text, string? slug) =>
        Assert.Equal(slug, Resolver.Resolve(kind, new CandidateFields { CategoryText = text }).CategorySlug);

    [Fact]
    public void A_category_slug_mapped_by_a_site_parser_survives_when_it_exists_for_the_kind()
    {
        var kept = Resolver.Resolve(ItemKind.Event, new CandidateFields { CategoryText = "Music", CategorySlug = "muzike" });
        var unknown = Resolver.Resolve(ItemKind.Event, new CandidateFields { CategoryText = "Music", CategorySlug = "nuk-ekziston" });
        var wrongKind = Resolver.Resolve(ItemKind.Deal, new CandidateFields { CategoryText = "Music", CategorySlug = "muzike" });

        Assert.Equal("muzike", kept.CategorySlug);
        Assert.Null(unknown.CategorySlug);
        Assert.Null(wrongKind.CategorySlug);
    }
}
