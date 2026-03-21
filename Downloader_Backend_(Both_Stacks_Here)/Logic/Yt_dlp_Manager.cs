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
        public sealed record YtDlpStrategyComponent(string Name, List<string> Arguments, List<string> ErrorTriggers);
        private readonly List<YtDlpStrategyComponent> _registry;

        public YT_Dlp_Strategy_Engine(ILogger<Utility> logger, ProcessControl processControl, File_Saver file_Saver, Utility utility)
        {
            _processControl = processControl;
            _utility = utility;
            _logger = logger;
            _fileSaver = file_Saver;



            string browser = _utility.GetYtDlpCompatibleBrowser() ?? "chrome";

            _registry =
            [
            new("Impersonate",    ["--impersonate", $"{browser}", "--extractor-args", $"{UniversalAllSitesExtractorArgs}"], ["cloudflare", "403", "forbidden", "anti-bot", "cf-ray", "checking your browser", "just a moment", "attention required", "generic:impersonate", "Cloudflare anti-bot challenge","Got HTTP Error 403 caused by Cloudflare","anti-bot challenge"]),
            new("GeoBypass",      ["--geo-bypass", "--xff", "default"],  ["geo", "geoblocked", "not available in your country", "not available in your region", "region"]),
            new("HeavyDefenses",  ["--sleep-interval", "2", "--max-sleep-interval", "8"], ["rate limit", "429", "too many", "blocked", "slow down"]),
            new("JSRuntime",      ["--js-runtimes", "deno", "--remote-components", "ejs:github"],["javascript", "nsig", "signature", "n function", "player", "runtime", "no supported javascript", "javascript runtime", "JS challenge", "signature extraction failed"]),
            new("Cookies",        ["--cookies-from-browser", $"{browser}"], ["cookies", "login", "sign in", "sabr", "authentication required", "private video", "not available"])
            ];

        }

        const string UniversalAllSitesExtractorArgs =
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




        private static List<string> GetBaseArgs(string url)
        {
            return
        [
        "--ignore-errors", "--no-warnings",
        "--allow-dynamic-mpd", "--check-formats",
        "--extractor-retries", "15",
        "--retries", "10",
        "--socket-timeout", "15",
        "--no-check-certificates",
        "--clean-info-json", "--restrict-filenames",
        "--skip-unavailable-fragments",
        "--no-playlist", "--js-runtimes", "deno",
        "--extractor-args", UniversalAllSitesExtractorArgs,
        url
        ];
        }


        private static List<string> GetFormatArgs(string url)
        {
            var args = GetBaseArgs(url);
            args.Insert(args.Count - 2, "-J"); // insert before the URL
            return args;
        }


        private static List<string> GetTitleArgs(string url)
        {
            var args = GetBaseArgs(url);
            args.Insert(args.Count - 2, "--print");
            args.Insert(args.Count - 2, "title");
            args.Insert(args.Count - 2, "--playlist-items");
            args.Insert(args.Count - 2, "1");
            return args;
        }


        private List<string> BuildDownloadBaseArguments(DownloadJob job, string outputFile) =>
        [
        "--js-runtimes", "node",
        "--concurrent-fragments", "4",
        "--http-chunk-size", "10M",
        "-f", job.Format,
        "--merge-output-format", "mp4",
        "-o", outputFile,
        "--progress-template", "prog:%(progress._percent_str)s|%(progress._downloaded_bytes_str)s|%(progress._total_bytes_str)s|%(progress._speed_str)s",
        "--newline",
        job.Url
        ];


        // PURE decision - NO yt-dlp execution - used by download cache logic
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

            return (accumulated.ToList(), "Vanilla (no escalation needed)");
        }



        // Full execution (used only by GetTitle + ExtractMediaInfo)
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
            catch (Exception ex)
            {
                string error = $"Error happened in format fetching method {ex.Message}";
                _logger.LogError("{failed}", error);
                return (false, "", error, error);
            }
        }



        // DRY core process (unchanged from previous, kept for title/formats)
        private async Task<(bool Success, string StdOut, string StdErr)> ExecuteYtDlpProcessCoreAsync(List<string> args, CancellationToken ct)
        {
            Process? proc = null;
            try
            {

                var (ytDlpPath, _, nodePath) = _utility.Local_Executables_Path();
                var psi = new ProcessStartInfo()
                {
                    Environment = { ["PATH"] = $"{nodePath}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}" },
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
                await proc.WaitForExitAsync(ct);
                return (proc.ExitCode == 0, await outTask, await errTask);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError("Get format / Title Cancelled By User and cancelation token get fired {ex}", ex.Message);
                throw;
            }
            finally
            {
                _utility.SafeKillProcessTree(proc);   // your existing helper
                if (proc != null && proc.HasExited) proc?.Dispose();
            }
        }


        // =============== GET TITLE (adaptive + title cache) ===============
        public async Task<string?> GetTitle(string mediaUrl, CancellationToken cancellationToken)
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



        // =============== FORMATS / INFO (adaptive + persist chain + title) ===============
        public async Task<(bool Success, string stdOut, string stdErr, bool loginRequired)> Get_Formats_Helper(string url, CancellationToken ct)
        {
            var baseArgs = GetFormatArgs(url);
            // make a cache of url so if same url then try with stored strategy file

            var (success, stdOut, stdErr, chain) = await ExecuteWithFullAdaptiveEscalationAsync(baseArgs, ct, allowCookies: true);

            bool loginRequired = CookieTriggers.Any(trigger => stdErr.Contains(trigger, StringComparison.OrdinalIgnoreCase));

            if (success)
            {
                if (!string.IsNullOrEmpty(chain))
                {
                    _fileSaver.File_Path["Strategy_File"] = Path.Combine(Utility.Create_Path(Making_Logs_Path: true), "temp_file_strtegy.txt");
                    File.WriteAllText(_fileSaver.File_Path["Strategy_File"], chain);
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


        /* Download Logic */

        public async Task<IActionResult> DownloadAsync(DownloadJob job, CancellationToken linkedToken, GlobalCancellationService _globalCancellation, IDownloadPersistence _download_history, DownloadTracker _tracker, bool resume = false, bool restart = false, string Token_Key = "")
        {
            string videoPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            string downloadsFolder = Path.Combine(videoPath, "Dlp_downloads");
            Directory.CreateDirectory(downloadsFolder);

            var outputFile = Path.Combine(downloadsFolder, $"{job.Id}___{job.Title}.mp4");
            var (ytDlpPath, ffmpegPath, nodePath) = _utility.Local_Executables_Path();

            linkedToken.Register(() =>
            {
                job.Status = "canceled";
                job.ErrorLog += "[INFO] Job canceled by user.\n";
                _logger.LogInformation("User Cancel the job: {req}", linkedToken.IsCancellationRequested);
            });

            // Fire and forget - main method stays super clean
            job.DownloadTask = Task.Run(async () =>
            {
                await ExecuteDownloadWithFullAdaptiveOrCachedAsync(
                    job,
                    linkedToken,
                    _globalCancellation,
                    _download_history,
                    _tracker,
                    resume,
                    restart,
                    Token_Key,
                    outputFile,
                    ytDlpPath,
                    ffmpegPath,
                    nodePath);
            }, linkedToken);

            return new OkObjectResult(new { jobId = job.Id, title = job.Title });
        }


        private async Task ExecuteDownloadWithFullAdaptiveOrCachedAsync(
            DownloadJob job,
            CancellationToken linkedToken,
            GlobalCancellationService globalCancellation,
            IDownloadPersistence history,
            DownloadTracker tracker,
            bool resumeRequested,
            bool restartRequested,
            string tokenKey,
            string outputFile,
            string ytDlpPath,
            string ffmpegPath,
            string nodePath)
        {
            job.ErrorLog = string.Empty;
            string cachedChain = "";
            List<string> escalatedArgs = [];
            string chainName = "";

            if (_fileSaver.File_Path.TryGetValue("Strategy_File", out string? Strategy_File))
                if (!string.IsNullOrWhiteSpace(Strategy_File))
                {
                    if (File.Exists(Strategy_File))
                    {
                        string res = File.ReadAllText(Strategy_File);
                        if (!string.IsNullOrWhiteSpace(res))
                        {
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

                var cachedFullArgs = RebuildArgumentsFromChain(baseArgs, cachedChain);
                overallSuccess = await ExecuteSingleAttemptAsync(
                    job, linkedToken, globalCancellation, history, tracker,
                    cachedFullArgs, resumeRequested, restartRequested, outputFile,
                    ytDlpPath, ffmpegPath, nodePath, tokenKey);
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
                    if (cacheExists && attempt == 0) (escalatedArgs, chainName) = ComputeCumulativeStrategyArguments(baseArgs, errorForDecision, applied, allowCookiesAsLastResort: true);

                    job.ErrorLog += $"\n=== ATTEMPT {attempt}/7 → {chainName} ===\n";
                    job.Status = $"attempting ({chainName})";
                    await history.Save_And_UpdateJobAsync(job);

                    bool successThisAttempt = await ExecuteSingleAttemptAsync(
                        job, linkedToken, globalCancellation, history, tracker,
                        escalatedArgs, resumeRequested && attempt == 1, restartRequested && attempt == 1,
                        outputFile, ytDlpPath, ffmpegPath, nodePath, tokenKey);

                    if (successThisAttempt)
                    {
                        overallSuccess = true;
                        job.Status = "completed";
                        await history.Save_And_UpdateJobAsync(job);
                    }
                    else
                    {
                        (escalatedArgs, chainName) = ComputeCumulativeStrategyArguments(baseArgs, errorForDecision, applied, allowCookiesAsLastResort: true);
                        applied.Add(chainName.Split(' ')[0]);
                    }
                }
            }

            if (!overallSuccess)
            {
                job.Status = "failed";
                if (File.Exists(Strategy_File)) File.Delete(Strategy_File);
                await history.Save_And_UpdateJobAsync(job);
                _logger.LogInformation("Download {JobId} exhausted all options", job.Id);
            }

            globalCancellation.RemoveTokenSource(tokenKey);
            job.DownloadTask = null;
        }


        private List<string> RebuildArgumentsFromChain(List<string> baseArgs, string chain)
        {
            var full = baseArgs.ToList();
            var parts = chain.Split([" → "], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var clean = part.Replace("(fallback)", "").Replace("(last resort)", "").Trim();
                var component = _registry.FirstOrDefault(record => record.Name == clean); // access via friend or expose getter
                component?.Arguments.ForEach(full.Add);
            }
            return full;
        }


        private async Task<bool> ExecuteSingleAttemptAsync(
            DownloadJob job,
            CancellationToken linkedToken,
            GlobalCancellationService _globalCancellation,
            IDownloadPersistence _download_history,
            DownloadTracker _tracker,
            List<string> fullArgs,
            bool applyResume,
            bool applyRestart,
            string outputFile,
            string ytDlpPath,
            string ffmpegPath,
            string nodePath,
            string Token_Key)
        {

            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Environment = { ["FFMPEG"] = ffmpegPath, ["PATH"] = $"{nodePath}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}" },
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

                bool result = proc.ExitCode == 0;
                job.Status = result ? "completed" : "failed";
                await _download_history.Save_And_UpdateJobAsync(job);

                return result;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError("Download Cancelled By User and cancelation token get fired {ex}", ex.Message);
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
