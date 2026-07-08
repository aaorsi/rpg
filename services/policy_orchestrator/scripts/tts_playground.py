#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import re
import subprocess
import sys
import time
import wave
from pathlib import Path
from typing import Iterable

import numpy as np
import torch
from pocket_tts import TTSModel

BUILTIN_ENGLISH_VOICES = [
    "alba",
    "anna",
    "azelma",
    "bill_boerst",
    "caro_davy",
    "charles",
    "cosette",
    "eponine",
    "eve",
    "fantine",
    "george",
    "jane",
    "jean",
    "javert",
    "marius",
    "mary",
    "michael",
    "paul",
    "peter_yearsley",
    "stuart_bell",
    "vera",
]

VOICE_FILE_EXTENSIONS = {".wav", ".mp3", ".safetensors"}
DEFAULT_OUTPUT_DIR = Path("tts_outputs")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Interactive Pocket TTS playground. Select a voice (built-in or sample file), "
            "type text, and generate WAV files."
        )
    )
    parser.add_argument("--language", default="english", help="Pocket TTS language (default: english)")
    parser.add_argument(
        "--quantize",
        default=True,
        type=parse_bool,
        help="Use quantized model true/false (default: true)",
    )
    parser.add_argument(
        "--output-dir",
        default=str(DEFAULT_OUTPUT_DIR),
        help=f"Directory for generated WAV files (default: {DEFAULT_OUTPUT_DIR})",
    )
    parser.add_argument(
        "--voice",
        default="alba",
        help="Initial voice id (built-in) or voice sample file path",
    )
    parser.add_argument(
        "--no-autoplay",
        action="store_true",
        help="Disable automatic audio playback after synthesis",
    )
    return parser.parse_args()


def parse_bool(value: str) -> bool:
    normalized = str(value).strip().lower()
    if normalized in {"1", "true", "yes", "on", "y"}:
        return True
    if normalized in {"0", "false", "no", "off", "n"}:
        return False
    raise argparse.ArgumentTypeError(f"invalid boolean value: {value}")


def normalize_voice_choice(raw: str) -> str:
    value = (raw or "").strip()
    if not value:
        return ""

    expanded = Path(value).expanduser()
    if expanded.suffix.lower() in VOICE_FILE_EXTENSIONS:
        return str(expanded)
    return value


def validate_voice_choice(voice: str) -> tuple[bool, str]:
    if not voice:
        return False, "Voice cannot be empty."
    if voice in BUILTIN_ENGLISH_VOICES:
        return True, ""

    path = Path(voice).expanduser()
    if not path.exists():
        return False, f"Voice sample file not found: {path}"
    if path.suffix.lower() not in VOICE_FILE_EXTENSIONS:
        return False, "Voice sample must end with .wav, .mp3, or .safetensors"
    return True, ""


def sanitize_for_filename(value: str) -> str:
    cleaned = re.sub(r"[^a-zA-Z0-9_-]+", "_", value.strip())
    return cleaned[:40].strip("_") or "voice"


def concat_chunks(chunks: Iterable[torch.Tensor]) -> torch.Tensor:
    chunk_list = list(chunks)
    if not chunk_list:
        raise RuntimeError("No audio chunks were produced.")
    return torch.cat(chunk_list, dim=0)


def save_wav(path: Path, audio_tensor: torch.Tensor, sample_rate: int) -> None:
    arr = audio_tensor.detach().cpu().numpy().astype(np.float32)
    arr = np.clip(arr, -1.0, 1.0)
    pcm16 = (arr * 32767.0).astype(np.int16)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        wf.writeframes(pcm16.tobytes())


def maybe_autoplay(path: Path, enabled: bool) -> None:
    if not enabled:
        return
    if sys.platform != "darwin":
        return
    try:
        subprocess.run(["afplay", str(path)], check=False)
    except Exception:
        pass


def print_help() -> None:
    print(
        "\nCommands:\n"
        "  /voices                 List built-in voices\n"
        "  /voice <id-or-path>     Switch active voice\n"
        "  /lang <language>        Reload model with another language\n"
        "  /quant <true|false>     Reload model with quantization on/off\n"
        "  /autoplay <on|off>      Toggle automatic playback\n"
        "  /help                   Show commands\n"
        "  /quit                   Exit\n"
    )


