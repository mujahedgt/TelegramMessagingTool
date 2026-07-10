# Media Agent Tool Roadmap

## Main Points

1. Add direct voice-agent commands for saved audio: `/voicebrief <audio-file-id>` and `/voiceplan <audio-file-id>`. Status: complete.
2. Add image-agent prompt helper: `/imageprompt <image-file-id|idea>`. Status: pending.
3. Add OCR/image text extraction command: `/ocrimage <image-file-id>`. Status: pending.
4. Harden media provider diagnostics and examples for local STT/TTS/OCR providers. Status: pending.
5. Release, restart, smoke-test, and push each completed slice. Status: pending.

## Execution Order

1. Voice-agent commands. Added `/voicebrief <audio-file-id>` and `/voiceplan <audio-file-id>` so the user can transcribe, save the transcript, and receive either a concise brief or a review-only task plan without manually chaining `/transcribe` + `/transcriptinsights` or `/transcripttasks`.
2. Image-agent prompt helper. Generate safe prompts/instructions from an image file or idea without requiring image generation providers.
3. OCR/image text extraction. Keep local-provider gated and sandboxed.
4. Provider docs/diagnostics. Keep all secrets out of health/status output.
