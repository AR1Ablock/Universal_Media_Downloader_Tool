using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Downloader_Backend.Model;

namespace Downloader_Backend.Logic
{
    public partial class ProcessControl(ILogger<ProcessControl>? logger)
    {
        private readonly ILogger<ProcessControl>? _logger = logger;



        public bool ProcessExists(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch { return false; }
        }


        public bool TryGetPid(Process? process, out int pid)
        {
            pid = 0;
            if (process == null) return false;

            try
            {
                pid = process.Id;
                return true;
            }
            catch
            {
                return false;
            }
        }


        public void Suspend(DownloadJob job)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    Parallel.ForEach(job.ProcessTreePids, SuspendProcessAndChildren);
                }
                catch (Exception ex)
                {
                    _logger?.LogInformation("------Error suspending processes. " + ex.Message);
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                Process? killProc = null;
                try
                {
                    Parallel.ForEach(job.ProcessTreePids, pid =>
                    {
                        if (!ProcessExists(pid)) return;

                        var tree = GetProcessTree(pid); // now we use the correct root
                        foreach (var p in tree.OrderByDescending(x => x)) // children first is safer
                        {
                            if (ProcessExists(p))
                            {
                                _logger?.LogInformation("Suspending the pid: {pid}", p);
                                killProc = Process.Start("kill", $"-STOP {p}");
                                killProc?.WaitForExit(10);
                                // now kill the command below.
                                if (killProc != null)
                                {
                                    var proc_tree = GetProcessTree(killProc.Id);
                                    KillProcessTree(proc_tree); // kill the process tree
                                    killProc?.Dispose(); // ensure the process is disposed
                                }
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    if (killProc != null)
                    {
                        killProc.WaitForExit(50);
                        killProc.Dispose();
                    }
                    _logger?.LogInformation("------Error suspending processes. " + ex.Message);
                }
            }
        }



        public void Resume(DownloadJob job)
        {
            if (OperatingSystem.IsWindows())
            {

                try
                {
                    Parallel.ForEach(job.ProcessTreePids, ResumeProcessAndChildren);
                }
                catch (Exception ex)
                {
                    _logger?.LogInformation("------Error resuming processes. " + ex.Message);
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                Process? Resume_Proc = null;
                try
                {
                    Parallel.ForEach(job.ProcessTreePids, pid =>
                    {
                        if (!ProcessExists(pid)) return;

                        var tree = GetProcessTree(pid); // now we use the correct root
                        foreach (var p in tree.OrderByDescending(x => x)) // children first is safer
                        {
                            if (ProcessExists(p))
                            {
                                _logger?.LogInformation("Resuming the pid: {pid}", p);
                                Resume_Proc = Process.Start("kill", $"-CONT {p}");
                                Resume_Proc?.WaitForExit(10);
                                // now kill the command below.
                                if (Resume_Proc != null)
                                {
                                    var proc_tree = GetProcessTree(Resume_Proc.Id);
                                    KillProcessTree(proc_tree); // kill the process tree
                                    Resume_Proc?.Dispose(); // ensure the process is disposed
                                }
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    if (Resume_Proc != null)
                    {
                        Resume_Proc.WaitForExit(50);
                        Resume_Proc.Dispose();
                    }
                    _logger?.LogInformation("------Error Resuming processes. " + ex.Message);
                }

            }
        }


        public List<int> GetProcessTree(int rootPid)
        {
            if(rootPid <= 0 ) return [];
            var pids = new ConcurrentBag<int> { rootPid };  // Start with root

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var searcher = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessId={rootPid}");
                    var children = searcher.Get().Cast<ManagementObject>();

                    Parallel.ForEach(children, obj =>
                    {
                        var childPid = Convert.ToInt32(obj["ProcessId"]);
                        var childTree = GetProcessTree(childPid);  // Recurse
                        foreach (var pid in childTree)
                        {
                            pids.Add(pid);  // Thread-safe add
                        }
                    });
                }
                catch
                {
                    _logger?.LogInformation("-----Error fetching process tree for PID: " + rootPid);
                }
            }
            else  // Linux or macOS (no change needed, as it's sequential)
            {
                Process? proc = null;
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "pgrep",
                        Arguments = $"-P {rootPid}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(); // here we dont need to add ms due to this operation is longer.
                        Parallel.ForEach(output.Split('\n', StringSplitOptions.RemoveEmptyEntries), line =>
                        {
                            if (int.TryParse(line.Trim(), out int childPid))
                            {
                                var childTree = GetProcessTree(childPid);
                                foreach (var pid in childTree)
                                {
                                    pids.Add(pid);  // Safe even if you parallelize this later
                                }
                            }
                        });
                    }
                }
                catch
                {
                    _logger?.LogInformation("Error fetching process tree for PID: " + rootPid);
                }
                finally
                {
                    proc?.Dispose();
                }
            }

            return [.. pids.Distinct()];  // Convert to List with distinct
        }



        public List<int> GetAlivePids(List<int> pids)
        {
            var alive = new ConcurrentBag<int>();  // Start empty, collect alive PIDs

            Parallel.ForEach(pids, pid =>
            {
                try
                {
                    if (ProcessExists(pid))
                        alive.Add(pid);
                }
                catch (ArgumentException)
                {
                    _logger?.LogInformation($"PID {pid} does not exist.");
                }
                catch (InvalidOperationException)
                {
                    _logger?.LogInformation($"PID {pid} already exited.");
                }
                catch (Exception ex)
                {
                    _logger?.LogInformation($"PID {pid} error: {ex.Message}");
                }
            });

            return [.. alive];
        }



        public void KillProcessTree(List<int> pids)
        {
            var alivePids = GetAlivePids(pids);

            if (alivePids.Count == 0)
            {
                _logger?.LogInformation("No alive PIDs to kill.");
                return;
            }

            _logger?.LogInformation("Alive PIDs to kill: " + string.Join(", ", alivePids));

            Parallel.ForEach(alivePids, pid =>
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (ProcessExists(pid))
                    {
                        _logger?.LogInformation("killing process tree: {proc}", pid);
                        proc.Kill(entireProcessTree: true); // Kill tree on Windows; single process on Unix (loop handles all)
                        proc.WaitForExit(50);
                        proc.Dispose();
                    }
                }
                catch (ArgumentException)
                {
                    _logger?.LogInformation("Process with PID " + pid + " does not exist.");
                }
                catch (InvalidOperationException)
                {
                    _logger?.LogInformation("Process with PID " + pid + " has already exited.");
                }
                catch (Exception ex)
                {
                    _logger?.LogInformation($"Failed to kill PID {pid}: {ex.Message}");
                }
            });
        }



        // === WINDOWS-ONLY HELPERS ===
        private void SuspendProcessAndChildren(int pid)
        {
            Parallel.ForEach(EnumerateThreadIds(pid), SuspendThreadById);
        }

        private void ResumeProcessAndChildren(int pid)
        {
            Parallel.ForEach(EnumerateThreadIds(pid), ResumeThreadById);
        }

        private static IEnumerable<int> EnumerateThreadIds(int owningProcessId)
        {
            var tids = new List<int>();
            var snapshot = CreateToolhelp32Snapshot(SnapshotFlags.Thread, 0);
            if (snapshot == IntPtr.Zero) yield break;

            var tEntry = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };
            if (Thread32First(snapshot, ref tEntry))
            {
                do
                {
                    if (tEntry.th32OwnerProcessID == (uint)owningProcessId)
                        tids.Add((int)tEntry.th32ThreadID);
                }
                while (Thread32Next(snapshot, ref tEntry));
            }
            CloseHandle(snapshot);

            foreach (var tid in tids) yield return tid;
        }


        private void SuspendThreadById(int threadId)
        {
            var h = OpenThread(ThreadAccess.SUSPEND_RESUME, false, threadId);
            if (h == IntPtr.Zero)
                _logger?.LogInformation($"[Suspend] OpenThread({threadId}) failed: {Marshal.GetLastWin32Error()}");
            else
            {
                uint prev = SuspendThread(h);
                _logger?.LogInformation($"[Suspend] TID={threadId}, PrevCount={prev}");
                CloseHandle(h);
            }
        }


        private void ResumeThreadById(int threadId)
        {
            var h = OpenThread(ThreadAccess.SUSPEND_RESUME, false, threadId);
            if (h == IntPtr.Zero)
                _logger?.LogInformation($"[Resume] OpenThread({threadId}) failed: {Marshal.GetLastWin32Error()}");
            else
            {
                // loop until fully resumed
                uint cnt;
                do { cnt = ResumeThread(h); }
                while (cnt > 0);
                _logger?.LogInformation($"[Resume] TID={threadId} resumed");
                CloseHandle(h);
            }
        }


        // === P/Invoke and structs ===
        [Flags]
        private enum SnapshotFlags : uint
        {
            Thread = 0x00000004
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct THREADENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ThreadID;
            public uint th32OwnerProcessID;
            public int tpBasePri;
            public int tpDeltaPri;
            public uint dwFlags;
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr CreateToolhelp32Snapshot(SnapshotFlags dwFlags, uint th32ProcessID);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr OpenThread(ThreadAccess dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwThreadId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial uint SuspendThread(IntPtr hThread);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial uint ResumeThread(IntPtr hThread);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(IntPtr hObject);

        [Flags]
        private enum ThreadAccess : uint
        {
            TERMINATE = 0x0001,
            SUSPEND_RESUME = 0x0002,
            GET_CONTEXT = 0x0008,
            SET_CONTEXT = 0x0010,
            SET_INFORMATION = 0x0020,
            QUERY_INFORMATION = 0x0040,
            SET_THREAD_TOKEN = 0x0080,
            IMPERSONATE = 0x0100,
            DIRECT_IMPERSONATION = 0x0200
        }
    }
}