def main() -> int:
    args = parse_args()
    output_dir = Path(args.output_dir).expanduser()
    output_dir.mkdir(parents=True, exist_ok=True)

    active_voice = normalize_voice_choice(args.voice)
    ok, err = validate_voice_choice(active_voice)
    if not ok:
        print(f"[error] {err}")
        return 1

    autoplay = not args.no_autoplay
    language = args.language.strip() or "english"
    quantize = bool(args.quantize)

    print(f"Loading Pocket TTS model (language={language}, quantize={quantize}) ...")
    model = TTSModel.load_model(language=language, quantize=quantize)
    voice_state = model.get_state_for_audio_prompt(active_voice)
    print("Model loaded.")

    print_help()
    print(f"Active voice: {active_voice}")
    if autoplay and sys.platform == "darwin":
        print("Autoplay: ON (uses afplay)")
    else:
        print("Autoplay: OFF")

    while True:
        raw = input("\nText (or /command): ").strip()
        if not raw:
            continue
        if raw == "/quit":
            print("Bye.")
            return 0
        if raw == "/help":
            print_help()
            continue
        if raw == "/voices":
            print("\nBuilt-in voices:")
            for idx, voice in enumerate(BUILTIN_ENGLISH_VOICES, start=1):
                marker = " *" if voice == active_voice else ""
                print(f"  {idx:>2}. {voice}{marker}")
            continue
        if raw.startswith("/voice "):
            candidate = normalize_voice_choice(raw[len("/voice ") :])
            ok, err = validate_voice_choice(candidate)
            if not ok:
                print(f"[error] {err}")
                continue
            try:
                voice_state = model.get_state_for_audio_prompt(candidate)
            except Exception as ex:
                print(f"[error] failed loading voice state: {ex}")
                continue
            active_voice = candidate
            print(f"Active voice changed to: {active_voice}")
            continue
        if raw.startswith("/lang "):
            new_lang = raw[len("/lang ") :].strip()
            if not new_lang:
                print("[error] language cannot be empty")
                continue
            language = new_lang
            try:
                print(f"Reloading model (language={language}, quantize={quantize}) ...")
                model = TTSModel.load_model(language=language, quantize=quantize)
                voice_state = model.get_state_for_audio_prompt(active_voice)
                print("Model reloaded.")
            except Exception as ex:
                print(f"[error] failed reloading model: {ex}")
            continue
        if raw.startswith("/quant "):
            val = raw[len("/quant ") :].strip()
            try:
                quantize = parse_bool(val)
            except Exception:
                print("[error] use /quant true or /quant false")
                continue
            try:
                print(f"Reloading model (language={language}, quantize={quantize}) ...")
                model = TTSModel.load_model(language=language, quantize=quantize)
                voice_state = model.get_state_for_audio_prompt(active_voice)
                print("Model reloaded.")
            except Exception as ex:
                print(f"[error] failed reloading model: {ex}")
            continue
        if raw.startswith("/autoplay "):
            val = raw[len("/autoplay ") :].strip().lower()
            if val in {"on", "true", "1", "yes"}:
                autoplay = True
            elif val in {"off", "false", "0", "no"}:
                autoplay = False
            else:
                print("[error] use /autoplay on|off")
                continue
            print(f"Autoplay now {'ON' if autoplay else 'OFF'}")
            continue
        if raw.startswith("/"):
            print("[error] unknown command. Use /help")
            continue

        text = raw
        started = time.perf_counter()
        try:
            chunks = model.generate_audio_stream(voice_state, text)
            audio = concat_chunks(chunks)
        except Exception as ex:
            print(f"[error] synthesis failed: {ex}")
            continue

        elapsed_ms = int((time.perf_counter() - started) * 1000.0)
        timestamp = dt.datetime.now().strftime("%Y%m%d_%H%M%S")
        voice_name = sanitize_for_filename(Path(active_voice).stem if "/" in active_voice else active_voice)
        out_path = output_dir / f"{timestamp}_{voice_name}.wav"
        save_wav(out_path, audio, int(model.sample_rate))

        duration_sec = float(audio.shape[-1]) / float(model.sample_rate)
        rtf = duration_sec / max(0.001, elapsed_ms / 1000.0)
        print(
            f"[ok] saved {out_path} | sample_rate={model.sample_rate} "
            f"| synth_ms={elapsed_ms} | duration_s={duration_sec:.2f} | rtf={rtf:.3f}"
        )
        maybe_autoplay(out_path, autoplay)


if __name__ == "__main__":
    raise SystemExit(main())
