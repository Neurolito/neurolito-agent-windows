using System.Text.Json;

namespace Tharga.Neurolito.Screensaver;

/// <summary>
/// One line from the agent's pipe: its <c>state</c>, or the shape of its work - <c>job-start</c>, <c>tokens</c>,
/// <c>job-end</c>. Word and token counts only; the pipe never carries text.
/// </summary>
internal sealed record AgentMessage
{
    public const string State = "state";
    public const string JobStart = "job-start";
    public const string Tokens = "tokens";
    public const string JobEnd = "job-end";

    public required string Kind { get; init; }

    /// <summary><c>Available</c>, <c>InUse</c> or <c>Asleep</c>, on <see cref="State"/>.</summary>
    public string Presence { get; init; }

    public bool Working { get; init; }
    public string Machine { get; init; }
    public string Engine { get; init; }
    public string Model { get; init; }

    /// <summary>Words in on <see cref="JobStart"/>, words out on <see cref="JobEnd"/>.</summary>
    public int Words { get; init; }

    /// <summary>Tokens on <see cref="Tokens"/>.</summary>
    public int Count { get; init; }

    public double DurationMs { get; init; }

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
            if (kind is not (State or JobStart or Tokens or JobEnd)) return null;

            return new AgentMessage
            {
                Kind = kind,
                Presence = Text(root, "presence"),
                Working = root.TryGetProperty("working", out var w) && w.ValueKind == JsonValueKind.True,
                Machine = Text(root, "machine"),
                Engine = Text(root, "engine"),
                Model = Text(root, "model"),
                Words = Number(root, "words"),
                Count = Number(root, "count"),
                DurationMs = root.TryGetProperty("durationMs", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0
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
}
