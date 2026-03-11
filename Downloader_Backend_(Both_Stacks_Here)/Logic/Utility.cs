using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Downloader_Backend.Model;
using Microsoft.AspNetCore.Mvc;
using Spectre.Console;

namespace Downloader_Backend.Logic
{
    public partial class Utility(ILogger<Utility> logger, ProcessControl processControl)
    {

        private readonly ProcessControl _processControl = processControl;
        private readonly ILogger<Utility> _logger = logger;

        public static string Create_Path(bool Making_Logs_Path = true)
        {
            string Dir_Path;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Dir_Path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MediaDownloader",
                    Making_Logs_Path ? "Logs" : "Database");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (Making_Logs_Path)
                {
                    Dir_Path = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library", "Logs", "MediaDownloader");
                }
                else
                {
                    Dir_Path = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library", "Application Support", "MediaDownloader");
                }
            }
            else // Linux/Unix
            {
                Dir_Path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share", "MediaDownloader",
                    Making_Logs_Path ? "Logs" : "Database");
            }

            if (!Directory.Exists(Dir_Path))
            {
                Directory.CreateDirectory(Dir_Path);
            }

            if (!Making_Logs_Path)
            {
                Dir_Path = Path.Combine(Dir_Path, "Downloads.db");
            }

            return Dir_Path;
        }

        public void Log_pids_tree(DownloadJob job)
        {
            foreach (var item in job.ProcessTreePids)
            {
                _logger.LogInformation("----Killing Trees " + item);
            }
        }


        public void DeleteAllDownloadArtifacts(string outputPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(outputPath) ?? ".";
                var baseName = Path.GetFileName(outputPath);
                var filePrefix = baseName.Split('.')[0];

                // Define regex patterns (note the comma between pattern and options)
                var regexPatterns = new[]
                {
            new Regex($@"^{Regex.Escape(filePrefix)}.*\.part.*$", RegexOptions.IgnoreCase), // Note: comma here!
            new Regex($@"^{Regex.Escape(filePrefix)}.*\.ytdl$", RegexOptions.IgnoreCase),
            new Regex($@"^{Regex.Escape(filePrefix)}.*\.temp$", RegexOptions.IgnoreCase),
            new Regex($@"^{Regex.Escape(filePrefix)}.*\.m4a$", RegexOptions.IgnoreCase),
            new Regex($@"^{Regex.Escape(filePrefix)}.*\.f.*\..*\.part$", RegexOptions.IgnoreCase)
        };

                // Single enumeration
                var allFiles = Directory.EnumerateFiles(dir).ToList();

                // Parallel filter and delete
                Parallel.ForEach(allFiles, file =>
                {
                    var fileName = Path.GetFileName(file);
                    if (regexPatterns.Any(r => r.IsMatch(fileName)))
                    {
                        try
                        {
                            File.Delete(file);
                            _logger.LogInformation($"🗑 Deleted: {file}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInformation($"⚠️ Failed to delete {file}: {ex.Message}");
                        }
                    }
                });

                // Delete main file
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                    _logger.LogInformation($"🗑 Deleted main file: {outputPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"❌ Error cleaning download files: {ex.Message}");
            }
        }


        public (string, string) Local_Executables_Path()
        {
            // we add tools folder in csproj and during runtime it will be copied to basedirectroy 
            var baseDir = AppContext.BaseDirectory;

            // Build the path to your local tools folder
            // Build the path to your local tools folder
            var yt_dlp_executable = OperatingSystem.IsWindows() ? "yt-dlp.exe" : OperatingSystem.IsMacOS() ? "yt-dlp_mac" : "yt-dlp";
            var ffmpeg_executable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : OperatingSystem.IsMacOS() ? "ffmpeg_mac" : "ffmpeg";

            var ytDlpPath = Path.Combine(baseDir, "tools", yt_dlp_executable);
            var ffmpegPath = Path.Combine(baseDir, "tools", ffmpeg_executable);

            // Check if the files exist
            if (!File.Exists(ytDlpPath))
            {
                throw new FileNotFoundException($"yt-dlp executable not found at {ytDlpPath}");
            }
            if (!File.Exists(ffmpegPath))
            {
                throw new FileNotFoundException($"ffmpeg executable not found at {ffmpegPath}");
            }
            return (ytDlpPath, ffmpegPath);
        }


        public void Checking_And_Starting_Linux_Service()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            var state = Run_Open_Media_Directory_Process("systemctl", "--user is-active mediadownloader").Trim().ToLower();

            _logger.LogInformation("Systemd state: {State}", state);

            if (state != "active" && state != "activating")
            {
                Run_Open_Media_Directory_Process("systemctl", "--user daemon-reload");
                Run_Open_Media_Directory_Process("systemctl", "--user enable --now mediadownloader");
                Run_Open_Media_Directory_Process("systemctl", "--user daemon-reload");

                _logger.LogInformation("Systemd user service enabled and started.");
            }
        }



        public string Run_Open_Media_Directory_Process(string fileName, string arguments)
        {
            Process? proc = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName, // for different os file managers.
                    Arguments = arguments, // arguments based on OS type.
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                proc = Process.Start(psi);

                proc?.WaitForExit(2000);

                string output = proc!.StandardOutput.ReadToEnd().Trim();
                string error = proc.StandardError.ReadToEnd().Trim();


                if (!string.IsNullOrEmpty(error))
                {
                    _logger?.LogError("Global Process Runner STD err variable: {Error}", error);
                }
                return output;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open media directory: {Error}", ex.Message);
                return $"Exception: {ex.Message}";
            }
            finally
            {
                proc?.Dispose(); // release resources
            }
        }


        public bool IsDesktopLaunch()
        {
            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                _logger.LogInformation("args---> :{arg}", arg);
            }

            // Desktop icon (highest priority)
            if (args.Any(a => a.Equals("--desktop", StringComparison.OrdinalIgnoreCase)))
                return true;

            // Service mode
            if (args.Any(a => a.Equals("--service", StringComparison.OrdinalIgnoreCase)) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOCATION_ID")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JOURNAL_STREAM")))
                return false;

            // Fallback for Windows/macOS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Environment.UserInteractive;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LAUNCH_JOB_LABEL"));

            return true; // safe fallback
        }


        public async Task WaitForBackendAsync(int port, PortKiller portKiller, int timeoutMs = 10000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    if (portKiller.Is_Our_Backend_Running(port))
                        return; // success
                }
                catch (Exception ex)
                {
                    // Optional: log the exception for visibility
                    _logger.LogWarning(ex, "Error while checking backend state in Application start by user, first time linux service start, waiting app fecth port, will retry...");
                }

                await Task.Delay(500);
            }

            throw new TimeoutException($"Backend did not start within {timeoutMs / 1000} sec");
        }


        public void OpenBrowser(string url)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Run_Open_Media_Directory_Process("cmd.exe", $"/c start {url}");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Run_Open_Media_Directory_Process("/bin/bash", $"-c \"open {url}\"");
                }
                else // Linux
                {
                    Run_Open_Media_Directory_Process("/bin/bash", $"-c \"xdg-open {url}\"");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open browser");
                AnsiConsole.Markup("[red]Failed to open browser automatically.[/]\n");
                AnsiConsole.Markup($"Please open your default browser and navigate to [blue]{url}[/].\n");
            }
        }


        public string SafeGetString(JsonElement e, string prop, string defaultValue = "unknown")
        {
            return e.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String
                ? val.GetString()!
                : defaultValue;
        }

        // Safely retrieve a file-size-like number as long bytes, even if it's a float
        public long SafeGetInt64(JsonElement e, string prop)
        {
            if (e.TryGetProperty(prop, out var val))
            {
                if (val.ValueKind == JsonValueKind.Number)
                {
                    try
                    {
                        return val.GetInt64();
                    }
                    catch
                    {
                        // if not an integer, fall back to double
                        return (long)val.GetDouble();
                    }
                }
                if (val.ValueKind == JsonValueKind.String && long.TryParse(val.GetString(), out var result))
                    return result;
            }
            return 0L;
        }


        public int SafeGetInt32(JsonElement e, string prop)
        {
            return e.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Number
                ? val.GetInt32()
                : 0;
        }

        public double? SafeGetDoubleNullable(JsonElement e, string prop)
        {
            return e.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Number
                ? val.GetDouble()
                : (double?)null;
        }


        public double ParseHumanReadableSize(string input)
        {
            input = input.Trim();

            var units = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        { "B", 1.0 / 1024 / 1024 },
        { "KiB", 1.0 / 1024 },
        { "MiB", 1 },
        { "GiB", 1024 },
        { "TiB", 1024 * 1024 }
    };

            foreach (var kvp in units)
            {
                if (input.EndsWith(kvp.Key))
                {
                    var numberPart = input[..^kvp.Key.Length].Trim();
                    if (double.TryParse(numberPart, out var value))
                        return value * kvp.Value;
                }
            }

            return 0;
        }


        public async Task<string?> GetTitle(string url, CancellationToken cancellationToken)
        {
            Process? proc = null;
            try
            {
                var (ytDlpPath, _) = Local_Executables_Path();

                var psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = $"--get-title {url}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,           // ✅ Hides console window
                    WindowStyle = ProcessWindowStyle.Hidden // ✅ Ensures no console flash
                };

                proc = Process.Start(psi);
                if (proc is null)
                {
                    _logger.LogInformation("Failed to start yt-dlp for title fetch.");
                    return null;
                }

                // Read stdout in parallel, observing cancellation
                var readTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);

                // Wait for yt-dlp to exit, or throw if cancelled
                await proc.WaitForExitAsync(cancellationToken);

                // If we get here, the process ended normally
                var output = await readTask;
                return output.Trim();
            }
            catch (OperationCanceledException)
            {
                // Client refreshed/navigated away → kill the yt-dlp process tree
                if (proc?.Id > 0)
                {
                    var tree = _processControl.GetProcessTree(proc.Id);
                    _processControl.KillProcessTree(tree);
                }
                _logger.LogInformation("GetTitle operation cancelled by client.");
                return null;
            }
            catch (Exception ex)
            {
                // Any other error
                if (proc?.Id > 0)
                {
                    var tree = _processControl.GetProcessTree(proc.Id);
                    _processControl.KillProcessTree(tree);
                }
                _logger.LogInformation($"Error getting title: {ex.Message}");
                return null;
            }
            finally
            {
                if (proc?.Id > 0)
                {
                    var tree = _processControl.GetProcessTree(proc.Id);
                    _processControl.KillProcessTree(tree);
                }
                proc?.Dispose();
            }
        }

        public List<Format> ParseStandardFormats(JsonElement root)
        {

            if (!root.TryGetProperty("formats", out var fmts) || fmts.ValueKind != JsonValueKind.Array)
                return [];

            string thumbnail = root.TryGetProperty("thumbnail", out var thmbnail) && thmbnail.ValueKind == JsonValueKind.String
            ? thmbnail.GetString()!
            : ""; // default thumbnail

            string Title = root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
            ? title.GetString()!
            : "Media Name Not Found!"; // default thumbnail

            var formats = fmts.EnumerateArray()
        .AsParallel()   // Enable parallel processing
        .AsOrdered()    // Preserve original order in output
        .Select(f =>
            {
                // skip encrypted signatures
                if (f.TryGetProperty("signatureCipher", out _))
                    return null;

                string id = SafeGetString(f, "format_id");
                string ext = SafeGetString(f, "ext");

                if (!string.IsNullOrWhiteSpace(ext) && ext.Contains("mhtml"))
                    return null; // skip MHTML formats

                string acodec = SafeGetString(f, "acodec", "none");
                string vcodec = SafeGetString(f, "vcodec", "none");

                long fs = SafeGetInt64(f, "filesize");
                int height = SafeGetInt32(f, "height");
                string note = SafeGetString(f, "format_note", "");

                if (!string.IsNullOrWhiteSpace(note) && note.Contains("storyboard"))
                    return null; // skip storyboard formats

                string resolution = SafeGetString(f, "resolution", "");

                string Protocol = SafeGetString(f, "protocol", "");
                string Slow_Or_Fast = "fast"; // default to fast
                if (!string.IsNullOrWhiteSpace(Protocol) && MyRegex_1().IsMatch(Protocol))
                    Slow_Or_Fast = "slow"; // HLS / m3u8 is considered slow

                bool isAudioOnly = vcodec == "none" && acodec != "none";
                bool isVideoOnly = acodec == "none" && vcodec != "none";

                // Convert to MB
                long sizeInMb = fs / 1024 / 1024;

                // If yt-dlp reported 0, show "--MB"
                string sizeLabel = sizeInMb == 0 ? "--MB" : $"{sizeInMb}MB";

                string label = $"{(resolution.Contains("audio only") ? "" : $"{height}p • ")}{sizeLabel} • {ext} • {Slow_Or_Fast} • {resolution}";
                //  | A:{acodec} | V:{vcodec}

                return new Format
                {
                    Id = id,
                    Title = Title,
                    Label = label,
                    Thumbnail = thumbnail,
                    IsAudioOnly = isAudioOnly,
                    IsVideoOnly = isVideoOnly
                };
            })
            .Where(f => f != null)  // Filter out skipped (null) items
            .ToList();
            return formats!;
        }

        public List<Format> ParseFacebookFormats(JsonElement root)
        {

            if (!root.TryGetProperty("formats", out var fmts) || fmts.ValueKind != JsonValueKind.Array)
                return [];

            // fallback get duration safely
            double duration = root.TryGetProperty("duration", out var dElem) && dElem.ValueKind == JsonValueKind.Number
                ? dElem.GetDouble()
                : 0;

            string thumbnail = root.TryGetProperty("thumbnail", out var thmbnail) && thmbnail.ValueKind == JsonValueKind.String
                ? thmbnail.GetString()!
                : "https://www.facebook.com/images/fb_icon_325x325.png"; // default thumbnail

            string Title = root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                ? title.GetString()!
                : "Media Name Not Found!"; // default thumbnail


            var formats = fmts.EnumerateArray()
        .AsParallel()   // Enable parallel processing
        .AsOrdered()    // Preserve original order in output
        .Select(f =>
            {
                string id = SafeGetString(f, "format_id");
                string ext = SafeGetString(f, "ext", "mp4");

                if (!string.IsNullOrWhiteSpace(ext) && ext.Contains("mhtml"))
                    return null; // skip MHTML formats

                string Protocol = SafeGetString(f, "protocol", "");
                string Slow_Or_Fast = "fast"; // default to fast
                if (!string.IsNullOrWhiteSpace(Protocol) && (Protocol.Contains("m3u8_native") || Protocol == "hls"))
                    Slow_Or_Fast = "slow"; // HLS / m3u8 is considered slow

                string acodec = SafeGetString(f, "acodec", "none");
                string vcodec = SafeGetString(f, "vcodec", "none");

                int? h = f.TryGetProperty("height", out var hElem) && hElem.ValueKind == JsonValueKind.Number
                                   ? hElem.GetInt32() : (int?)null;

                double? tbr = SafeGetDoubleNullable(f, "tbr");

                bool isAudioOnly = vcodec == "none" && acodec != "none";
                bool isVideoOnly = acodec == "none" && vcodec != "none";
                string resolution = h.HasValue ? $"{h.Value}p" : "audio only";

                string size = tbr.HasValue && duration > 0
                                       ? $"{Math.Round(tbr.Value * duration / 8 / 1024, 1)}MB"
                                       : "--MB";

                string label = $"{ext} • {resolution} • {Slow_Or_Fast} • {size}";
                //| A:{(acodec == "none" ? "none" : acodec)} | V:{(vcodec == "none" ? "none" : vcodec)}

                return new Format
                {
                    Id = id,
                    Title = Title,
                    Label = label,
                    Thumbnail = thumbnail,
                    IsAudioOnly = isAudioOnly,
                    IsVideoOnly = isVideoOnly
                };
            })
            .Where(f => f != null)  // Filter out skipped (null) items
            .ToList();

            return formats!;
        }



        public async Task<IActionResult> DownloadAsync(DownloadJob job, CancellationToken linkedToken, GlobalCancellationService _globalCancellation, IDownloadPersistence _download_history, DownloadTracker _tracker, bool resume = false, bool restart = false, bool tryCookies = false, bool tryImpersonate = false, string Token_Key = "")
        {
            string videoPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            string downloadsFolder = Path.Combine(videoPath, "Dlp_downloads");
            Directory.CreateDirectory(downloadsFolder);

            var outputFile = Path.Combine(downloadsFolder, $"{job.Id}___{job.Title}.mp4");
            var (ytDlpPath, ffmpegPath) = Local_Executables_Path();

            linkedToken.Register(() =>
            {
                job.Status = "canceled";
                job.ErrorLog += "[INFO] Job canceled by user.\n";
                _logger.LogInformation("User Cancel the job: {req}", linkedToken.IsCancellationRequested);
            });

            // Fire and forget - main method stays super clean
            job.DownloadTask = Task.Run(async () =>
            {
                await ExecuteDownloadWithRetriesAsync(
                    job,
                    linkedToken,
                    _globalCancellation,
                    _download_history,
                    _tracker,
                    resume,
                    restart,
                    tryCookies,
                    tryImpersonate,
                    Token_Key,
                    outputFile,
                    ytDlpPath,
                    ffmpegPath);
            }, linkedToken);

            return new OkObjectResult(new { jobId = job.Id, title = job.Title });
        }



        // ===================================================================
        // 1. Strategy decision (exactly what you asked)
        // ===================================================================
        private List<DownloadStrategy> GetDownloadStrategies(bool tryCookies, bool tryImpersonate)
        {
            var list = new List<DownloadStrategy>();

            if (tryCookies || tryImpersonate)
            {
                // Caller forced one combination → only run that
                list.Add(new DownloadStrategy(tryCookies, tryImpersonate,
                    tryCookies && tryImpersonate ? "both" : tryCookies ? "cookies" : "impersonate"));
            }
            else
            {
                // Default case (no flags passed) → order you wanted:
                // impersonate → cookies → both
                list.Add(new DownloadStrategy(false, true, "impersonate"));
                list.Add(new DownloadStrategy(true, false, "cookies"));
                list.Add(new DownloadStrategy(true, true, "both"));
            }

            return list;
        }



        // ===================================================================
        // 2. Main retry orchestrator (tries ALL strategies then = failed)
        // ===================================================================
        private async Task ExecuteDownloadWithRetriesAsync(
            DownloadJob job,
            CancellationToken linkedToken,
            GlobalCancellationService _globalCancellation,
            IDownloadPersistence _download_history,
            DownloadTracker _tracker,
            bool resume,
            bool restart,
            bool tryCookies,
            bool tryImpersonate,
            string Token_Key,
            string outputFile,
            string ytDlpPath,
            string ffmpegPath)
        {
            try
            {
                job.ErrorLog = ""; // fresh log
                var strategies = GetDownloadStrategies(tryCookies, tryImpersonate);
                bool overallSuccess = false;

                for (int i = 0; i < strategies.Count; i++)
                {
                    var strategy = strategies[i];

                    job.ErrorLog += $"\n\n=== ATTEMPT {i + 1}/{strategies.Count} → {strategy.Name.ToUpper()} ===\n";
                    job.Status = $"attempting ({strategy.Name})";
                    await _download_history.Save_And_UpdateJobAsync(job);

                    bool success = await ExecuteSingleAttemptAsync(
                        job,
                        linkedToken,
                        _globalCancellation,
                        _download_history,
                        _tracker,
                        strategy.UseCookies,
                        strategy.UseImpersonate,
                        resume && i == 0,     // resume/restart only on first attempt
                        restart && i == 0,
                        outputFile,
                        ytDlpPath,
                        ffmpegPath,
                        Token_Key);

                    if (success)
                    {
                        overallSuccess = true;
                        break;
                    }
                }

                if (!overallSuccess)
                {
                    job.Status = "failed";
                    await _download_history.Save_And_UpdateJobAsync(job);
                    _logger.LogInformation($"Download job {job.Id} failed after trying all strategies");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Download Cancelled By User and cancelation token get fired 1");
                job.ErrorLog += "[INFO] Attempt canceled by user.\n";
                job.Status = "canceled";
                throw;   // ← IMPORTANT: rethrow so upper level stops immediately (no more strategies)
            }
            catch (Exception ex)
            {
                _logger.LogError("Downloading error occured 1: {er}", ex.Message);
                job.ErrorLog += $"[FATAL] {ex.Message}\n";
                job.Status = "failed-ct";
                await _download_history.Save_And_UpdateJobAsync(job);
            }
            finally
            {
                _globalCancellation.RemoveTokenSource(Token_Key);
                job.DownloadTask = null;
            }
        }



        // ===================================================================
        // 3. Build ProcessStartInfo (clean & reusable)
        // ===================================================================
        private ProcessStartInfo BuildProcessStartInfo(
            string ytDlpPath,
            string ffmpegPath,
            bool useCookies,
            bool useImpersonate,
            bool applyResume,
            bool applyRestart,
            DownloadJob job,
            string outputFile)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Environment = { ["FFMPEG"] = ffmpegPath },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (useCookies)
            {
                psi.ArgumentList.Add("--cookies-from-browser");
                psi.ArgumentList.Add(GetYtDlpCompatibleBrowser() ?? "chrome");
            }

            if (useImpersonate)
            {
                psi.ArgumentList.Add("--impersonate");
                psi.ArgumentList.Add(GetYtDlpCompatibleBrowser() ?? "chrome");
                psi.ArgumentList.Add("--extractor-args");
                psi.ArgumentList.Add("generic:impersonate");
            }

            if (applyResume)
            {
                psi.ArgumentList.Add("--continue");
                job.Status = "resuming";
            }
            else if (applyRestart)
            {
                job.Status = "restarting";
                // Task.Delay moved to caller to avoid blocking
            }

            // Common args
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(job.Format);
            psi.ArgumentList.Add("--merge-output-format"); psi.ArgumentList.Add("mp4");
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outputFile);
            psi.ArgumentList.Add("--progress-template");
            psi.ArgumentList.Add("prog:%(progress._percent_str)s|%(progress._downloaded_bytes_str)s|%(progress._total_bytes_str)s|%(progress._speed_str)s");
            psi.ArgumentList.Add("--newline");
            psi.ArgumentList.Add(job.Url);

            return psi;
        }



        // ===================================================================
        // 4. Single attempt (process + monitoring) - now fully separate
        // ===================================================================
        private async Task<bool> ExecuteSingleAttemptAsync(
            DownloadJob job,
            CancellationToken linkedToken,
            GlobalCancellationService _globalCancellation,
            IDownloadPersistence _download_history,
            DownloadTracker _tracker,
            bool useCookies,
            bool useImpersonate,
            bool applyResume,
            bool applyRestart,
            string outputFile,
            string ytDlpPath,
            string ffmpegPath,
            string Token_Key)
        {
            var psi = BuildProcessStartInfo(ytDlpPath, ffmpegPath, useCookies, useImpersonate, applyResume, applyRestart, job, outputFile);

            Process? proc = null;
            try
            {
                proc = Process.Start(psi);
                if (proc == null)
                {
                    job.ErrorLog += "[ERROR] Process failed to start.\n";
                    job.Status = "failed";
                    await _download_history.Save_And_UpdateJobAsync(job);
                    return false;
                }

                job.Process = proc;
                job.ProcessTreePids = _processControl.GetProcessTree(proc.Id);
                job.Status = "downloading";

                // === Monitoring tasks (same as before) ===
                var stderrTask = Task.Run(async () =>
                {
                    while (!proc.StandardError.EndOfStream)
                    {
                        var line = await proc.StandardError.ReadLineAsync(linkedToken);
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
                    await _download_history.Save_And_UpdateJobAsync(job);

                    string? line;
                    while ((line = await proc.StandardOutput.ReadLineAsync(linkedToken)) != null)
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
                                    job.LastProgressAt = DateTimeOffset.UtcNow;
                                }
                                job.Downloaded = (long)ParseHumanReadableSize(parts[1]);
                                job.Total = (long)ParseHumanReadableSize(parts[2]);
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

                bool success = proc.ExitCode == 0;
                job.Status = success ? "completed" : "failed";
                await _download_history.Save_And_UpdateJobAsync(job);

                return success;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Download Cancelled By User and cancelation token get fired 2");
                job.ErrorLog += "[INFO] Attempt canceled by user.\n";
                job.Status = "canceled";
                throw;   // ← IMPORTANT: rethrow so upper level stops immediately (no more strategies)
            }
            catch (Exception ex)
            {
                _logger.LogError("Downloading error occured 2: {er}", ex.Message);
                job.ErrorLog += $"[EXCEPTION] {ex.Message}\n";
                job.Status = "failed-ct";
                return false;
            }
            finally
            {
                if (proc?.Id > 0)
                {
                    var tree = _processControl.GetProcessTree(proc.Id);
                    _processControl.KillProcessTree(tree);
                    Log_pids_tree(job);
                }
                proc?.Dispose();
            }
        }

        // Small helper record (cleaner than tuples)
        private sealed record DownloadStrategy(bool UseCookies, bool UseImpersonate, string Name);




        public readonly string[] CookieTriggers =
        [
    "cookies", "login", "429", "too many requests", "rate limit",
    "sabr", "sign in", "log in", "authentication required"
        ];

        public readonly string[] ImpersonateTriggers =
        [
    "Cloudflare anti-bot challenge",
    "Got HTTP Error 403 caused by Cloudflare",
    "generic:impersonate",
    "anti-bot challenge",
    "attention required!",
    "checking your browser",
    "just a moment",
    "cf-ray",
    "Ray ID"
        ];


        public readonly string[] PermanentErrorTriggers =
        [
    "video unavailable",
    "this video is unavailable",
    "private video",
    "private playlist",
    "this video is private",
    "sign in to view",
    "login required to view",
    "copyright",
    "copyrighted content",
    "removed by the uploader",
    "geo restricted",
    "geoblocked",
    "not available in your country",
    "not available in your region",
    "this content is not available",
    "age restricted",
    "age-restricted",
    "age gate"
        ];


        public enum YtDlpStrategy
        {
            Normal,
            CookiesOnly,
            ImpersonateOnly,
            CookiesAndImpersonate
        }


        public async Task<(bool success, string stdout, string stderr, bool loginRequired)> TryRunYtDlpAsync(string[] args, CancellationToken cancellationToken)
        {
            bool triedCookies = false;
            bool triedImpersonate = false;
            bool loginRequired = false;
            string lastError = string.Empty;

            // === Step 1: Normal attempt ===
            var result = await ExecuteWithStrategyAsync(args, YtDlpStrategy.Normal, cancellationToken);
            lastError = result.stderr;

            if (result.success)
                return (true, result.stdout, result.stderr, false);

            var errLower = lastError.ToLowerInvariant();

            // Permanent error → throw immediately (your controller will catch it)
            if (PermanentErrorTriggers.Any(t => errLower.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("MEDIA_PERMANENT_ERROR: This media is private, unavailable, or age / geo restricted.");
            }

            // Classify this error
            loginRequired = CookieTriggers.Any(t => errLower.Contains(t, StringComparison.OrdinalIgnoreCase));
            bool isCloudflare = ImpersonateTriggers.Any(t => errLower.Contains(t, StringComparison.OrdinalIgnoreCase));

            // === Step 2: Handle Login Error (early exit if cookies also fail) ===
            if (loginRequired)
            {
                if (!triedCookies)
                {
                    triedCookies = true;
                    result = await ExecuteWithStrategyAsync(args, YtDlpStrategy.CookiesOnly, cancellationToken);
                    lastError = result.stderr;
                    errLower = lastError.ToLowerInvariant();

                    if (result.success)
                        return (true, result.stdout, result.stderr, true);

                    loginRequired = CookieTriggers.Any(t => errLower.Contains(t, StringComparison.OrdinalIgnoreCase));
                }

                // If cookies also failed → this is truly login required, stop here
                if (loginRequired)
                    return (false, "", lastError, true);
            }

            // === Step 3: Handle Cloudflare / Impersonate ===
            if (isCloudflare || !triedImpersonate)
            {
                if (!triedImpersonate)
                {
                    triedImpersonate = true;
                    result = await ExecuteWithStrategyAsync(args, YtDlpStrategy.ImpersonateOnly, cancellationToken);
                    lastError = result.stderr;

                    if (result.success)
                        return (true, result.stdout, result.stderr, loginRequired);
                }

                // Final powerful attempt: both together (most sites that need both)
                if (!triedCookies)
                {
                    triedCookies = true;
                    result = await ExecuteWithStrategyAsync(args, YtDlpStrategy.CookiesAndImpersonate, cancellationToken);
                    lastError = result.stderr;
                }
            }

            // === Final fallback ===
            return (false, "", lastError, loginRequired);
        }

        private async Task<(bool success, string stdout, string stderr)> ExecuteWithStrategyAsync(string[] baseArgs, YtDlpStrategy strategy, CancellationToken ct)
        {
            Process? proc = null;
            try
            {
                var (ytDlpPath, _) = Local_Executables_Path();

                var psi = new ProcessStartInfo(ytDlpPath)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                foreach (var arg in baseArgs)
                    psi.ArgumentList.Add(arg);

                // Apply strategy
                switch (strategy)
                {
                    case YtDlpStrategy.CookiesOnly:
                    case YtDlpStrategy.CookiesAndImpersonate:
                        psi.ArgumentList.Add("--cookies-from-browser");
                        psi.ArgumentList.Add(GetYtDlpCompatibleBrowser() ?? "chrome");
                        break;
                }

                switch (strategy)
                {
                    case YtDlpStrategy.ImpersonateOnly:
                    case YtDlpStrategy.CookiesAndImpersonate:
                        psi.ArgumentList.Add("--impersonate");
                        psi.ArgumentList.Add(GetYtDlpCompatibleBrowser() ?? "chrome");
                        psi.ArgumentList.Add("--extractor-args");
                        psi.ArgumentList.Add("generic:impersonate");
                        break;
                }

                proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start yt-dlp");

                var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = proc.StandardError.ReadToEndAsync(ct);

                await proc.WaitForExitAsync(ct);

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                SafeKillProcessTree(proc);   // your existing helper
                if (proc != null && !proc.HasExited) proc?.Dispose();

                return (proc?.ExitCode == 0, stdout, stderr);
            }
            finally
            {
                SafeKillProcessTree(proc);   // your existing helper
                if (proc != null && proc.HasExited) proc?.Dispose();
            }
        }


        private void SafeKillProcessTree(Process? process)
        {
            if (process == null || process.HasExited) return;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Built-in tree kill on Windows – extremely fast, no WMI query needed
                    _logger.LogInformation("going to kill windows process tree of get format method: {process}", process);
                    process.Kill(entireProcessTree: true);
                }
                else
                {
                    // Only Linux/macOS fallback – still parallelized in your existing KillProcessTree
                    var tree = _processControl.GetProcessTree(process.Id);
                    _logger.LogInformation("going to kill linux / mac process tree of get format method: {tree}", tree);
                    _processControl.KillProcessTree(tree);
                }
            }
            catch (Exception ex)
            {
                // Best-effort – we don't want a failed kill to crash the method
                _logger.LogWarning(ex, "Failed to terminate yt-dlp process tree (PID {Pid})", process?.Id);
            }
        }


        public string? GetYtDlpCompatibleBrowser()
        {
            static string? MapToSupportedBrowser(string id)
            {
                string lowerId = id.ToLowerInvariant();
                if (lowerId.Contains("chrome")) return "chrome";
                if (lowerId.Contains("chromium")) return "chromium";
                if (lowerId.Contains("brave")) return "brave";
                if (lowerId.Contains("firefox")) return "firefox";
                if (lowerId.Contains("edge")) return "edge";
                if (lowerId.Contains("opera")) return "opera";
                if (lowerId.Contains("safari")) return "safari";
                if (lowerId.Contains("vivaldi")) return "vivaldi";
                if (lowerId.Contains("whale")) return "whale";
                return null;
            }

            string? browser = null;
            Process? process = null;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var regPath = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice";
                    using var userChoice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPath);
                    var progId = userChoice?.GetValue("ProgId")?.ToString();
                    if (!string.IsNullOrWhiteSpace(progId))
                        browser = MapToSupportedBrowser(progId);
                }
                else if (OperatingSystem.IsLinux())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "xdg-settings",
                        Arguments = "get default-web-browser",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,           // ✅ Hides console window
                        WindowStyle = ProcessWindowStyle.Hidden // ✅ Ensures no console flash
                    };

                    process = Process.Start(psi);
                    if (process != null)
                    {
                        string result = process.StandardOutput.ReadToEnd().Trim();
                        process.WaitForExit();
                        browser = MapToSupportedBrowser(result);
                    }
                }
                else if (OperatingSystem.IsMacOS())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "sh",
                        Arguments = "-c \"plutil -extract LSHandlers xml1 -o - ~/Library/Preferences/com.apple.LaunchServices/com.apple.launchservices.secure.plist | grep -A1 'https' | grep LSHandlerRoleAll | awk -F '>' '{print $2}' | awk -F '<' '{print $1}' | head -n1\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,           // ✅ Hides console window
                        WindowStyle = ProcessWindowStyle.Hidden // ✅ Ensures no console flash
                    };

                    process = Process.Start(psi);
                    if (process != null)
                    {
                        string result = process.StandardOutput.ReadToEnd().Trim();
                        process.WaitForExit();
                        browser = MapToSupportedBrowser(result);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogInformation($"------Error determining default browser: {ex.Message}");
            }
            finally
            {
                if (process!.Id > 0)
                {
                    var tree = _processControl.GetProcessTree(process!.Id);
                    _processControl.KillProcessTree(tree); // kill the process tree
                }
                process?.Dispose(); // ensure the process is disposed
            }

            return browser;
        }


        public string SanitizeUrl(string url)
        {
            try
            {
                Uri uri = new(url);
                var host = uri.Host.ToLowerInvariant();
                string cleanUrl = url;

                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                string path = uri.AbsolutePath;

                if (host.Contains("youtube.com") && query["v"] != null)
                {
                    cleanUrl = $"https://www.youtube.com/watch?v={query["v"]}";
                }
                else if (host.Contains("youtu.be"))
                {
                    cleanUrl = $"https://youtu.be{path}";
                }
                else if (host.Contains("tiktok.com"))
                {
                    var parts = path.Split('/').Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                    if (parts.Length >= 3 && parts[1] == "video")
                    {
                        var username = parts[0].TrimStart('@');
                        cleanUrl = $"https://www.tiktok.com/@{username}/video/{parts[2]}";
                    }
                }
                else if (host.Contains("facebook.com"))
                {
                    if (path.Contains("/videos/"))
                    {
                        var segments = path.Split('/');
                        var videoId = segments.LastOrDefault(s => s.All(char.IsDigit));
                        if (!string.IsNullOrEmpty(videoId))
                            cleanUrl = $"https://www.facebook.com/watch/?v={videoId}";
                    }
                }
                else if (host.Contains("instagram.com"))
                {
                    var parts = path.Split('/');
                    if (parts.Length > 2 && parts[1] == "reel" || parts[1] == "p" || parts[1] == "tv")
                    {
                        cleanUrl = $"https://www.instagram.com/{parts[1]}/{parts[2]}/";
                    }
                }
                else if (host.Contains("x.com") || host.Contains("twitter.com"))
                {
                    var parts = path.Split('/');
                    if (parts.Length >= 4 && parts[2] == "status")
                    {
                        cleanUrl = $"https://{host}/{parts[1]}/status/{parts[3]}";
                    }
                }
                else if (host.Contains("reddit.com"))
                {
                    var match = MyRegex_2().Match(path);
                    if (match.Success)
                    {
                        cleanUrl = $"https://{host}{match.Value}";
                    }
                }
                else if (host.Contains("vimeo.com"))
                {
                    var id = path.Trim('/').Split('/').LastOrDefault();
                    if (long.TryParse(id, out _))
                    {
                        cleanUrl = $"https://vimeo.com/{id}";
                    }
                }
                else if (host.Contains("dailymotion.com"))
                {
                    var parts = path.Split('/');
                    if (parts.Length > 2 && parts[1] == "video")
                    {
                        cleanUrl = $"https://www.dailymotion.com/video/{parts[2]}";
                    }
                }
                else if (host.Contains("soundcloud.com"))
                {
                    // Typically soundcloud.com/user/track
                    var parts = path.Split('/');
                    if (parts.Length >= 3)
                    {
                        cleanUrl = $"https://soundcloud.com/{parts[1]}/{parts[2]}";
                    }
                }
                else if (host.Contains("linkedin.com"))
                {
                    var match = MyRegex_3().Match(path);
                    if (match.Success)
                    {
                        cleanUrl = $"https://www.linkedin.com{match.Value}";
                    }
                }

                return cleanUrl;
            }
            catch
            {
                return url; // Fallback
            }
        }


        [GeneratedRegex(@"\b(?:m3u8_native|h3u8|hls)\b", RegexOptions.IgnoreCase, "en-US")]
        private partial Regex MyRegex_1();

        [GeneratedRegex(@"^/r/[^/]+/comments/([^/]+)")]
        private static partial Regex MyRegex_2();

        [GeneratedRegex(@"^/posts/[^/?]+")]
        private static partial Regex MyRegex_3();

    }
}
