namespace Downloader_Backend.Logic
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    public class YtDlpCookieRunner (ILogger<YtDlpCookieRunner> logger)
    {
        private readonly ILogger<YtDlpCookieRunner> _logger = logger;

        // yt-dlp supported browser ids
        private static readonly string[] AllSupportedBrowsers =
            ["brave", "chrome", "chromium", "edge", "firefox", "opera", "safari", "vivaldi", "whale"];

        public record Result(bool Success, string StdOut, string StdErr, bool LoginRequired, string? UsedBrowser);

        /// <summary>
        /// Top-level method: tries one plain yt-dlp run; if login/cookies required, tries to extract cookies
        /// using installed browsers in priority order (default -> chrome -> firefox -> others). Only uses
        /// a browser when extracting cookies; it does not run plain runs per browser.
        /// </summary>
        public async Task<Result> RunYtDlpWithCookieFallbackAsync(
            string ytDlpPath,
            string[] baseArgs,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(ytDlpPath)) throw new ArgumentNullException(nameof(ytDlpPath));
            ArgumentNullException.ThrowIfNull(baseArgs);

            _logger.LogInformation("--- Log message. Starting plain (no-cookie) attempt of yt-dlp.");
            var plain = await RunYtDlpProcessAsync(ytDlpPath, baseArgs, cancellationToken);
            _logger.LogInformation($"--- Log message. Plain attempt exitCode={plain.ExitCode} loginRequired={plain.LoginRequired}");

            if (plain.Success)
                return new Result(true, plain.StdOut, plain.StdErr, plain.LoginRequired, null);

            // if not loginRequired, nothing cookie-based can fix it — return the plain result
            if (!plain.LoginRequired)
            {
                _logger.LogInformation("--- Log message. Plain failure but not loginRequired. Aborting cookie attempts.");
                return new Result(false, plain.StdOut, plain.StdErr, plain.LoginRequired, null);
            }

            // Determine browser priority: default, chrome, firefox, then others
            var defaultBrowser = await GetDefaultBrowserAsync();
            var prioritized = new List<string>();
            if (!string.IsNullOrEmpty(defaultBrowser)) prioritized.Add(defaultBrowser.ToLowerInvariant());
            AddIfNotPresent(prioritized, "chrome");
            AddIfNotPresent(prioritized, "firefox");
            foreach (var b in AllSupportedBrowsers) AddIfNotPresent(prioritized, b);

            // Filter to installed browsers only
            var installed = new List<string>();
            _logger.LogInformation($"--- Log message. Checking installed browsers from prioritized list: {string.Join(",", prioritized)}");
            foreach (var b in prioritized.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (await IsBrowserInstalledAsync(b))
                {
                    _logger.LogInformation($"--- Log message. Browser installed: {b}");
                    installed.Add(b);
                }
                else
                {
                    _logger.LogInformation($"--- Log message. Browser NOT installed: {b}");
                }
            }

            if (installed.Count == 0)
            {
                _logger.LogInformation("--- Log message. No browsers installed. Returning original plain failure.");
                return new Result(false, plain.StdOut, plain.StdErr, plain.LoginRequired, null);
            }

            // Try cookie extraction per installed browser; if extraction yields >0 cookies then run actual full attempt with that browser
            foreach (var browser in installed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation($"--- Log message. Trying cookie extraction probe with browser: {browser}");

                // cookie probe uses --cookies-from-browser <browser> + --skip-download to avoid heavy download
                List<string> probeArgs = [baseArgs[1]]; // currently only add url and exclude -j
                probeArgs.Add("--cookies-from-browser");
                probeArgs.Add(browser);
                probeArgs.Add("--skip-download");
                probeArgs.Add("-v");

                var probe = await RunYtDlpProcessAsync(ytDlpPath, [.. probeArgs], cancellationToken);
                // parse cookie extraction count from stderr (yt-dlp prints to stderr)
                var cookiesExtracted = ParseExtractedCookies(probe.StdErr + "\n" + probe.StdOut);
                _logger.LogInformation($"--- Log message. Probe result for {browser}: exitCode={probe.ExitCode}, cookiesExtracted={cookiesExtracted}");
                if (cookiesExtracted <= 0)
                {
                    _logger.LogInformation($"--- Log message. No usable cookies from {browser} (or extraction failed). Trying next browser.");
                    continue;
                }

                // we got cookies; now do the real download attempt using this browser
                _logger.LogInformation($"--- Log message. Cookies found ({cookiesExtracted}) for {browser}. Running full yt-dlp with cookies-from-browser {browser}.");
                var actualArgs = baseArgs.ToList();
                actualArgs.Add("--cookies-from-browser");
                actualArgs.Add(browser);

                var actual = await RunYtDlpProcessAsync(ytDlpPath, [.. actualArgs], cancellationToken);
                _logger.LogInformation($"--- Log message. Actual run for {browser} finished. success={actual.Success} exitCode={actual.ExitCode}");

                if (actual.Success)
                    return new Result(true, actual.StdOut, actual.StdErr, actual.LoginRequired, browser);

                // If actual failed but indicates loginRequired (or other recoverable error), we continue to next browser
                _logger.LogInformation($"--- Log message. Actual run for {browser} failed. Err indicates loginRequired={actual.LoginRequired}. Trying next browser if any.");
            }

            // none succeeded
            _logger.LogInformation("--- Log message. All browser cookie attempts exhausted. Returning last known plain failure.");
            return new Result(false, plain.StdOut, plain.StdErr, plain.LoginRequired, null);
        }

        private static void AddIfNotPresent(List<string> list, string value)
        {
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
                list.Add(value);
        }

        // Parse "Extracted N cookies" (yt-dlp prints to stderr). Returns -1 if parsing impossible, 0 if none, >0 if found.
        private static int ParseExtractedCookies(string combinedOutput)
        {
            if (string.IsNullOrWhiteSpace(combinedOutput)) return 0;
            // patterns we expect: "Extracted 271 cookies from edge (63 could not be decrypted)"
            var m = Regex.Match(combinedOutput, @"Extracted\s+(\d+)\s+cookies", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;

            // fallback patterns: "Extracted: 271" or "Found 271 cookies"
            m = Regex.Match(combinedOutput, @"\b(found|extracted|extracted:|extracted,)\s*(\d{1,6})\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                // group 2 may be number or group 1 depending; find digits
                var digits = Regex.Match(m.Value, @"\d+");
                if (digits.Success && int.TryParse(digits.Value, out var n2)) return n2;
            }

            return 0;
        }

        // Data returned for a single yt-dlp process run
        private class ProcResult
        {
            public bool Success { get; init; }
            public string StdOut { get; init; } = "";
            public string StdErr { get; init; } = "";
            public int ExitCode { get; init; }
            public bool LoginRequired { get; init; }
        }

        // Run yt-dlp with given args, capture stdout+stderr, compute loginRequired heuristics
        private async Task<ProcResult> RunYtDlpProcessAsync(string ytDlpPath, string[] args, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stdoutSb = new StringBuilder();
            var stderrSb = new StringBuilder();

            var stdOutTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stdErrTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) stdOutTcs.TrySetResult(true);
                else stdoutSb.AppendLine(e.Data);
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) stdErrTcs.TrySetResult(true);
                else stderrSb.AppendLine(e.Data);
            };

            try
            {
                if (!proc.Start()) throw new InvalidOperationException("Failed to start yt-dlp process.");
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                using (cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                    }
                    catch { /* swallow */ }
                }))
                {
                    await proc.WaitForExitAsync(cancellationToken);
                }

                await Task.WhenAll(stdOutTcs.Task, stdErrTcs.Task);

                var outp = stdoutSb.ToString();
                var err = stderrSb.ToString();

                var combined = (outp + "----------\n---------" + err).ToLowerInvariant();
                _logger.LogInformation("--- log Combined yt-dlp output:\n" + combined.ToString());
                bool fatalError =
                Regex.IsMatch(combined, @"fragment\s+\d+\s+not\s+found") || // fragment N not found
                err.Contains("http error 403") ||
                err.Contains("http error") ||
                err.Contains("403") ||
                err.Contains("forbidden") ||
                err.Contains("unable to continue");

                bool loginRequired =
                combined.Contains("cookies") ||
                combined.Contains("login") ||
                combined.Contains("sign in") ||
                combined.Contains("429") ||
                combined.Contains("too many requests") ||
               (combined.Contains("sabr") && fatalError) ||
                combined.Contains("unavailable");

                var success = proc.ExitCode == 0 && !fatalError;

                return new ProcResult
                {
                    Success = success,
                    StdOut = outp,
                    StdErr = err,
                    ExitCode = proc.ExitCode,
                    LoginRequired = loginRequired
                };
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                return new ProcResult { Success = false, StdOut = "", StdErr = "CANCELLED_BY_CLIENT", ExitCode = -1, LoginRequired = false };
            }
            catch (Exception ex)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                return new ProcResult { Success = false, StdOut = "", StdErr = ex.Message, ExitCode = -1, LoginRequired = false };
            }
        }

        // ---------------------------
        // Platform helpers (same style as before)
        // ---------------------------

        private static async Task<string?> GetDefaultBrowserAsync()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
                        var progId = key?.GetValue("ProgId")?.ToString();
                        if (!string.IsNullOrWhiteSpace(progId)) return MapToSupportedBrowser(progId);
                    }
                    catch { }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var (ok, outp, _) = await RunCommandCaptureAsync("xdg-settings", "get default-web-browser");
                    if (ok && !string.IsNullOrWhiteSpace(outp)) return MapToSupportedBrowser(outp.Trim());
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var (ok, outp, _) = await RunCommandCaptureAsync("sh", "-c \"plutil -convert xml1 -o - ~/Library/Preferences/com.apple.LaunchServices/com.apple.launchservices.secure.plist 2>/dev/null | grep -A1 'https' | grep LSHandlerRoleAll | awk -F '>' '{print $2}' | awk -F '<' '{print $1}' | head -n1\"");
                    if (ok && !string.IsNullOrWhiteSpace(outp)) return MapToSupportedBrowser(outp.Trim());
                }
            }
            catch { }
            return null;
        }

        private static string? MapToSupportedBrowser(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var lower = id.ToLowerInvariant();
            if (lower.Contains("chrome")) return "chrome";
            if (lower.Contains("chromium")) return "chromium";
            if (lower.Contains("brave")) return "brave";
            if (lower.Contains("firefox")) return "firefox";
            if (lower.Contains("edge") || lower.Contains("msedge")) return "edge";
            if (lower.Contains("opera")) return "opera";
            if (lower.Contains("safari")) return "safari";
            if (lower.Contains("vivaldi")) return "vivaldi";
            if (lower.Contains("whale")) return "whale";
            return null;
        }

        private static async Task<(bool Ok, string StdOut, string StdErr)> RunCommandCaptureAsync(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                var outp = await p.StandardOutput.ReadToEndAsync();
                var err = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                return (true, outp, err);
            }
            catch { return (false, "", ""); }
        }

        private static async Task<bool> IsBrowserInstalledAsync(string browserId)
        {
            browserId = browserId?.ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(browserId)) return false;

            var possible = GetPossibleExecutableNames(browserId);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (var exe in possible)
                {
                    var (ok, outp, _) = await RunCommandCaptureAsync("where", exe);
                    if (ok && !string.IsNullOrWhiteSpace(outp)) return true;
                    // check common Program Files
                    var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    var candidates = new[]
                    {
                    Path.Combine(pf, exe),
                    Path.Combine(pf86, exe),
                    Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(pf86, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(pf, "Mozilla Firefox", "firefox.exe")
                };
                    if (candidates.Any(File.Exists)) return true;
                }
                return false;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                foreach (var exe in possible)
                {
                    var (ok, outp, _) = await RunCommandCaptureAsync("which", exe);
                    if (ok && !string.IsNullOrWhiteSpace(outp)) return true;
                    var candidates = new[]
                    {
                    $"/usr/bin/{exe}",
                    $"/usr/local/bin/{exe}",
                    $"/snap/bin/{exe}",
                    $"/opt/{exe}/{exe}"
                };
                    if (candidates.Any(File.Exists)) return true;
                }
                return false;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                foreach (var exe in possible)
                {
                    var appPaths = new[]
                    {
                    $"/Applications/{exe}.app",
                    $"/Applications/{exe} Browser.app",
                    $"/Applications/Google Chrome.app",
                    $"/Applications/Firefox.app"
                };
                    if (appPaths.Any(Directory.Exists)) return true;
                }
                return false;
            }

            return false;
        }

        private static IEnumerable<string> GetPossibleExecutableNames(string browserId)
        {
            switch (browserId.ToLowerInvariant())
            {
                case "chrome": return ["google-chrome", "google-chrome-stable", "chrome", "chrome.exe"];
                case "chromium": return ["chromium", "chromium-browser", "chromium.exe"];
                case "brave": return ["brave", "brave-browser", "brave.exe"];
                case "firefox": return ["firefox", "firefox.exe"];
                case "edge": return ["msedge", "edge", "microsoft-edge", "msedge.exe", "edge.exe"];
                case "opera": return ["opera", "opera.exe"];
                case "safari": return ["safari"];
                case "vivaldi": return ["vivaldi", "vivaldi-stable", "vivaldi.exe"];
                case "whale": return ["naver-whale", "whale", "naver", "whale.exe"];
                default: return [browserId];
            }
        }
    }
}