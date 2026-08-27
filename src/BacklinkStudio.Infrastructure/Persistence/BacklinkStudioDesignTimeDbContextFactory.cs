using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class BacklinkStudioDesignTimeDbContextFactory : IDesignTimeDbContextFactory<BacklinkStudioDbContext>
{
    public BacklinkStudioDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BacklinkStudio")
            ?? "Host=localhost;Port=5432;Database=backlinkstudio;Username=backlinkstudio;Password=development-only";
        var options = new DbContextOptionsBuilder<BacklinkStudioDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new BacklinkStudioDbContext(options);
    }
}
