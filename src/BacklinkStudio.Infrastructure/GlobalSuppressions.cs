using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "PostgresJobQueue is the PostgreSQL implementation of the documented durable job queue.",
    Scope = "type",
    Target = "~T:BacklinkStudio.Infrastructure.Persistence.PostgresJobQueue")]
