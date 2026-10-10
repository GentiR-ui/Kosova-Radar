namespace App.Application.Audit;

/// <summary>Audited actions (design.md §5).</summary>
public static class AuditActions
{
    public const string CandidateApprove = "candidate.approve";
    public const string CandidateReject = "candidate.reject";
    public const string CandidateEdit = "candidate.edit";
    public const string CandidateMerge = "candidate.merge";
    public const string CandidateAutoPublish = "candidate.autoPublish";
    public const string ItemEdit = "item.edit";
    public const string ItemExpire = "item.expire";
    public const string ItemWithdraw = "item.withdraw";
    public const string SourceCreate = "source.create";
    public const string SourceUpdate = "source.update";
    public const string SourceFetch = "source.fetch";
    public const string UserCreate = "user.create";
    public const string UserDeactivate = "user.deactivate";
}

/// <summary>Audited entity types (design.md §5).</summary>
public static class AuditEntityTypes
{
    public const string Candidate = "candidate";
    public const string Event = "event";
    public const string Deal = "deal";
    public const string Source = "source";
    public const string User = "user";
}
