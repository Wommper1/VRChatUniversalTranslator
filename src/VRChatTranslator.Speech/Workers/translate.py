"""Offline translation worker. JSON over private stdio; no HTTP listener or telemetry."""
import collections
import json
import pathlib
import sys

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")
# Imports are lazy so an unavailable runtime yields an actionable JSON error.
cache = collections.OrderedDict()

class BpeTokenizer:
    def __init__(self, root, metadata):
        from sacremoses import MosesTokenizer, MosesDetokenizer, MosesPunctNormalizer
        from subword_nmt.apply_bpe import BPE
        self.tokenizer = MosesTokenizer(metadata["from_code"])
        self.detokenizer = MosesDetokenizer(metadata["to_code"])
        self.normalizer = MosesPunctNormalizer(metadata["from_code"])
        with (root / "bpe.model").open(encoding="utf-8") as codes:
            self.bpe = BPE(codes)
    def encode(self, text, out_type=str):
        return self.bpe.segment_tokens(self.tokenizer.tokenize(self.normalizer.normalize(text)))
    def decode(self, tokens):
        return self.detokenizer.detokenize(" ".join(tokens).replace("@@ ", "").split(" "))

def load_pack(directory):
    import ctranslate2
    import sentencepiece
    key = str(pathlib.Path(directory).resolve())
    if key not in cache:
        root = pathlib.Path(key)
        metadata = json.loads((root / "metadata.json").read_text(encoding="utf-8"))
        tokenizer = (sentencepiece.SentencePieceProcessor(model_file=str(root / "sentencepiece.model"))
                     if (root / "sentencepiece.model").exists() else BpeTokenizer(root, metadata))
        # Float32 avoids invalid repeated-token output observed with an official BPE pack in int8 CPU mode.
        engine = ctranslate2.Translator(str(root / "model"), device="cpu", compute_type="float32", inter_threads=1, intra_threads=2)
        cache[key] = (engine, tokenizer, metadata)
        while len(cache) > 2:
            cache.popitem(last=False)
    cache.move_to_end(key)
    return cache[key]

def translate_step(text, directory):
    engine, tokenizer, metadata = load_pack(directory)
    tokens = tokenizer.encode(text, out_type=str)
    if len(tokens) > 400:
        raise ValueError("Please translate a shorter message.")
    prefix = metadata.get("target_prefix", "")
    options = {"beam_size": 2, "max_decoding_length": 512}
    if prefix:
        options["target_prefix"] = [[prefix]]
    result = engine.translate_batch([tokens], **options)[0].hypotheses[0]
    if len(result) >= 512:
        raise ValueError("Translation was too long; shorten the message.")
    if prefix and result and result[0] == prefix:
        result = result[1:]
    # Some older SentencePiece packs emit the visible word-boundary marker as text.
    return tokenizer.decode(result).replace("▁", " ").strip()

for line in sys.stdin:
    try:
        request = json.loads(line)
        packs = request["packs"]
        if not isinstance(packs, list) or not 1 <= len(packs) <= 2:
            raise ValueError("At most one pivot language is supported.")
        if request.get("preload") is True:
            for pack in packs:
                load_pack(pack)
            print(json.dumps({"ready": True}), flush=True)
            continue
        text = request["text"]
        if not isinstance(text, str) or len(text) > 4000:
            raise ValueError("Text must be at most 4000 characters.")
        for pack in packs:
            text = translate_step(text, pack)
        print(json.dumps({"text": text}, ensure_ascii=False), flush=True)
    except Exception as error:
        # Never echo input text in an error.
        print(json.dumps({"error": type(error).__name__ + ": local translation failed; check the installed language packs."}), flush=True)
