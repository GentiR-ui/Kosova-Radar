using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace App.Infrastructure.Identity;

/// <summary>
/// Server-side auth sessions (ADR-017): the cookie carries only a session key, so logout removes exactly that
/// session and a replayed copy of the cookie gets 401, while the user's other sessions stay valid.
/// In memory: sessions end on restart, acceptable for a single instance (ADR-004).
/// </summary>
public sealed class MemoryTicketStore(IMemoryCache cache) : ITicketStore
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = "auth-session:" + Guid.NewGuid().ToString("N");
        Set(key, ticket);
        return Task.FromResult(key);
    }

    // A request that started before logout must not bring the removed session back when it renews it.
    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (!cache.TryGetValue(RemovedKey(key), out _) && cache.TryGetValue(key, out _)) Set(key, ticket);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        Task.FromResult(cache.TryGetValue(key, out AuthenticationTicket? ticket) ? ticket : null);

    public Task RemoveAsync(string key)
    {
        cache.Set(RemovedKey(key), true, DefaultLifetime); // tombstone blocks late renewals
        cache.Remove(key);
        return Task.CompletedTask;
    }

    private static string RemovedKey(string key) => "removed:" + key;

    private void Set(string key, AuthenticationTicket ticket) =>
        cache.Set(key, ticket, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow + DefaultLifetime,
        });
}
