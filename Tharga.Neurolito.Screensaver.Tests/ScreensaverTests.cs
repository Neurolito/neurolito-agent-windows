namespace Tharga.Neurolito.Screensaver.Tests;

public class ScreensaverArgumentsTests
{
    [Theory]
    [InlineData(new[] { "/s" }, "Run", 0)]
    [InlineData(new[] { "/S" }, "Run", 0)]
    [InlineData(new[] { "-s" }, "Run", 0)]
    [InlineData(new[] { "/p", "1510572" }, "Preview", 1510572)]
    [InlineData(new[] { "/p:593044" }, "Preview", 593044)]
    [InlineData(new[] { "/c" }, "Settings", 0)]
    [InlineData(new[] { "/c:4242" }, "Settings", 4242)]
    [InlineData(new string[0], "Settings", 0)]
    public void WindowsStartsItInEverySpelling(string[] args, string mode, long parent)
    {
        Assert.Equal(new ScreensaverArguments(Enum.Parse<ScreensaverMode>(mode), new IntPtr(parent)), ScreensaverArguments.Parse(args));
    }
}

public class AgentMessageTests
{
    [Fact]
    public void TheStateSaysWhatTheMachineIsAndWhatItRuns()
    {
        var message = AgentMessage.Parse("""{"type":"state","presence":"Available","working":true,"machine":"DESK1","engine":"llama.cpp b11541","model":"qwen3:1.7b","version":"0.1.1"}""");

        Assert.Equal((AgentMessage.State, "Available", true, "DESK1", "llama.cpp b11541", "qwen3:1.7b", "0.1.1"),
            (message.Kind, message.Presence, message.Working, message.Machine, message.Engine, message.ModelName, message.Version));
    }

