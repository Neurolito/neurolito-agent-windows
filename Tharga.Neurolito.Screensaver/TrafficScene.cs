namespace Tharga.Neurolito.Screensaver;

/// <summary>
/// What the screen shows, built from the agent's messages and a clock: the prompt arriving as words that fly in, its
/// letters swirling while the model reads it, the answer growing as it is generated, a line about each finished job,
/// the agent's state, a model loading or unloading, the machine's work by the hour and the queue it could take from.
/// Every word is lorem ipsum; only the amounts are real.
/// </summary>
/// <remarks>Driven by the time it is given rather than the wall clock, so it can be stepped in a test.</remarks>
internal sealed class TrafficScene
{
    /// <summary>The most words of one prompt that fly in; a longer prompt is shown by its count.</summary>
    public const int MaxIncoming = 160;

    /// <summary>The most words of an answer kept on screen; the oldest scroll away.</summary>
    public const int MaxAnswerWords = 600;

    /// <summary>The most letters that swirl between the prompt and the answer.</summary>
    public const int MaxSwirl = 240;

    /// <summary>Roughly how many tokens make a word in the languages these models answer in: 13 tokens to 10 words.</summary>
    public const double TokensPerWord = 1.3;

    public static readonly TimeSpan Flight = TimeSpan.FromSeconds(1.6);
    public static readonly TimeSpan LaunchWindow = TimeSpan.FromSeconds(1.2);
    public static readonly TimeSpan Fade = TimeSpan.FromSeconds(6);

    /// <summary>How long a landed letter takes to join the swirl, and to leave it for the answer once the first token comes.</summary>
    public static readonly TimeSpan Gather = TimeSpan.FromSeconds(1.2);
    public static readonly TimeSpan Release = TimeSpan.FromSeconds(0.9);

    /// <summary>Where the swirl turns and where the answer begins, as shares of the screen's width and height.</summary>
    public const float SwirlX = 0.435f;
    public const float SwirlY = 0.61f;
    public const float AnswerX = 0.47f;
    public const float AnswerY = 0.32f;
    public const float LandingX = 0.40f;
    public const float LanesTop = 0.32f;
    public const float LanesHeight = 0.584f;

    private readonly bool _screensaverRunning;
    private readonly string _version;
    private readonly Lock _lock = new();
    private readonly Random _random;
    private readonly List<FlyingWord> _incoming = [];
    private readonly List<string> _answer = [];
    private readonly List<SwirlLetter> _swirl = [];

    private Lorem _lorem;
    private bool _connected;
    private AgentMessage _state;
    private long _tokens;
    private long _answerWords;
    private int _jobWordsIn;
    private TimeSpan? _answerDoneAt;
    private TimeSpan? _releasedAt;
    private string _summary;
    private TimeSpan _summaryAt;
    private int _jobs;
    private long _wordsIn;
    private long _wordsOut;
    private double _workUnits;
    private (string Model, TimeSpan Since)? _loading;
    private (string Text, TimeSpan At)? _modelNote;
    private AgentMessage _work;
    private AgentMessage _queue;

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
            if (!connected)
            {
                _state = null;
                _loading = null;
                _queue = null;
            }
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
                    _swirl.Clear();
                    _answerDoneAt = null;
                    _releasedAt = null;
                    _tokens = 0;
                    _answerWords = 0;
                    _lorem = new Lorem(_random.Next());
                    Launch(Math.Min(message.Words, MaxIncoming), now);
                    break;

                case AgentMessage.Tokens:
                    //NOTE: The first token: the model has read the prompt, so the letters leave the swirl for the answer.
                    if (_tokens == 0) Released(now);

                    //NOTE: Counted from the total, in whole numbers, so rounding never drifts over a long answer.
                    _tokens += message.Count;
                    for (var target = _tokens * 10 / 13; _answerWords < target; _answerWords++) _answer.Add(_lorem.Next());

                    if (_answer.Count > MaxAnswerWords) _answer.RemoveRange(0, _answer.Count - MaxAnswerWords);
                    break;

                case AgentMessage.JobEnd:
                    Released(now);
                    _jobs++;
                    _wordsOut += message.Words;
                    _workUnits += message.WorkUnits ?? 0;
                    _answerDoneAt = now;
                    _summary = $"{Words(_jobWordsIn)} in · {Words(message.Words)} out · {message.DurationMs / 1000:0.0} s"
                               + (message.WorkUnits is { } wu ? $" · {WorkUnitsText(wu)} WU" : "");
                    _summaryAt = now;
                    break;

                case AgentMessage.Model when message.Change == AgentMessage.Loading:
                    _loading = (message.ModelName, now);
                    break;

