# Media provider configuration

Telegram media automation can use three trusted local providers:

1. **Image OCR**: extracts readable text from a selected sandboxed image file to stdout.
2. **Speech-to-text**: converts the incoming Telegram `.ogg` voice note to transcript text on stdout.
3. **Text-to-speech**: converts the bot reply text to an output audio file at `{output}`.

The app runs all providers directly with `UseShellExecute=false`; these are not shell command strings. Do not put secrets in these values.

## Required environment variables

Set these as Windows **User** environment variables on the machine that runs the bot:

```powershell
[Environment]::SetEnvironmentVariable('ENABLE_IMAGE_OCR', 'true', 'User')
[Environment]::SetEnvironmentVariable('IMAGE_OCR_COMMAND', 'C:\Tools\ocr\ocr-image.cmd', 'User')
[Environment]::SetEnvironmentVariable('IMAGE_OCR_ARGUMENTS', '"{file}"', 'User')
[Environment]::SetEnvironmentVariable('IMAGE_OCR_TIMEOUT_SECONDS', '120', 'User')

[Environment]::SetEnvironmentVariable('ENABLE_AUDIO_TRANSCRIPTION', 'true', 'User')
[Environment]::SetEnvironmentVariable('AUDIO_TRANSCRIPTION_COMMAND', 'C:\Tools\voice\transcribe-voice.cmd', 'User')
[Environment]::SetEnvironmentVariable('AUDIO_TRANSCRIPTION_ARGUMENTS', '"{file}"', 'User')
[Environment]::SetEnvironmentVariable('AUDIO_TRANSCRIPTION_TIMEOUT_SECONDS', '300', 'User')

[Environment]::SetEnvironmentVariable('ENABLE_TEXT_TO_SPEECH', 'true', 'User')
[Environment]::SetEnvironmentVariable('TEXT_TO_SPEECH_COMMAND', 'C:\Tools\voice\tts-reply.cmd', 'User')
[Environment]::SetEnvironmentVariable('TEXT_TO_SPEECH_ARGUMENTS', '"{text}" "{output}"', 'User')
[Environment]::SetEnvironmentVariable('TEXT_TO_SPEECH_TIMEOUT_SECONDS', '120', 'User')
[Environment]::SetEnvironmentVariable('TEXT_TO_SPEECH_OUTPUT_EXTENSION', '.ogg', 'User')
```

Restart the latest release after setting them. Use `/providers`, `/status`, and `/riskconfig` to verify:

```text
Image OCR: enabled, provider configured
Audio transcription: enabled
Audio provider: local command configured
Text-to-speech: enabled
TTS provider: local command configured
TTS output: .ogg
```

## Provider contract

### `ocr-image.cmd`

Input:

```text
ocr-image.cmd "C:\path\to\uploaded-image.png"
```

Required behavior:

- Exit code `0` means success.
- Write only extracted text to stdout.
- Non-zero exit code means failure; stderr/stdout is shown safely to the user.

Example wrapper around Tesseract:

```cmd
@echo off
set IMAGE=%~1
C:\Program Files\Tesseract-OCR\tesseract.exe "%IMAGE%" stdout --psm 6
```

If your provider prints banners, confidence tables, or JSON, wrap it so stdout contains only the readable text you want saved into the sandbox.

### `transcribe-voice.cmd`

Input:

```text
transcribe-voice.cmd "C:\path\to\telegram-voice.ogg"
```

Required behavior:

- Exit code `0` means success.
- Write only the transcript text to stdout.
- Non-zero exit code means failure; stderr/stdout is shown safely to the user.

Example wrapper around whisper.cpp:

```cmd
@echo off
set AUDIO=%~1
C:\Tools\whisper.cpp\build\bin\Release\whisper-cli.exe -m C:\Models\whisper\ggml-base.en.bin -f "%AUDIO%" -nt -np
```

If your provider prints headers/timestamps, wrap it so stdout contains only the final transcript.

### `tts-reply.cmd`

Input:

```text
tts-reply.cmd "reply text" "C:\temp\TelegramMessagingTool_TTS_xxx.ogg"
```

Required behavior:

- Exit code `0` means success.
- Create the exact output file path passed as argument 2.
- For Telegram voice-note bubbles, output must be OGG/Opus and `TEXT_TO_SPEECH_OUTPUT_EXTENSION=.ogg`.
- If the provider outputs MP3/WAV/M4A instead, the bot sends it as a normal audio file instead of a voice-note bubble.

Example wrapper around Piper + FFmpeg:

```cmd
@echo off
set TEXT=%~1
set OUT=%~2
set TMP=%TEMP%\telegram-tts-%RANDOM%.wav

echo %TEXT% | C:\Tools\piper\piper.exe --model C:\Models\piper\en_US-lessac-medium.onnx --output_file "%TMP%"
C:\Tools\ffmpeg\bin\ffmpeg.exe -y -i "%TMP%" -c:a libopus -b:a 32k "%OUT%" >NUL 2>NUL
del "%TMP%" >NUL 2>NUL
```

## Runtime behavior

When you run `/ocrimage <image-id>`:

1. The bot verifies the image belongs to the current chat/user and exists under the sandbox.
2. The bot runs the OCR provider against only that saved image file.
3. Extracted text is saved as a sandboxed `.txt` document with source `ocr`.
4. The reply includes follow-up commands such as `/readfile`, `/askfile`, and `/indexfile`.

When you send a Telegram voice message:

1. The bot saves it under the normal `UserFiles/<chatId>/` sandbox.
2. The bot runs the transcription provider against only that saved file.
3. The transcript is saved as a `*-transcript.txt` document and added to chat history.
4. The local Ollama agent answers using the transcript as the user message.
5. If TTS is configured, the bot synthesizes the answer.
6. If the TTS output is `.ogg`, `.oga`, or `.opus`, the bot replies with Telegram `SendVoice`; otherwise it replies with `SendAudio`.

`/speaktext <text>` still stores generated TTS output only and does not auto-send it. Use `/sendaudio <audio-file-id>` to manually send any saved sandboxed audio file back to Telegram. Use `/transcripttasks <transcript-file-id>` to turn a saved transcript into a draft task list plus a suggested `/plan ...` command without creating a task automatically. Automatic audio sending is also available as direct replies to inbound Telegram voice messages when both transcription and TTS providers are configured.