    [Fact]
    public void AJobIsItsShapeOnly()
    {
        Assert.Equal(48, AgentMessage.Parse("""{"type":"job-start","model":"qwen3:1.7b","words":48}""").Words);
        Assert.Equal(3, AgentMessage.Parse("""{"type":"tokens","count":3}""").Count);
        var end = AgentMessage.Parse("""{"type":"job-end","words":412,"durationMs":12400}""");
        Assert.Equal((412, 12400d), (end.Words, end.DurationMs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"type":"something-new","words":1}""")]
    [InlineData("""{"words":1}""")]
    public void ALineThatIsNotAKnownMessageIsIgnored(string line)
    {
        Assert.Null(AgentMessage.Parse(line));
    }
}

public class TrafficSceneTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(10);

    [Fact]
    public void WithoutTheAgentTheScreenSaysSo()
    {
        var scene = new TrafficScene(screensaverRunning: true);

        Assert.Equal("The Neurolito agent is not running on this machine.", scene.Frame(T0).Status);
        Assert.False(scene.Frame(T0).Connected);
    }

    [Theory]
    [InlineData("Available", false, true, "Ready - waiting for work")]
    [InlineData("Available", true, true, "Working")]
    [InlineData("InUse", false, true, "Getting ready - work starts in a moment")]
    [InlineData("InUse", false, false, "In use - the agent works while the screensaver runs")]
    [InlineData("Asleep", false, true, "Going to sleep")]
    public void TheStateLineSaysWhatTheAgentIsDoing(string presence, bool working, bool screensaverRunning, string status)
    {
        var scene = new TrafficScene(screensaverRunning);
        scene.Apply(new AgentMessage { Kind = AgentMessage.State, Presence = presence, Working = working, Machine = "DESK1" }, T0);

        Assert.Equal(status, scene.Frame(T0).Status);
    }

    [Fact]
    public void APromptFliesInAsManyWordsAsItHadAndThenIsGone()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobStart, Words = 12 }, T0);

        var after = scene.Frame(T0 + TrafficScene.LaunchWindow).Incoming;
        Assert.Equal(12, after.Count);
        Assert.All(after, x => Assert.InRange(x.Progress, 0, 1));
        Assert.Empty(scene.Frame(T0 + TrafficScene.LaunchWindow + TrafficScene.Flight + TimeSpan.FromMilliseconds(1)).Incoming);
    }

    [Fact]
    public void ALongPromptIsCapped()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobStart, Words = 5000 }, T0);

        Assert.Equal(TrafficScene.MaxIncoming, scene.Frame(T0 + TrafficScene.LaunchWindow).Incoming.Count);
    }

    [Fact]
    public void TheAnswerGrowsAboutAWordForEveryTokenAndAThird()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobStart, Words = 5 }, T0);
        for (var i = 0; i < 130; i++) scene.Apply(new AgentMessage { Kind = AgentMessage.Tokens, Count = 1 }, T0);

        Assert.Equal(100, scene.Frame(T0).Answer.Count);
        Assert.All(scene.Frame(T0).Answer, word => Assert.False(string.IsNullOrWhiteSpace(word)));
    }

    [Fact]
    public void AFinishedJobLeavesASummaryAndItsAnswerFades()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobStart, Words = 48 }, T0);
        scene.Apply(new AgentMessage { Kind = AgentMessage.Tokens, Count = 26 }, T0);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobEnd, Words = 412, DurationMs = 12400 }, T0);

        var now = scene.Frame(T0);
        //NOTE: In the reader's own number format; 12,4 s on a Swedish machine.
        Assert.Equal($"48 words in · 412 words out · {12.4:0.0} s", now.Summary);
        Assert.Equal(1f, now.AnswerAlpha);
        Assert.Equal("1 job · 48 words in · 412 words out since the screensaver started", now.Totals);

        Assert.Empty(scene.Frame(T0 + TrafficScene.Fade).Answer);
    }

    [Fact]
    public void ANewJobClearsTheLastAnswer()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobStart, Words = 1 }, T0);
        scene.Apply(new AgentMessage { Kind = AgentMessage.Tokens, Count = 13 }, T0);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobStart, Words = 1 }, T0);

        Assert.Empty(scene.Frame(T0).Answer);
    }

    [Fact]
    public void AModelLoadingIsShownUntilItHasLoadedAndThenSaysHowLongItTook()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.State, Presence = "Available", LoadedModels = [] }, T0);
        scene.Apply(new AgentMessage { Kind = AgentMessage.Model, Change = AgentMessage.Loading, ModelName = "qwen3-coder:30b" }, T0);

        Assert.Equal("Loading qwen3-coder:30b into memory · 12 s", scene.Frame(T0 + TimeSpan.FromSeconds(12)).ModelActivity);

        scene.Apply(new AgentMessage { Kind = AgentMessage.Model, Change = AgentMessage.Loaded, ModelName = "qwen3-coder:30b" }, T0 + TimeSpan.FromSeconds(14.2));
        var loaded = scene.Frame(T0 + TimeSpan.FromSeconds(15));
        Assert.Null(loaded.ModelActivity);
        Assert.Equal($"Loaded qwen3-coder:30b in {14.2:0.0} s", loaded.ModelNote);

        scene.Apply(new AgentMessage { Kind = AgentMessage.Model, Change = AgentMessage.Unloaded, ModelName = "qwen3-coder:30b" }, T0 + TimeSpan.FromMinutes(10));
        Assert.StartsWith("Unloaded qwen3-coder:30b", scene.Frame(T0 + TimeSpan.FromMinutes(10)).ModelNote);
    }

    [Fact]
    public void ThePromptsLettersSwirlUntilTheFirstTokenAndThenJoinTheAnswer()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobStart, Words = 20 }, T0);

        var landed = T0 + TrafficScene.LaunchWindow + TrafficScene.Flight + TrafficScene.Gather;
        var swirling = scene.Frame(landed).Swirl;
        Assert.NotEmpty(swirling);
        Assert.All(swirling, x => Assert.Equal(0, x.Arrival));
        Assert.All(swirling, x => Assert.InRange(x.X, 0.2f, 0.7f));

        scene.Apply(new AgentMessage { Kind = AgentMessage.Tokens, Count = 1 }, landed);
        Assert.Empty(scene.Frame(landed + TrafficScene.Release + TimeSpan.FromSeconds(1)).Swirl);
    }

    [Fact]
    public void ALongPromptSwirlsAtMostSoManyLetters()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobStart, Words = 5000 }, T0);

        Assert.Equal(TrafficScene.MaxSwirl, scene.Frame(T0 + TimeSpan.FromSeconds(10)).Swirl.Count);
    }

    [Fact]
    public void WorkAndTheQueueAreShownOnceTheAgentSendsThem()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.State, Presence = "Available" }, T0);
        Assert.Null(scene.Frame(T0).WorkToday);
        Assert.Null(scene.Frame(T0).Queue);

        scene.Apply(AgentMessage.Parse("""{"type":"work","workUnitsToday":12345.6,"jobsToday":18,"hours":[1,2,3],"days":[1000,2000.5]}"""), T0);
        scene.Apply(AgentMessage.Parse("""{"type":"queue","pickable":4,"unpickable":7,"missing":[{"model":"qwen3-coder:30b","jobs":5}]}"""), T0);

        var frame = scene.Frame(T0);
        Assert.Equal("12.3k WU today · 18 jobs", frame.WorkToday.Replace(',', '.'));
        Assert.Equal([1d, 2d, 3d], frame.WorkHours);
        Assert.Equal(new QueueFrame(4, 7, [("qwen3-coder:30b", 5)]).Pickable, frame.Queue.Pickable);
        Assert.Equal(("qwen3-coder:30b", 5), frame.Queue.Missing.Single());

        scene.SetConnected(false);
        Assert.Null(scene.Frame(T0).Queue);
    }

    [Fact]
    public void AFinishedJobSaysItsWorkUnitsWhenTheAgentDoes()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobStart, Words = 48 }, T0);
        scene.Apply(new AgentMessage { Kind = AgentMessage.JobEnd, Words = 412, DurationMs = 12400, WorkUnits = 35.24 }, T0);

        Assert.EndsWith($" · {35.2:0.#} WU", scene.Frame(T0).Summary);
        Assert.Contains($" · {35.2:0.#} WU since the screensaver started", scene.Frame(T0).Totals);
    }

    [Theory]
    [InlineData(35.24, "35.2")]
    [InlineData(812, "812")]
    [InlineData(12345, "12.3k")]
    [InlineData(4_100_000, "4.1M")]
    public void WorkUnitsReadAtAGlance(double workUnits, string expected)
    {
        Assert.Equal(expected, TrafficScene.WorkUnitsText(workUnits).Replace(',', '.'));
    }

    [Fact]
    public void LosingTheAgentForgetsItsState()
    {
        var scene = new TrafficScene(true);
        scene.Apply(new AgentMessage { Kind = AgentMessage.State, Presence = "Available" }, T0);
        scene.SetConnected(false);

        Assert.Equal("The Neurolito agent is not running on this machine.", scene.Frame(T0).Status);
    }

    [Theory]
    [InlineData("0.1.1", null, "Version 0.1.1")]
    [InlineData("0.1.1", "0.1.1", "Version 0.1.1")]
    [InlineData("0.1.1", "0.1.0", "Screensaver 0.1.1 · agent 0.1.0")]
    [InlineData(null, "0.1.0", "Agent 0.1.0")]
    [InlineData(null, null, null)]
    public void TheVersionShowsWhetherTheAgentHasCaughtUp(string screensaver, string agent, string expected)
    {
        var scene = new TrafficScene(true, version: screensaver);
        scene.Apply(new AgentMessage { Kind = AgentMessage.State, Presence = "Available", Version = agent }, T0);

        Assert.Equal(expected, scene.Frame(T0).Version);
    }

    [Fact]
    public void ALostAgentNoLongerClaimsAVersion()
    {
        var scene = new TrafficScene(true, version: "0.1.1");
        scene.Apply(new AgentMessage { Kind = AgentMessage.State, Version = "0.1.0" }, T0);
        scene.SetConnected(false);

        Assert.Equal("Version 0.1.1", scene.Frame(T0).Version);
    }
}

public class LoremTests
{
    [Fact]
    public void TheWordsRunRoundAndStartWhereTheSeedSays()
    {
        var lorem = new Lorem();
        var first = lorem.Next();
        for (var i = 1; i < Lorem.Count; i++) lorem.Next();

        Assert.Equal("lorem", first);
        Assert.Equal(first, lorem.Next());
        Assert.NotEqual("lorem", new Lorem(5).Next());
    }
}