                case AgentMessage.Model when message.Change == AgentMessage.Loaded:
                    _modelNote = _loading is { } loading && string.Equals(loading.Model, message.ModelName, StringComparison.OrdinalIgnoreCase)
                        ? ($"Loaded {message.ModelName} in {(now - loading.Since).TotalSeconds:0.0} s", now)
                        : ($"Loaded {message.ModelName}", now);
                    _loading = null;
                    break;

                case AgentMessage.Model when message.Change == AgentMessage.Unloaded:
                    _modelNote = ($"Unloaded {message.ModelName} - its memory is free again", now);
                    break;

                case AgentMessage.Work:
                    _work = message;
                    break;

                case AgentMessage.Queue:
                    _queue = message;
                    break;
            }
        }
    }

    public SceneFrame Frame(TimeSpan now)
    {
        lock (_lock)
        {
            _incoming.RemoveAll(x => now - x.Launch > Flight);
            if (_releasedAt is { } released && now - released > Release + TimeSpan.FromSeconds(0.6)) _swirl.Clear();

            var answerAlpha = _answerDoneAt is { } done ? 1f - Clamp((float)((now - done) / Fade)) : 1f;
            if (answerAlpha <= 0) _answer.Clear();

            return new SceneFrame
            {
                Status = Status(),
                Connected = _connected && _state != null,
                Machine = _state?.Machine,
                Engine = _state?.Engine,
                Model = _state?.ModelName,
                Version = Version(),
                ModelActivity = ModelActivity(now),
                ModelNote = _modelNote?.Text,
                ModelNoteAlpha = _modelNote is { } note ? 1f - Clamp((float)((now - note.At) / (Fade * 2))) : 0,
                InMemory = _connected && _state?.LoadedModels is { Count: > 0 } loaded ? $"In memory: {string.Join(", ", loaded)}" : null,
                Incoming = _incoming
                    .Where(x => now >= x.Launch)
                    .Select(x => new FlyingWordFrame(x.Text, Clamp((float)((now - x.Launch) / Flight)), x.Lane))
                    .ToArray(),
                Swirl = SwirlFrame(now),
                Answer = _answer.ToArray(),
                AnswerAlpha = Math.Max(0, answerAlpha),
                Summary = _summary,
                SummaryAlpha = _summary == null ? 0 : 1f - Clamp((float)((now - _summaryAt) / (Fade * 3))),
                Totals = _jobs == 0 ? null : $"{_jobs} {(_jobs == 1 ? "job" : "jobs")} · {Words(_wordsIn)} in · {Words(_wordsOut)} out"
                                             + (_workUnits > 0 ? $" · {WorkUnitsText(_workUnits)} WU" : "") + " since the screensaver started",
                WorkToday = _work == null ? null : $"{WorkUnitsText(_work.WorkUnitsToday)} WU today · {_work.JobsToday} {(_work.JobsToday == 1 ? "job" : "jobs")}",
                WorkWeek = _work == null ? null : $"{WorkUnitsText(_work.Days.Sum())} WU in 7 days",
                WorkHours = _work?.Hours ?? [],
                Queue = _queue == null || !_connected ? null : new QueueFrame(_queue.Pickable, _queue.Unpickable, _queue.Missing)
            };
        }
    }

    private void Launch(int words, TimeSpan now)
    {
        var letters = 0;
        for (var i = 0; i < words; i++)
        {
            var delay = words <= 1 ? TimeSpan.Zero : LaunchWindow * ((double)i / (words - 1));
            var word = new FlyingWord(_lorem.Next(), now + delay, (float)_random.NextDouble());
            _incoming.Add(word);

            //NOTE: Each word's letters join the swirl where it lands, until the answer begins.
            foreach (var letter in word.Text)
            {
                if (letters++ >= MaxSwirl) break;
                _swirl.Add(new SwirlLetter(letter, word.Launch + Flight, word.Lane,
                    (float)(_random.NextDouble() * Math.PI * 2),
                    0.03f + (float)_random.NextDouble() * 0.08f,
                    (float)(0.6 + _random.NextDouble() * 1.4) * (_random.Next(2) == 0 ? 1 : -1),
                    (float)_random.NextDouble()));
            }
        }
    }

    private void Released(TimeSpan now) => _releasedAt ??= now;

    /// <summary>
    /// Where each letter is: from where its word landed into a turning ellipse between the prompt and the answer, and,
    /// once the first token comes, into the top of the answer column, fading as it arrives.
    /// </summary>
    private IReadOnlyList<SwirlLetterFrame> SwirlFrame(TimeSpan now)
    {
        if (_swirl.Count == 0) return [];

        var frames = new List<SwirlLetterFrame>(_swirl.Count);
        foreach (var letter in _swirl)
        {
            if (now < letter.Born) continue;

            var age = (float)(now - letter.Born).TotalSeconds;
            var gather = Ease(Clamp(age / (float)Gather.TotalSeconds));
            var angle = letter.Angle + letter.Speed * age;
            var radius = letter.Radius * (1 + 0.15f * MathF.Sin(age * 1.7f + letter.Angle));
            var orbitX = SwirlX + radius * MathF.Cos(angle);
            var orbitY = SwirlY + radius * 1.6f * MathF.Sin(angle);
            var startY = LanesTop + letter.Lane * LanesHeight;

            var x = LandingX + (orbitX - LandingX) * gather;
            var y = startY + (orbitY - startY) * gather;
            var alpha = 0.3f + 0.5f * gather;
            var arrival = 0f;

            if (_releasedAt is { } released)
            {
                //NOTE: A letter that had not landed yet goes straight to the answer from where it would have been.
                var leave = Clamp((float)((now - released).TotalSeconds / Release.TotalSeconds) - letter.Delay * 0.3f);
                arrival = leave * leave;
                x += (AnswerX + letter.Delay * 0.05f - x) * arrival;
                y += (AnswerY + letter.Delay * 0.03f - y) * arrival;
                alpha *= 1 - arrival;
            }

            if (alpha > 0.01f) frames.Add(new SwirlLetterFrame(letter.Letter, x, y, alpha, arrival));
        }

        return frames;
    }

    private string ModelActivity(TimeSpan now)
    {
        if (_loading is not { } loading || !_connected) return null;
        return $"Loading {loading.Model} into memory · {(now - loading.Since).TotalSeconds:0} s";
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

    /// <summary>Work units as a reader takes them in at a glance: 35.2, 812, 12.3k, 4.1M.</summary>
    public static string WorkUnitsText(double workUnits) => workUnits switch
    {
        < 100 => $"{workUnits:0.#}",
        < 10_000 => $"{workUnits:N0}",
        < 1_000_000 => $"{workUnits / 1000:0.#}k",
        _ => $"{workUnits / 1_000_000:0.#}M"
    };

    private static string Words(long count) => count == 1 ? "1 word" : $"{count:N0} words";

    private static float Clamp(float value) => value < 0 ? 0 : value > 1 ? 1 : value;

    private static float Ease(float t) => 1 - (1 - t) * (1 - t);

    private sealed record FlyingWord(string Text, TimeSpan Launch, float Lane);

    private sealed record SwirlLetter(char Letter, TimeSpan Born, float Lane, float Angle, float Radius, float Speed, float Delay);
}

