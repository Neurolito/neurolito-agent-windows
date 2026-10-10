# Security policy

## Reporting a vulnerability

**Do not open a public issue for a security problem.** Report it privately through GitHub:
[Report a vulnerability](https://github.com/Neurolito/neurolito-agent-windows/security/advisories/new).

Say what you found, how to reproduce it, and which version you tested. You will get an answer within a few working
days, and we will tell you when a fix is released.

## Scope

- The screensaver
- The installer, and what it sets up: the service, its account, its files and their permissions
- The local pipe between the agent service and the screensaver

Problems in the agent itself go to [`neurolito-agent`](https://github.com/Neurolito/neurolito-agent/security/advisories/new).

## How the pieces are meant to be exposed

- The agent service runs as its own virtual account, not as LocalSystem.
- The pipe the screensaver reads (`\\.\pipe\neurolito-agent`) accepts users signed in on the machine and refuses the
  network. It carries the agent's state and the shape of its work - word and token counts - never prompt or answer
  text. Text reaching the pipe, or the pipe being reachable from another machine, is a vulnerability.
- The team key is kept where only the service account and administrators can read it.
