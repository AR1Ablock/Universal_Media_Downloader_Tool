namespace Downloader_Backend.Logic
{
    using Microsoft.Extensions.Hosting;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using Downloader_Backend.Model;

    public class FileCleanupService(ILogger<FileCleanupService> logger, ProcessControl processControl, Utility utility, IServiceProvider service, DownloadTracker downloadTracker) : BackgroundService
    {

        public bool CleanUp_Service_Toggle { get; set; } = false;


        // Services
        private readonly ProcessControl _processControl = processControl;
        private readonly Utility _utility = utility;
        private readonly DownloadTracker _downloadTracker = downloadTracker;
        private readonly IServiceProvider _service = service;
        private readonly ILogger<FileCleanupService> _logger = logger;


        // Scan interval
        private readonly TimeSpan _scanInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan Completed_File_Keep_Duration = TimeSpan.FromHours(3);
        private readonly TimeSpan Stuck_File_Keep_Duration = TimeSpan.FromHours(6);


        // Log cleanup settings. Max days to keep logs - Max folder size
        private readonly TimeSpan _logMaxAge = TimeSpan.FromDays(3);
        private const long MaxLogFolderSizeBytes = 50L * 1024 * 1024; // 50 MB
        private readonly string LogDirectory = Utility.Create_Log_Path();


        // Reused collections to minimize GC pressure during high load
        private readonly List<DownloadJob> _toRemoveBuffer = new(64);
        private readonly List<Task> _cleanupTasks = new(32);
        private readonly ConcurrentBag<DownloadJob> _stuckJobsToKillEarly = [];

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FileCleanupService started | Download cleanup: {Status} | Log cleanup: ALWAYS",
                CleanUp_Service_Toggle ? "ENABLED" : "DISABLED");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Always clean logs
                    CleanupLogs();

                    // Download cleanup only if toggle is ON
                    if (CleanUp_Service_Toggle)
                    {
                        await PerformDownloadCleanupAsync(stoppingToken);
                    }
                    else if (!_downloadTracker.Jobs.IsEmpty)
                    {
                        _logger.LogInformation("--- Download cleanup is OFF — {Count} active jobs waiting", _downloadTracker.Jobs.Count);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "--- Unhandled exception in FileCleanupService main loop — service continues");
                }

                await Task.Delay(_scanInterval, stoppingToken);
            }

            _logger.LogInformation("--- FileCleanupService stopped gracefully.");
        }

        private async Task PerformDownloadCleanupAsync(CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;
            _toRemoveBuffer.Clear();
            _stuckJobsToKillEarly.Clear();

            // Phase 1: Fast scan — collect jobs to remove + early kill stuck ones
            _logger.LogInformation("--- Starting download cleanup scan over {Count} jobs", _downloadTracker.Jobs.Count);

            foreach (var kv in _downloadTracker.Jobs)
            {
                var job = kv.Value;
                var age = now - job.CreatedAt;
                var sinceProgress = now - job.LastProgressAt;

                if (job.Status == "completed" && age > Completed_File_Keep_Duration)
                {
                    _toRemoveBuffer.Add(job);
                }
                else if (job.Status != "completed" && sinceProgress > Stuck_File_Keep_Duration)
                {
                    _toRemoveBuffer.Add(job);
                    _stuckJobsToKillEarly.Add(job); // Kill early for faster termination
                }
            }

            if (_toRemoveBuffer.Count == 0)
                return;

            _logger.LogInformation("--- Starting cleanup of {Count} jobs (parallel, max 8 concurrent)", _toRemoveBuffer.Count);

            // Phase 2: Kill stuck processes early (fast, fire-and-forget safe)
            Parallel.ForEach(_stuckJobsToKillEarly, job =>
            {
                try
                {
                    _processControl.KillProcessTree(job.ProcessTreePids);
                }
                catch { /* Swallow exceptions here to not block cleanup */ }
            });

            // Phase 3: Parallel cleanup of DB + files + tracker removal
            _cleanupTasks.Clear();
            _logger.LogInformation("--- Cleaning up jobs...");

            const int MaxParallelism = 8; // Optimal for most servers (I/O bound)
            var semaphore = new SemaphoreSlim(MaxParallelism);

            foreach (var job in _toRemoveBuffer)
            {
                var task = Task.Run(async () =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        // Create isolated scope per job — thread-safe
                        using var scope = _service.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<IDownloadPersistence>();

                        // Remove from in-memory tracker (thread-safe)
                        _downloadTracker.Jobs.TryRemove(job.Id, out _);

                        // Log PIDs once
                        _utility.Log_pids_tree(job);

                        // Delete from DB
                        try
                        {
                            await db.DeleteJobAsync(job.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "--- [DB] Failed to delete job {JobId}", job.Id);
                        }

                        // Delete files — this is the slowest part, fully parallelized inside Utility
                        try
                        {
                            _utility.DeleteAllDownloadArtifacts(job.OutputPath);
                            _logger.LogInformation("--- Cleaned artifacts :: {Title} ({JobId})", job.Title, job.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "--- Failed to delete files for job {JobId}", job.Id);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct);

                _cleanupTasks.Add(task);
            }

            // Wait for all jobs to finish
            try
            {
                await Task.WhenAll(_cleanupTasks);
                _logger.LogInformation("--- Cleanup completed :: {Count} jobs removed", _toRemoveBuffer.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "--- One or more cleanup tasks failed — others may have succeeded");
            }
        }

        private void CleanupLogs()
        {
            if (!Directory.Exists(LogDirectory))
            {
                _logger.LogInformation("--- Log directory -> {LogDirectory} does not exist, skipping log cleanup.", LogDirectory);
                return;
            }

            try
            {
                var files = Directory.GetFiles(LogDirectory, "log-*.txt")
                                     .Select(f => new FileInfo(f))
                                     .OrderBy(f => f.CreationTimeUtc)
                                     .ToList();

                _logger.LogInformation("--- Log cleanup started. Found {Count} log files.", files.Count);

                var now = DateTime.UtcNow;

                // Delete old logs by age
                foreach (var file in files)
                {
                    if (now - file.CreationTimeUtc > _logMaxAge)
                    {
                        SafeDelete(file);
                    }
                }

                // Delete oldest if folder too big
                long totalSize = files.Sum(f => f.Length);
                if (totalSize > MaxLogFolderSizeBytes)
                {
                    foreach (var file in files)
                    {
                        if (totalSize <= MaxLogFolderSizeBytes)
                            break;

                        if (SafeDelete(file))
                            totalSize -= file.Length;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "--- Failed during log cleanup");
            }
        }

        private bool SafeDelete(FileInfo file)
        {
            try
            {
                file.Delete();
                _logger.LogDebug("--- Deleted old log: {FileName}", file.FullName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "--- Failed to delete log file: {FileName}", file.FullName);
                return false;
            }
        }
    }
}
