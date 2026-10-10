using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace Tharga.Neurolito.Screensaver;

/// <summary>
/// The connection to the agent service's pipe. Says <c>idle</c> while the screensaver runs, so the agent takes work,
/// and <c>active</c> when it ends; feeds every message to the scene. Reconnects if the agent restarts.
/// </summary>
internal sealed class AgentLink : IDisposable
{
    public const string PipeName = "neurolito-agent";
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(2);

    private readonly TrafficScene _scene;
    private readonly Stopwatch _clock;
    private readonly bool _announceIdle;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private StreamWriter _writer;

    /// <param name="announceIdle">True for the screensaver proper; the preview must not tell the agent the machine is free.</param>
    public AgentLink(TrafficScene scene, Stopwatch clock, bool announceIdle)
    {
        _scene = scene;
        _clock = clock;
        _announceIdle = announceIdle;
        _loop = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
                await pipe.ConnectAsync(2000, _stop.Token);

                using var reader = new StreamReader(pipe, leaveOpen: true);
                _writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                if (_announceIdle) await _writer.WriteLineAsync("idle");

                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    _scene.Apply(AgentMessage.Parse(line), _clock.Elapsed);
                }
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
                Log.Write($"pipe: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                _writer = null;
                _scene.SetConnected(false);
            }

            try { await Task.Delay(Retry, _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        try
        {
            //NOTE: The agent also treats a dropped connection as active; saying it is quicker and plainer in its log.
            if (_announceIdle) _writer?.WriteLine("active");
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }

        _stop.Cancel();
        try { _loop.Wait(TimeSpan.FromSeconds(1)); } catch { /* stopping */ }
        _stop.Dispose();
    }
}
