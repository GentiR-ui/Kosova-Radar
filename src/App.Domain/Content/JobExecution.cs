namespace App.Domain.Content;

/// <summary>History of one scheduled or manual job run (design.md §4).</summary>
public sealed class JobExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public JobName JobName { get; set; }
    public Guid? SourceId { get; set; }
    public JobTrigger Trigger { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public JobStatus Status { get; set; }
    public int Received { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Failed { get; set; }

    /// <summary>PascalCase code such as <c>Timeout</c> or <c>Interrupted</c>.</summary>
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public required string CorrelationId { get; set; }
}
