using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Downloader_Backend.Logic
{
    // === Function that works for Windows and Linux ===
    public class PortKiller (ILogger<PortKiller> logger, ProcessControl processControl)
    {
        private readonly ILogger<PortKiller> _logger = logger;

        private readonly ProcessControl _processControl = processControl;

        public void KillProcessUsingPort(int port)
        {
            if (!IsPortInUse(port)) return;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c for /f \"tokens=5\" %a in ('netstat -aon ^| findstr :{port}') do taskkill /f /pid %a",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,           // ✅ Hides console window
                        WindowStyle = ProcessWindowStyle.Hidden // ✅ Ensures no console flash
                    };
                    using var proc = Process.Start(processInfo);
                    string output = proc!.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    _logger.LogInformation("Port kill output: " + output);
                    _logger.LogInformation("Port kill error: " + error);
                    var tree = _processControl.GetProcessTree(proc.Id);
                    _processControl.KillProcessTree(tree);
                    proc.Dispose();

                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                         RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"fuser -k {port}/tcp\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,           // ✅ Hides console window
                        WindowStyle = ProcessWindowStyle.Hidden // ✅ Ensures no console flash
                    };
                    using var proc = Process.Start(processInfo);
                    string output = proc!.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    _logger.LogInformation("Port kill output: " + output);
                    _logger.LogInformation("Port kill error: " + error);
                    var tree = _processControl.GetProcessTree(proc.Id);
                    _processControl.KillProcessTree(tree);
                    proc.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Error trying to free port {port}: {ex.Message}");
            }
        }

        private static bool IsPortInUse(int port)
        {
            bool inUse = false;
            try
            {
                TcpListener listener = new(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
            }
            catch (SocketException)
            {
                inUse = true;
            }
            return inUse;
        }
    }
}