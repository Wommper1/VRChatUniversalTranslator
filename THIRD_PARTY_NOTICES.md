# Third-party components

The portable app includes upstream license files in `licenses/`. OpenVR C# bindings and native DLL are pinned to Valve's v2.15.6 release; the generated bindings only add compiler nullable/warning directives. No VRChat injection or modification is used.

- NAudio: MIT — https://github.com/naudio/NAudio
- Whisper.net 1.9.1 and whisper.cpp: MIT — https://github.com/sandrohanea/whisper.net and https://github.com/ggml-org/whisper.cpp
- sherpa-onnx 1.13.8: Apache-2.0 — https://github.com/k2-fsa/sherpa-onnx
- ONNX Runtime, included by sherpa-onnx: MIT — https://github.com/microsoft/onnxruntime
- Development only, not in the portable ZIP: the Silero VAD voice-detection model `tests/VRChatTranslator.Core.Tests/vad/silero_vad.onnx` (silero-vad v4 exported to ONNX by k2-fsa), used by the test tools. MIT, Copyright (c) 2020-present Silero Team, licence text in `tests/VRChatTranslator.Core.Tests/vad/SILERO_LICENSE.txt` — https://github.com/snakers4/silero-vad
- OpenVR 2.15.6: Valve BSD-style license — https://github.com/ValveSoftware/openvr
- Microsoft .NET / ASP.NET / Windows Desktop runtime: accompanying Microsoft runtime notices apply.

Optional downloads are separate from the portable ZIP: CPython 3.13.15 from the official NuGet package (PSF license); CTranslate2 4.8.1 (MIT); SentencePiece 0.2.2 (Apache-2.0); Sacremoses 0.2.0 (MIT); subword-nmt 0.3.8 (MIT), plus their ten package dependencies (click, cloudpickle, colorama, joblib, mock, numpy, PyYAML, regex, setuptools, tqdm), each under its own license, which is recorded in the `.dist-info` folder of the installed package. The exact versions and hashes are in `scripts/requirements-translation.txt`. No system-wide Python installation is modified.

The speech models (`ggml-tiny.bin`, `ggml-base.bin`, `ggml-small.bin`) are not part of this repository or the portable ZIP; the app downloads them on request from the whisper.cpp model repository on Hugging Face. They are conversions of OpenAI's Whisper model weights, which OpenAI released under the MIT license.

The application icon is original artwork for this project and is covered by the project's MIT license.

Translation packs are selected from the official Argos package index at https://github.com/argosopentech/argospm-index and downloaded over HTTPS from argos-net.com. Package metadata/README files remain beside each downloaded model. The optional speaker model is 3D-Speaker ERes2Net from the sherpa-onnx speaker-recognition release: https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-recongition-models . Model-specific upstream licensing applies; no trained speaker identity data from this app is distributed.

Tests download public fixture recordings from whisper.cpp and csukuangfj/sr-data into ignored `artifacts/` folders. They are not shipped in the portable ZIP, and user audio is not used for those checks.

## Optional fast English recogniser

When you click **Download fast English model** in Settings, the app downloads NVIDIA's **Parakeet TDT-CTC 110M** English speech model, converted to ONNX (int8) by the sherpa-onnx project, from https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models . It is not part of this repository or the portable ZIP.

- Model: `nvidia/parakeet-tdt_ctc-110m`, Copyright (c) NVIDIA Corporation, licensed under **Creative Commons Attribution 4.0 (CC-BY-4.0)** — https://creativecommons.org/licenses/by/4.0/ . Model card: https://huggingface.co/nvidia/parakeet-tdt_ctc-110m . The ONNX conversion and int8 quantisation were made by the sherpa-onnx project (Apache-2.0); the app does not modify the model further.
- SharpZipLib 1.4.2: MIT, Copyright (c) SharpZipLib contributors — https://github.com/icsharpcode/SharpZipLib . Used only to open that one downloaded archive.

Development measurements (kept outside the repository, never shipped) also used the public FLEURS speech set from Google (CC-BY-4.0, https://huggingface.co/datasets/google/fleurs) for the language-check safety test, alongside the LibriSpeech test-clean set above.
