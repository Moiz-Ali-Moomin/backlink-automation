namespace BacklinkStudio.Domain;

public sealed class DiscoveryQuery
{
    private DiscoveryQuery() { }

    public DiscoveryQuery(
        Guid projectId,
        DiscoveryProviderKind provider,
        string? queryText,
        string inputJson,
        int maximumResults,
        DateTimeOffset now)
    {
        if (maximumResults is < 1 or > 5_000)
        {
            throw new DomainRuleException("Discovery maximum results must be between 1 and 5000.");
        }

        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        Provider = provider;
        QueryText = Guard.Optional(queryText, 2_000, nameof(QueryText));
        InputJson = Guard.Required(inputJson, 1_048_576, nameof(InputJson));
        MaximumResults = maximumResults;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public DiscoveryProviderKind Provider { get; private set; }
    public string? QueryText { get; private set; }
    public string InputJson { get; private set; } = null!;
    public int MaximumResults { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class DiscoveryRun
{
    private DiscoveryRun() { }

    public DiscoveryRun(Guid projectId, Guid discoveryQueryId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        DiscoveryQueryId = discoveryQueryId;
        Status = DiscoveryRunStatus.Queued;
        Errors = [];
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid DiscoveryQueryId { get; private set; }
    public Guid? JobId { get; private set; }
    public DiscoveryRunStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public int UrlsDiscovered { get; private set; }
    public int UrlsAccepted { get; private set; }
    public int DuplicateCount { get; private set; }
    public int BlockedCount { get; private set; }
    public int InvalidCount { get; private set; }
    public int ErrorCount { get; private set; }
    public string[] Errors { get; private set; } = [];

    public void AttachJob(Guid jobId)
    {
        if (JobId is not null)
        {
            throw new DomainRuleException("Discovery run already has a job.");
        }

        JobId = jobId;
    }

    public void Start(DateTimeOffset now)
    {
        if (Status == DiscoveryRunStatus.Running)
        {
            return;
        }
        if (Status is not (DiscoveryRunStatus.Queued or DiscoveryRunStatus.Failed))
        {
            throw new DomainRuleException($"Discovery run in state {Status} cannot start.");
        }

        Status = DiscoveryRunStatus.Running;
        StartedAt ??= now;
        FinishedAt = null;
    }

    public void Complete(
        int discovered,
        int accepted,
        int duplicates,
        int blocked,
        int invalid,
        IReadOnlyCollection<string> errors,
        DateTimeOffset now)
    {
        if (Status != DiscoveryRunStatus.Running)
        {
            throw new DomainRuleException("Only a running discovery run can complete.");
        }

        if (discovered < 0 || accepted < 0 || duplicates < 0 || blocked < 0 || invalid < 0)
        {
            throw new DomainRuleException("Discovery counters cannot be negative.");
        }

        Status = DiscoveryRunStatus.Succeeded;
        UrlsDiscovered = discovered;
        UrlsAccepted = accepted;
        DuplicateCount = duplicates;
        BlockedCount = blocked;
        InvalidCount = invalid;
        Errors = NormalizeErrors(errors);
        ErrorCount = errors.Count;
        FinishedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        if (Status != DiscoveryRunStatus.Running)
        {
            throw new DomainRuleException("Only a running discovery run can fail.");
        }

        Status = DiscoveryRunStatus.Failed;
        Errors = NormalizeErrors([error]);
        ErrorCount = 1;
        FinishedAt = now;
    }

    private static string[] NormalizeErrors(IEnumerable<string> errors) => errors
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Take(100)
        .Select(x => x.Trim().Length <= 500 ? x.Trim() : x.Trim()[..500])
        .ToArray();
}