/// <summary>A word on its way in: <see cref="Progress"/> from 0 at the edge to 1 where it lands; <see cref="Lane"/> its height, 0 to 1.</summary>
internal readonly record struct FlyingWordFrame(string Text, float Progress, float Lane);

/// <summary>
/// A letter of the prompt while the model reads it: where it is as shares of the screen, how visible, and how far on its
/// way into the answer (0 while it swirls, 1 when it has arrived).
/// </summary>
internal readonly record struct SwirlLetterFrame(char Letter, float X, float Y, float Alpha, float Arrival);

/// <summary>The jobs the machine can take now, those waiting for a model it lacks, and the requests it lacks most.</summary>
internal sealed record QueueFrame(int Pickable, int Unpickable, IReadOnlyList<(string Model, int Jobs)> Missing);

/// <summary>Everything one frame draws.</summary>
internal sealed record SceneFrame
{
    public required string Status { get; init; }
    public bool Connected { get; init; }
    public string Machine { get; init; }
    public string Engine { get; init; }
    public string Model { get; init; }
    public string Version { get; init; }
    public string ModelActivity { get; init; }
    public string ModelNote { get; init; }
    public float ModelNoteAlpha { get; init; }
    public string InMemory { get; init; }
    public IReadOnlyList<FlyingWordFrame> Incoming { get; init; } = [];
    public IReadOnlyList<SwirlLetterFrame> Swirl { get; init; } = [];
    public IReadOnlyList<string> Answer { get; init; } = [];
    public float AnswerAlpha { get; init; }
    public string Summary { get; init; }
    public float SummaryAlpha { get; init; }
    public string Totals { get; init; }
    public string WorkToday { get; init; }
    public string WorkWeek { get; init; }
    public IReadOnlyList<double> WorkHours { get; init; } = [];
    public QueueFrame Queue { get; init; }
}
