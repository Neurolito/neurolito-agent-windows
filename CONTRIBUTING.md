# Contributing

Thank you for helping. Issues and pull requests are both welcome.

## Before you start

For anything larger than a small fix, open an issue first, so we can agree on the approach before you spend time on
it. Security problems go through [SECURITY.md](SECURITY.md), not an issue. Changes to the agent itself belong in
[`neurolito-agent`](https://github.com/Neurolito/neurolito-agent).

## Making a change

1. Fork the repository and branch from `main`.
2. Build and test, on Windows:
   ```bash
   dotnet build Tharga.Neurolito.Windows.slnx -c Release
   dotnet test Tharga.Neurolito.Windows.slnx -c Release
   ```
3. Add a test for the change. A bug fix comes with a test that fails without it.
4. Open a pull request against `main`. The build and tests run on it, and a maintainer reviews it.

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org): `feat:`, `fix:`, `docs:`, `test:`,
`chore:`.

## The screensaver

- It is a view. Anything the agent does belongs in the agent; the screensaver only draws what the agent's pipe says.
- It never shows prompt or answer text, and the pipe never carries it. Keep it that way.
- Windows starts it with `/s` (run), `/p <window>` (preview) or `/c` (settings), sometimes by its short 8.3 file name,
  so nothing may depend on the file name.

## Code style

- Code should explain itself. Public APIs carry XML documentation; other comments are for what the code cannot say.
- Prefer `init` over `set`, and records for data.

## Licence

By contributing you agree that your contribution is licensed under the [Apache-2.0](LICENSE) licence.
