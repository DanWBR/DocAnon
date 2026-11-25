"""Prepares a Hugging Face token-classification model for DocAnon.

The script takes a folder that holds either an ONNX export (model.onnx) or a
PyTorch checkpoint, quantizes the ONNX graph to int8 and writes the files that
DocAnon.Ner loads:

    models/ner/model.onnx       int8 dynamic-quantized graph
    models/ner/vocab.txt        WordPiece vocabulary
    models/ner/ner-model.json   labels, tokenizer options and provenance

Usage:
    python tools/export_ner_model.py --source models/ner/source --out models/ner \
        --model-id Davlan/bert-base-multilingual-cased-ner-hrl --license afl-3.0

Only BERT-family models with a WordPiece vocabulary are supported, because
DocAnon tokenizes with Microsoft.ML.Tokenizers.BertTokenizer.
"""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path


def export_from_pytorch(source: Path, target: Path) -> None:
    import torch
    from transformers import AutoModelForTokenClassification

    model = AutoModelForTokenClassification.from_pretrained(source)
    model.eval()
    dummy = torch.ones((1, 16), dtype=torch.long)
    torch.onnx.export(
        model,
        (dummy, dummy, torch.zeros_like(dummy)),
        str(target),
        input_names=["input_ids", "attention_mask", "token_type_ids"],
        output_names=["logits"],
        dynamic_axes={
            "input_ids": {0: "batch", 1: "sequence"},
            "attention_mask": {0: "batch", 1: "sequence"},
            "token_type_ids": {0: "batch", 1: "sequence"},
            "logits": {0: "batch", 1: "sequence"},
        },
        opset_version=17,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--model-id", default="")
    parser.add_argument("--license", default="")
    parser.add_argument("--no-quantize", action="store_true")
    args = parser.parse_args()

    source: Path = args.source
    out: Path = args.out
    out.mkdir(parents=True, exist_ok=True)

    config = json.loads((source / "config.json").read_text(encoding="utf-8"))
    tokenizer_config_path = source / "tokenizer_config.json"
    tokenizer_config = (
        json.loads(tokenizer_config_path.read_text(encoding="utf-8")) if tokenizer_config_path.exists() else {}
    )
    if config.get("model_type") not in ("bert", "distilbert"):
        print(f"model_type {config.get('model_type')!r} is not a WordPiece model", file=sys.stderr)
        return 1

    float_model = source / "model.onnx"
    if not float_model.exists():
        print("model.onnx is missing, exporting from the PyTorch checkpoint")
        export_from_pytorch(source, float_model)

    target_model = out / "model.onnx"
    if args.no_quantize:
        shutil.copyfile(float_model, target_model)
    else:
        from onnxruntime.quantization import QuantType, quantize_dynamic

        print("quantizing to int8 ...")
        quantize_dynamic(str(float_model), str(target_model), weight_type=QuantType.QInt8)

    shutil.copyfile(source / "vocab.txt", out / "vocab.txt")

    id2label = {int(k): v for k, v in config["id2label"].items()}
    descriptor = {
        "modelFile": "model.onnx",
        "vocabFile": "vocab.txt",
        "labels": [id2label[i] for i in range(len(id2label))],
        "lowerCase": bool(tokenizer_config.get("do_lower_case", False)),
        "maxSequenceLength": int(config.get("max_position_embeddings", 512)),
        "usesTokenTypeIds": config.get("model_type") == "bert",
        "labelMap": {"PER": "PESSOA", "ORG": "EMPRESA", "LOC": "LOCAL"},
        "source": args.model_id,
        "license": args.license,
        "quantized": not args.no_quantize,
    }
    (out / "ner-model.json").write_text(json.dumps(descriptor, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"wrote {target_model} ({target_model.stat().st_size / 1e6:.0f} MB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
