using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Win32;

namespace Tharga.Neurolito.Screensaver;

/// <summary>
/// Started with <c>/user</c> at every user's logon, without a window: tells the agent this user's screensaver, so it can
/// be shown on the agent's page on the web, and makes the changes asked for there - only to a Neurolito screensaver.
/// </summary>
/// <remarks>
/// The agent runs as a service and cannot read or change a user's own settings; this runs as the user, in their session.
/// It never says <c>idle</c> or <c>active</c>: it is not a screensaver, and the machine's presence is not its business.
/// </remarks>
internal sealed class UserSettingsHelper : IDisposable
{
    /// <summary>Held by the helper running in this session; another sees it and ends at once.</summary>
    public const string MutexName = @"Local\NeurolitoScreensaverSettings";

    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Check = TimeSpan.FromSeconds(15);

    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _write = new(1, 1);
    private Task _loop;

    /// <summary>Runs until the user logs off: one per session.</summary>
    public static void Run()
    {
        if (HelperStartup.Uninstalled()) return;

        using var single = new Mutex(true, MutexName, out var first);
        if (!first) return;

        using var helper = new UserSettingsHelper();
        helper._loop = Task.Run(helper.RunAsync);

        //NOTE: A message loop with no window, so Windows' session-ending notice arrives and the helper ends cleanly.
        SystemEvents.SessionEnding += (_, _) => Application.Exit();
        Application.Run();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", AgentLink.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
                await pipe.ConnectAsync(2000, _stop.Token);
                await ServeAsync(pipe);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // The agent is not running, or went away. Try again.
            }
            catch (Exception e)
            {
                Log.Write($"user settings: {e.GetType().Name}: {e.Message}");
            }

            try { await Task.Delay(Retry, _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ServeAsync(NamedPipeClientStream pipe)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);

        var last = ScreensaverRegistry.Read();
        await SendAsync(writer, last, null);

        //NOTE: Changed in Windows' own settings, or by policy: said within a check of it.
        var watch = Task.Run(async () =>
        {
            while (!connection.IsCancellationRequested)
            {
                await Task.Delay(Check, connection.Token);
                var now = ScreensaverRegistry.Read();
                if (now == last) continue;
                last = now;
                await SendAsync(writer, now, null);
            }
        }, CancellationToken.None);

        try
        {
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                if (UserSettingsProtocol.ParseSet(line) is not { } change) continue;

                var error = ScreensaverRegistry.Apply(change);
                if (error != null) Log.Write($"user settings: not changed: {error}.");
                last = ScreensaverRegistry.Read();
                await SendAsync(writer, last, error);
            }
        }
        finally
        {
            await connection.CancelAsync();
            try { await watch; } catch { /* the connection is gone */ }
        }
    }

    private async Task SendAsync(StreamWriter writer, ScreensaverSetting setting, string error)
    {
        await _write.WaitAsync();
        try
        {
            await writer.WriteLineAsync(UserSettingsProtocol.ReportLine(setting, error));
        }
        finally
        {
            _write.Release();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { /* stopping */ }
        _stop.Dispose();
        _write.Dispose();
    }
}
