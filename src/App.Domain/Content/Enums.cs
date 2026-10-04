namespace App.Domain.Content;

public enum CandidateStatus { Pending, Rejected, Published, Merged }

public enum ValidationOutcome { Valid, NeedsReview, Rejected }

public enum ItemStatus { Published, Expired, Withdrawn }

public enum JobName { SourceFetch, Expiration }

public enum JobTrigger { Schedule, Manual }

public enum JobStatus { Running, Succeeded, Failed }
