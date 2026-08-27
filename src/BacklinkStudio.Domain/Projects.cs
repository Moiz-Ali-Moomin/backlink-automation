namespace BacklinkStudio.Domain;

public sealed class Project
{
    private Project() { }

    public Project(string name, string primaryDomain, string? description, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        Name = Guard.Required(name, 200, nameof(Name));
        PrimaryDomain = Guard.Required(primaryDomain, 253, nameof(PrimaryDomain)).ToLowerInvariant();
        Description = Guard.Optional(description, 2_000, nameof(Description));
        Status = ProjectStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string PrimaryDomain { get; private set; } = null!;
    public string? Description { get; private set; }
    public ProjectStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}

public sealed class ProjectTarget
{
    private ProjectTarget() { }

    public ProjectTarget(
        Guid projectId,
        string url,
        string normalizedUrl,
        string? label,
        string[] keywords,
        string? preferredAnchor,
        string? category,
        int priority,
        DateTimeOffset now)
    {
        if (priority is < 0 or > 100)
        {
            throw new DomainRuleException("Priority must be between 0 and 100.");
        }

        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        Url = Guard.Required(url, 2_048, nameof(Url));
        NormalizedUrl = Guard.Required(normalizedUrl, 2_048, nameof(NormalizedUrl));
        Label = Guard.Optional(label, 200, nameof(Label));
        Keywords = keywords.Select(x => Guard.Required(x, 100, nameof(Keywords))).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToArray();
        PreferredAnchor = Guard.Optional(preferredAnchor, 500, nameof(PreferredAnchor));
        Category = Guard.Optional(category, 100, nameof(Category));
        Priority = priority;
        Enabled = true;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Url { get; private set; } = null!;
    public string NormalizedUrl { get; private set; } = null!;
    public string? Label { get; private set; }
    public string[] Keywords { get; private set; } = [];
    public string? PreferredAnchor { get; private set; }
    public string? Category { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
