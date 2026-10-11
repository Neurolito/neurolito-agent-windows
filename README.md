# Neurolito Agent for Windows

Turns a Windows desktop into an agent for [Neurolito](https://neurolito.com) that works while nobody is using the
machine and steps aside when someone does. Its screensaver shows the work going on: text arriving and leaving, drawn as
lorem ipsum.

> **Installing an agent?** Use the [install guide](https://neurolito.com/install-agent). It shows the route that fits
> your machine.

The agent itself - models, jobs, the connection to Neurolito - is
[`neurolito-agent`](https://github.com/Neurolito/neurolito-agent). This repository is what makes it a Windows
desktop application: the screensaver, the installer, and the build that puts them together with a published agent and
[llama.cpp](https://github.com/ggml-org/llama.cpp).

## What it installs

| Part | What it does |
|---|---|
| **The agent, as a Windows service** | Runs the models and takes jobs from your team's queue. It runs as its own low-privilege account (`NT SERVICE\NeurolitoAgent`), starts with Windows, and works whether or not anyone is logged on. |
| **llama.cpp** (Vulkan build) | The engine the agent drives. One build uses NVIDIA, AMD and Intel graphics cards; no Ollama or Docker needed. Models are downloaded when your team's model set asks for them. |
| **The screensaver** | A view onto the service. It shows the shape of the work - how much text came in and went out - never the text itself, since the agent serves other teams' jobs too. |

## Working only when the machine is free

The service cannot see your mouse or keyboard, so it watches what Windows can tell it:

- **The screensaver is running** - Windows decided you are away.
- **The machine is locked**, or **nobody is logged on**.

When any of these is true the agent takes work; when you come back it finishes the job it is running and takes no new
one. Neurolito shows the machine as *In use*, *Ready*, *Working* or *Asleep*.

It can also keep the machine from going to sleep while it works - never, only while a job runs, or whenever it is
ready for work. The default is never: installing the agent does not change how your machine sleeps unless you ask it to.

## What is here

| Folder | What it is |
|---|---|
| `Tharga.Neurolito.Screensaver` | The screensaver (`Neurolito.scr`) |
| `Tharga.Neurolito.Screensaver.Tests` | Its tests |
| `Tharga.Neurolito.Installer` | The MSI ([WiX](https://wixtoolset.org) v5) |
| `build/build.ps1` | Builds the MSI: publishes the agent, the screensaver and a pinned, checksum-verified llama.cpp, then the installer |

## Installing

Run `NeurolitoAgent.msi` and enter your team's install key. Or, for many machines:

```powershell
msiexec /i NeurolitoAgent.msi /qn TEAMKEY=<key> ENERGYMODE=WhileReady SETSCREENSAVER=0
```

| Property | |
|---|---|
| `TEAMKEY` | Your team's install key. Required on a first install; kept on upgrade. |
| `ENERGYMODE` | `Never` (default), `WhileWorking` or `WhileReady`. Kept on upgrade. |
| `SETSCREENSAVER` | `1` (default) makes the Neurolito screensaver the installing user's, starting after 10 minutes. `0` leaves it alone. |
| `SERVERADDRESS` | For testing against another Neurolito server only. |

What goes where:

- `C:\Program Files\Neurolito Agent` - the agent and llama.cpp. The service is `NeurolitoAgent`, running as `NT SERVICE\NeurolitoAgent`.
- `C:\ProgramData\Neurolito` - models and logs, writable by the service only.
- `C:\Windows\System32\Neurolito.scr` - the screensaver.
- `HKLM\SOFTWARE\Neurolito\Agent` - the agent's settings, readable by the service and administrators only.

Uninstalling removes all of it, models included.

## Building from source

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build Tharga.Neurolito.Windows.slnx -c Release
dotnet test Tharga.Neurolito.Windows.slnx -c Release
```

The MSI, from a checkout of [`neurolito-agent`](https://github.com/Neurolito/neurolito-agent) next to this one:

```powershell
./build/build.ps1 -AgentProject ../neurolito-agent/Tharga.Neurolito.Agent/Tharga.Neurolito.Agent.csproj -Version 0.1.0
```

It lands in `artifacts/msi`, with the update package beside it in `artifacts/releases`.

## Releases and updates

An installed agent updates itself: it looks at this repository's [releases](https://github.com/Neurolito/neurolito-agent-windows/releases)
every few hours, downloads a new version, finishes the job it is running and restarts on the new one. The update
replaces the agent, llama.cpp and the screensaver together.

A release is a tag, `v<major>.<minor>.<patch>`. The `Release` workflow builds the agent named in `build/agent.json`
with llama.cpp and the screensaver, makes the update package (small, against the release before), and publishes it
with the MSI. Every pull request builds the same as a trial, kept as the run's artifacts.

To try the screensaver without installing it, run `Neurolito.exe /s` from the build output (any key or mouse movement
ends it), or `/p 0` for nothing. With no agent running it says so; with an agent running in desktop mode
(`Desktop:Enabled`), it shows that agent's work.

## Licence

[Apache-2.0](LICENSE).
