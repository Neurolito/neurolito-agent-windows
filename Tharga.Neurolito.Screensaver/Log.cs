namespace Tharga.Neurolito.Screensaver;

/// <summary>A small log in <c>%LOCALAPPDATA%\Neurolito\screensaver.log</c>, for what goes wrong. A screensaver has nowhere else to say it.</summary>
internal static class Log
{
    private const long MaxBytes = 256 * 1024;
    private static readonly Lock Gate = new();
    private static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neurolito", "screensaver.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                if (System.IO.File.Exists(File) && new FileInfo(File).Length > MaxBytes) System.IO.File.Delete(File);
                System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // A screensaver must never fail because of its log.
        }
    }
}
