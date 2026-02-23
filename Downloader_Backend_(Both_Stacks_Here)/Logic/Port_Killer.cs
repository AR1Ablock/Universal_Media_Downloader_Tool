using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Downloader_Backend.Logic
{
    /// <summary>
    /// Completely robust, production-ready PortKiller.
    /// - Pure .NET port detection (IPv4 + IPv6)
    /// - Best-in-class PID detection per OS (PowerShell + ss + lsof fallbacks)
    /// - Uses Environment.ProcessId for perfect self-detection
    /// - Graceful kill (SIGTERM → SIGKILL / taskkill graceful → force)
    /// - Post-kill verification + timeout
    /// - Works on Windows, Linux, macOS (including Docker/Alpine/minimal images)
    /// </summary>
    public partial class PortKiller(ILogger<PortKiller> logger, Utility utility)
    {
        private readonly ILogger<PortKiller> _logger = logger;
        private readonly Utility _utility = utility;

        private static readonly TimeSpan KillGracePeriod = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan PortFreeTimeout = TimeSpan.FromSeconds(6);

        /// <summary>
        /// Ensures the port is free. Returns true if port is now available.
        /// </summary>
        public bool EnsurePortAvailable(int port)
        {
            try
            {
                if (!IsPortInUse(port))
                {
                    _logger.LogInformation("Port {Port} is already free.", port);
                    return true;
                }

                var pid = GetPidUsingPort(port);
                if (pid <= 0)
                {
                    _logger.LogWarning("Port {Port} is in use but PID could not be determined.", port);
                    return false;
                }

                if (pid == Environment.ProcessId)
                {
                    _logger.LogInformation("Port {Port} is used by our own backend (PID {Pid}). Skipping kill.", port, pid);
                    return true;
                }

                _logger.LogWarning("Port {Port} is occupied by external process (PID {Pid}). Freeing it now...", port, pid);

                if (KillProcessGracefully(pid) && WaitForPortToBeFree(port))
                {
                    _logger.LogInformation("✅ Port {Port} is now free.", port);
                    return true;
                }

                _logger.LogError("Failed to free port {Port} after killing process.", port);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EnsurePortAvailable failed for port {Port}", port);
                return false;
            }
        }

        private bool IsPortInUse(int port)
        {
            try
            {
                var properties = IPGlobalProperties.GetIPGlobalProperties();
                var listeners = properties.GetActiveTcpListeners();

                return listeners.Any(l => l.Port == port &&
                    (l.Address.Equals(IPAddress.Any) ||
                     l.Address.Equals(IPAddress.Loopback) ||
                     l.Address.Equals(IPAddress.IPv6Any) ||
                     l.Address.Equals(IPAddress.IPv6Loopback)));
            }
            catch
            {
                // Ultra-safe fallback (original method)
                try
                {
                    using var listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return false;
                }
                catch (SocketException) { return true; }
            }
        }

        private int GetPidUsingPort(int port)
        {
            try
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? GetPidWindows(port)
                    : GetPidUnix(port);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to detect PID for port {Port}", port);
                return -1;
            }
        }

        private int GetPidWindows(int port)
        {
            // Primary method - most reliable on Windows 10/11/Server
            string psOutput = _utility.Run_Open_Media_Directory_Process(
                "powershell.exe",
                $"-NoProfile -NonInteractive -Command \"Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess\"");

            if (int.TryParse(psOutput.Trim(), out int pid) && pid > 0)
                return pid;

            // Fallback - netstat (still works everywhere)
            string netstatOutput = _utility.Run_Open_Media_Directory_Process(
                "cmd.exe", $"/c netstat -ano | findstr \":{port} \"");

            var match = MyRegex().Match(netstatOutput);
            return match.Success && int.TryParse(match.Groups[1].Value, out pid) ? pid : -1;
        }

        private int GetPidUnix(int port)
        {
            // 1. Linux: ss is fastest and always present on modern distros
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string ssOutput = _utility.Run_Open_Media_Directory_Process("/bin/sh", $"-c \"ss -ltnp 'sport = :{port}' 2>/dev/null | grep -oP '(?<=pid=)[0-9]+' | head -n1\"");

                if (int.TryParse(ssOutput.Trim(), out int pid) && pid > 0)
                    return pid;
            }

            // 2. macOS + Linux fallback: lsof (most universal)
            string lsofOutput = _utility.Run_Open_Media_Directory_Process("/bin/sh", $"-c \"lsof -iTCP:{port} -sTCP:LISTEN -t 2>/dev/null | head -n1\"");

            return int.TryParse(lsofOutput.Trim(), out int pid2) && pid2 > 0 ? pid2 : -1;
        }


        /// <summary>
        /// True ONLY if OUR backend is reachable on localhost:5050.
        /// Follows your exact logic: localhost-connect first → then PID + name check.
        /// Handles "Downloade" Linux truncation, external processes, self-detection.
        /// </summary>
        public bool Is_Our_Backend_Running(int port = 5050)
        {
            try
            {
                // Step 1: Can localhost actually connect? (critical for Vue/browser)
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", port);

                if (!connectTask.Wait(900)) // 900ms is reliable even on slow machines
                {
                    _logger.LogDebug("Port {Port} is NOT reachable from localhost → not our backend.", port);
                    return false;
                }

                _logger.LogDebug("Port {Port} is reachable from localhost → checking owner...", port);

                // Step 2: Something is listening on localhost → who owns it?
                var pid = GetPidUsingPort(port);
                if (pid <= 0)
                {
                    _logger.LogWarning("Port {Port} reachable but PID detection failed.", port);
                    return false;
                }

                if (pid == Environment.ProcessId)
                {
                    _logger.LogInformation("Our current instance is using port {Port}.", port);
                    return true;
                }

                // Step 3: Check process name (handles Linux 15-char truncation "Downloade")
                using var process = Process.GetProcessById(pid);
                string name = process.ProcessName.ToLowerInvariant().Trim();

                _logger.LogDebug("Port {Port} owner PID {Pid} → ProcessName: {Name}", port, pid, name);

                bool isOurs = name.Contains("downloade") ||        // matches your lsof output
                              name.Contains("downloader") ||
                              name.Contains("mediadownloader") ||
                              name.Contains("media_downloader") ||
                              name.Contains("downloaderbackend") ||
                              name.Contains("downloader_backend");

                if (isOurs)
                    _logger.LogInformation("✅ Our backend is already running on localhost:{Port} (PID {Pid})", port, pid);
                else
                    _logger.LogWarning("⚠️ Foreign process using localhost:{Port} (PID {Pid}, Name: {Name})", port, pid, name);

                return isOurs;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fully check backend on port {Port}", port);
                return false;
            }
        }



        private bool KillProcessGracefully(int pid)
        {
            try
            {
                _logger.LogInformation("Attempting to kill PID {Pid}...", pid);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows: try graceful first, then force
                    _utility.Run_Open_Media_Directory_Process("cmd.exe", $"/c taskkill /PID {pid}");
                    if (WaitForProcessExit(pid, KillGracePeriod))
                        return true;

                    _logger.LogWarning("Graceful kill failed on Windows, using force...");
                    _utility.Run_Open_Media_Directory_Process("cmd.exe", $"/c taskkill /F /PID {pid}");
                }
                else
                {
                    // Unix: SIGTERM → wait → SIGKILL
                    _utility.Run_Open_Media_Directory_Process("/bin/sh", $"-c \"kill {pid}\"");
                    if (WaitForProcessExit(pid, KillGracePeriod))
                        return true;

                    _logger.LogWarning("Process {Pid} did not exit gracefully, sending SIGKILL...", pid);
                    _utility.Run_Open_Media_Directory_Process("/bin/sh", $"-c \"kill -9 {pid}\"");
                }

                return WaitForProcessExit(pid, TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to kill PID {Pid}", pid);
                return false;
            }
        }

        private bool WaitForProcessExit(int pid, TimeSpan timeout)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return p.WaitForExit((int)timeout.TotalMilliseconds);
            }
            catch (ArgumentException)
            {
                return true; // already gone
            }
            catch
            {
                return false;
            }
        }

        private bool WaitForPortToBeFree(int port)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < PortFreeTimeout)
            {
                if (!IsPortInUse(port))
                    return true;

                Thread.Sleep(400);
            }
            return false;
        }

        [GeneratedRegex(@"LISTENING\s+(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline, "en-US")]
        private static partial Regex MyRegex();
    }
}
