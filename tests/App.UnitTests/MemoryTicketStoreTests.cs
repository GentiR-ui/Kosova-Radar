using System.Security.Claims;
using App.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace App.UnitTests;

public class MemoryTicketStoreTests
{
    private static AuthenticationTicket Ticket() =>
        new(new ClaimsPrincipal(new ClaimsIdentity("test")), "Identity.Application");

    [Fact]
    public async Task A_late_renewal_does_not_revive_a_removed_session()
    {
        var store = new MemoryTicketStore(new MemoryCache(new MemoryCacheOptions()));
        var key = await store.StoreAsync(Ticket());
        Assert.NotNull(await store.RetrieveAsync(key));

        await store.RemoveAsync(key);
        await store.RenewAsync(key, Ticket());

        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task Renewal_keeps_a_live_session()
    {
        var store = new MemoryTicketStore(new MemoryCache(new MemoryCacheOptions()));
        var key = await store.StoreAsync(Ticket());
        await store.RenewAsync(key, Ticket());
        Assert.NotNull(await store.RetrieveAsync(key));
    }
}
