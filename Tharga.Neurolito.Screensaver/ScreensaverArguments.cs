namespace Tharga.Neurolito.Screensaver;

/// <summary>How Windows started the screensaver.</summary>
internal enum ScreensaverMode
{
    /// <summary><c>/s</c>: full screen, because the machine is idle.</summary>
    Run,

    /// <summary><c>/p &lt;window&gt;</c>: inside the little monitor of the screen saver settings.</summary>
    Preview,

    /// <summary><c>/c</c>, or no argument: the Settings button.</summary>
    Settings
}

internal readonly record struct ScreensaverArguments(ScreensaverMode Mode, IntPtr Parent)
{
    /// <summary>
    /// <c>/s</c>, <c>/S</c>, <c>-s</c>, <c>/p 1234</c>, <c>/p:1234</c>, <c>/c</c>, <c>/c:1234</c>. Windows uses
    /// several spellings, and starts the screensaver with no argument when it is opened from Explorer.
    /// </summary>
    public static ScreensaverArguments Parse(string[] args)
    {
        if (args == null || args.Length == 0) return new ScreensaverArguments(ScreensaverMode.Settings, IntPtr.Zero);

        var first = args[0].Trim().TrimStart('/', '-').ToLowerInvariant();
        var colon = first.IndexOf(':');
        var handle = colon >= 0 ? first[(colon + 1)..] : args.Length > 1 ? args[1] : null;
        var parent = long.TryParse(handle, out var h) ? new IntPtr(h) : IntPtr.Zero;

        var mode = first.Length == 0 ? 'c' : first[0];
        return mode switch
        {
            's' => new ScreensaverArguments(ScreensaverMode.Run, IntPtr.Zero),
            'p' => new ScreensaverArguments(ScreensaverMode.Preview, parent),
            _ => new ScreensaverArguments(ScreensaverMode.Settings, parent)
        };
    }
}
