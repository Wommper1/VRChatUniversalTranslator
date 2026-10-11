# Architecture

## Decision summary

The application is a native Windows x64 .NET desktop application. WPF provides the control window and a separate borderless layered overlay because it offers mature control over transparency, always-on-top behavior, persisted placement, and Win32 extended window styles for click-through interaction. The application targets .NET 10, matching the SDK installed on the development PC.

The code is split into a UI process and modular in-process services. There is one audio and inference pipeline and one central stream of immutable `CaptionEvent` values. The Windows overlay, bounded in-memory history, diagnostics view, and optional phone server consume that same event stream.

```text
VRChat.exe process tree
  -> WASAPI process loopback capture
  -> bounded PCM buffer / resampler / VAD
  -> reusable local Whisper model
  -> language detection and optional translation
  -> CaptionEvent hub
       -> Windows overlay
       -> temporary history
       -> local phone SSE stream
```

No component uploads audio or text. The phone path receives caption and diagnostic JSON only, never audio.

## Windows process-specific audio capture

Normal mode is `VRCHAT ONLY`. It will use the Windows Application Loopback API available on Windows 10 build 20348 and later:

1. Locate `VRChat.exe` and retain its PID plus process-start identity so PID reuse can be detected.
2. Call `ActivateAudioInterfaceAsync` with the virtual device path `VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK`.
3. Pass a `PROPVARIANT` containing `AUDIOCLIENT_ACTIVATION_PARAMS` with:
   - `ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`
   - `TargetProcessId = detected VRChat PID`
   - `ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE`
4. Retrieve `IAudioClient`, initialize a shared-mode event-driven capture stream, and read packets through `IAudioCaptureClient` on a dedicated background thread.
5. Copy packet data immediately into owned bounded buffers; never expose COM-owned memory to downstream workers.

This captures render streams belonging to VRChat and its child processes independent of the physical output endpoint. Other application audio is excluded. If VRChat has no active render stream, Windows supplies silence.

The implementation uses the stable `NAudio.Wasapi` 3.0.1 `WasapiRecorderBuilder.WithProcessLoopback` API, which wraps this exact activation contract, and is isolated behind `IProcessAudioCaptureService`. The app requests 48 kHz stereo 32-bit IEEE-float PCM with a 50 ms event-driven buffer and MMCSS `Audio` scheduling. The zero-copy WASAPI callback computes a meter immediately, copies owned PCM into a bounded 64-chunk channel, and never retains COM-owned memory.

An executable two-source proof validates the boundary: selecting a silent target while an unrelated process rendered a tone produced a `0.000%` peak; targeting the tone-producing process produced `14.999%`. This test is kept in `tests/VRChatTranslator.AudioProofSource` and the core test runner.

Fallback mode will use ordinary endpoint WASAPI loopback and will be labeled `SYSTEM AUDIO FALLBACK` everywhere, including diagnostics and logs. It will never be described as process-specific capture.

Sources:

