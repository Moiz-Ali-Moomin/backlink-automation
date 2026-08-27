using System.Net;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.UnitTests;

public sealed class JobRetryPolicyTests
{
    [Fact]
    public void Classifier_DistinguishesTransientAndPermanentFailures()
    {
        var classifier = new DefaultJobFailureClassifier();

        Assert.Equal(new JobFailureDecision(false, JobFailureKind.InvalidInput), classifier.Classify(new ValidationException("invalid")));
        Assert.Equal(new JobFailureDecision(true, JobFailureKind.RateLimited), classifier.Classify(new HttpRequestException("limited", null, HttpStatusCode.TooManyRequests)));
        Assert.Equal(new JobFailureDecision(false, JobFailureKind.AuthorizationDenied), classifier.Classify(new UnauthorizedAccessException()));
        Assert.Equal(new JobFailureDecision(true, JobFailureKind.DependencyUnavailable), classifier.Classify(new HttpRequestException("network")));
    }

    [Fact]
    public void Backoff_IsDeterministicBoundedAndExponential()
    {
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));
        var id = Guid.Parse("0198ba11-1000-7000-8000-000000000001");

        var first = policy.GetDelay(id, 1);
        var second = policy.GetDelay(id, 2);

        Assert.Equal(first, policy.GetDelay(id, 1));
        Assert.InRange(first, TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(2.4));
        Assert.InRange(second, TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(4.8));
        Assert.InRange(policy.GetDelay(id, 20), TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(60));
    }
}
