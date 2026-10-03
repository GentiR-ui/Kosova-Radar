namespace App.Application.Abstractions;

/// <summary>Current time. Always UTC. Use this instead of DateTimeOffset.UtcNow so time rules are testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
