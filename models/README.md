# Local models

Model binaries are deliberately ignored by Git and are not stored in this repository. The in-app model manager downloads the selected multilingual model with progress and an atomic temporary file into:

`%LOCALAPPDATA%\TheLevelEverythingCompany\VRChatUniversalTranslator\models`

Current mode files are `ggml-tiny.bin`, `ggml-base.bin`, and `ggml-small.bin`. Balanced/base is the verified default. Deletion and a full installed-model inventory UI remain future model-management conveniences; they are not required for local captioning.

Only multilingual Whisper models will be offered for normal modes. Do not place private recordings in this directory.
