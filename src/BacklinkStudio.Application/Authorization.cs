using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public static class AuthorizationScopes
{
    public const string ProjectsRead = "projects:read";
    public const string ProjectsWrite = "projects:write";
    public const string CandidatesRead = "candidates:read";
    public const string OpportunitiesRead = "opportunities:read";
    public const string OpportunitiesApprove = "opportunities:approve";
    public const string CampaignsRead = "campaigns:read";
    public const string CampaignsWrite = "campaigns:write";
    public const string CampaignsExecute = "campaigns:execute";
    public const string BacklinksRead = "backlinks:read";
    public const string VerificationExecute = "verification:execute";
    public const string SchedulesRead = "schedules:read";
    public const string SchedulesWrite = "schedules:write";
    public const string ReportsRead = "reports:read";
    public const string ReportsWrite = "reports:write";
    public const string OwnedNetworksRead = "owned_networks:read";
    public const string OwnedNetworksWrite = "owned_networks:write";
    public const string SubmissionSourcesRead = "submission_sources:read";
    public const string SubmissionSourcesWrite = "submission_sources:write";
    public const string SubmissionTemplatesRead = "submission_templates:read";
    public const string SubmissionTemplatesWrite = "submission_templates:write";
    public const string SubmissionIdentitiesRead = "submission_identities:read";
    public const string SubmissionIdentitiesWrite = "submission_identities:write";
    public const string SubmissionsRead = "submissions:read";
    public const string SubmissionsExecute = "submissions:execute";
    public const string ManualActionsRead = "manual_actions:read";
    public const string ManualActionsWrite = "manual_actions:write";
    public const string Admin = "admin";

    public static readonly string[] All =
    [
        ProjectsRead, ProjectsWrite, CandidatesRead, OpportunitiesRead, OpportunitiesApprove,
        CampaignsRead, CampaignsWrite, CampaignsExecute, BacklinksRead, VerificationExecute,
        SchedulesRead, SchedulesWrite, ReportsRead, ReportsWrite, OwnedNetworksRead, OwnedNetworksWrite,
        SubmissionSourcesRead, SubmissionSourcesWrite, SubmissionTemplatesRead, SubmissionTemplatesWrite,
        SubmissionIdentitiesRead, SubmissionIdentitiesWrite, SubmissionsRead, SubmissionsExecute,
        ManualActionsRead, ManualActionsWrite, Admin
    ];

    /// <summary>
    /// Scopes that grant control over agent credentials themselves. A transport must never receive
    /// these implicitly; they are only ever granted by explicit operator configuration.
    /// </summary>
    public static readonly string[] CredentialManagement = [Admin];

    /// <summary>
    /// Least-privilege default for locally attached STDIO clients: read-only inspection with no
    /// write, execute, approval, or credential-management authority.
    /// </summary>
    public static readonly string[] DefaultStdio =
    [
        ProjectsRead, CandidatesRead, OpportunitiesRead, CampaignsRead, BacklinksRead,
        SchedulesRead, ReportsRead, OwnedNetworksRead, SubmissionSourcesRead, SubmissionTemplatesRead,
        SubmissionIdentitiesRead, SubmissionsRead, ManualActionsRead
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    public static bool IsKnown(string scope) => Known.Contains(scope);

    public static bool IsCredentialManagement(string scope) => Array.IndexOf(CredentialManagement, scope) >= 0;
}

public sealed record ActorContext(
    ActorType ActorType,
    string ActorId,
    Guid? CredentialId,
    string RequestId,
    string? SourceAddress)
{
    public static ActorContext System(string operation) =>
        new(ActorType.System, "backlinkstudio", null, operation, null);
}
