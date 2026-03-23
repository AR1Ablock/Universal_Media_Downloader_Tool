
using System.Runtime.InteropServices;
using System.Text.Json;
using Downloader_Backend.Model;
using Microsoft.AspNetCore.Mvc;
using My_Files = System.IO.File;

namespace Downloader_Backend.Logic
{

    [ApiController]
    [Route("[controller]")]
    public partial class DownloaderController(DownloadTracker tracker, ILogger<DownloaderController> logger, IDownloadPersistence download_history, ProcessControl processControl, Utility utility, GlobalCancellationService globalCancellation, File_Saver file_Saver, YT_Dlp_Strategy_Engine yt_Dlp_Strategy_Engine) : ControllerBase
    {
        private readonly DownloadTracker _tracker = tracker;
        private readonly ILogger<DownloaderController> _logger = logger;
        private readonly GlobalCancellationService _globalCancellation = globalCancellation;
        private readonly Utility _utility = utility;
        private readonly YT_Dlp_Strategy_Engine _yt_Dlp_Strategy_Engine = yt_Dlp_Strategy_Engine;
        private readonly ProcessControl _processControl = processControl;
        private readonly IDownloadPersistence _download_history = download_history;
        private readonly File_Saver _fileSaver = file_Saver;
        private readonly bool Download_Enable = false; // used for prodction build if app is hosted on a server.


