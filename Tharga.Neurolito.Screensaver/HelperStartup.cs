using Microsoft.Win32;

namespace Tharga.Neurolito.Screensaver;

/// <summary>
/// Makes sure the settings helper starts at this user's logon, and runs now: done by the screensaver each time it runs,
/// since an update replaces the screensaver but never the installer's logon entry.
/// </summary>
/// <remarks>
/// The logon entry must name a program, not the screensaver file: Windows opens a <c>.scr</c> as <c>"%1" /S</c>, so
/// <c>Neurolito.scr /user</c> started from the shell runs the screensaver instead of the helper. The user's own entry
/// names this per-version copy, which the screensaver rewrites when a new version runs. It is not made when the
/// machine's entry names a program, which the helper then starts from.
/// </remarks>
internal static class HelperStartup
{
    public const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValue = "NeurolitoScreensaverSettings";

    public static void Ensure()
    {
        try
        {
            var executable = Environment.ProcessPath;
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            using var machine = Registry.LocalMachine.OpenSubKey(RunKey);
            using var user = Registry.CurrentUser.CreateSubKey(RunKey);
            var before = user.GetValue(RunValue) as string;
            var after = UserEntry(machine?.GetValue(RunValue) as string, before, executable, localData);

            if (after == null && before != null) user.DeleteValue(RunValue, false);
            else if (after != null && after != before) user.SetValue(RunValue, after);

            if (after != null && !Mutex.TryOpenExisting(UserSettingsHelper.MutexName, out _)) OnDemandTask.Start(executable, "/user");
        }
        catch (Exception e)
        {
            Log.Write($"helper startup: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// The agent is no longer installed: the user's logon entry, which uninstalling cannot reach, is removed. The helper
    /// then ends, rather than look for an agent every few seconds for the whole session.
    /// </summary>
    public static bool Uninstalled()
    {
        try
        {
            using var agent = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Neurolito\Agent");
            if (agent?.GetValue("InstallFolder") is string folder && Directory.Exists(folder)) return false;

            using var user = Registry.CurrentUser.OpenSubKey(RunKey, true);
            user?.DeleteValue(RunValue, false);
            return true;
        }
        catch (Exception e)
        {
            Log.Write($"helper startup: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// What the user's logon entry should be: null for none. Left as it is when this is not the per-user copy, which
    /// would hold the install folder open for the whole session and keep the agent from updating.
    /// </summary>
    internal static string UserEntry(string machine, string user, string executable, string localData)
    {
        if (string.IsNullOrEmpty(executable) || string.IsNullOrEmpty(localData)) return user;

        var ours = Path.GetFullPath(Path.Combine(localData, "Neurolito")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(executable).StartsWith(ours, StringComparison.OrdinalIgnoreCase)) return user;

        if (!string.IsNullOrWhiteSpace(machine) && !NamesAScreensaver(machine)) return null;
        return $"\"{executable}\" /user";
    }

    /// <summary>The command's program is a <c>.scr</c>, which the shell runs as a screensaver whatever it is given.</summary>
    internal static bool NamesAScreensaver(string command)
    {
        var text = Environment.ExpandEnvironmentVariables(command ?? "").Trim();
        var program = text.StartsWith('"') ? text[1..].Split('"')[0] : text.Split(' ')[0];
        return program.EndsWith(".scr", StringComparison.OrdinalIgnoreCase);
    }
}
