namespace BacklinkStudio.Domain;

public sealed class BlocklistEntry
{
    private BlocklistEntry() { }

    public BlocklistEntry(Guid projectId, BlocklistMatchType matchType, string value, string reason, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        MatchType = matchType;
        Value = Guard.Required(value, 2_048, nameof(value));
        Reason = Guard.Required(reason, 500, nameof(reason));
        Enabled = true;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public BlocklistMatchType MatchType { get; private set; }
    public string Value { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class PolicyDefinition
{
    private PolicyDefinition() { }

    public PolicyDefinition(Guid projectId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        AutomationEnabled = false;
        MinimumQualityScore = 60;
        MaximumRiskScore = 30;
        ManualReviewRequired = true;
        HourlyActionLimit = 10;
        DailyActionLimit = 50;
        PerDomainActionLimit = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public bool AutomationEnabled { get; private set; }
    public int MinimumQualityScore { get; private set; }
    public int MaximumRiskScore { get; private set; }
    public bool ManualReviewRequired { get; private set; }
    public int HourlyActionLimit { get; private set; }
    public int DailyActionLimit { get; private set; }
    public int PerDomainActionLimit { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(bool automationEnabled, int minimumQualityScore, int maximumRiskScore, bool manualReviewRequired, int hourlyActionLimit, int dailyActionLimit, int perDomainActionLimit, DateTimeOffset now)
    {
        if (minimumQualityScore is < 0 or > 100 || maximumRiskScore is < 0 or > 100)
        {
            throw new DomainRuleException("Policy scores must be between 0 and 100.");
        }

        if (hourlyActionLimit is < 0 or > 10_000 || dailyActionLimit is < 0 or > 100_000 || perDomainActionLimit is < 0 or > 10_000)
        {
            throw new DomainRuleException("Policy action limits are outside the supported range.");
        }

        AutomationEnabled = automationEnabled;
        MinimumQualityScore = minimumQualityScore;
        MaximumRiskScore = maximumRiskScore;
        ManualReviewRequired = manualReviewRequired;
        HourlyActionLimit = hourlyActionLimit;
        DailyActionLimit = dailyActionLimit;
        PerDomainActionLimit = perDomainActionLimit;
        UpdatedAt = now;
    }
}
