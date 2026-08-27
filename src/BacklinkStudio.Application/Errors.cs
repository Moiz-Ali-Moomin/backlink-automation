namespace BacklinkStudio.Application;

public abstract class BacklinkStudioException(string message) : Exception(message);

public sealed class ValidationException(string message) : BacklinkStudioException(message);

public sealed class ResourceNotFoundException(string resource, Guid id)
    : BacklinkStudioException($"{resource} '{id}' was not found.");

public sealed class ConflictException(string message) : BacklinkStudioException(message);

public sealed class IdempotencyConflictException()
    : BacklinkStudioException("The idempotency key was already used with different input.");

public sealed class PolicyRejectedException(string message) : BacklinkStudioException(message);
