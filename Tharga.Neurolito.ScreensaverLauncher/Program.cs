using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

// Windows starts this as the screensaver (Neurolito.scr in System32). It starts the real screensaver, which is updated
// with the agent, from the agent's install folder - passing on /s, /p <window> or /c as they came - and ends with it.
// With /user, at logon, it starts the settings helper and ends at once.
//
// The real screensaver is run from a copy in the user's own folder, one per version. The agent updates itself while
// nobody uses the machine - which is when the screensaver runs - and the update replaces the install folder's current\
// as a whole, which Windows refuses while a program in it is running.

var folder = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Neurolito\Agent")?.GetValue("InstallFolder") as string;
var installed = folder == null ? null : Path.Combine(folder, "current", "screensaver", "Neurolito.exe");
if (installed == null || !File.Exists(installed)) return 1;

var screensaver = Copy.Of(installed) ?? installed;

var start = new ProcessStartInfo(screensaver) { UseShellExecute = false };
foreach (var argument in args) start.ArgumentList.Add(argument);

using var process = Process.Start(start);
if (process == null) return 1;

//NOTE: The settings helper (/user) lives for the whole session, and nothing ends it but the logoff, so the launcher need
//not stay beside it. Its per-version copy stays in use while it runs; the cleanup below skips a folder it cannot delete.
if (args.Length > 0 && args[0].Trim().TrimStart('/', '-').StartsWith('u')) return 0;

//NOTE: Tied to this process, so a screensaver Windows ends by ending the launcher does not stay behind on the screen.
Job.Add(process);
process.WaitForExit();
return process.ExitCode;

internal static class Copy
{
    /// <summary>
    /// The screensaver copied to %LOCALAPPDATA%\Neurolito\screensaver\&lt;version&gt;\, made once per version; older
    /// versions' copies are removed. Null when it cannot be made, and the installed one is run instead.
    /// </summary>
    public static string Of(string installed)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(installed).FileVersion ?? File.GetLastWriteTimeUtc(installed).Ticks.ToString();
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neurolito", "screensaver");
            var target = Path.Combine(root, version, "Neurolito.exe");

            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(installed).Length)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var partial = target + ".partial";
                File.Copy(installed, partial, true);
                File.Move(partial, target, true);
            }

            foreach (var old in Directory.EnumerateDirectories(root).Where(x => !string.Equals(Path.GetFileName(x), version, StringComparison.OrdinalIgnoreCase)))
            {
                try { Directory.Delete(old, true); }
                catch (IOException) { /* still running, or in use: next time */ }
                catch (UnauthorizedAccessException) { }
            }

            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

internal static class Job
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;
    private static SafeFileHandle _job;

    public static void Add(Process process)
    {
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job.IsInvalid) return;

        var info = new ExtendedLimit { Basic = new BasicLimit { LimitFlags = KillOnJobClose } };
        var size = Marshal.SizeOf<ExtendedLimit>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (SetInformationJobObject(_job, ExtendedLimitInformation, pointer, (uint)size)) AssignProcessToJobObject(_job, process.Handle);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
}
