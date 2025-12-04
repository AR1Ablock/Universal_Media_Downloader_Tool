using Downloader_Backend.Data;
using Downloader_Backend.Model;
using Microsoft.EntityFrameworkCore;

namespace Downloader_Backend.Logic
{
    public interface IDownloadPersistence
    {
        Task Save_And_UpdateJobAsync(DownloadJob job);
        Task GetAllJobsAsync_From_DB();
        Task DeleteJobAsync(string jobId);
    }

    public class DownloadPersistence(IDbContextFactory<DownloadContext> factoryDB, ILogger<DownloadPersistence> logger, DownloadTracker tracker) : IDownloadPersistence
    {
        private readonly IDbContextFactory<DownloadContext> _factoryDB = factoryDB;
        private readonly DownloadTracker _tracker = tracker;
        private readonly ILogger<DownloadPersistence> _logger = logger;

        public async Task Save_And_UpdateJobAsync(DownloadJob job)
        {
            try
            {

                using var _dbContext = await _factoryDB.CreateDbContextAsync();

                if (job == null)
                {
                    _logger.LogError("---[DB ERROR] job is null");
                    return;
                }

                var existingJob = await _dbContext.DownloadJobs.FindAsync(job.Id);
                if (existingJob == null)
                {
                    _dbContext.DownloadJobs.Add(job);
                }
                else
                {
                    _dbContext.Entry(existingJob).CurrentValues.SetValues(job);
                }

                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"---[DB ERROR] Failed to save or update job {job.Id}: {ex.Message}");
            }
        }


        public async Task DeleteJobAsync(string jobId)
        {
            try
            {

                using var _dbContext = await _factoryDB.CreateDbContextAsync();

                var job = await _dbContext.DownloadJobs.FindAsync(jobId);
                if (job != null)
                {
                    _dbContext.DownloadJobs.Remove(job);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogDebug($"---[DB] Job {jobId} deleted successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"---[DB ERROR] Failed to delete job {jobId}: {ex.Message}");
            }
        }

        public async Task GetAllJobsAsync_From_DB()
        {
            try
            {

                using var _dbContext = await _factoryDB.CreateDbContextAsync();

                var All_Jobs = await _dbContext.DownloadJobs.ToListAsync();
                _logger.LogInformation("---[DB] Retrieved " + All_Jobs.Count + " jobs from database.");
                foreach (var job in All_Jobs)
                {
                    _tracker.Jobs[job.Id] = job;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"---[DB ERROR] Failed to retrieve jobs: {ex.Message}");
            }
        }
    }
}
