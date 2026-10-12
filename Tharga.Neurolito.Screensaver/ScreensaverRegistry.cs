using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Tharga.Neurolito.Screensaver;

/// <summary>The signed-in user's screensaver: read from their registry, changed the way Windows' own settings change it.</summary>
internal static class ScreensaverRegistry
{
    private const string DesktopKey = @"Control Panel\Desktop";
    private const string PolicyKey = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";
    private static readonly string[] Settings = ["ScreenSaveActive", "ScreenSaveTimeOut", "SCRNSAVE.EXE"];

    private const uint SetScreensaveTimeout = 0x000F;
    private const uint SetScreensaveActive = 0x0011;
    private const uint UpdateIniFile = 0x01;
    private const uint SendChange = 0x02;

    public static ScreensaverSetting Read()
    {
        using var desktop = Registry.CurrentUser.OpenSubKey(DesktopKey);
        var file = desktop?.GetValue("SCRNSAVE.EXE") as string;
        var active = $"{desktop?.GetValue("ScreenSaveActive")}" == "1" && !string.IsNullOrWhiteSpace(file);
        var timeout = int.TryParse($"{desktop?.GetValue("ScreenSaveTimeOut")}", out var seconds) ? seconds : 0;

        using var policy = Registry.CurrentUser.OpenSubKey(PolicyKey);
        var managed = policy != null && Settings.Any(x => policy.GetValue(x) != null);

        return new ScreensaverSetting(ScreensaverSetting.IsNeurolito(file, LongName), active, timeout, managed);
    }

    /// <summary>Makes the change, or says why not: null when it was made.</summary>
    public static string Apply(ScreensaverChange change)
    {
        if (change.Refusal(Read()) is { } refusal) return refusal;

        //NOTE: Through SystemParametersInfo rather than the registry alone, so Windows takes the new timeout at once.
        if (change.TimeoutSeconds is { } seconds && !SystemParametersInfo(SetScreensaveTimeout, (uint)seconds, IntPtr.Zero, UpdateIniFile | SendChange))
        {
            return $"Windows refused the timeout (error {Marshal.GetLastWin32Error()})";
        }

        if (change.Active is { } active && !SystemParametersInfo(SetScreensaveActive, active ? 1u : 0u, IntPtr.Zero, UpdateIniFile | SendChange))
        {
            return $"Windows refused to turn the screensaver {(active ? "on" : "off")} (error {Marshal.GetLastWin32Error()})";
        }

        return null;
    }

    private static string LongName(string path)
    {
        var buffer = new StringBuilder(1024);
        return GetLongPathName(path, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, int buffer);
}
