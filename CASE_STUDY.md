# Case study: local captions across languages

## At a glance
**Alex Shade · independent, AI-assisted project.** [v1.1.5 pre-release](https://github.com/Wommper1/VRChatUniversalTranslator/releases/tag/v1.1.5), Windows 11 x64. Evidence reviewed October 28, 2026. The default branch includes later development changes; a source build and the downloadable ZIP are not necessarily identical.

## The challenge
Follow VRChat speech across languages using local recognition and captions, without a paid cloud transcription service. This is an independent project, unaffiliated with VRChat Inc.

## The investigation
[VERIFICATION.md](VERIFICATION.md) records controlled speech/crowd experiments, process-capture proof and limits of real-session testing. Overlapping voices can sharply reduce accuracy; model confidence alone did not reliably identify overlap. Those observations are documented development measurements on particular fixtures and versions, not a general accuracy guarantee.

## The approach and build
The repository contains C#/WPF desktop UI, Windows per-process audio capture via NAudio, local Whisper.net speech processing, optional local language tools, and desktop/SteamVR/phone display components. See [ARCHITECTURE.md](ARCHITECTURE.md), the project files under `src/`, and [PRIVACY.md](PRIVACY.md). Initial model downloads require internet; caption processing is local and CPU-based in this release. The optional phone view deliberately shares captions over the trusted local network.

<img src="docs/images/phone-display.png" width="260" alt="Actual phone-display interface containing made-up sample captions, not a live conversation">

*Actual interface with sample text, not a recording of recognized speech.*

Alex's contribution is concept, product direction and AI-assisted building and troubleshooting. Speech engines and other dependencies are credited by the repository; the implementation is not evidence of personally training a speech model or writing every component unaided.

## The obstacles
The verification record describes noisy-input hallucinations, multi-speaker overlap, latency tradeoffs, bounded queues, and the phone-display PIN limiter. Changes were checked against specific tests. Their presence does not mean real lobby accuracy or every device path has been accepted.

## The result and limits
The v1.1.5 verification section reports 74 automated checks, startup and phone-server self-tests, and a controlled process-capture proof. These are prior records, not new tests run for this document. A physical SteamVR headset, phone through the network, and full live-session acceptance remain open. Automatic captions and speaker labels can be wrong; the phone view is plain HTTP and is intended only for a trusted network.

The default-branch commit `01fa1005` updates sherpa-onnx and fast-English model validation after the release. Read the matching tag's documentation when assessing the v1.1.5 ZIP; do not attribute later source changes to that package.

## Try it / what comes next
Use the [download instructions](README.md#download-and-install) and [QUICK_START.md](QUICK_START.md), including the model download. Follow the consent and responsible-use guidance before captioning other people. The [acceptance checklist](ACCEPTANCE_CHECKLIST.md) is the clearest route to more useful real-world evidence; no completion date is promised.

Read [CONTRIBUTING.md](CONTRIBUTING.md) for code contributions. Use [Issues](https://github.com/Wommper1/VRChatUniversalTranslator/issues/new/choose) for redacted bug reports and [SECURITY.md](SECURITY.md) for private vulnerability reporting.

[Meet Alex](https://github.com/Wommper1) · [The Level Everything Company](https://leveleverythingcompany.github.io/)
