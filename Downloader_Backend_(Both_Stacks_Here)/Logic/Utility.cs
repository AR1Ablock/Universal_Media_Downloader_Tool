using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Downloader_Backend.Model;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;

namespace Downloader_Backend.Logic
{
    public partial class Utility(ILogger<Utility> logger, ProcessControl processControl, File_Saver file_Saver)
    {

        private readonly ProcessControl _processControl = processControl;
        private readonly ILogger<Utility> _logger = logger;
        private readonly File_Saver _fileSaver = file_Saver;

        private static readonly HashSet<string> KnownAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
        // Core ones (YouTube, most sites)
        "mp3", "m4a", "opus", "ogg", "wav", "flac", "aac", "mpga", "weba", "mka",
    
        // Wikipedia + generic/HTML5MediaEmbed + DASH audio
        "webm", "oga",
    
        // Rare but real yt-dlp outputs on other sites
        "mp4", "3gp", "m4b",
        "aiff", "aif", "caf",
        "spx", "tta",
        "wma", "alac"
        };

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


        public DownloadJob Create_Download_Job(DownloadJob job, string status, string Method_Caller, CancellationTokenSource cts)
        {
            bool Is_From_Restart = Method_Caller == "Restart";
            bool Is_From_Broken_Resume = Method_Caller == "Broken_Resume";
            //
            long Downloaded_Size = Is_From_Restart ? 0 : job.Downloaded;
            double Progress = Is_From_Restart ? 0 : job.Progress;
            string Speed = Is_From_Restart ? "0 B" : job.Speed;
            string Error_Logs = Is_From_Broken_Resume ? job.ErrorLog : "nan"; 
            CancellationTokenSource CTS = job.TokenSource ?? cts;

            
            return new DownloadJob
            {
                Id = job.Id,
                Url = job.Url,
                Format = job.Format,
                Key = job.Key,
                Status = status,
                Method = "YT_DLP",
                Title = job.Title,
                Thumbnail = job.Thumbnail,
                Total = job.Total,
                //
                Downloaded = Downloaded_Size,
                Progress = Progress,
                Speed = Speed,
                //
                ErrorLog = Error_Logs,
                TokenSource = CTS,
                OutputPath = job.OutputPath,
                DownloadTask = null,
                ProcessTreePids = [],

            };
        }


