using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BacklinkStudio.Application;

public static class BacklinkStudioTelemetry
{
    private static long _queueDepth;
    private static long _runningJobs;
    private static long _failedJobs;
    private static long _deadLetterJobs;
    private static long _onlineWorkers;
    private static long _dueSchedules;
    private static double _workerUtilization;

    public const string SourceName = "BacklinkStudio.Engine";
    public static readonly ActivitySource Activities = new(SourceName);
    public static readonly Meter Meter = new(SourceName);
    public static readonly Counter<long> JobsQueued = Meter.CreateCounter<long>("backlinkstudio.jobs.queued");
    public static readonly Counter<long> JobsCompleted = Meter.CreateCounter<long>("backlinkstudio.jobs.completed");
    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>("backlinkstudio.jobs.failed");
    public static readonly Counter<long> JobsClaimed = Meter.CreateCounter<long>("backlinkstudio.jobs.claimed");
    public static readonly Counter<long> JobsRetried = Meter.CreateCounter<long>("backlinkstudio.jobs.retried");
    public static readonly Counter<long> JobsDeadLettered = Meter.CreateCounter<long>("backlinkstudio.jobs.dead_lettered");
    public static readonly Counter<long> JobsRecovered = Meter.CreateCounter<long>("backlinkstudio.jobs.recovered");
    public static readonly Counter<long> JobsPaused = Meter.CreateCounter<long>("backlinkstudio.jobs.paused");
    public static readonly Counter<long> JobLeasesLost = Meter.CreateCounter<long>("backlinkstudio.jobs.lease_lost");
    public static readonly Counter<long> RateLimitDelays = Meter.CreateCounter<long>("backlinkstudio.rate_limit.delays");
    public static readonly UpDownCounter<long> WorkerActiveJobs = Meter.CreateUpDownCounter<long>("backlinkstudio.worker.active_jobs");
    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>("backlinkstudio.job.duration", "s");
    public static readonly Counter<long> AnalysisRequests = Meter.CreateCounter<long>("backlinkstudio.analysis.requests");
    public static readonly Counter<long> AnalysisFailures = Meter.CreateCounter<long>("backlinkstudio.analysis.failures");
    public static readonly Histogram<double> AnalysisDuration = Meter.CreateHistogram<double>("backlinkstudio.analysis.duration", "s");
    public static readonly Counter<long> VerificationChecks = Meter.CreateCounter<long>("backlinkstudio.verification.checks");
    public static readonly Counter<long> VerificationFound = Meter.CreateCounter<long>("backlinkstudio.verification.found");
    public static readonly Counter<long> BacklinksLost = Meter.CreateCounter<long>("backlinkstudio.backlinks.lost");
    public static readonly Histogram<double> VerificationDuration = Meter.CreateHistogram<double>("backlinkstudio.verification.duration", "s");
    public static readonly Counter<long> SchedulesClaimed = Meter.CreateCounter<long>("backlinkstudio.schedules.claimed");
    public static readonly Counter<long> ScheduleOccurrences = Meter.CreateCounter<long>("backlinkstudio.schedules.occurrences");
    public static readonly Counter<long> ScheduleFailures = Meter.CreateCounter<long>("backlinkstudio.schedules.failures");
    public static readonly Counter<long> ScheduledJobs = Meter.CreateCounter<long>("backlinkstudio.schedules.jobs_queued");
    public static readonly Counter<long> ScheduleLeasesLost = Meter.CreateCounter<long>("backlinkstudio.schedules.lease_lost");
    public static readonly Counter<long> ReportsGenerated = Meter.CreateCounter<long>("backlinkstudio.reports.generated");
    public static readonly Counter<long> ReportRows = Meter.CreateCounter<long>("backlinkstudio.reports.rows");
    public static readonly Counter<long> BacklinkSourcesImported = Meter.CreateCounter<long>("backlink_sources_total");
    public static readonly Counter<long> BacklinkSourcesValidated = Meter.CreateCounter<long>("backlink_sources_validated");
    public static readonly Counter<long> BacklinkSourcesWordPress = Meter.CreateCounter<long>("backlink_sources_wordpress");
    public static readonly Counter<long> BacklinkSourcesCommentCapable = Meter.CreateCounter<long>("backlink_sources_comment_capable");
    public static readonly Counter<long> BacklinkSubmissionJobsQueued = Meter.CreateCounter<long>("backlink_submission_jobs_queued");
    public static readonly Counter<long> BacklinkSubmissionsSubmitted = Meter.CreateCounter<long>("backlink_submissions_submitted");
    public static readonly Counter<long> BacklinkSubmissionsPendingModeration = Meter.CreateCounter<long>("backlink_submissions_pending_moderation");
    public static readonly Counter<long> BacklinkSubmissionsApproved = Meter.CreateCounter<long>("backlink_submissions_approved");
    public static readonly Counter<long> BacklinkSubmissionsRejected = Meter.CreateCounter<long>("backlink_submissions_rejected");
    public static readonly Counter<long> BacklinkSubmissionsDuplicate = Meter.CreateCounter<long>("backlink_submissions_duplicate");
    public static readonly Counter<long> BacklinkSubmissionsFailed = Meter.CreateCounter<long>("backlink_submissions_failed");
    public static readonly Counter<long> BacklinksPendingVerification = Meter.CreateCounter<long>("backlinks_pending_verification");
    public static readonly Counter<long> BacklinksVerified = Meter.CreateCounter<long>("backlinks_verified");
    public static readonly Counter<long> BacklinksMissing = Meter.CreateCounter<long>("backlinks_missing");
    public static readonly Histogram<double> SubmissionDuration = Meter.CreateHistogram<double>("submission_duration", "s");
    public static readonly Histogram<double> SourceValidationDuration = Meter.CreateHistogram<double>("validation_duration", "s");
    public static readonly ObservableGauge<long> QueueDepth = Meter.CreateObservableGauge("backlinkstudio.jobs.queue_depth", () => Volatile.Read(ref _queueDepth));
    public static readonly ObservableGauge<long> RunningJobs = Meter.CreateObservableGauge("backlinkstudio.jobs.running", () => Volatile.Read(ref _runningJobs));
    public static readonly ObservableGauge<long> FailedJobs = Meter.CreateObservableGauge("backlinkstudio.jobs.failed_current", () => Volatile.Read(ref _failedJobs));
    public static readonly ObservableGauge<long> DeadLetterJobs = Meter.CreateObservableGauge("backlinkstudio.jobs.dead_letter_current", () => Volatile.Read(ref _deadLetterJobs));
    public static readonly ObservableGauge<long> OnlineWorkers = Meter.CreateObservableGauge("backlinkstudio.workers.online", () => Volatile.Read(ref _onlineWorkers));
    public static readonly ObservableGauge<long> DueSchedules = Meter.CreateObservableGauge("backlinkstudio.schedules.due", () => Volatile.Read(ref _dueSchedules));
    public static readonly ObservableGauge<double> WorkerUtilization = Meter.CreateObservableGauge("backlinkstudio.worker.utilization", () => Volatile.Read(ref _workerUtilization), "1");

    public static void UpdateOperationalMetrics(OperationalMetricsSnapshot snapshot)
    {
        Interlocked.Exchange(ref _queueDepth, snapshot.QueueDepth);
        Interlocked.Exchange(ref _runningJobs, snapshot.RunningJobs);
        Interlocked.Exchange(ref _failedJobs, snapshot.FailedJobs);
        Interlocked.Exchange(ref _deadLetterJobs, snapshot.DeadLetterJobs);
        Interlocked.Exchange(ref _onlineWorkers, snapshot.OnlineWorkers);
        Interlocked.Exchange(ref _dueSchedules, snapshot.DueSchedules);
    }

    public static void UpdateWorkerUtilization(int activeJobs, int concurrency) =>
        Interlocked.Exchange(ref _workerUtilization, concurrency <= 0 ? 0 : Math.Clamp((double)activeJobs / concurrency, 0, 1));
}
