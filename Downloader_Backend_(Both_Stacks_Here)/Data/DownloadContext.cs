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

            // Configure DownloadJob entity
            modelBuilder.Entity<DownloadJob>()
                .HasKey(d => d.Id);

            modelBuilder.Entity<DownloadJob>()
                .Property(d => d.Id)
                .IsRequired();

            modelBuilder.Entity<DownloadJob>()
                .Property(d => d.Url)
                .IsRequired();

            modelBuilder.Entity<DownloadJob>()
                .Property(d => d.Format)
                .IsRequired();

            modelBuilder.Entity<DownloadJob>()
                .Property(d => d.Key)
                .IsRequired();

            modelBuilder.Entity<DownloadJob>()
                .Property(d => d.CreatedAt)
                .HasDefaultValueSql("datetime('now')");
        }
    }
}
