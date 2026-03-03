using Microsoft.EntityFrameworkCore;
using Downloader_Backend.Model;

namespace Downloader_Backend.Data
{
public class DownloadContext(DbContextOptions<DownloadContext> options) : DbContext(options)
{
    public DbSet<DownloadJob> DownloadJobs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Only keep what attributes can't express
        modelBuilder.Entity<DownloadJob>()
            .Property(d => d.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP"); // optional, if you want SQL default
    }
}
}
