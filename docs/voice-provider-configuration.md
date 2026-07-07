# Voice provider configuration

Telegram voice-message automation needs two trusted local providers:

1. **Speech-to-text**: converts the incoming Telegram `.ogg` voice note to transcript text on stdout.
2. **Text-to-speech**: converts the bot reply text to an output audio file at `{output}`.

The app runs both providers directly with `UseShellExecute=false`; these are not shell command strings. Do not put secrets in these values.

## Required environment variables

Set these as Windows **User** environment variables on the machine that runs the bot:

```powershell
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

Restart the latest release after setting them. Use `/status` and `/riskconfig` to verify:

```text
Audio transcription: enabled
Audio provider: local command configured
Text-to-speech: enabled
TTS provider: local command configured
TTS output: .ogg
```

## Provider contract

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

When you send a Telegram voice message:

1. The bot saves it under the normal `UserFiles/<chatId>/` sandbox.
2. The bot runs the transcription provider against only that saved file.
3. The transcript is saved as a `*-transcript.txt` document and added to chat history.
4. The local Ollama agent answers using the transcript as the user message.
5. If TTS is configured, the bot synthesizes the answer.
6. If the TTS output is `.ogg`, `.oga`, or `.opus`, the bot replies with Telegram `SendVoice`; otherwise it replies with `SendAudio`.

`/speaktext <text>` still stores generated TTS output only and does not auto-send it. Automatic audio sending is limited to direct replies to inbound Telegram voice messages.
