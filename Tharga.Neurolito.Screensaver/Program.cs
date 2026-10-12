using System.Diagnostics;
using System.Reflection;

namespace Tharga.Neurolito.Screensaver;

/// <summary>
/// The Neurolito screensaver: a view onto the agent service running on this machine. While it runs it tells the agent
/// the machine is free, and shows the agent's work as lorem ipsum - how much text arrives and leaves, never the text.
/// </summary>
internal static class Program
{
    /// <summary>The release this screensaver is, without the commit it was built from.</summary>
    private static readonly string Version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];

    [STAThread]
    private static void Main(string[] args)
    {
        var arguments = ScreensaverArguments.Parse(args);
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log.Write($"ui: {e.Exception}");

        try
        {
            switch (arguments.Mode)
            {
                case ScreensaverMode.Run:
                    Run();
                    break;
                case ScreensaverMode.Preview when arguments.Parent != IntPtr.Zero:
                    Preview(arguments.Parent);
                    break;
                case ScreensaverMode.Preview:
                    break;
                case ScreensaverMode.UserSettings:
                    UserSettingsHelper.Run();
                    break;
                default:
                    Settings();
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Write($"{arguments.Mode}: {e}");
        }
    }

    private static void Run()
    {
        var clock = Stopwatch.StartNew();
        var scene = new TrafficScene(screensaverRunning: true, seed: Environment.TickCount, version: Version);
        using var link = new AgentLink(scene, clock, announceIdle: true);

        //NOTE: Every monitor shows the scene; closing any - a key or a mouse move on any screen - ends them all.
        var forms = Screen.AllScreens.Select(s => new SaverForm(scene, () => clock.Elapsed, s.Bounds)).ToList();
        foreach (var form in forms) form.FormClosed += (_, _) => Application.Exit();
        foreach (var form in forms) form.Show();
        Application.Run();
    }

    private static void Preview(IntPtr parent)
    {
        var clock = Stopwatch.StartNew();
        var scene = new TrafficScene(screensaverRunning: false, seed: Environment.TickCount, version: Version);
        using var link = new AgentLink(scene, clock, announceIdle: false);
        Application.Run(new SaverForm(scene, () => clock.Elapsed, parent));
    }

    private static void Settings()
    {
        MessageBox.Show(
            "The Neurolito screensaver shows what the Neurolito agent on this computer is doing, as lorem ipsum: how much " +
            "text arrives and leaves, never the text itself.\n\n" +
            "While it runs, the agent takes work. When you come back, the agent finishes the job it is running and takes no " +
            "new one until the screensaver starts again.\n\n" +
            "There is nothing to set here. Choose when the screensaver starts in Windows, or on this machine's page on the " +
            "Neurolito web, which shows the screensaver and the power settings and can change them.",
            $"Neurolito screensaver {Version}", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
