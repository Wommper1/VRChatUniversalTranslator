# Tests

The core test executable has no test-framework dependency. It verifies process selection, event deduplication, restart, exit, visible errors, float audio metering, exact diagnostic WAV output, language labels, speech segmentation, and bounded caption history deterministically:

```powershell
dotnet run --project .\tests\VRChatTranslator.Core.Tests\VRChatTranslator.Core.Tests.csproj
```

The WPF shell retains a separate `--smoke-test` startup path.

The embedded phone server has a full local client self-test:

```powershell
dotnet run --project .\src\VRChatTranslator.App\VRChatTranslator.App.csproj -- --phone-self-test
```

It covers startup, fixed-page serving, the PIN (missing and wrong PINs are refused), refusal of a foreign Host name, the wrong-PIN limit, the cap on open displays, SSE broadcast, reconnect/history replay, caption escaping, port conflicts, and listener shutdown. It listens on this PC only (`loopbackOnly`), so running it never raises a Windows Firewall prompt; the all-adapters bind is the single `ListenAnyIP` call in `PhoneDisplayServer.StartAsync`. It does not substitute for opening the URL on a physical phone through the user’s router/firewall.

## Process-isolation proof

After building the solution, run the opt-in proof below. It plays two quiet, short tones through Windows:

```powershell
$proof = Resolve-Path .\tests\VRChatTranslator.AudioProofSource\bin\Debug\net10.0-windows\VRChatTranslator.AudioProofSource.exe
dotnet run --project .\tests\VRChatTranslator.Core.Tests\VRChatTranslator.Core.Tests.csproj -- --capture-proof $proof
```

The first case targets a silent process while an unrelated process renders audio and expects silence. The second targets the tone-rendering process and expects a non-zero signal. Passing both cases distinguishes `VRCHAT ONLY` capture from endpoint-wide system loopback.
