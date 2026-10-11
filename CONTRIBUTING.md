# Contributing

Thanks for helping. This is a small, local-first Windows app, so a few ground rules keep it that way.

Be kind and assume good faith. Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## What the project will and will not do

- **Local first.** Captions and translation run on the user's PC. No telemetry, no accounts, no cloud services of its own, no paid APIs.
- **Honest status.** Do not describe something as working unless it was run. [VERIFICATION.md](VERIFICATION.md) records what was and was not checked; keep it true.
- **Safe by default.** Anything that listens on a network, downloads code or data, or starts another program needs a clear reason, a limit, and a test. Read [SECURITY.md](SECURITY.md) first.

## Build and test

You need Windows 11 x64 and the .NET SDK named in `global.json`.

```powershell
dotnet restore .\VRChatUniversalTranslator.slnx --configfile .\NuGet.Config
dotnet build   .\VRChatUniversalTranslator.slnx --configuration Debug --no-restore
dotnet run --no-build --project .\tests\VRChatTranslator.Core.Tests\VRChatTranslator.Core.Tests.csproj
dotnet run --no-build --project .\src\VRChatTranslator.App\VRChatTranslator.App.csproj -- --smoke-test
dotnet run --no-build --project .\src\VRChatTranslator.App\VRChatTranslator.App.csproj -- --phone-self-test
```

The build treats warnings as errors. All checks above must pass before you open a pull request. The test runner is a plain console program (`tests/VRChatTranslator.Core.Tests`); add new checks to the list in `Program.cs`.

## Pull requests

- Keep each change focused, and say what you tested and how.
- Add or update a test for behavior you change. For a fix, show that the new test fails without it.
- Do not put real VRChat user names, real conversations, audio, API keys, or personal file paths in code, tests, fixtures, or docs. Use made-up names.
- Update the documentation that your change makes untrue (`README.md`, `QUICK_START.md`, `PRIVACY.md`, `RELEASE_NOTES.md`).
- If a change adds a download, record its exact size and SHA-256 (see `DownloadIntegrity`) and add it to the table in [PRIVACY.md](PRIVACY.md).

## Reporting problems

- Bugs and ideas: open an issue and fill in the template.
- Security problems: **do not** open a public issue. Follow [SECURITY.md](SECURITY.md).

## License

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE) that covers the project.
