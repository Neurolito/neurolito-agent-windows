using System.Text.Json;

namespace Tharga.Neurolito.Screensaver;

/// <summary>
/// One line from the agent's pipe: its <c>state</c>; the shape of its work - <c>job-start</c>, <c>tokens</c>,
/// <c>job-end</c>; a <c>model</c> loading, loaded or unloaded; its <c>work</c> by hour and day; and the server's count of
/// the <c>queue</c>. Word, token and job counts only; the pipe never carries text.
/// </summary>
internal sealed record AgentMessage
{
    public const string State = "state";
    public const string JobStart = "job-start";
    public const string Tokens = "tokens";
    public const string JobEnd = "job-end";
    public const string Model = "model";
    public const string Work = "work";
    public const string Queue = "queue";

    public const string Loading = "loading";
    public const string Loaded = "loaded";
    public const string Unloaded = "unloaded";

    public required string Kind { get; init; }

    /// <summary><c>Available</c>, <c>InUse</c> or <c>Asleep</c>, on <see cref="State"/>.</summary>
    public string Presence { get; init; }

    public bool Working { get; init; }
    public string Machine { get; init; }
    public string Engine { get; init; }

    /// <summary>The model of the last job on <see cref="State"/> and <see cref="JobStart"/>; the one that changed on <see cref="Model"/>.</summary>
    public string ModelName { get; init; }

    /// <summary>The agent's release, on <see cref="State"/>; null from an agent too old to say.</summary>
    public string Version { get; init; }

    /// <summary>The models in memory, on <see cref="State"/>; null from an agent too old to say.</summary>
    public IReadOnlyList<string> LoadedModels { get; init; }

    /// <summary><see cref="Loading"/>, <see cref="Loaded"/> or <see cref="Unloaded"/>, on <see cref="Model"/>.</summary>
    public string Change { get; init; }

    /// <summary>Words in on <see cref="JobStart"/>, words out on <see cref="JobEnd"/>.</summary>
    public int Words { get; init; }

    /// <summary>Tokens on <see cref="Tokens"/>.</summary>
    public int Count { get; init; }

    public double DurationMs { get; init; }

    /// <summary>The job's work units on <see cref="JobEnd"/>; null from an agent too old to say.</summary>
    public double? WorkUnits { get; init; }

    /// <summary>On <see cref="Work"/>: today's work units and jobs, the last 24 hours and the last 7 days, oldest first.</summary>
    public double WorkUnitsToday { get; init; }
    public int JobsToday { get; init; }
    public IReadOnlyList<double> Hours { get; init; } = [];
    public IReadOnlyList<double> Days { get; init; } = [];

    /// <summary>On <see cref="Queue"/>: jobs the machine can take, jobs waiting for a model it lacks, and which.</summary>
    public int Pickable { get; init; }
    public int Unpickable { get; init; }
    public IReadOnlyList<(string Model, int Jobs)> Missing { get; init; } = [];

    /// <summary>The message, or null for a line that is not one - including kinds a newer agent sends that this does not know.</summary>
    public static AgentMessage Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var kind = Text(root, "type");
            if (kind is not (State or JobStart or Tokens or JobEnd or Model or Work or Queue)) return null;

            return new AgentMessage
            {
                Kind = kind,
                Presence = Text(root, "presence"),
                Working = root.TryGetProperty("working", out var w) && w.ValueKind == JsonValueKind.True,
                Machine = Text(root, "machine"),
                Engine = Text(root, "engine"),
                ModelName = Text(root, "model"),
                Version = Text(root, "version"),
                LoadedModels = root.TryGetProperty("loaded", out var l) && l.ValueKind == JsonValueKind.Array
                    ? l.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToArray()
                    : null,
                Change = Text(root, "change"),
                Words = Number(root, "words"),
                Count = Number(root, "count"),
                DurationMs = Decimal(root, "durationMs") ?? 0,
                WorkUnits = Decimal(root, "workUnits"),
                WorkUnitsToday = Decimal(root, "workUnitsToday") ?? 0,
                JobsToday = Number(root, "jobsToday"),
                Hours = Numbers(root, "hours"),
                Days = Numbers(root, "days"),
                Pickable = Number(root, "pickable"),
                Unpickable = Number(root, "unpickable"),
                Missing = root.TryGetProperty("missing", out var m) && m.ValueKind == JsonValueKind.Array
                    ? m.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => (Text(x, "model"), Number(x, "jobs"))).Where(x => x.Item1 != null).ToArray()
                    : []
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? Math.Max(0, n) : 0;

    private static double? Decimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? Math.Max(0, value.GetDouble()) : null;

    private static double[] Numbers(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? Math.Max(0, x.GetDouble()) : 0).ToArray()
            : [];
}
