# Contributing to Better SS

Thank you for helping improve Better SS. Bug reports, documentation fixes, design
feedback, and focused pull requests are all welcome.

## Development setup

1. Use Windows 10 version 2004 or newer, or Windows 11.
2. Install the .NET 8 SDK.
3. Fork and clone the repository.
4. Run `./scripts/setup-ffmpeg.ps1` if you will work on recording or video editing.
5. Run `dotnet build BetterSS/BetterSS.csproj -c Release`.
6. Start the app with `dotnet run --project BetterSS/BetterSS.csproj`.

The repository does not redistribute development capture sounds. Choose a local
audio file from Preferences if your work needs one.

## Making a change

- Keep each pull request limited to one coherent change.
- Preserve the local-first model; network features require explicit design and
  privacy review.
- Keep Windows interop concentrated in `Native.cs` where practical.
- Include regression coverage for behavior changes. Tests currently run as
  executable modes rather than through a separate unit-test project.
- Do not commit build output, test artifacts, downloaded tools, or personal media.
- Update user-facing and architecture documentation when behavior or boundaries
  change.

## Checks

Run the smallest relevant set, plus a Release build:

```powershell
dotnet build BetterSS/BetterSS.csproj -c Release
dotnet run --project BetterSS/BetterSS.csproj -- --editor-test
dotnet run --project BetterSS/BetterSS.csproj -- --video-cutter-test
./build.ps1 -Test
```

`--live-capture-test` interacts with real displays and input devices. Run it only
when you are prepared for temporary test windows and pointer movement.

## Pull requests

Describe the user-visible result, the technical approach, the checks you ran,
and any known limitations. Screenshots or short recordings are useful for visual
changes, but ensure they contain no private information.

By contributing, you agree that your contribution may be distributed under the
repository's MIT License.
