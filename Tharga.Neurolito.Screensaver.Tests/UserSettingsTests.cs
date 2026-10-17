namespace Tharga.Neurolito.Screensaver.Tests;

public class UserSettingsArgumentsTests
{
    [Theory]
    [InlineData("/user")]
    [InlineData("/USER")]
    [InlineData("-user")]
    public void TheLogonEntryStartsTheHelper(string argument)
    {
        Assert.Equal(ScreensaverMode.UserSettings, ScreensaverArguments.Parse([argument]).Mode);
    }
}

public class NeurolitoScreensaverTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32\Neurolito.scr")]
    [InlineData(@"c:\windows\system32\NEUROLITO.SCR")]
    [InlineData(@"""C:\Windows\System32\Neurolito.scr""")]
    [InlineData(@"%SystemRoot%\System32\Neurolito.scr")]
    [InlineData("Neurolito.scr")]
    public void ItIsOursInAnySpelling(string path)
    {
        Assert.True(ScreensaverSetting.IsNeurolito(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Windows\System32\Mystify.scr")]
    [InlineData(@"C:\Windows\System32\Neurolito.scr.bak")]
    [InlineData(@"C:\Neurolito.scr\Bubbles.scr")]
    public void AnotherScreensaverIsNot(string path)
    {
        Assert.False(ScreensaverSetting.IsNeurolito(path));
    }

    [Fact]
    public void AShortNameIsResolved()
    {
        Assert.True(ScreensaverSetting.IsNeurolito(@"C:\WINDOWS\system32\NEUROL~1.SCR", _ => @"C:\Windows\System32\Neurolito.scr"));
        Assert.False(ScreensaverSetting.IsNeurolito(@"C:\WINDOWS\system32\MYSTIF~1.SCR", _ => @"C:\Windows\System32\Mystify.scr"));
        Assert.False(ScreensaverSetting.IsNeurolito(@"C:\WINDOWS\system32\NEUROL~1.SCR", _ => null));
    }
}

public class UserSettingsProtocolTests
{
    [Fact]
    public void TheReportSaysTheSetting()
    {
        Assert.Equal("""{"type":"screensaver","neurolito":true,"active":true,"timeoutSeconds":600,"managed":false}""",
            UserSettingsProtocol.ReportLine(new ScreensaverSetting(true, true, 600, false)));
    }

    [Fact]
    public void ARefusalIsReportedWithTheSetting()
    {
        Assert.Equal("""{"type":"screensaver","neurolito":false,"active":true,"timeoutSeconds":300,"managed":false,"error":"no"}""",
            UserSettingsProtocol.ReportLine(new ScreensaverSetting(false, true, 300, false), "no"));
    }

    [Fact]
    public void AChangeLeavesOutWhatItDoesNotSay()
    {
        Assert.Equal(new ScreensaverChange(true, 900), UserSettingsProtocol.ParseSet("""{"type":"set-screensaver","active":true,"timeoutSeconds":900}"""));
        Assert.Equal(new ScreensaverChange(null, 120), UserSettingsProtocol.ParseSet("""{"type":"set-screensaver","timeoutSeconds":120}"""));
        Assert.Equal(new ScreensaverChange(false, null), UserSettingsProtocol.ParseSet("""{"type":"set-screensaver","active":false,"timeoutSeconds":null}"""));
    }

    [Theory]
    [InlineData("""{"type":"state","presence":"Available"}""")]
    [InlineData("""{"type":"tokens","count":3}""")]
    [InlineData("idle")]
    [InlineData("")]
    [InlineData("[1,2]")]
    [InlineData("{not json")]
    public void EveryOtherLineIsNotForTheHelper(string line)
    {
        Assert.Null(UserSettingsProtocol.ParseSet(line));
    }
}

public class ScreensaverChangeTests
{
    private static readonly ScreensaverSetting Ours = new(true, true, 600, false);

    [Fact]
    public void OursIsChanged()
    {
        Assert.Null(new ScreensaverChange(false, 60).Refusal(Ours));
        Assert.Null(new ScreensaverChange(true, null).Refusal(Ours));
    }

    [Fact]
    public void AnotherScreensaverIsLeftAlone()
    {
        Assert.NotNull(new ScreensaverChange(null, 600).Refusal(Ours with { Neurolito = false }));
    }

    [Fact]
    public void PolicyWins()
    {
        Assert.NotNull(new ScreensaverChange(true, null).Refusal(Ours with { Managed = true }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(-1)]
    public void ATimeoutUnderAMinuteIsRefused(int seconds)
    {
        Assert.NotNull(new ScreensaverChange(null, seconds).Refusal(Ours));
    }
}

public class HelperStartupTests
{
    private const string LocalData = @"C:\Users\ann\AppData\Local";
    private const string Copy = @"C:\Users\ann\AppData\Local\Neurolito\screensaver\0.1.5\Neurolito.exe";
    private const string Ours = $"\"{Copy}\" /user";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"""C:\Windows\System32\Neurolito.scr"" /user")]
    [InlineData(@"%SystemRoot%\System32\Neurolito.scr /user")]
    public void WithoutAMachineEntryThatWorksTheUserGetsTheirOwn(string machine)
    {
        Assert.Equal(Ours, HelperStartup.UserEntry(machine, null, Copy, LocalData));
    }

    [Fact]
    public void ANewVersionMovesTheUsersEntryToItsCopy()
    {
        var old = @"""C:\Users\ann\AppData\Local\Neurolito\screensaver\0.1.4\Neurolito.exe"" /user";
        Assert.Equal(Ours, HelperStartup.UserEntry(null, old, Copy, LocalData));
    }

    [Fact]
    public void AMachineEntryThatNamesAProgramNeedsNoneOfTheUsers()
    {
        Assert.Null(HelperStartup.UserEntry(@"""C:\Program Files\Neurolito Agent\NeurolitoLauncher.exe"" /user", Ours, Copy, LocalData));
    }

    [Fact]
    public void TheInstallFoldersScreensaverIsNeverTheLogonEntry()
    {
        var installed = @"C:\Program Files\Neurolito Agent\current\screensaver\Neurolito.exe";
        Assert.Null(HelperStartup.UserEntry(null, null, installed, LocalData));
        Assert.Equal(Ours, HelperStartup.UserEntry(null, Ours, installed, LocalData));
        Assert.Null(HelperStartup.UserEntry(null, null, @"C:\Users\ann\AppData\Local\NeurolitoOther\Neurolito.exe", LocalData));
    }

    [Theory]
    [InlineData(@"""C:\Windows\System32\Neurolito.scr"" /user", true)]
    [InlineData(@"C:\Windows\System32\NEUROLITO.SCR /user", true)]
    [InlineData(@"""C:\Program Files\Neurolito Agent\NeurolitoLauncher.exe"" /user", false)]
    [InlineData(@"""C:\screens.scr\Neurolito.exe"" /user", false)]
    public void AScreensaverFileIsNotAProgram(string command, bool screensaver)
    {
        Assert.Equal(screensaver, HelperStartup.NamesAScreensaver(command));
    }

    [Fact]
    public void TheTaskNamesTheProgramSafely()
    {
        var xml = System.Xml.Linq.XDocument.Parse(OnDemandTask.Definition(@"C:\Users\a&b\Neurolito.exe", "/user"));
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal(@"C:\Users\a&b\Neurolito.exe", xml.Descendants(ns + "Command").Single().Value);
        Assert.Equal("PT0S", xml.Descendants(ns + "ExecutionTimeLimit").Single().Value);
    }
}
