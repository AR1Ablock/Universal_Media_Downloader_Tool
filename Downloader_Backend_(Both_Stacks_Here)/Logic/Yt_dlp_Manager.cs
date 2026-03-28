using System.Diagnostics;
using System.Text;
using Downloader_Backend.Model;
using Microsoft.AspNetCore.Mvc;

namespace Downloader_Backend.Logic
{
    public partial class YT_Dlp_Strategy_Engine
    {
        private readonly ProcessControl _processControl;
        private readonly Utility _utility;
        private readonly ILogger<Utility> _logger;
        private readonly File_Saver _fileSaver;
        private sealed record YtDlpStrategyComponent(string Name, List<string> Arguments, List<string> ErrorTriggers);
        private readonly List<YtDlpStrategyComponent> _registry;

        public YT_Dlp_Strategy_Engine(ILogger<Utility> logger, ProcessControl processControl, File_Saver file_Saver, Utility utility)
        {
            _processControl = processControl;
            _utility = utility;
            _logger = logger;
            _fileSaver = file_Saver;



            string browser = _utility.GetYtDlpCompatibleBrowser() ?? "chrome";
            string deno_bin = _utility.Local_Executables_Path().deno;

            _registry =
            [
            new("Impersonate",    ["--impersonate", $"{browser}", "--extractor-args", $"{UniversalAllSitesExtractorArgs}"], ["cloudflare", "403", "forbidden", "anti-bot", "cf-ray", "checking your browser", "just a moment", "attention required", "generic:impersonate", "Cloudflare anti-bot challenge","Got HTTP Error 403 caused by Cloudflare","anti-bot challenge"]),
            new("GeoBypass",      ["--geo-bypass", "--xff", "default"],  ["geo", "geoblocked", "not available in your country", "not available in your region", "region", "country", "not supported in your country", "not supported in your region"]),
            new("HeavyDefenses",  ["--sleep-interval", "2", "--max-sleep-interval", "8"], ["rate limit", "429", "too many", "blocked", "slow down"]),
            new("Cookies",        ["--cookies-from-browser", $"{browser}"], ["cookies", "login", "sign in", "sabr", "authentication required", "private video", "not available"]),
            new("JSRuntime",      ["--no-js-runtimes", "--js-runtimes", $"{deno_bin}", "--remote-components", "ejs:github"],["javascript", "nsig", "signature", "n function", "player", "runtime", "no supported javascript", "javascript runtime", "JS challenge", "signature extraction failed"]),
            ];

        }

        public TimeSpan timeout = TimeSpan.FromSeconds(90); // configurable

        private const string UniversalAllSitesExtractorArgs =
        "generic:impersonate,prefer_ffmpeg,fragment_query,variant_query,hls_key=;" +          // generic fallback + HLS fixes + ffmpeg success
        "youtube:player_client=android,web,mweb,ios,tv,web_embedded;formats=incomplete;player_skip=js,configs,webpage;use_ad_playback_context=true;po_token=web.gvs+;webpage_client=web;skip=hls,dash;" +  // BEST YouTube success combo (multi-client fallback + more formats + skip blocks + PO/ad bypass)
        "youtubetab:skip=webpage;" +                                                          // faster playlist/channel fetch (avoids timeout failures)
        "instagram:app_id=936619743392459;" +                                                 // fixes GraphQL 403 on IG
        "twitch:client_id=kimne78kx3ncx6brgo4mv6wki5h1ko;" +                                  // bypasses Twitch rate-limit/403
        "tiktok:app_name=trill,app_version=34.1.2,aid=1180;" +                                // proven TikTok app spoof for region/403 bypass
        "twitter:api=graphql;" +                                                              // reliable Twitter/X API fallback
        "bilibili:prefer_multi_flv;" +                                                        // more formats on Bilibili
        "vimeo:client=web;" +                                                                 // forces working Vimeo client
        "soundcloud:formats=http_aac,hls_aac,http_opus,hls_opus;";                            // more audio formats (prevents "no formats" fail)




        // Triggers
        private readonly string[] CookieTriggers =
        [
        "cookies", "login", "429", "too many requests", "rate limit",
        "sabr", "sign in", "log in", "authentication required"
        ];


        private readonly string[] ImpersonateTriggers =
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


        private readonly string[] PermanentErrorTriggers =
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

        private readonly string[] Http_File_not_Found_Trigger =
        [
        "HTTP Error 416",
        "Error 416",
        "Requested range not satisfiable",
        ];




