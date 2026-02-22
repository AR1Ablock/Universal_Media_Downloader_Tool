using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Downloader_Backend.Model;

namespace Downloader_Backend.Logic
{
    public partial class Utility(ILogger<Utility> logger, ProcessControl processControl)
    {

        private readonly ProcessControl _processControl = processControl;
        private readonly ILogger<Utility> _logger = logger;
        private static readonly string[] sourceArray = ["cookies", "login", "429", "too many requests", "sabr", "unable to download webpage"];


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

        /*         public void DeleteAllDownloadArtifacts(string outputPath)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(outputPath) ?? ".";
                        var baseName = Path.GetFileName(outputPath); // full file name
                        var filePrefix = baseName.Split('.')[0]; // just the GUID_name part

                        // match: .mp4.part, .part-FragX.part, .ytdl, etc.
                        var patterns = new[] {
                    $"{filePrefix}*.part*",        // main.part + .part-FragX.part
                    $"{filePrefix}*.ytdl",         // .ytdl meta
                    $"{filePrefix}*.temp",         // if yt-dlp leaves .temp
                    $"{filePrefix}*.m4a",          // if temp audio
                    $"{filePrefix}*.f*.*.part"     // old format_id naming
                };

                        Parallel.ForEach(patterns, pattern =>

                        {
                            foreach (var file in Directory.GetFiles(dir, pattern))
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
         */


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


        public void Run_Open_Media_Directory_Process(string fileName, string arguments)
        {
            Process? proc = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName, // for different os file managers.
                    Arguments = arguments, // arguments based on OS type.
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                proc = Process.Start(psi);
                // Wait briefly so the helper process can hand off to the OS
                proc?.WaitForExit(2000); 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open media directory: {Error}", ex.Message);
            }
            finally
            {
                proc?.Dispose(); // release resources
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


        public async Task<(bool success, string stdout, string stderr, bool loginRequired)> TryRunYtDlpAsync(
                    string[] args,
                    CancellationToken cancellationToken,
                    bool tryWithCookies = false)
        {
            Process? proc = null;

            // Local helper – fastest possible kill, Windows = instant, Linux = only when needed
            void SafeKillProcessTree(Process? process)
            {
                if (process == null || process.HasExited) return;

                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        // Built-in tree kill on Windows – extremely fast, no WMI query needed
                        process.Kill(entireProcessTree: true);
                    }
                    else
                    {
                        // Only Linux/macOS fallback – still parallelized in your existing KillProcessTree
                        var tree = _processControl.GetProcessTree(process.Id);
                        _processControl.KillProcessTree(tree);
                    }
                }
                catch (Exception ex)
                {
                    // Best-effort – we don't want a failed kill to crash the method
                    _logger.LogWarning(ex, "Failed to terminate yt-dlp process tree (PID {Pid})", process?.Id);
                }
            }

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

                foreach (var a in args)
                    psi.ArgumentList.Add(a);

                if (tryWithCookies)
                {
                    psi.ArgumentList.Add("--cookies-from-browser");
                    var browser = GetYtDlpCompatibleBrowser() ?? "chrome";
                    psi.ArgumentList.Add(browser);
                }

                proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start yt-dlp executable.");

                // Kick off reads immediately – they complete when the process exits
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

                // Wait for process exit (this is the only place cancellation can interrupt the actual work)
                await proc.WaitForExitAsync(cancellationToken);

                // Process has exited normally → reads are guaranteed complete or nearly complete
                var output = await stdoutTask;
                var error = await stderrTask;

                var success = proc.ExitCode == 0;

                // Faster contains checks – no ToLowerInvariant() allocation on potentially large strings
                var loginRequired = sourceArray.Any(term => error.Contains(term, StringComparison.OrdinalIgnoreCase));

                // Retry with cookies only once – recursive is fine (max depth = 2)
                if (!success && loginRequired && !tryWithCookies)
                {
                    // First attempt ended → clean up before retrying
                    SafeKillProcessTree(proc); // usually not needed (already exited), but safe
                    return await TryRunYtDlpAsync(args, cancellationToken, tryWithCookies: true);
                }

                return (success, output, error, loginRequired);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("yt-dlp operation cancelled by client (PID {Pid})", proc?.Id);
                SafeKillProcessTree(proc);
                return (false, "", "CANCELLED_BY_CLIENT", false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "yt-dlp unexpected error (PID {Pid})", proc?.Id);
                SafeKillProcessTree(proc);
                return (false, "", ex.Message, false);
            }
            finally
            {
                // Final safety net – ensures nothing is left behind even in weird edge cases
                SafeKillProcessTree(proc);
                proc?.Dispose();
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
