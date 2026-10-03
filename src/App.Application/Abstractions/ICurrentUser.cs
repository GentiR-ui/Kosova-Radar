namespace App.Application.Abstractions;

/// <summary>The signed-in user of the current request.</summary>
public interface ICurrentUser
{
    /// <summary>Throws when there is no signed-in user: audited actions always have an actor.</summary>
    (Guid Id, string Email) Require();
}
