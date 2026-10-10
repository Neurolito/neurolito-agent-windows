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

The installer and the release build follow.

## Building from source

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build Tharga.Neurolito.Windows.slnx -c Release
dotnet test Tharga.Neurolito.Windows.slnx -c Release
```

To try the screensaver without installing it, run `Neurolito.exe /s` from the build output (any key or mouse movement
ends it), or `/p 0` for nothing. With no agent running it says so; with an agent running in desktop mode
(`Desktop:Enabled`), it shows that agent's work.

## Licence

[Apache-2.0](LICENSE).
