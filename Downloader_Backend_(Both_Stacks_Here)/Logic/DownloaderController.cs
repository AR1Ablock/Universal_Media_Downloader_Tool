// DownloaderController.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Downloader_Backend.Model;
using Microsoft.AspNetCore.Mvc;

namespace Downloader_Backend.Logic
{

    [ApiController]
    [Route("[controller]")]
    public partial class DownloaderController(DownloadTracker tracker, ILogger<DownloaderController> logger, IDownloadPersistence download_history, ProcessControl processControl, Utility utility, GlobalCancellationService globalCancellation) : ControllerBase
    {
        private readonly DownloadTracker _tracker = tracker;
        private readonly ILogger<DownloaderController> _logger = logger;
        private readonly GlobalCancellationService _globalCancellation = globalCancellation;
        private readonly Utility _utility = utility;
        private readonly ProcessControl _processControl = processControl;
        private readonly IDownloadPersistence _download_history = download_history;
        private readonly bool Download_Enable = false; // used for prodction build if app is hosted on a server.


        [HttpPost("formats")]
        public async Task<IActionResult> GetFormats([FromBody] FormatRequest req)
        {
            var Token_Key = _globalCancellation.GenerateKey();
            var Token_Source = _globalCancellation.CreateTokenSource(Token_Key);

            try
            {
                var url = _utility.SanitizeUrl(req.Url);

                if (string.IsNullOrWhiteSpace(url))
                {
                    return BadRequest("Invalid URL provided.");
                }

                string[] basicArgs = ["-j", url];

                var (success, output, error, loginRequired) = await _utility.TryRunYtDlpAsync(basicArgs, Token_Source.Token);

                if (!success)
                {
                    if (loginRequired)
                    {
                        return Ok(new { Url = new Uri(url).GetLeftPart(UriPartial.Authority), loginRequired = true, message = "Login required to fetch formats." });
                    }

                    return BadRequest($"yt-dlp failed: {error}");
                }

                using var doc = JsonDocument.Parse(output);
                var root = doc.RootElement;

                string extractor = root.GetProperty("extractor_key").GetString()?.ToLower() ?? "";

                List<Format> formats = extractor.Contains("facebook")
                    ? _utility.ParseFacebookFormats(root)
                    : _utility.ParseStandardFormats(root);

                return Ok(formats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error occured in get format method");
                return BadRequest(ex.Message);
            }
            finally
            {
                _globalCancellation.RemoveTokenSource(Token_Key);
            }

        }


        [HttpPost("download")]
        public async Task<IActionResult> NormalDownload([FromBody] DownloadRequest req)    // ← accept the request token
        {
            var Token_Key = _globalCancellation.GenerateKey();
            var Token_Source = _globalCancellation.CreateTokenSource(Token_Key);

            try
            {
                return await Task.Run(async () =>
                {
                    var url = _utility.SanitizeUrl(req.Url);
                    if (string.IsNullOrWhiteSpace(url))
                        return BadRequest("Invalid URL provided.");

                    // now pass the token into GetTitle
                    string rawTitle;
                    try
                    {
                        rawTitle = await _utility.GetTitle(url, Token_Source.Token)
                        ?? req.DownloadId;     // if null (due to cancel), fallback
                    }
                    catch (OperationCanceledException)
                    {
                        // client gave up before we even started downloading
                        return StatusCode(499, "Client cancelled before download could start.");
                    }

                    // sanitize for filesystem
                    var invalidChars = Path.GetInvalidFileNameChars();
                    var safeTitle = string.Join("_", rawTitle.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

                    // build and fire‐and‐forget
                    var job = new DownloadJob(req.DownloadId, url, req.Format)
                    {
                        Thumbnail = req.Thumbnail,
                        Title = safeTitle,
                        Method = "yt-dlp",
                        Status = "pending",
                        Key = req.Key,
                    };
                    // await _download_history.Save_And_UpdateJobAsync(job); // save initial job state
                    var result = await DownloadAsync(job, Token_Source.Token, false, false, false, false, Token_Key);
                    return result;
                }, Token_Source.Token);

            }
            catch (Exception ex)
            {
                _globalCancellation.RemoveTokenSource(Token_Key);
                _logger.LogInformation($"Error in NormalDownload: {ex.Message}");
                return StatusCode(500, "Internal server error: " + ex.Message);
            }

        }


        private async Task<IActionResult> DownloadAsync(DownloadJob job, CancellationToken linkedToken, bool resume = false, bool restart = false, bool tryCookies = false, bool tryImpersonate = false, string Token_Key = "")
        {
            /*             var downloadsFolder = Path.Combine("downloads");
                        Directory.CreateDirectory(downloadsFolder); */

            string videoPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            string downloadsFolder = Path.Combine(videoPath, "Dlp_downloads");

            Directory.CreateDirectory(downloadsFolder); // Ensures folder exists

            var outputFile = Path.Combine(downloadsFolder, $"{job.Id}___{job.Title}.mp4");

            var (ytDlpPath, ffmpegPath) = _utility.Local_Executables_Path();

            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Environment = { ["FFMPEG"] = ffmpegPath }, // set FFMPEG
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,           // ✅ Hides console window
                WindowStyle = ProcessWindowStyle.Hidden, // ✅ Ensures no console flash
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (tryCookies)
            {
                psi.ArgumentList.Add("--cookies-from-browser");
                psi.ArgumentList.Add("chrome");
            }

            if (tryImpersonate)
            {
                psi.ArgumentList.Add("--extractor-args");
                psi.ArgumentList.Add("generic:impersonate");
            }

            if (resume)
            {
                psi.ArgumentList.Add("--continue");
                job.Status = "resuming";
            }
            else if (restart)
            {
                ///psi.ArgumentList.Add("--no-continue");
                job.Status = "restarting";
                await Task.Delay(100); // give it a moment to settle
            }

            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(job.Format);
            psi.ArgumentList.Add("--merge-output-format");
            psi.ArgumentList.Add("mp4");
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outputFile);
            psi.ArgumentList.Add("--progress-template");
            psi.ArgumentList.Add("prog:%(progress._percent_str)s|%(progress._downloaded_bytes_str)s|%(progress._total_bytes_str)s|%(progress._speed_str)s");
            psi.ArgumentList.Add("--newline");
            psi.ArgumentList.Add(job.Url);

            try
            {
                var proc = Process.Start(psi);
                if (proc == null)
                {
                    job.Status = "failed";
                    job.ErrorLog = "Process failed to start.";
                    return StatusCode(500, "Failed to start download.");
                }

                job.Status = "downloading";
                job.Process = proc;
                job.ProcessTreePids = _processControl.GetProcessTree(proc.Id);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var stderr = proc.StandardError;
                        var stdout = proc.StandardOutput;

                        var stderrTask = Task.Run(async () =>
                        {
                            while (!stderr.EndOfStream)
                            {
                                var line = await stderr.ReadLineAsync(linkedToken);
                                if (!string.IsNullOrWhiteSpace(line))
                                    job.ErrorLog += "[stderr] " + line + "\n";
                                job.Status = "fail-err";
                            }
                        }, linkedToken);

                        var stdoutTask = Task.Run(async () =>
                        {
                            _globalCancellation.DetachTokenSource(Token_Key);
                            job.OutputPath = outputFile;
                            _tracker.Jobs[job.Id] = job;
                            await _download_history.Save_And_UpdateJobAsync(job);  // now in DB
                            string? line;
                            while ((line = await stdout.ReadLineAsync(linkedToken)) != null)
                            {
                                if (line.StartsWith("prog:"))
                                {
                                    var parts = line[5..].Split('|');
                                    if (parts.Length >= 4)
                                    {
                                        var pct = parts[0].TrimEnd('%', ' ');
                                        if (double.TryParse(pct, out var percent))
                                        {
                                            job.Progress = percent;
                                            job.LastProgressAt = DateTimeOffset.UtcNow;    // ← update here
                                        }

                                        job.Downloaded = (long)_utility.ParseHumanReadableSize(parts[1]);
                                        job.Total = (long)_utility.ParseHumanReadableSize(parts[2]);
                                        job.Speed = parts[3].Trim();
                                        job.Status = "downloading";
                                    }
                                }
                                else
                                {
                                    job.ErrorLog += "[stdout] " + line + "\n";
                                    job.Status = "Trying";
                                }
                            }
                        }, linkedToken);

                        await Task.WhenAll(stderrTask, stdoutTask);
                        await proc.WaitForExitAsync(linkedToken);
                        job.Status = proc.ExitCode == 0 ? "completed" : "failed";
                        await _download_history.Save_And_UpdateJobAsync(job); // save initial job state

                        if (job.Status == "failed")
                        {
                            bool hasCookieError = _utility.CookieTriggers.Any(t => job.ErrorLog.Contains(t, StringComparison.OrdinalIgnoreCase));
                            bool hasImpersonateError = _utility.ImpersonateTriggers.Any(t => job.ErrorLog.Contains(t, StringComparison.OrdinalIgnoreCase));

                            if (hasCookieError && !tryCookies)
                            {
                                _logger.LogInformation("Downalod retrying with cookies");
                                _processControl.KillProcessTree(job.ProcessTreePids);
                                await DownloadAsync(job, linkedToken, resume, restart, tryCookies: true, false, Token_Key); // Retry with cookies
                            }
                            else if (hasImpersonateError && !tryImpersonate)
                            {
                                _logger.LogInformation("Downalod retrying with impersonate");
                                _processControl.KillProcessTree(job.ProcessTreePids);
                                await DownloadAsync(job, linkedToken, resume, restart, false, tryImpersonate: true, Token_Key); // Retry with cookies
                            }
                            else
                            {
                                _logger.LogInformation("Downalod retrying with impersonate and cookies");
                                _processControl.KillProcessTree(job.ProcessTreePids);
                                await DownloadAsync(job, linkedToken, resume, restart, tryCookies: true, tryImpersonate: true, Token_Key); // Retry with cookies
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _processControl.KillProcessTree(job.ProcessTreePids);
                        _utility.Log_pids_tree(job);
                        job.Status = "failed-ct";
                        job.ErrorLog += $"Exception: {ex.Message}\n";
                    }
                    finally
                    {
                        if (proc?.Id > 0)
                        {
                            var tree = _processControl.GetProcessTree(proc.Id);
                            _processControl.KillProcessTree(tree);
                            _utility.Log_pids_tree(job);
                            _globalCancellation.RemoveTokenSource(Token_Key);
                        }
                    }
                }, linkedToken);

                return Ok(new { jobId = job.Id, title = job.Title });
            }
            catch (Exception ex)
            {
                _processControl.KillProcessTree(job.ProcessTreePids);
                _utility.Log_pids_tree(job);
                job.Status = "failed-ct";
                job.ErrorLog = $"Start Error: {ex.Message}";
                _globalCancellation.RemoveTokenSource(Token_Key);
                return StatusCode(500, "Process start failed: " + ex.Message);
            }
        }