        public void Log_pids_tree(DownloadJob job)
        {
            foreach (var item in job.ProcessTreePids)
            {
                _logger.LogInformation("----logging Killing Trees: {tree}", item);
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
            new Regex($@"^{Regex.Escape(filePrefix)}.*\.f.*\..*\.part$", RegexOptions.IgnoreCase),
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


        public (string ytDlp, string ffmpeg, string deno, string node) Local_Executables_Path()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;

                string osFolder;
                string archFolder;

                if (OperatingSystem.IsWindows())
                    osFolder = "windows";
                else if (OperatingSystem.IsLinux())
                    osFolder = "linux";
                else if (OperatingSystem.IsMacOS())
                    osFolder = "mac";
                else
                    throw new PlatformNotSupportedException("Unsupported OS");

                archFolder = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";

                string yt_dlp_executable;
                string ffmpeg_executable;
                string deno_executable;
                string node_executable;

                if (OperatingSystem.IsWindows())
                {
                    yt_dlp_executable = $"yt_dlp_win_{archFolder}.exe";
                    ffmpeg_executable = $"ffmpeg_win_{archFolder}.exe";
                    deno_executable = $"deno_win_{archFolder}.exe";
                    node_executable = $"node_win_{archFolder}.exe";
                }
                else if (OperatingSystem.IsLinux())
                {
                    yt_dlp_executable = $"yt_dlp_linux_{archFolder}";
                    ffmpeg_executable = $"ffmpeg_linux_{archFolder}";
                    deno_executable = $"deno_linux_{archFolder}";
                    node_executable = $"node_linux_{archFolder}";
                }
                else // macOS
                {
                    yt_dlp_executable = "yt_dlp_mac_universal";
                    ffmpeg_executable = "ffmpeg_mac_universal";
                    deno_executable = archFolder == "arm64" ? "deno_mac_arm64" : "deno_mac_x64";
                    node_executable = archFolder == "arm64" ? "node_mac_arm64" : "node_mac_x64";
                }

                var ytDlpPath = Path.Combine(baseDir, "tools", osFolder, archFolder, yt_dlp_executable);
                var ffmpegPath = Path.Combine(baseDir, "tools", osFolder, archFolder, ffmpeg_executable);
                var denoPath = Path.Combine(baseDir, "tools", osFolder, archFolder, deno_executable);
                var nodePath = Path.Combine(baseDir, "tools", osFolder, archFolder, node_executable);

                if (!File.Exists(ytDlpPath))
                    throw new FileNotFoundException($"yt-dlp executable not found at {ytDlpPath}");
                if (!File.Exists(ffmpegPath))
                    throw new FileNotFoundException($"ffmpeg executable not found at {ffmpegPath}");
                if (!File.Exists(denoPath))
                    throw new FileNotFoundException($"deno executable not found at {denoPath}");
                if (!File.Exists(nodePath))
                    throw new FileNotFoundException($"deno executable not found at {nodePath}");

                return (ytDlpPath, ffmpegPath, denoPath, nodePath);
            }
            catch (Exception ex)
            {
                _logger.LogError("error occured in local executable path method {ex}", ex.Message);
                throw new FileNotFoundException("a file not found in path: {ex}", ex.Message);
            }
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
                    Run_Open_Media_Directory_Process("cmd.exe", $"/c start explorer /select,\"{url}\"");
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
                { "MiB", 1 }, { "GiB", 1024 },
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



        public async Task<List<Format>> ParseStandardFormats(JsonElement root, string url, YT_Dlp_Strategy_Engine yt_Dlp_Strategy_Engine, CancellationToken cancellationToken)
        {

            JsonElement fmts;

            // Case 1: normal video
            if (root.TryGetProperty("formats", out var formatsElement))
            {
                fmts = formatsElement;
            }
            // Case 2: playlist (like Wikipedia)
            else if (root.TryGetProperty("entries", out var entries) &&
                     entries.ValueKind == JsonValueKind.Array &&
                     entries.GetArrayLength() > 0 &&
                     entries[0].TryGetProperty("formats", out var entryFormats))
            {
                fmts = entryFormats;
                root = entries[0]; // update root so title/thumbnail work
            }
            else
            {
                return [];
            }

            if (fmts.ValueKind != JsonValueKind.Array)
                return [];

            string thumbnail = root.TryGetProperty("thumbnail", out var thmbnail) && thmbnail.ValueKind == JsonValueKind.String
            ? thmbnail.GetString()!
            : ""; // default thumbnail

            string Title = root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
            ? title.GetString() ?? ""
            : ""; // default thumbnail

            if (string.IsNullOrEmpty(Title))
            {
                Title = await yt_Dlp_Strategy_Engine.GetTitle(url, cancellationToken) ?? "";
                if (!string.IsNullOrEmpty(Title))
                {
                    Path.Combine(_fileSaver.fileMap["Title_File"], "Title_File.txt");
                    File.WriteAllText(_fileSaver.fileMap["Title_File"], Title);
                }
            }
            else
            {
                Path.Combine(_fileSaver.fileMap["Title_File"], "Title_File.txt");
                File.WriteAllText(_fileSaver.fileMap["Title_File"], Title);
            }

            var formats = fmts.EnumerateArray()
        .AsParallel()   // Enable parallel processing
        .AsOrdered()    // Preserve original order in output
        .Select(f =>
            {
                // skip encrypted signatures
                if (f.TryGetProperty("signatureCipher", out _))
                    return null;

                string ext = SafeGetString(f, "ext");
                long fs = SafeGetInt64(f, "filesize");
                int height = SafeGetInt32(f, "height");
                string id = SafeGetString(f, "format_id");
                string note = SafeGetString(f, "format_note", "");
                string Protocol = SafeGetString(f, "protocol", "");
                string acodec = SafeGetString(f, "acodec", "none");
                string vcodec = SafeGetString(f, "vcodec", "none");
                string resolution = SafeGetString(f, "resolution", "");
                string videoExt = SafeGetString(f, "video_ext", "none");
                string audioExt = SafeGetString(f, "audio_ext", "none");


                if (!string.IsNullOrWhiteSpace(ext) && ext.Contains("mhtml"))
                    return null; // skip MHTML formats

                if (!string.IsNullOrWhiteSpace(note) && note.Contains("storyboard"))
                    return null; // skip storyboard formats


                if (height == 0 || string.IsNullOrWhiteSpace(resolution) || resolution == "unknown")
                {
                    string notes = SafeGetString(f, "format_note", "");
                    if (!string.IsNullOrWhiteSpace(notes) && notes.Contains('p'))
                    {
                        var m = Containe_resolution_Symbol_Regex().Match(notes);
                        if (m.Success) height = int.Parse(m.Groups[1].Value);
                        resolution = notes;
                    }
                    else
                    {
                        string fmtUrl = SafeGetString(f, "url", "");
                        var m = Resolution_regex().Match(fmtUrl);
                        if (m.Success)
                        {
                            height = int.Parse(m.Groups[1].Value);
                            resolution = height + "p";
                        }
                    }
                }

                string Slow_Or_Fast = "fast"; // default to fast
                if (!string.IsNullOrWhiteSpace(Protocol) && MyRegex_1().IsMatch(Protocol))
                    Slow_Or_Fast = "slow"; // HLS / m3u8 is considered slow


                long sizeInMb = fs / 1024 / 1024;
                string sizeLabel = sizeInMb == 0 ? "--MB" : $"{sizeInMb}MB";
                string label = $"{(resolution.Contains("audio only") ? "" : $"{height}p • ")}{sizeLabel} • {ext} • {Slow_Or_Fast} • {resolution}";

                // Treat null, empty, whitespace, or "none" as "none"
                bool IsNone(string? s) => string.IsNullOrWhiteSpace(s) || s == "none";

                bool hasVideo = !IsNone(vcodec) || !IsNone(videoExt);
                bool hasAudio = !IsNone(acodec) || !IsNone(audioExt);

                // Ultimate fallback for completely broken extractors
                if (!hasVideo && !hasAudio)
                {
                    if (KnownAudioExtensions.Contains(ext))
                        hasAudio = true;
                    else if (!string.IsNullOrWhiteSpace(ext))
                        hasVideo = true;   // safest default
                }

                bool isAudioOnly = hasAudio && !hasVideo;
                bool isVideoOnly = (hasVideo && !hasAudio) || (hasVideo && hasAudio);

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

        public async Task<List<Format>> ParseFacebookFormats(JsonElement root, string url, YT_Dlp_Strategy_Engine yt_Dlp_Strategy_Engine, CancellationToken cancellationToken)
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
                ? title.GetString() ?? ""
                : ""; // default thumbnail


            if (string.IsNullOrWhiteSpace(Title))
            {
                Title = await yt_Dlp_Strategy_Engine.GetTitle(url, cancellationToken) ?? "";
                if (!string.IsNullOrWhiteSpace(Title))
                {
                    Path.Combine(_fileSaver.fileMap["Title_File"], "Title_File.txt");
                    File.WriteAllText(_fileSaver.fileMap["Title_File"], Title);
                }
            }
            else
            {
                Path.Combine(_fileSaver.fileMap["Title_File"], "Title_File.txt");
                File.WriteAllText(_fileSaver.fileMap["Title_File"], Title);
            }


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
                if (_processControl.TryGetPid(process, out var pid))
                {
                    var tree = _processControl.GetProcessTree(pid);
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


        [GeneratedRegex(@"\.(\d{2,4})p\.", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
        private static partial Regex Resolution_regex();

        [GeneratedRegex(@"(\d+)p")]
        private static partial Regex Containe_resolution_Symbol_Regex();
    }
}
