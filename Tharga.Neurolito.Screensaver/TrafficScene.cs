namespace Tharga.Neurolito.Screensaver;

/// <summary>
/// What the screen shows, built from the agent's messages and a clock: the prompt arriving as words that fly in, the
/// answer growing as it is generated, a line about each finished job, and the agent's state. Every word is lorem ipsum;
/// only the amounts are real.
/// </summary>
/// <remarks>Driven by the time it is given rather than the wall clock, so it can be stepped in a test.</remarks>
internal sealed class TrafficScene
{
    /// <summary>The most words of one prompt that fly in; a longer prompt is shown by its count.</summary>
    public const int MaxIncoming = 160;

    /// <summary>The most words of an answer kept on screen; the oldest scroll away.</summary>
    public const int MaxAnswerWords = 600;

    /// <summary>Roughly how many tokens make a word in the languages these models answer in: 13 tokens to 10 words.</summary>
    public const double TokensPerWord = 1.3;

    public static readonly TimeSpan Flight = TimeSpan.FromSeconds(1.6);
    public static readonly TimeSpan LaunchWindow = TimeSpan.FromSeconds(1.2);
    public static readonly TimeSpan Fade = TimeSpan.FromSeconds(6);

    private readonly bool _screensaverRunning;
    private readonly string _version;
    private readonly Lock _lock = new();
    private readonly Random _random;
    private readonly List<FlyingWord> _incoming = [];
    private readonly List<string> _answer = [];

    private Lorem _lorem;
    private bool _connected;
    private AgentMessage _state;
    private long _tokens;
    private long _answerWords;
    private int _jobWordsIn;
    private TimeSpan? _answerDoneAt;
    private string _summary;
    private TimeSpan _summaryAt;
    private int _jobs;
    private long _wordsIn;
    private long _wordsOut;

    /// <param name="screensaverRunning">
    /// True when this is the screensaver Windows started because the machine is idle; false for the preview, which
    /// runs while someone is at the machine.
    /// </param>
    /// <param name="version">This screensaver's release, shown with the agent's so an update can be seen to have arrived.</param>
    public TrafficScene(bool screensaverRunning, int seed = 0, string version = null)
    {
        _screensaverRunning = screensaverRunning;
        _version = version;
        _random = new Random(seed);
        _lorem = new Lorem(seed);
    }

    public void SetConnected(bool connected)
    {
        lock (_lock)
        {
            _connected = connected;
            if (!connected) _state = null;
        }
    }

    public void Apply(AgentMessage message, TimeSpan now)
    {
        if (message == null) return;

        lock (_lock)
        {
            switch (message.Kind)
            {
                case AgentMessage.State:
                    _connected = true;
                    _state = message;
                    break;

                case AgentMessage.JobStart:
                    _jobWordsIn = message.Words;
                    _wordsIn += message.Words;
                    _answer.Clear();
                    _answerDoneAt = null;
                    _tokens = 0;
                    _answerWords = 0;
                    _lorem = new Lorem(_random.Next());
                    Launch(Math.Min(message.Words, MaxIncoming), now);
                    break;

                case AgentMessage.Tokens:
                    //NOTE: Counted from the total, in whole numbers, so rounding never drifts over a long answer.
                    _tokens += message.Count;
                    for (var target = _tokens * 10 / 13; _answerWords < target; _answerWords++) _answer.Add(_lorem.Next());

                    if (_answer.Count > MaxAnswerWords) _answer.RemoveRange(0, _answer.Count - MaxAnswerWords);
                    break;

                case AgentMessage.JobEnd:
                    _jobs++;
                    _wordsOut += message.Words;
                    _answerDoneAt = now;
                    _summary = $"{Words(_jobWordsIn)} in · {Words(message.Words)} out · {message.DurationMs / 1000:0.0} s";
                    _summaryAt = now;
                    break;
            }
        }
    }

    public SceneFrame Frame(TimeSpan now)
    {
        lock (_lock)
        {
            _incoming.RemoveAll(x => now - x.Launch > Flight);

            var answerAlpha = _answerDoneAt is { } done ? 1f - Clamp((float)((now - done) / Fade)) : 1f;
            if (answerAlpha <= 0) _answer.Clear();

            return new SceneFrame
            {
                Status = Status(),
                Connected = _connected && _state != null,
                Machine = _state?.Machine,
                Engine = _state?.Engine,
                Model = _state?.Model,
                Version = Version(),
                Incoming = _incoming
                    .Where(x => now >= x.Launch)
                    .Select(x => new FlyingWordFrame(x.Text, Clamp((float)((now - x.Launch) / Flight)), x.Lane))
                    .ToArray(),
                Answer = _answer.ToArray(),
                AnswerAlpha = Math.Max(0, answerAlpha),
                Summary = _summary,
                SummaryAlpha = _summary == null ? 0 : 1f - Clamp((float)((now - _summaryAt) / (Fade * 3))),
                Totals = _jobs == 0 ? null : $"{_jobs} {(_jobs == 1 ? "job" : "jobs")} · {Words(_wordsIn)} in · {Words(_wordsOut)} out since the screensaver started"
            };
        }
    }

    private void Launch(int words, TimeSpan now)
    {
        for (var i = 0; i < words; i++)
        {
            var delay = words <= 1 ? TimeSpan.Zero : LaunchWindow * ((double)i / (words - 1));
            _incoming.Add(new FlyingWord(_lorem.Next(), now + delay, (float)_random.NextDouble()));
        }
    }

    private string Status()
    {
        if (!_connected) return "The Neurolito agent is not running on this machine.";
        if (_state == null) return "Connecting to the agent…";
        if (_state.Working) return "Working";

        return _state.Presence switch
        {
            "Available" => "Ready - waiting for work",
            "Asleep" => "Going to sleep",
            _ when _screensaverRunning => "Getting ready - work starts in a moment",
            _ => "In use - the agent works while the screensaver runs"
        };
    }

    /// <summary>
    /// One number when the screensaver and the agent are the same release, which they are once an update has been applied;
    /// both while they differ - the service restarts on a new version when its running job is done.
    /// </summary>
    private string Version()
    {
        var agent = _connected ? _state?.Version : null;
        if (string.IsNullOrEmpty(_version)) return string.IsNullOrEmpty(agent) ? null : $"Agent {agent}";
        if (string.IsNullOrEmpty(agent) || agent == _version) return $"Version {_version}";
        return $"Screensaver {_version} · agent {agent}";
    }

    private static string Words(long count) => count == 1 ? "1 word" : $"{count:N0} words";

    private static float Clamp(float value) => value < 0 ? 0 : value > 1 ? 1 : value;

    private sealed record FlyingWord(string Text, TimeSpan Launch, float Lane);
}

/// <summary>A word on its way in: <see cref="Progress"/> from 0 at the edge to 1 where it lands; <see cref="Lane"/> its height, 0 to 1.</summary>
internal readonly record struct FlyingWordFrame(string Text, float Progress, float Lane);

/// <summary>Everything one frame draws.</summary>
internal sealed record SceneFrame
{
    public required string Status { get; init; }
    public bool Connected { get; init; }
    public string Machine { get; init; }
    public string Engine { get; init; }
    public string Model { get; init; }
    public string Version { get; init; }
    public IReadOnlyList<FlyingWordFrame> Incoming { get; init; } = [];
    public IReadOnlyList<string> Answer { get; init; } = [];
    public float AnswerAlpha { get; init; }
    public string Summary { get; init; }
    public float SummaryAlpha { get; init; }
    public string Totals { get; init; }
}