        [HttpGet("download-file/{jobId}")]
        public IActionResult GetDownloadableFile(string jobId)
        {
            if (_tracker.Jobs.TryGetValue(jobId, out var job))
            {
                if (job.Status != "completed")
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Download is not completed yet"
                    });
                }

                if (System.IO.File.Exists(job.OutputPath))
                {
                    string fileName = Path.GetFileName(job.OutputPath);
                    string contentType = "video/mp4";

                    // Return the file as downloadable content
                    return PhysicalFile(job.OutputPath, contentType, fileName);
                }

                return NotFound(new
                {
                    success = false,
                    message = "File not found on server"
                });
            }

            return NotFound(new
            {
                success = false,
                message = "Job not found"
            });
        }




        [HttpGet("progress")]
        public IActionResult Progress(string Key)
        {
            if (string.IsNullOrWhiteSpace(Key))
                return BadRequest("Key is Missing. Login required.");

            // Get all jobs for this user's key
            var userJobs = _tracker.Jobs.Values
                .Where(x => x.Key == Key)
                .ToList();

            if (userJobs.Count == 0)
            {
                _logger.LogInformation($"No jobs found with key: {Key}");
                return Ok(new List<DownloadJob>()); // Return empty array instead of 404
            }

            return Ok(userJobs); // Return array of jobs
        }



        [HttpPost("Pause_All_Tasks")]
        public async Task<IActionResult> Pause_All_Jobs()
        {
            try
            {

                var jobs = _tracker.Jobs.ToList();

                _globalCancellation.CancelAndDisposeAll();

                if (jobs.Count == 0)
                {
                    return Ok("No jobs to pause.");
                }

                var pausedJobs = new List<string>();

                foreach (var job in jobs)
                {

                    var res = await Pause(new JobActionRequest(job.Value.Id));
                    if (res is OkResult)
                    {
                        pausedJobs.Add(job.Value.Key);
                    }
                }

                if (pausedJobs.Count == 0)
                {
                    return Ok("No running jobs were paused.");
                }

                return Ok(new { message = "Paused jobs", jobs = pausedJobs });

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error in pause all jobs method");
                return BadRequest(ex.Message);
            }
        }



        [HttpPost("pause")]
        public async Task<IActionResult> Pause([FromBody] JobActionRequest req)
        {
            if (_tracker.Jobs.TryGetValue(req.JobId, out var job) && job.Process != null && !job.Process.HasExited)
            {
                job.ProcessTreePids = _processControl.GetProcessTree(job.Process.Id);
                _processControl.Suspend(job);
                job.Status = "paused";
                await _download_history.Save_And_UpdateJobAsync(job); // save paused state
                return Ok();
            }
            return NotFound();
        }



        [HttpPost("resume")]
        public async Task<IActionResult> Resume([FromBody] JobActionRequest req)
        {
            if (_tracker.Jobs.TryGetValue(req.JobId, out var job))
            {
                if (job.Process != null && !job.Process.HasExited)
                {
                    job.ProcessTreePids = _processControl.GetProcessTree(job.Process.Id);
                    _processControl.Resume(job);
                    job.Status = "downloading";
                    await _download_history.Save_And_UpdateJobAsync(job); // save resumed state
                    return Ok();
                }
                else
                {
                    await ResumeNewUrl(new ResumeNewUrlRequest { JobId = job.Id, NewUrl = job.Url });
                }
            }
            return NotFound();
        }



        [HttpPost("resume-fresh")]
        public async Task<IActionResult> ResumeFresh([FromBody] JobActionRequest req)
        {
            if (!_tracker.Jobs.TryGetValue(req.JobId, out var job))
                return NotFound();
            await Pause(req);
            // 1) If there’s a live Process, kill it and wait for exit
            if (job.Process != null && !job.Process.HasExited)
            {
                _processControl.KillProcessTree(job.ProcessTreePids);
                _utility.Log_pids_tree(job);
                job.Process.WaitForExit();
            }

            // 2) Clean up all partial files
            _utility.DeleteAllDownloadArtifacts(job.OutputPath);

            // 3) Reset state
            job.ErrorLog = "";
            job.Status = "restarting";

            var Token_Key = _globalCancellation.GenerateKey();
            var Token_Source = _globalCancellation.CreateTokenSource(Token_Key);

            await Task.Delay(100, Token_Source.Token);

            // 4) Kick off a fresh download
            //    restart=true will *not* pass --continue, so yt-dlp starts from scratch
            return await DownloadAsync(job, Token_Source.Token, resume: false, restart: true, false, false, Token_Key);
        }





        [HttpPost("resume-new-url")]
        public async Task<IActionResult> ResumeNewUrl([FromBody] ResumeNewUrlRequest req)
        {
            if (_tracker.Jobs.TryGetValue(req.JobId, out var oldJob))
            {
                await Pause(new JobActionRequest(req.JobId));
                string NewUrl = _utility.SanitizeUrl(req.NewUrl);
                oldJob.Url = NewUrl;
                _processControl.KillProcessTree(oldJob.ProcessTreePids);
                _utility.Log_pids_tree(oldJob);
                // Reconstruct new job with updated URL
                oldJob.Status = "resuming";

                var Token_Key = _globalCancellation.GenerateKey();
                var Token_Source = _globalCancellation.CreateTokenSource(Token_Key);

                await Task.Delay(100, Token_Source.Token);
                return await DownloadAsync(oldJob, Token_Source.Token, resume: true, false, false, false, Token_Key);
            }

            return NotFound("Item not found.");
        }




        [HttpPost("delete-ui")]
        public async Task<IActionResult> DeleteUIOnlyAsync([FromBody] JobActionRequest req)
        {
            await Pause(req);
            await Task.Delay(250);
            if (_tracker.Jobs.TryRemove(req.JobId, out var job))
            {
                _processControl.KillProcessTree(job.ProcessTreePids);
                _utility.Log_pids_tree(job);
                await _download_history.DeleteJobAsync(req.JobId); // remove from history
                return Ok();
            }
            return NotFound();
        }



        [HttpGet("OS_Check")]
        public IActionResult OS_Check()
        {
            string os = OperatingSystem.IsWindows() ? "Windows" :
                        OperatingSystem.IsLinux() ? "Linux" :
                        OperatingSystem.IsMacOS() ? "Mac" :
                        OperatingSystem.IsAndroid() ? "Android" :
                        OperatingSystem.IsIOS() ? "Apple" :
                        "Unknown";

            return Ok(new { OS = os, Is_Download_Enable = Download_Enable });
        }



        [HttpPost("delete-file")]
        public async Task<IActionResult> DeleteFile([FromBody] JobActionRequest req)
        {
            await Pause(req);
            if (_tracker.Jobs.TryGetValue(req.JobId, out var job))
            {
                try
                {
                    if (job.Process != null && !job.Process.HasExited)
                    {
                        _processControl.KillProcessTree(job.ProcessTreePids);
                        _utility.Log_pids_tree(job);
                        job.Process!.WaitForExit(); // ensure process is done                    
                    }
                    _processControl.KillProcessTree(job.ProcessTreePids);
                    _utility.Log_pids_tree(job);
                    _utility.DeleteAllDownloadArtifacts(job.OutputPath);
                    _tracker.Jobs.TryRemove(req.JobId, out _); // remove from tracker
                    await _download_history.DeleteJobAsync(req.JobId); // remove from history
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("----Error deleting download artifacts. " + ex.Message);
                }

                return Ok();
            }

            return NotFound();
        }



        [HttpPost("open-file")]
        public IActionResult Open_Selected_File_Path(JobActionRequest req)
        {
            if (_tracker.Jobs.TryGetValue(req.JobId, out var oldJob))
            {
                var filePath = oldJob?.OutputPath;
                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning("File not found for job {JobId}", req.JobId);
                    return NotFound(new { message = "File not found" });
                }

                _logger.LogInformation("Opening video with id: {VideoId} at {FilePath}", req.JobId, filePath);

                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // Explorer with file selected
                        _utility.Run_Open_Media_Directory_Process("explorer.exe", $"/select,\"{filePath}\"");
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        // Finder with file revealed
                        _utility.Run_Open_Media_Directory_Process("open", "-R \"" + filePath + "\"");
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        // Linux: open folder (cannot highlight file reliably)
                        var dir = Path.GetDirectoryName(filePath);
                        _utility.Run_Open_Media_Directory_Process("xdg-open", dir!);
                    }

                    return Ok(new { message = "Folder opened" });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to open folder for job {JobId}", req.JobId);
                    return StatusCode(500, new { message = "Media not found, Failed to open folder" });
                }
            }

            return NotFound(new { message = "Media not found" });
        }
    }
}
