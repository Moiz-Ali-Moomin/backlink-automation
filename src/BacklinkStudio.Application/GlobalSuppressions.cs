using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "IJobQueue is the product's durable queue boundary and matches the documented ubiquitous language.",
    Scope = "type",
    Target = "~T:BacklinkStudio.Application.IJobQueue")]
