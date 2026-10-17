using System.Security;

namespace Tharga.Neurolito.Screensaver;

/// <summary>Starts a program in this user's session through the Task Scheduler, so it is not a child of the screensaver.</summary>
/// <remarks>
/// Windows' screensaver launcher ties the screensaver to a job that ends everything in it when the screensaver ends, and
/// does not let a child leave it. A program the Task Scheduler starts is in no job of ours. The task is registered for
/// this user only, run once and removed; what it started keeps running.
/// </remarks>
internal static class OnDemandTask
{
    private const int CreateOrUpdate = 6;
    private const int InteractiveToken = 3;

    public static void Start(string program, string arguments)
    {
        var name = $"Neurolito helper {Environment.UserName} {Guid.NewGuid():N}";
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!;
        service.Connect();
        dynamic folder = service.GetFolder(@"\");
        dynamic task = folder.RegisterTask(name, Definition(program, arguments), CreateOrUpdate, null, null, InteractiveToken);
        try
        {
            task.Run(null);
        }
        finally
        {
            folder.DeleteTask(name, 0);
        }
    }

    /// <summary>No time limit and no battery rule: the helper runs for the whole session, on any power.</summary>
    internal static string Definition(string program, string arguments) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Principals>
            <Principal id="User">
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <AllowHardTerminate>false</AllowHardTerminate>
            <Hidden>true</Hidden>
          </Settings>
          <Actions Context="User">
            <Exec>
              <Command>{SecurityElement.Escape(program)}</Command>
              <Arguments>{SecurityElement.Escape(arguments)}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
}