        private List<string> GetBaseArgs()
        {
            // Defined once here so all three methods inherit the correct runtime path
            var (_, ffmpeg_bin, _, node_bin) = _utility.Local_Executables_Path();

            return [
            "--ignore-errors",
            "--no-warnings",
            "--allow-dynamic-mpd",
            "--extractor-retries", "15",
            "--retries", "10",
            "--socket-timeout", "15",
            "--no-check-certificates",
            "--restrict-filenames",
            "--skip-unavailable-fragments",
            "--no-playlist",
            "--ffmpeg-location", ffmpeg_bin,
            "--no-js-runtimes",
            "--js-runtimes", $"node:{node_bin}", // Injected into base
            // "--extractor-args", UniversalAllSitesExtractorArgs
            ];
        }

        private List<string> GetFormatArgs(string url)
        {
            var args = GetBaseArgs();

            args.AddRange([
            "--clean-info-json",
            "--check-formats",
            "-J",
            url
            ]);

            return args;
        }

        private List<string> GetTitleArgs(string url)
        {
            var args = GetBaseArgs();

            args.AddRange([
            "--print", "title",
            "--playlist-items", "1",
            url
            ]);

            return args;
        }


        private List<string> BuildDownloadBaseArguments(DownloadJob job, string outputFile)
        {
            var args = GetBaseArgs();

            args.AddRange([
                "--concurrent-fragments", "4",
                "--http-chunk-size", "10M",
                "-f", job.Format,
                "--merge-output-format", "mp4",
                "-o", outputFile,
                "--progress-template", "prog:%(progress._percent_str)s|%(progress._downloaded_bytes_str)s|%(progress._total_bytes_str)s|%(progress._speed_str)s",
                "--newline",
                job.Url
            ]);

            return args;
        }

