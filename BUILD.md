# Build and implementation plan

Development proceeds in evidence-gated phases. A phase is complete only after its stated observable result is tested; future service states remain visibly unavailable until then.

## Phase 1 — Foundation (complete)

- Initialize the repository and modular .NET/WPF solution.
- Record verified architecture and privacy boundaries.
- Create a calm, scalable control-window shell.
- Define central settings, diagnostics, performance modes, and caption-event models.
- Build and run a non-interactive application startup smoke test.

Exit evidence: clean restore/build, smoke process exit code 0, and no falsely active pipeline indicators.

## Phase 2 — VRChat process detection (complete)

- Poll safely for `VRChat.exe`, validate process identity, expose PID and lifecycle events.
- Display detected/waiting states and add unit tests around selection/restart behavior.

Exit evidence: the detector refreshes immediately and every second, selects the newest valid process, retains PID/start identity, suppresses duplicate events, follows restarts/exits, surfaces scan failures, and passes its deterministic test suite. The WPF shell displays the confirmed state without enabling audio capture.

## Phase 3 — Process audio capture (complete)

- Implement Application Loopback process capture and reserve the explicit `SYSTEM AUDIO FALLBACK` label if a fallback is ever required.
- Add start/stop, audio format diagnostics, level metering, bounded buffers, and capture proofs.
- Prove another audio-producing process is excluded from `VRCHAT ONLY` mode.

Implementation evidence on the target Windows 11 PC:

- Stable `NAudio.Wasapi` 3.2.0 calls `ActivateAudioInterfaceAsync` with the target PID and `IncludeTargetProcessTree`.
- Capture negotiates 48 kHz, stereo, 32-bit IEEE float with a 50 ms event-driven buffer.
- PCM enters a newest-first bounded 64-chunk channel; dropped chunks are counted instead of allowing unlimited backlog.
- An automated two-source proof selected a silent process while an unrelated process played a tone: captured peak remained `0.000%`.
- The selected process then played its own tone: captured peak measured `14.999%`.
- This proves the normal mode is process-specific on this PC. No system-audio fallback is currently needed or silently substituted.

## Phase 4 — Debug audio snapshot (complete)

- Add an explicitly developer-only, user-triggered ten-second WAV snapshot of exactly the normalized inference input.
- Never record continuously.

Exit evidence: the explicit button subscribes to the exact inference PCM stream for ten seconds, writes an atomic 48 kHz stereo float WAV, and an automated header/length test proves the requested duration.

## Phases 5–7 — Local speech pipeline (complete)

- Add explicit Whisper.net CPU runtime and multilingual model management.
- Add VAD, language detection, transcription, English translation, vocabulary hints where supported, cancellation, and measured latency.
- Keep CUDA optional; this machine currently runs the verified CPU backend and reports that truthfully.

Exit evidence: multilingual `ggml-base.bin` was downloaded to local application data and Whisper.net CPU translated the official JFK sample exactly. Automatic language detection, energy VAD, English translation, deduplication, latency, bounded queues, dropped-segment counts, and reusable processors are integrated. A physical multilingual VRChat session remains an external acceptance test; it is not replaced by the English sample.

## Phase 8 — Caption consumers (complete)

- Implement the transparent Windows overlay and bounded central history.
- Implement the opt-in Kestrel phone server, SSE stream, responsive caption/conversation/dual/diagnostic views, optional PIN protocol support, fullscreen, and Screen Wake Lock control.
- Use a fake-caption generator to prove both displays consume the same event.

Exit evidence: a central 100-item in-memory hub feeds the WPF view, click-through overlay, history replay, and SSE clients. The automated phone test covers startup, LAN listener, caption broadcast, reconnect/history, JSON/DOM escaping, port conflict, and clean shutdown. Physical-phone firewall/Wi-Fi validation is still required before claiming phone hardware verification. QR generation is deferred because the LAN URL is already directly displayed and avoiding another dependency is preferable for this build.

## Phase 9 — Performance (complete)

- Benchmark modes on the target PC while VRChat is active.
- Map Ultra Light to multilingual tiny/2 CPU threads, Balanced to multilingual base/4 CPU threads, Responsive CPU (internal enum `GpuFast`, kept for settings compatibility) to base on CPU with shorter utterances; no GPU backend is enabled, Accurate to multilingual small/6 CPU threads, and Custom to bounded safe defaults.
- Monitor VRChat, translator, and system CPU; translator/system RAM; NVIDIA utilization/VRAM; queue depth; and caption latency.
- Keep the inference queue at 1–5 entries by profile and drop oldest/stale work under backlog.

The base CPU backend passed an offline inference run and a real VRChat conversation on the target PC. User testing confirmed process capture and live captions work in-game. The follow-up responsiveness pass removed a redundant language-detection inference, shortened end-of-speech detection, bounded the Balanced queue at two, and stabilized language labels across noisy chunks.

Representative cold CLI evidence on this PC: loading `ggml-base.bin` and translating the official Whisper JFK WAV completed successfully in 2.81 seconds wall-clock. This includes process/model startup and is not presented as steady-state live-caption latency; the in-app processor remains loaded between utterances.

## Phase 10 — Polish and packaging (complete)

- Settings persist atomically under local application data, including mode and overlay placement/size/lock state.
- Malformed settings recover to privacy-safe defaults. Local logs record lifecycle/errors/metrics categories without conversation text.
- Normal close awaits server, inference, capture, detector, and monitor shutdown before the process exits.
- `scripts/setup.ps1`, `scripts/run.ps1`, and `scripts/build-release.ps1` cover setup, launch, and self-contained win-x64 publication with a ready-to-extract ZIP.
- Version is `1.0.0`. The application carries its .NET runtime, while the multilingual speech model remains a one-click first-run download.
- A small SVG icon asset is included as the packaging placeholder.
