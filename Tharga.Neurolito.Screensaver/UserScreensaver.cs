using System.Text.Json;

namespace Tharga.Neurolito.Screensaver;

/// <summary>A user's screensaver as Windows has it set: which one, whether it is on, after how long, and whether policy sets it.</summary>
internal sealed record ScreensaverSetting(bool Neurolito, bool Active, int TimeoutSeconds, bool Managed)
{
    public const string FileName = "Neurolito.scr";

    /// <summary>
    /// Whether <c>SCRNSAVE.EXE</c> names the Neurolito screensaver: in any folder, quoted or not, with environment
    /// variables, or by its short name (<c>NEUROL~1.SCR</c>), which <paramref name="longName"/> resolves.
    /// </summary>
    public static bool IsNeurolito(string path, Func<string, string> longName = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var value = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (string.Equals(Name(value), FileName, StringComparison.OrdinalIgnoreCase)) return true;
        if (!value.Contains('~') || longName == null) return false;

        return string.Equals(Name(longName(value)), FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string Name(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var slash = path.LastIndexOfAny(['\\', '/']);
        return slash >= 0 ? path[(slash + 1)..] : path;
    }
}

/// <summary>A change the agent asks for. A null field is left as it is.</summary>
internal sealed record ScreensaverChange(bool? Active, int? TimeoutSeconds)
{
    /// <summary>The shortest timeout taken, as Windows' own settings offer.</summary>
    public const int MinimumTimeoutSeconds = 60;

    /// <summary>Why the change cannot be made to <paramref name="current"/>, or null when it can.</summary>
    public string Refusal(ScreensaverSetting current)
    {
        if (!current.Neurolito) return "the user's screensaver is not the Neurolito one";
        if (current.Managed) return "the screensaver is set by policy";
        if (TimeoutSeconds is < MinimumTimeoutSeconds) return $"the timeout must be at least {MinimumTimeoutSeconds} seconds";
        return null;
    }
}

/// <summary>
/// The helper's lines on the agent's pipe. Out: <c>screensaver</c>, the user's setting. In: <c>set-screensaver</c>;
/// every other line - the state and work the agent sends the screensaver - is not for the helper.
/// </summary>
internal static class UserSettingsProtocol
{
    public const string Report = "screensaver";
    public const string Set = "set-screensaver";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static string ReportLine(ScreensaverSetting setting, string error = null) => JsonSerializer.Serialize(new
    {
        type = Report,
        neurolito = setting.Neurolito,
        active = setting.Active,
        timeoutSeconds = setting.TimeoutSeconds,
        managed = setting.Managed,
        error
    }, Json);

    /// <summary>The change a <c>set-screensaver</c> line asks for, or null for any other line.</summary>
    public static ScreensaverChange ParseSet(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != Set) return null;

            bool? active = root.TryGetProperty("active", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False ? a.GetBoolean() : null;
            int? timeout = root.TryGetProperty("timeoutSeconds", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var seconds) ? seconds : null;
            return new ScreensaverChange(active, timeout);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
