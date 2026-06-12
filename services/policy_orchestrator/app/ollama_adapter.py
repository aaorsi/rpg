from __future__ import annotations

from typing import Any, Dict, List, Optional

import httpx

from .models import MessageDto


class OllamaAdapter:
    """Thin chat wrapper around a shared, reusable httpx client."""

    def __init__(self, client: httpx.AsyncClient, timeout_seconds: int = 120) -> None:
        self._client = client
        self._timeout_seconds = timeout_seconds

    async def chat(
        self,
        base_url: str,
        model: str,
        messages: List[MessageDto],
        api_token: Optional[str] = None,
        max_tokens: int = 512,
    ) -> str:
        root = (base_url or "http://127.0.0.1:11434").rstrip("/")
        url = f"{root}/api/chat"
        payload: Dict[str, Any] = {
            "model": model,
            "messages": [{"role": m.role, "content": m.content} for m in messages],
            "stream": False,
            "options": {"num_predict": max_tokens},
        }
        headers = {"Content-Type": "application/json"}
        if api_token:
            headers["Authorization"] = f"Bearer {api_token}"
        response = await self._client.post(
            url, json=payload, headers=headers, timeout=self._timeout_seconds
        )
        response.raise_for_status()
        obj = response.json()
        msg = obj.get("message") or {}
        content = msg.get("content") or msg.get("thinking") or ""
        return content.strip()