        [HttpPost("formats")]
        public async Task<IActionResult> GetFormats([FromBody] FormatRequest req)
        {
            var Token_Key = _globalCancellation.GenerateKey();
            var Token_Source = _globalCancellation.CreateTokenSource(Token_Key);
            var token = Token_Source.Token;

            try
            {
                var url = _utility.SanitizeUrl(req.Url);

                if (string.IsNullOrWhiteSpace(url))
                {
                    return BadRequest("Invalid URL provided.");
                }

                var (success, output, error, loginRequired) = await _yt_Dlp_Strategy_Engine.Get_Formats_Helper(url, token);

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
                    ? await _utility.ParseFacebookFormats(root, req.Url, _yt_Dlp_Strategy_Engine, token)
                    : await _utility.ParseStandardFormats(root, req.Url, _yt_Dlp_Strategy_Engine, token);

                return Ok(formats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error occured in get format method");
                return BadRequest(ex.Message);
                throw;
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
                    string rawTitle = "";
                    try
                    {
                        if (_fileSaver.fileMap.TryGetValue("Title_File", out string? Title_File))
                        {
                            if (!string.IsNullOrWhiteSpace(Title_File))
                            {
                                if (My_Files.Exists(Title_File))
                                {
                                    string res = My_Files.ReadAllText(Title_File);
                                    if (!string.IsNullOrWhiteSpace(res))
                                    {
                                        rawTitle = res;
                                    }
                                }
                            }
                        }

                        if (string.IsNullOrWhiteSpace(rawTitle))
                            rawTitle = await _yt_Dlp_Strategy_Engine.GetTitle(url, Token_Source.Token) ?? req.DownloadId;
                        {
                            Title_File = Path.Combine(Utility.Create_Path(Making_Logs_Path: true), "Title_File.txt");
                            My_Files.WriteAllText(Title_File, rawTitle);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // client gave up before we even started downloading
                        return StatusCode(499, "Client cancelled before download could start.");
                    }
                    catch (Exception ex)
                    {
                        return BadRequest(ex.Message);
                    }

                    // sanitize for filesystem
                    var invalidChars = Path.GetInvalidFileNameChars();
                    var safeTitle = string.Join("_", rawTitle.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

                    // build and fire‐and‐forget
                    var job = new DownloadJob
                    {
                        Id = req.DownloadId,
                        Url = req.Url,
                        Format = req.Format,
                        Thumbnail = req.Thumbnail,
                        Title = safeTitle,
                        Method = "yt-dlp",
                        Status = "pending",
                        Key = req.Key,
                        TokenSource = Token_Source,
                    };
                    // await _download_history.Save_And_UpdateJobAsync(job); // save initial job state
                    var result = await _yt_Dlp_Strategy_Engine.DownloadAsync(job, Token_Source.Token, _globalCancellation, _download_history, _tracker, false, false, Token_Key);
                    return result;
                }, Token_Source.Token);

            }
            catch (Exception ex)
            {
                _globalCancellation.RemoveTokenSource(Token_Key);
                _logger.LogInformation($"Error in NormalDownload: {ex.Message}");
                return StatusCode(500, "Internal server error: " + ex.Message);
                throw;
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

                if (My_Files.Exists(job.OutputPath))
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
            if (_tracker.Jobs.TryGetValue(req.JobId, out var job) && _processControl.TryGetPid(job.Process, out int pid) && _processControl.ProcessExists(pid))
            {
                job.ProcessTreePids = _processControl.GetProcessTree(pid);
                _processControl.Suspend(job);
                job.Status = "paused";
                await _download_history.Save_And_UpdateJobAsync(job); // save paused state
                return Ok();
            }
            return NotFound("Process has already dead in pause method");
        }



        [HttpPost("resume")]
        public async Task<IActionResult> Resume([FromBody] JobActionRequest req)
        {
            if (_tracker.Jobs.TryGetValue(req.JobId, out var job))
            {
                if (_processControl.TryGetPid(job.Process, out int pid) && _processControl.ProcessExists(pid))
                {
                    job.ProcessTreePids = _processControl.GetProcessTree(pid);
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

            var (Cts_Key, Cts) = _globalCancellation.Get_Token_With_SC();
            var New_Job = _utility.Create_Download_Job(job, status: "restarting", Method_Caller: "Restart", Cts);

            await DeleteFile(req, Preserve_File_DB_Rec: true, Preserve_File_UI_Rec: true);

            _tracker.Jobs[job.Id] = New_Job;

            // 4) Kick off a fresh download
            return await _yt_Dlp_Strategy_Engine.DownloadAsync(New_Job, Cts.Token, _globalCancellation, _download_history, _tracker, resume: false, restart: true, Cts_Key);
        }





        [HttpPost("resume-new-url")]
        public async Task<IActionResult> ResumeNewUrl([FromBody] ResumeNewUrlRequest req)
        {
            if (_tracker.Jobs.TryGetValue(req.JobId, out var oldJob))
            {
                await Pause(new JobActionRequest(req.JobId));
                string NewUrl = _utility.SanitizeUrl(req.NewUrl);
                oldJob.Url = NewUrl;

                var (Cts_Key, Cts) = _globalCancellation.Get_Token_With_SC();
                var New_Job = _utility.Create_Download_Job(oldJob, status: "resuming", Method_Caller: "Broken_Resume", Cts);

                await DeleteFile(new JobActionRequest(req.JobId), Preserve_File:true, Preserve_File_DB_Rec: true, Preserve_File_UI_Rec: true);

                _tracker.Jobs[oldJob.Id] = New_Job;

                await Task.Delay(100, Cts.Token);

                return await _yt_Dlp_Strategy_Engine.DownloadAsync(oldJob, Cts.Token, _globalCancellation, _download_history, _tracker, resume: true, restart: false, Cts_Key);
            }

            return NotFound("Item not found.");
        }




        [HttpPost("delete-ui")]
        public async Task<IActionResult> DeleteUIOnlyAsync([FromBody] JobActionRequest req)
        {
            await Pause(req);
            
            if (_tracker.Jobs.TryRemove(req.JobId, out var _))
            {
                await DeleteFile(new JobActionRequest(req.JobId), Preserve_File:true);
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
        public async Task<IActionResult> DeleteFile([FromBody] JobActionRequest req, bool Preserve_File = false, bool Preserve_File_DB_Rec = false, bool Preserve_File_UI_Rec = false)
        {
            await Pause(req);
            if (_tracker.Jobs.TryGetValue(req.JobId, out var job))
            {
                try
                {
                    if (job?.TokenSource != null)
                    {
                        if (!job.TokenSource.IsCancellationRequested)
                            job.TokenSource?.Cancel();   // signal cancellation
                        //
                        job.TokenSource?.Dispose();  // free resources
                        job.TokenSource = null;     // clear reference
                    }

                    if (_processControl.TryGetPid(job?.Process, out int pid))
                    {
                        _processControl.KillProcessTree(job?.ProcessTreePids!);
                    }

                    if (job?.DownloadTask != null)
                        await Task.WhenAny(job.DownloadTask, Task.Delay(1500));

                    if (!Preserve_File)
                        _utility.DeleteAllDownloadArtifacts(job?.OutputPath!);

                    if (!Preserve_File_UI_Rec)
                        _tracker.Jobs.TryRemove(req.JobId, out _); // remove from tracker

                    if (!Preserve_File_DB_Rec)
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