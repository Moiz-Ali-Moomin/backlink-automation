using System.Reflection;

namespace BacklinkStudio.ContractTests;

/// <summary>
/// Marker proving a tool call passed the transport scope gate and reached an Application service.
/// </summary>
public sealed class ScopeGatePassedException(string member) : Exception($"Application service member '{member}' was reached.")
{
    public string Member { get; } = member;
}

/// <summary>
/// Generates Application service stubs that throw as soon as any member is invoked, so a test can
/// distinguish "rejected by scope authorization" from "authorized and dispatched".
/// </summary>
public class ThrowingServiceProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new ScopeGatePassedException($"{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}");

    public static T Create<T>() => DispatchProxy.Create<T, ThrowingServiceProxy>()!;
}
