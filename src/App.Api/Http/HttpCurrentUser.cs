using System.Security.Claims;
using App.Application.Abstractions;

namespace App.Api.Http;

internal sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public (Guid Id, string Email) Require()
    {
        var principal = http.HttpContext?.User;
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal?.FindFirstValue(ClaimTypes.Email);
        if (!Guid.TryParse(id, out var userId) || string.IsNullOrEmpty(email))
            throw new InvalidOperationException("An audited action needs a signed-in user.");
        return (userId, email);
    }
}
