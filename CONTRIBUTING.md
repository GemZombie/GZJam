# Contributing to GZJam

Thanks for wanting to help. GZJam aims to stay simple, intuitive and useful, so what goes in is
decided by its maintainer, [@GemZombie](https://github.com/GemZombie).

## Questions and ideas

[Discussions](https://github.com/GemZombie/GZJam/discussions) is the place to ask how to do
something (Q&A), talk an idea over with other DJs (Ideas), or show your setup (Show and tell). New
releases are announced there too. Ideas the maintainer takes up become issues.

## Problems and requests

[Open an issue](https://github.com/GemZombie/GZJam/issues) for something that's wrong, or a clear
request. For a problem, say what you did, what happened and which computer you were on. Attach
the activity log if you can: it's in GZJam's data folder under `logs/` (`%LOCALAPPDATA%\GZJam` on
Windows, `~/.local/share/GZJam` on Linux, `~/Library/Application Support/GZJam` on a Mac).

## Pull requests

- **Talk first.** Open an issue before starting anything big, so you don't spend time on a change
  that doesn't fit.
- **Changes come in by pull request to `main`.** Every pull request needs the maintainer's approval,
  and CI must pass on Windows, macOS and Linux. Workflows from a first-time contributor's fork wait
  for the maintainer to start them.
- **Run the tests** before you send it: `dotnet test` (see the README).
- **Keep to what's there:** every feature works on all three systems and by touch, nothing is
  hover-only, and GZJam never writes to anyone's music files.
- **Licence:** by sending a pull request you agree your change is published under the
  [MIT licence](LICENSE), like the rest of GZJam's own code.

Security problems: please report them privately, from the repository's **Security** tab
(*Report a vulnerability*), not in a public issue.
