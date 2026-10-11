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
            (message.Kind, message.Presence, message.Working, message.Machine, message.Engine, message.Model, message.Version));
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