        // PURE decision - NO yt-dlp execution - used by All executioner methods 
        private (List<string> FinalArguments, string StrategyChain) ComputeCumulativeStrategyArguments(List<string> baseArguments, string lastNormalizedError, List<string> alreadyAppliedStrategies, bool allowCookiesAsLastResort)
        {
            var accumulated = new List<string>(baseArguments);
            var chain = new List<string>();

            // Permanent check
            if (PermanentErrorTriggers.Any(t => lastNormalizedError.Contains(t, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("MEDIA_PERMANENT_ERROR");

            // Specific match first (cumulative)
            var match = _registry.FirstOrDefault(record => !alreadyAppliedStrategies.Contains(record.Name) && record.ErrorTriggers.Any(t => lastNormalizedError.Contains(t, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                match.Arguments.ForEach(a => accumulated.Add(a));
                alreadyAppliedStrategies.Add(match.Name);
                chain.Add(match.Name);
                return (accumulated.ToList(), string.Join(" → ", chain));
            }

            // Unknown → fallback next unused
            var fallback = _registry.FirstOrDefault(record => !alreadyAppliedStrategies.Contains(record.Name));
            if (fallback != null)
            {
                fallback.Arguments.ForEach(a => accumulated.Add(a));
                alreadyAppliedStrategies.Add(fallback.Name);
                chain.Add($"{fallback.Name} (fallback)");
                return (accumulated.ToList(), string.Join(" → ", chain));
            }

            // Absolute final cookies
            if (allowCookiesAsLastResort && !alreadyAppliedStrategies.Contains("Cookies"))
            {
                var cookiesComp = _registry.First(record => record.Name == "Cookies");
                cookiesComp.Arguments.ForEach(a => accumulated.Add(a));
                chain.Add("Cookies (last resort)");
                return (accumulated.ToList(), string.Join(" → ", chain));
            }

            return (accumulated.ToList(), "no escalation needed");
        }



        // (Executioner method used by GetTitle + ExtractMediaInfo)
        private async Task<(bool Success, string StdOut, string StdErr, string StrategyChain)> ExecuteWithFullAdaptiveEscalationAsync(List<string> baseArguments, CancellationToken ct, bool allowCookies = true)
        {
            bool Success = false;
            string StdOut = "";
            string StdErr = "";
            try
            {
                var accumulated = new List<string>(baseArguments);
                var applied = new List<string>();
                var chainLog = new List<string>();

                for (int attempt = 0; attempt <= _registry.Count; attempt++)
                {
                    (Success, StdOut, StdErr) = await ExecuteYtDlpProcessCoreAsync([.. accumulated], ct);

                    if (Success)
                        return (true, StdOut, StdErr, string.Join(" → ", chainLog));

                    var normError = StdErr.ToLowerInvariant();

                    if (PermanentErrorTriggers.Any(t => normError.Contains(t, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("MEDIA_PERMANENT_ERROR");

                    var (newArgs, chain) = ComputeCumulativeStrategyArguments([.. accumulated], normError, applied, allowCookies);

                    accumulated = [.. newArgs];
                    chainLog.Add(chain);
                }

                return (false, "", $"All paths exhausted: {StdErr}", string.Join(" → ", chainLog));
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError("Get format / Title Cancelled By User and cancelation token get fired {ex}", ex.Message);
                return (false, "", StdErr + "Operation got Cancelled", "");
                throw;
            }
            catch (Exception ex)
            {
                string error = $"Error happened in format adaptive executioner {ex.Message}";
                _logger.LogError("{failed}", error);
                return (false, "", error, "");
            }
        }



        // DRY core process (Core executioner, for title / formats)
        private async Task<(bool Success, string StdOut, string StdErr)> ExecuteYtDlpProcessCoreAsync(List<string> args, CancellationToken ct)
        {
            Process? proc = null;
            try
            {

                var (ytDlpPath, _, _, _) = _utility.Local_Executables_Path();
                var psi = new ProcessStartInfo()
                {
                    FileName = ytDlpPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                args.ForEach(psi.ArgumentList.Add);

                proc = Process.Start(psi) ?? throw new InvalidOperationException("yt-dlp launch failed");
                var outTask = proc.StandardOutput.ReadToEndAsync(ct);
                var errTask = proc.StandardError.ReadToEndAsync(ct);
                var exitTask = proc.WaitForExitAsync(ct);

                var allTasks = Task.WhenAll(outTask, errTask, exitTask);

                var timeoutTask = Task.Delay(timeout, ct);

                var completed = await Task.WhenAny(allTasks, timeoutTask);

                if (completed == timeoutTask && !ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"server {timeout.TotalSeconds} seconds response timeout.");
                }

                // collect results first, then return
                var success = proc.ExitCode == 0;
                var stdout = await outTask;
                var stderr = await errTask;

                return (success, stdout, stderr);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError("Get format / Title Cancelled By User and cancelation token get fired {ex}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                string error = "";
                if (ct.IsCancellationRequested)
                    error = "Get format / Title Cancelled By User and cancellation token get fired {ex}" + ex.Message;
                else
                    error = "Get format / Title Cancelled By User and cancellation token get fired {ex}" + ex.Message;
                //
                _logger.LogError("Error occur in Get format / Title method {ex}", ex.Message);
                throw;
            }
            finally
            {
                if (_processControl.TryGetPid(proc, out var pid))
                {
                    var tree = _processControl.GetProcessTree(pid);
                    _processControl.KillProcessTree(tree);
                }
                if (proc != null && proc.HasExited) proc?.Dispose();
            }
        }


        // =============== GET TITLE (adaptive + title cache) ===============
        public async Task<string?> GetTitle(string mediaUrl, CancellationToken cancellationToken)
        {
            try
            {
                var baseArgs = GetTitleArgs(mediaUrl);

                var (success, stdout, _, chain) = await ExecuteWithFullAdaptiveEscalationAsync(baseArgs, cancellationToken, allowCookies: true);

                if (success && !string.IsNullOrWhiteSpace(stdout))
                {
                    _logger.LogInformation("Title success | Chain: {Chain}", chain);
                    return stdout.Trim();
                }
                _logger.LogWarning("Title failed | Chain: {Chain}", chain);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("error occured in title method: {Chain}", ex.Message);
                return null;
            }
        }



        // =============== FORMATS / INFO (adaptive + persist chain + title) ===============
        public async Task<(bool Success, string stdOut, string stdErr, bool loginRequired)> Get_Formats_Helper(string url, CancellationToken ct)
        {
            bool success = false;
            bool loginRequired = false;
            string stdOut = "";
            string stdErr = "";
            string chain = "";
            var cache_args = "";
            try
            {
                var baseArgs = GetFormatArgs(url);


                if (_fileSaver.fileMap.TryGetValue("Strategy_File", out string? Strategy_File))
                    if (!string.IsNullOrWhiteSpace(Strategy_File))
                    {
                        if (File.Exists(Strategy_File))
                        {
                            string res = File.ReadAllText(Strategy_File);
                            if (!string.IsNullOrWhiteSpace(res))
                            {
                                var stored_url = res.Split(',')[0];
                                if (stored_url == url)
                                    cache_args = res;
                            }
                        }
                    }

                if (!string.IsNullOrWhiteSpace(cache_args))
                {
                    var cachedArg = RebuildArgumentsFromChain(baseArgs, cache_args);
                    (success, stdOut, stdErr, chain) = await ExecuteWithFullAdaptiveEscalationAsync(cachedArg, ct, allowCookies: true);
                }
                else
                {
                    (success, stdOut, stdErr, chain) = await ExecuteWithFullAdaptiveEscalationAsync(baseArgs, ct, allowCookies: true);
                }


                loginRequired = CookieTriggers.Any(trigger => stdErr.Contains(trigger, StringComparison.OrdinalIgnoreCase));

                if (success)
                {
                    if (!string.IsNullOrEmpty(chain))
                    {
                        Path.Combine(_fileSaver.fileMap["Strategy_File"], "Strategy_File.txt");
                        string cache_to_write = $"{url}, {chain}";
                        File.WriteAllText(_fileSaver.fileMap["Strategy_File"], cache_to_write);
                    }
                    _logger.LogInformation("Media info extracted with chain: {Chain}", chain);
                    return (true, stdOut, stdErr, false);
                }

                if (PermanentErrorTriggers.Any(trigger => stdErr.Contains(trigger, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("MEDIA_PERMANENT_ERROR: This media is permanently unavailable, private, age-restricted, or geo-blocked.");
                }

                _logger.LogWarning("Media info extraction failed after full escalation chain: {Chain}", chain);
                return (false, stdOut, stdErr, loginRequired);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning("error occured due to token get canceled Get format executioner method {ex}", ex.Message);
                return (false, stdOut, stdErr, loginRequired);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("error occured in Get format executioner method {ex}", ex.Message);
                return (false, stdOut, stdErr, loginRequired);
            }
        }



        /* Download Logic */
        public async Task<IActionResult> DownloadAsync(DownloadJob job, CancellationToken linkedToken, GlobalCancellationService _globalCancellation, IDownloadPersistence _download_history, DownloadTracker _tracker, bool resume = false, bool restart = false, string Token_Key = "")
        {
            try
            {
                string videoPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                string downloadsFolder = Path.Combine(videoPath, "Dlp_downloads");
                Directory.CreateDirectory(downloadsFolder);

                var outputFile = Path.Combine(downloadsFolder, $"{job.Id}___{job.Title}.mp4");

                linkedToken.Register(async () =>
                {
                    if (job.Status != "completed")
                        job.Status = "canceled";
                    job.ErrorLog += "[INFO] Job canceled by user.\n";
                    await _download_history.Save_And_UpdateJobAsync(job);
                    await _tracker.NotifyJobUpdatedAsync(job.Key);
                    _logger.LogInformation("User Cancel the job: {req}", linkedToken.IsCancellationRequested);
                });

                // Fire and forget - main method
                job.DownloadTask = Task.Run(async () =>
                {
                    await ExecuteDownloadWithFullAdaptiveOrCachedAsync(job, linkedToken, _globalCancellation, _download_history, _tracker, resume, restart, Token_Key, outputFile);
                }, linkedToken);

                return new OkObjectResult(new { jobId = job.Id, title = job.Title });

            }
            catch (Exception ex)
            {
                _logger.LogError("en error occured in main downlaod method {ex}", ex);
                throw;
            }
        }


        private async Task ExecuteDownloadWithFullAdaptiveOrCachedAsync(DownloadJob job, CancellationToken linkedToken, GlobalCancellationService globalCancellation, IDownloadPersistence history, DownloadTracker tracker, bool resumeRequested, bool restartRequested, string tokenKey, string outputFile)
        {
            try
            {
                job.ErrorLog = string.Empty;
                string cachedChain = "";
                List<string> escalatedArgs = [];
                string chainName = "";

                if (_fileSaver.fileMap.TryGetValue("Strategy_File", out string? Strategy_File))
                    if (!string.IsNullOrWhiteSpace(Strategy_File))
                    {
                        if (File.Exists(Strategy_File))
                        {
                            string res = File.ReadAllText(Strategy_File);
                            if (!string.IsNullOrWhiteSpace(res))
                            {
                                var url = res.Split(',')[0];
                                if (url == job.Url)
                                    cachedChain = res;
                            }
                        }
                    }

                bool cacheExists = !string.IsNullOrWhiteSpace(cachedChain);

                var baseArgs = BuildDownloadBaseArguments(job, outputFile);
                bool overallSuccess = false;

                if (cacheExists)
                {
                    job.ErrorLog += $"\n=== CACHE HIT → using saved chain: {cachedChain} ===\n";
                    job.Status = "using_cached_strategy";
                    await history.Save_And_UpdateJobAsync(job);
                    await tracker.NotifyJobUpdatedAsync(job.Key);
                    //
                    var cachedFullArgs = RebuildArgumentsFromChain(baseArgs, cachedChain);
                    overallSuccess = await ExecuteSingleAttemptAsync(job, linkedToken, globalCancellation, history, tracker, cachedFullArgs, resumeRequested, restartRequested, outputFile, tokenKey);
                }

                if (!overallSuccess) // cache miss OR cached attempt failed
                {
                    if (!cacheExists)
                        job.ErrorLog += "\n=== NO CACHE → starting full adaptive escalation ===\n";
                    else
                        job.ErrorLog += "\n=== CACHED ATTEMPT FAILED → falling back to full adaptive ===\n";

                    var applied = new List<string>();
                    for (int attempt = 0; attempt <= _registry.Count && !overallSuccess; attempt++)
                    {
                        var errorForDecision = job.ErrorLog.ToLowerInvariant();

                        if (Http_File_not_Found_Trigger.Any(e => job.ErrorLog.Contains(e, StringComparison.OrdinalIgnoreCase)))
                        {
                            _logger.LogError("File not same, cant write media data on different media file, error 416");
                            break;
                        }

                        if (!cacheExists && attempt == 0)
                            escalatedArgs.AddRange(baseArgs); // start with base arg 
                        else
                            (escalatedArgs, chainName) = ComputeCumulativeStrategyArguments(baseArgs, errorForDecision, applied, allowCookiesAsLastResort: true);


                        job.ErrorLog += $"\n=== ATTEMPT {attempt}/7 → {chainName} ===\n";
                        job.Status = $"attempting ({chainName})";
                        await history.Save_And_UpdateJobAsync(job);
                        await tracker.NotifyJobUpdatedAsync(job.Key);
                        /*                         await history.Save_And_UpdateJobAsync(job); */

                        bool successThisAttempt = await ExecuteSingleAttemptAsync(job, linkedToken, globalCancellation, history, tracker, escalatedArgs, resumeRequested, restartRequested, outputFile, tokenKey);

                        if (successThisAttempt)
                        {
                            overallSuccess = true;
                            job.Status = "completed";
                            await history.Save_And_UpdateJobAsync(job);
                            await tracker.NotifyJobUpdatedAsync(job.Key);
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(chainName))
                                applied.Add(chainName.Split(' ')[0]);
                        }
                    }
                }

                if (!overallSuccess)
                {
                    job.Status = "failed";
                    if (File.Exists(Strategy_File)) File.Delete(Strategy_File);
                    await history.Save_And_UpdateJobAsync(job);
                    await tracker.NotifyJobUpdatedAsync(job.Key);
                    _logger.LogInformation("Download {JobId} exhausted all options", job.Id);
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError("Download caller Cancelled By User and cancelation token get fired helper {ex}", ex.Message);
                job.ErrorLog += "[INFO] Attempt canceled by user.\n";
                job.Status = "canceled";
                await history.Save_And_UpdateJobAsync(job);
                await tracker.NotifyJobUpdatedAsync(job.Key);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("Downloading caller error occured: {er}", ex.Message);
                job.ErrorLog += $"[EXCEPTION] {ex.Message}\n";
                job.Status = "error occured";
                await history.Save_And_UpdateJobAsync(job);
                await tracker.NotifyJobUpdatedAsync(job.Key);
            }
            finally
            {
                _logger.LogInformation("Download job of {job} completed, Going to cancel token", job.Title);
                var Sc = job.TokenSource;
                if (Sc != null)
                {
                    if (!Sc.IsCancellationRequested)
                        Sc?.Cancel();
                    Sc?.Dispose();
                }
                job.DownloadTask = null;
            }
        }



        private List<string> RebuildArgumentsFromChain(List<string> baseArgs, string chain)
        {
            var full = baseArgs.ToList();
            string url_freed = chain.Split(", ")[1];
            var parts = url_freed.Split([" → "], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                string clean = part.Replace("(fallback)", "").Replace("(last resort)", "").Trim();
                var component = _registry.FirstOrDefault(record => record.Name == clean); // access via friend or expose getter
                component?.Arguments.ForEach(full.Add);
            }

            return full;
        }



        private async Task<bool> ExecuteSingleAttemptAsync(DownloadJob job, CancellationToken linkedToken, GlobalCancellationService _globalCancellation, IDownloadPersistence _download_history, DownloadTracker _tracker, List<string> fullArgs, bool applyResume, bool applyRestart, string outputFile, string Token_Key)
        {

            string ytDlpPath = _utility.Local_Executables_Path().ytDlp;
            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            fullArgs.ForEach(a => psi.ArgumentList.Add(a));
            if (applyResume) { psi.ArgumentList.Add("--continue"); job.Status = "resuming"; }
            else if (applyRestart) job.Status = "restarting";


            Process? proc = null;
            try
            {
                proc = Process.Start(psi);
                if (proc == null)
                {
                    job.ErrorLog += "[ERROR] Process failed to start.\n";
                    job.Status = "failed";
                    await _download_history.Save_And_UpdateJobAsync(job);
                    await _tracker.NotifyJobUpdatedAsync(job.Key);
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
                        job.Status = "Trying-err";
                        await _tracker.NotifyJobUpdatedAsync(job.Key);   // fire-and-forget, won't block download
                    }
                }, linkedToken);

                var stdoutTask = Task.Run(async () =>
                {
                    _globalCancellation.DetachTokenSource(Token_Key);
                    job.OutputPath = outputFile;
                    _tracker.Jobs[job.Id] = job;
                    await _download_history.Save_And_UpdateJobAsync(job);
                    await _tracker.NotifyJobUpdatedAsync(job.Key);   // fire-and-forget, won't block download

                    string? line;
                    while ((line = await proc.StandardOutput.ReadLineAsync(linkedToken)) != null)
                    {
                        if (line.StartsWith("prog:"))
                        {
                            var parts = line[5..].Split('|');
                            if (parts.Length >= 4)
                            {
                                // Percent
                                job.Progress = _utility.ParsePercent(parts[0]);
                                job.LastProgressAt = DateTimeOffset.UtcNow;
                                // Display values
                                job.Downloaded = _utility.FormatSize(parts[1]);
                                job.Total =  _utility.FormatSize(parts[2]);
                                job.Speed = _utility.FormatSize(parts[3]);

                                job.Status = "downloading";
                                await _tracker.NotifyJobUpdatedAsync(job.Key);   // fire-and-forget, won't block download
                            }
                        }
                        else
                        {
                            job.ErrorLog += "[stdout] " + line + "\n";
                            job.Status = "Trying";
                            await _tracker.NotifyJobUpdatedAsync(job.Key);   // fire-and-forget, won't block download
                        }
                    }
                }, linkedToken);

                await Task.WhenAll(stderrTask, stdoutTask);
                await proc.WaitForExitAsync(linkedToken);

                bool result = proc.ExitCode == 0;
                job.Status = result ? "completed" : "failed";
                await _download_history.Save_And_UpdateJobAsync(job);
                await _tracker.NotifyJobUpdatedAsync(job.Key);   // fire-and-forget, won't block download

                return result;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError("Download Cancelled By User and cancelation token get fired {ex}", ex.Message);
                job.ErrorLog += "[INFO] Attempt canceled by user.\n";
                job.Status = "canceled";
                await _tracker.NotifyJobUpdatedAsync(job.Key);   // fire-and-forget, won't block download
                throw;   // ← IMPORTANT: rethrow so upper level stops immediately (no more strategies)
            }
            catch (Exception ex)
            {
                _logger.LogError("Downloading error occured: {er}", ex.Message);
                job.ErrorLog += $"[EXCEPTION] {ex.Message}\n";
                job.Status = "error occured";
                await _tracker.NotifyJobUpdatedAsync(job.Key);   // fire-and-forget, won't block download
                return false;
            }
            finally
            {
                if (_processControl.TryGetPid(proc, out var pid))
                {
                    var tree = _processControl.GetProcessTree(pid);
                    _processControl.KillProcessTree(tree);
                    _utility.Log_pids_tree(job);
                }
                proc?.Dispose();
            }
        }
    }


}