- [Microsoft Application loopback audio capture sample](https://learn.microsoft.com/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)
- [Microsoft `PROCESS_LOOPBACK_MODE`](https://learn.microsoft.com/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode)
- [Microsoft `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS`](https://learn.microsoft.com/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params)
- [NAudio process-loopback recorder documentation](https://github.com/naudio/NAudio/blob/main/Docs/WasapiRecorder.md#per-process-loopback-capture)

## Speech, VAD, and translation

The selected engine is [Whisper.net](https://github.com/sandrohanea/whisper.net), a maintained .NET binding over `whisper.cpp`. It keeps the UI and pipeline in one managed process while using packaged native inference runtimes and supports multilingual Whisper models, CPU/CUDA execution, model downloading, language detection, and translation to English. This build installs and forces the verified CPU runtime; it does not label CUDA active merely because an NVIDIA GPU exists.

We will install only explicitly selected native runtime packages. This allows Ultra Light and Balanced to force CPU rather than allowing runtime probing to consume GPU resources unexpectedly. GPU Fast and Accurate may select CUDA only after a startup capability check and representative benchmark. Any GPU failure must atomically return to a known CPU configuration and report the fallback.

All normal modes use multilingual models; `.en` models are prohibited. Models are downloaded on explicit user action into `%LOCALAPPDATA%\TheLevelEverythingCompany\VRChatUniversalTranslator\models` and never committed.

Current safe mappings are:

| Mode | Initial model/backend candidate | Priority |
|---|---|---|
| Ultra Light | tiny multilingual, CPU, 2 threads, queue 2, 8 s cap | Lowest gaming impact |
| Balanced | base multilingual, CPU, 4 threads, queue 3, 12 s cap | Everyday quality with bounded load |
| Responsive CPU (enum `GpuFast`) | base multilingual, CPU fallback until CUDA is separately proven | Low-latency policy without a false GPU claim |
| Accurate | small multilingual, CPU, 6 threads, queue 3, 15 s cap | Better recognition at higher cost |
| Custom | base multilingual with safe bounded defaults | Advanced configuration foundation |

Whisper translation produces English directly from multilingual speech. When both original and translated text are requested, the pipeline may require a second decoding pass; that cost will be explicit in the UI and benchmarks.

## Pipeline and performance rules

- Convert capture data to 16 kHz mono floating-point PCM once.
- Run conservative local RMS-energy VAD before inference and expose bounded sensitivity settings. This is intentionally documented as energy VAD, not Silero.
- Keep one reusable model instance per active configuration.
- Use bounded channels with cancellation and backpressure.
- Prefer dropping stale, superseded audio over accumulating conversational delay.
- Track dropped chunks, queue age, audio level, and stage latency.
- `Protect VRChat Performance` is on by default and may reduce work frequency or recommend a lighter mode, but it never silently mislabels the active model/backend.
- A mode change drains/cancels work safely and swaps the model as a controlled state transition.

## Phone display

The phone server is embedded with ASP.NET Core/Kestrel in the desktop process; there is no second backend. It is off by default and binds to all local interfaces only after explicit activation. Kestrel serves a fixed embedded mobile UI, a health endpoint, and a read-only SSE endpoint—never arbitrary files or commands.

Server-Sent Events (SSE) is the initial live transport because captions and diagnostics are read-only, server-to-client events. It has native browser reconnection semantics and lower protocol complexity than WebSocket for this one-way stream. Browser controls such as font size and view selection remain client-local. Caption text is assigned through DOM `textContent`, never injected as HTML.

The server will expose loopback and useful physical LAN addresses, detect port conflicts, never configure port forwarding or UPnP, and never change Windows Firewall without a separate explicit user action. Optional pairing uses a short-lived PIN stored only in memory; QR codes contain the LAN URL, not the PIN.

## Settings and data boundaries

Normal UI settings are stored as JSON under the user's local application-data folder, not beside program binaries. Configuration writes use a temporary file and atomic replace where possible. Invalid files are quarantined with a clear error and safe defaults are loaded.

Caption history is memory-only and capped at 100 entries unless transcript saving is explicitly enabled in a future phase. Diagnostic logs exclude caption text and raw audio by default.

## Failure handling

Services expose explicit lifecycle states: stopped, starting, running, degraded, and faulted. Errors retain their source stage. VRChat exit cancels capture, clears PID state, and returns to detection. Audio-device and process restarts create new capture sessions. No UI indicator may report capture, model, translation, overlay, or phone availability until the corresponding service confirms it.

## External acceptance checks still required

- Run extended capture/inference against the real `VRChat.exe` audio mix, including device changes and process restarts.
- Verify Spanish, Japanese, and German speakers or test recordings through the same live capture path.
- Benchmark CPU/FPS/latency while VRChat is under representative PCVR load; current monitor and bounded policies are ready to collect the data.
- Verify CUDA toolkit/runtime packaging and VRAM impact before enabling a true CUDA backend for GPU Fast.
- Verify LAN access from a physical phone; automated loopback tests alone do not establish firewall or Wi-Fi reachability.
