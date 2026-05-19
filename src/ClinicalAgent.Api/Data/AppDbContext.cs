using Microsoft.EntityFrameworkCore;

namespace ClinicalAgent.Api.Data;

/// <summary>EF Core DbContext backed by SQLite (dev) or Azure SQL (prod).</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<UploadRecord>    Uploads    => Set<UploadRecord>();
    public DbSet<ReportJobRecord> ReportJobs => Set<ReportJobRecord>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<UploadRecord>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.UserId);
            e.HasIndex(u => u.UploadedAt);
        });

        mb.Entity<ReportJobRecord>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.UserId);
            e.HasIndex(r => r.CreatedAt);
            e.HasIndex(r => r.UploadId);
        });
    }
}
