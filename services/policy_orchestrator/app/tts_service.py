from __future__ import annotations

import base64
import io
import os
import threading
import time
import wave
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, Optional, Tuple

import numpy as np


@dataclass(frozen=True)
class TtsConfig:
    enabled: bool
    default_language: str
    default_voice_id: str
    default_quantize: bool
    max_text_chars: int
    warmup_on_start: bool

    @staticmethod
    def from_env() -> "TtsConfig":
        return TtsConfig(
            enabled=_read_bool("TTS_ENABLED", default=False),
            default_language=os.getenv("TTS_LANGUAGE", "english").strip() or "english",
            default_voice_id=os.getenv("TTS_DEFAULT_VOICE", "alba").strip() or "alba",
            default_quantize=_read_bool("TTS_QUANTIZE", default=True),
            max_text_chars=max(32, _read_int("TTS_MAX_TEXT_CHARS", default=280)),
            warmup_on_start=_read_bool("TTS_WARMUP_ON_START", default=True),
        )


class PocketTtsService:
    def __init__(self, config: Optional[TtsConfig] = None) -> None:
        self._config = config or TtsConfig.from_env()
        self._lock = threading.Lock()
        self._models: Dict[Tuple[str, bool], object] = {}
        self._voice_states: Dict[Tuple[str, bool, str], object] = {}

    @property
    def enabled(self) -> bool:
        return self._config.enabled

    @property
    def warmup_on_start(self) -> bool:
        return self._config.warmup_on_start

    def warmup(self) -> None:
        if not self._config.enabled:
            return
        self.synthesize(
            text="Warmup.",
            voice_id=self._config.default_voice_id,
            language=self._config.default_language,
            quantize=self._config.default_quantize,
            speaker_role="system",
        )

    def synthesize(
        self,
        *,
        text: str,
        voice_id: str,
        language: str,
        quantize: bool,
        speaker_role: str,
    ) -> dict:
        if not self._config.enabled:
            raise ValueError("tts_disabled")
        clean_text = (text or "").strip()
        if not clean_text:
            raise ValueError("empty_text")
        if len(clean_text) > self._config.max_text_chars:
            raise ValueError("text_too_long")

        chosen_language = (language or self._config.default_language).strip() or self._config.default_language
        chosen_voice = (voice_id or self._config.default_voice_id).strip() or self._config.default_voice_id
        use_quantize = bool(quantize)

        model_key = (chosen_language, use_quantize)
        voice_key = (chosen_language, use_quantize, chosen_voice)

        started = time.perf_counter()
        with self._lock:
            model = self._models.get(model_key)
            if model is None:
                model = self._load_model(chosen_language, use_quantize)
                self._models[model_key] = model

            voice_state = self._voice_states.get(voice_key)
            if voice_state is None:
                voice_state = model.get_state_for_audio_prompt(self._resolve_voice(chosen_voice))
                self._voice_states[voice_key] = voice_state

        first_chunk_started = time.perf_counter()
        chunks = list(model.generate_audio_stream(voice_state, clean_text))
        if not chunks:
            raise RuntimeError("tts_empty_audio")

        first_chunk_ms = int(max(0.0, (time.perf_counter() - first_chunk_started) * 1000.0))
        audio = _concat_chunks(chunks)
        synth_ms = int(max(0.0, (time.perf_counter() - started) * 1000.0))
        duration_sec = max(0.001, float(audio.shape[-1]) / float(model.sample_rate))
        rtf = round(duration_sec / max(0.001, synth_ms / 1000.0), 3)
        wav_bytes = _to_wav_bytes(audio, int(model.sample_rate))

        return {
            "speakerRole": speaker_role,
            "sampleRate": int(model.sample_rate),
            "audioFormat": "wav",
            "audioBase64": base64.b64encode(wav_bytes).decode("ascii"),
            "synthesisMs": synth_ms,
            "rtf": rtf,
            "timeToFirstChunkMs": first_chunk_ms,
        }

    def stats(self) -> dict:
        return {
            "enabled": self._config.enabled,
            "loadedModels": len(self._models),
            "cachedVoices": len(self._voice_states),
            "defaultLanguage": self._config.default_language,
            "defaultVoiceId": self._config.default_voice_id,
            "maxTextChars": self._config.max_text_chars,
        }

    @staticmethod
    def _load_model(language: str, quantize: bool):
        try:
            from pocket_tts import TTSModel  # type: ignore
        except Exception as ex:  # pragma: no cover - exercised in integration only
            raise RuntimeError(f"pocket_tts_import_failed:{ex}") from ex
        return TTSModel.load_model(language=language, quantize=quantize)

    @staticmethod
    def _resolve_voice(voice_id: str) -> str:
        trimmed = voice_id.strip()
        if trimmed.endswith(".safetensors") or trimmed.endswith(".wav") or trimmed.endswith(".mp3"):
            expanded = Path(trimmed).expanduser()
            if expanded.exists():
                return str(expanded)
        return trimmed


def _concat_chunks(chunks: Iterable[object]):
    import torch

    if isinstance(chunks, list):
        return torch.cat(chunks, dim=0)
    return torch.cat(list(chunks), dim=0)


def _to_wav_bytes(audio_tensor, sample_rate: int) -> bytes:
    arr = audio_tensor.detach().cpu().numpy().astype(np.float32)
    arr = np.clip(arr, -1.0, 1.0)
    pcm16 = (arr * 32767.0).astype(np.int16)
    bio = io.BytesIO()
    with wave.open(bio, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        wf.writeframes(pcm16.tobytes())
    return bio.getvalue()


def _read_bool(name: str, *, default: bool) -> bool:
    value = os.getenv(name)
    if value is None:
        return default
    norm = value.strip().lower()
    return norm in {"1", "true", "yes", "on"}


def _read_int(name: str, *, default: int) -> int:
    value = os.getenv(name)
    if value is None:
        return default
    try:
        return int(value.strip())
    except ValueError:
        return default
