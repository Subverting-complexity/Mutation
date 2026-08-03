# Mutation

## Introduction
Mutation is a .NET productivity tool that provides configurable global hotkeys for essential accessibility and workflow tasks. It lets you toggle microphones, capture screens, run Optical Character Recognition (OCR), convert speech to text, speak text aloud, and process text with LLMs—all powered by Azure Vision Services, OpenAI, Anthropic, Deepgram, Groq, and other APIs.

## Features

### 1. Toggle Microphone Mute  
Press one hotkey to mute or unmute every enabled microphone system-wide—independent of which meeting app or input device you use. A real-time waveform visualisation shows microphone input levels, and audio beeps confirm the current mute state.

**Hotkey:** `MicrophoneToggleMuteHotKey`

### 2. Screen Capturing and OCR  
Mutation supports two OCR reading orders via Azure **Computer Vision**:

* **Natural layout** – reads top-to-bottom within each column, then left-to-right across columns. Best for newspapers, journals, brochures, or any multi-column PDF.  
* **Basic layout** – reads strictly left-to-right, top-to-bottom. Best for tables, spreadsheets, forms, invoices, or any row-oriented content.

**Hotkeys:**

| Hotkey | Description |
|--------|-------------|
| `ScreenshotHotKey` | Captures the full screen and lets you draw a rectangle with a crosshair cursor (press **Esc** to cancel); the selected region is copied to the clipboard. |
| `OcrHotKey` | OCR the clipboard image with **Natural** layout. |
| `ScreenshotOcrHotKey` | Take a screenshot and OCR it with **Natural** layout in one step. |
| `OcrLeftToRightTopToBottomHotKey` | OCR the clipboard image with **Basic** layout. |
| `ScreenshotLeftToRightTopToBottomOcrHotKey` | Take a screenshot and OCR it with **Basic** layout in one step. |
| `SendHotkeyAfterOcrOperation` | Sends a specified hotkey after OCR completes (e.g., to trigger screen reader). |

**Additional Options:**
- `InvertScreenshot` – inverts screenshot colours (useful for accessibility)
- `UseFreeTier` – respects Azure free tier limits by default
- `FreeTierPageLimit` – limits pages per PDF on free tier (default: 2)
- `MaxParallelDocuments` / `MaxParallelRequests` – concurrency controls for paid tiers
- `MaxDocumentBytes` – caps the size of a file/page sent for OCR; larger files are skipped before upload (default: 10 MB; 0 = no limit)

### 3. Speech to Text Conversion  
Press one hotkey to start recording, press it again to stop and send the audio for transcription. Supported providers:

* OpenAI Whisper family (gpt-4o-transcribe, gpt-4o-mini-transcribe)  
* Deepgram nova-3  
* Groq Whisper
* Any service exposing an OpenAI-compatible Whisper API

**Hotkeys:**

| Hotkey | Description |
|--------|-------------|
| `SpeechToTextHotKey` | Start/stop recording for transcription. |
| `SpeechToTextWithLlmProcessingHotKey` | Start/stop recording with automatic LLM processing applied. |
| `SendHotkeyAfterTranscriptionOperation` | Sends a specified hotkey after transcription completes. |

**Additional Features:**
- **Audio Session History:** Navigate through past recordings using session buttons; replay any previous recording.
- **Audio File Upload:** Transcribe existing audio or video files (MP3, WAV, M4A, AAC, FLAC, OGG, OPUS, WMA, WEBM, MP4, AVI, MKV, MOV, WMV, M4V).
- **Retry Transcription:** Re-transcribe the selected session with a different provider or prompt.
- **Dictation Insert Options:** Choose between pasting, typing (SendKeys), or clipboard-only for inserting transcriptions.

### 4. LLM Processing  
Process text through OpenAI, Anthropic, or any OpenAI-compatible endpoint. Define multiple prompts, assign each a hotkey, and optionally pin each prompt to a specific model.

**Hotkeys:**

| Hotkey | Description |
|--------|-------------|
| Per-prompt `Hotkey` | Trigger a specific prompt directly. |

**Prompt Configuration:**
- Create named prompts with custom system instructions
- Mark one prompt as "AutoRun" for the default LLM action
- Assign individual hotkeys to prompts for instant access
- Set `ModelName` on a prompt to override the default model on a per-prompt basis (must match a `Name` from `LlmSettings.Models`)

**Model Configuration:**
Each entry in `LlmSettings.Models` is an object with `Name`, `Provider` (`OpenAI` or `Anthropic`), and an optional `CustomTemperature` (leave `null` for models that only accept the API default — the request will then omit the temperature parameter). Provider API keys live in the top-level `ApiKeys` section: `OpenAiApiKey`, `AnthropicApiKey`, and `DeepgramApiKey`.

### 5. Transcript Formatting Rules  
Apply find-and-replace rules to transcripts before or instead of LLM processing:

- **Plain** – literal text replacement
- **RegEx** – regular expression matching
- **Smart** – intelligent matching (e.g., whole word boundaries)

Rules run before LLM processing, enabling pre-processing of transcribed text.

### 6. Hotkey Router  
Remap any global hotkey to another. When a "From" hotkey is pressed, Mutation sends the corresponding "To" hotkey instead. Useful for creating shortcut aliases or working around application conflicts.

**Configuration:**
```json
"HotKeyRouterSettings": {
  "Mappings": [
    { "FromHotKey": "Ctrl+Alt+1", "ToHotKey": "Ctrl+Shift+M" }
  ]
}
```

### 7. Custom Audio Feedback  
Replace the default system beeps with custom audio files for different actions:

- `BeepSuccessFile` – played on successful operations
- `BeepFailureFile` – played on errors
- `BeepStartFile` / `BeepEndFile` – for recording start/stop
- `BeepMuteFile` / `BeepUnmuteFile` – for microphone state changes

### 8. Text-to-Speech
Mutation can read text aloud — either whatever is on your clipboard or text you've selected in another app — through your default audio output. All hotkeys are global, so they work no matter which app is in front; you don't need to switch back to Mutation first. Once you know the handful of shortcuts below you can drive it like a media player: play, pause, skip ahead, jump back.

**Starting to read:**

| Hotkey | Default | Description |
|--------|---------|-------------|
| `SpeakClipboard` | `Ctrl+Shift+Alt+Q` | Copy text anywhere, then press this to read your clipboard aloud. |
| `SpeakSelectionHotKey` | `Ctrl+Shift+Q` | Highlight text in any app and press this — Mutation reads the selection without you having to copy first. |

If your clipboard is empty or holds something that can't be read (such as an image), Mutation announces that out loud rather than staying silent. For very long text (over ~5,000 characters) it first announces roughly how many minutes the reading will take.

**Pause and resume:** The `SpeakClipboard` hotkey doubles as pause/resume. Press it while reading to **pause**; press it again to **resume**, backing up a few words first for context (5 words by default, configurable). If you copied new text while paused, pressing it reads the new clipboard instead of resuming.

**Moving around the text** (sentence by sentence, like a media player's skip buttons):

| Hotkey | Default | Description |
|--------|---------|-------------|
| `SkipSentenceForwardHotKey` | `Ctrl+Shift+K` | Skip forward one sentence. Past the last sentence, announces "End of text." |
| `SkipSentenceBackwardHotKey` | `Ctrl+Shift+J` | Skip back one sentence. Just landed on a sentence? Back jumps to the previous one; settled in for a moment? Back restarts the current sentence. Before the first sentence, announces "Beginning of text." |
| `RestartFromBeginningHotKey` | `Ctrl+Shift+B` | Restart from the very beginning of the text. |

**Voice, rate, and volume:** The main window's **Voice & Speech** card lets you pick any voice installed on Windows (a short sample plays when you choose one), set the reading **Rate** from `-10` (slow) to `+10` (fast, default `8`), and set the **Volume** from 0–100%. To add more voices, install them through Windows' own speech settings and they'll appear in the dropdown.

**Tidier reading:** By default Mutation cleans text up before speaking so it sounds natural — skipping markdown clutter (`#`, `**bold**`, backticks, bullets), turning long links into "link to [site]," and expanding shorthand ("e.g." → "for example," "i.e." → "that is," "etc." → "et cetera," "vs." → "versus"). You can turn this off to hear text exactly as written.

**Customising it all:** Open **Settings** with `Ctrl+,`. The **Hotkeys** tab lets you rebind every shortcut above. The **Text to Speech** tab lets you toggle speech preprocessing, set how many words to rewind on resume (`0`–`20`, default `5`), and adjust the skip-back grace window (`250`–`5000` ms, default `1500`) that decides whether a back-press goes to the previous sentence or restarts the current one. Voice, rate, and volume live on the main window and are saved automatically.

## Getting Started
Install the .NET 10 runtime (or newer) and run **Mutation.exe**. On first launch, the app creates *Mutation.json* with sensible defaults, then shows a welcome message and automatically opens the in-app **Settings** dialog so you can add your API keys (OpenAI for dictation + LLM; Anthropic and Azure Computer Vision are optional). You can reopen Settings anytime with `Ctrl+,`.

## Configuration / Settings
All hotkeys are global and fully customisable. Below is an example covering every section a user is expected to edit. (Mutation also persists a few UI-state values such as window position/size and the active microphone — those are written automatically and you don't need to set them by hand.)

```json
{
  "AudioSettings": {
    "MicrophoneToggleMuteHotKey": "Ctrl+Shift+M",
    "EnableMicrophoneVisualization": true,
    "CustomBeepSettings": {
      "UseCustomBeeps": false,
      "BeepSuccessFile": "sounds/success.wav",
      "BeepFailureFile": "sounds/failure.wav",
      "BeepStartFile": "sounds/start.wav",
      "BeepEndFile": "sounds/end.wav",
      "BeepMuteFile": "sounds/mute.wav",
      "BeepUnmuteFile": "sounds/unmute.wav"
    }
  },

  "ApiKeys": {
    "OpenAiApiKey": "<your OpenAI key>",
    "AnthropicApiKey": "<your Anthropic key>",
    "DeepgramApiKey": "<your Deepgram key>"
  },

  "AzureComputerVisionSettings": {
    "ApiKey": "<your Azure key>",
    "Endpoint": "https://<region>.api.cognitive.microsoft.com/",
    "ScreenshotHotKey": "Ctrl+Shift+S",
    "OcrHotKey": "Ctrl+Shift+O",
    "ScreenshotOcrHotKey": "Ctrl+Shift+Q",
    "OcrLeftToRightTopToBottomHotKey": "Ctrl+Shift+L",
    "ScreenshotLeftToRightTopToBottomOcrHotKey": "Ctrl+Shift+K",
    "SendHotkeyAfterOcrOperation": "Ctrl+Alt+C",
    "InvertScreenshot": false,
    "UseFreeTier": true,
    "FreeTierPageLimit": 2,
    "MaxParallelDocuments": 2,
    "MaxParallelRequests": 4,
    "MaxDocumentBytes": 10485760
  },

  "SpeechToTextSettings": {
    "SpeechToTextHotKey": "Ctrl+Shift+T",
    "SpeechToTextWithLlmProcessingHotKey": "Ctrl+Shift+Y",
    "SendHotkeyAfterTranscriptionOperation": "Ctrl+Alt+V",
    "FileTranscriptionTimeoutSeconds": 300,
    "TempDirectory": null,
    "ActiveSpeechToTextService": "OpenAI gpt-4o-transcribe",
    "Services": [
      {
        "Name": "OpenAI gpt-4o-transcribe",
        "Provider": "OpenAi",
        "BaseDomain": "https://api.openai.com/",
        "ModelId": "gpt-4o-transcribe",
        "SpeechToTextPrompt": "Optional per-service prompt to bias transcription (e.g. domain vocabulary)."
      },
      {
        "Name": "Groq Whisper 3",
        "Provider": "OpenAi",
        "ApiKey": "<your Groq key>",
        "BaseDomain": "https://api.groq.com/openai/",
        "ModelId": "whisper-large-v3"
      },
      {
        "Name": "Deepgram Nova3",
        "Provider": "Deepgram",
        "BaseDomain": null,
        "ModelId": "nova-3"
      }
    ]
  },

  "LlmSettings": {
    "Models": [
      { "Name": "gpt-4.1",           "Provider": "OpenAI",    "CustomTemperature": null },
      { "Name": "claude-sonnet-4-6", "Provider": "Anthropic", "CustomTemperature": null }
    ],
    "Prompts": [
      {
        "Id": 1,
        "Name": "Fix Grammar",
        "Content": "Fix grammar and punctuation in the following text.",
        "Hotkey": "Ctrl+Alt+G",
        "AutoRun": true,
        "ModelName": "gpt-4.1",
        "FastMode": false
      }
    ]
  },

  "TranscriptFormatRules": [
    { "Find": "um", "ReplaceWith": "", "CaseSensitive": false, "MatchType": "Smart" }
  ],

  "TextToSpeechSettings": {
    "SpeakClipboard": "Ctrl+Shift+P"
  },

  "HotKeyRouterSettings": {
    "Mappings": [
      { "FromHotKey": "Ctrl+Alt+1", "ToHotKey": "Ctrl+Shift+M" }
    ]
  },

  "MainWindowUiSettings": {
    "MaxTextBoxLineCount": 5,
    "DictationInsertPreference": "Paste"
  }
}
```

> **Note on transcript formatting rules:** `TranscriptFormatRules` is a top-level setting. Rules run as a pre-processing pass on the transcript before any LLM processing is applied.

> **Note on prompt `Id`:** it identifies the prompt to the rest of the app and must be unique. If you add prompts by hand and leave `Id` out (or repeat one), Mutation fills in the missing values on the next start and saves them back, so you do not have to keep track of them yourself.

> **Note on `FastMode`:** per-prompt, off by default, and also available as a **Fast mode** check box in the prompt editor. It runs the same model faster — same weights, same answer quality — but is billed at roughly **twice** the standard input and output token price, so turn it on only where latency actually costs you something. Not every model offers Fast mode, and on Anthropic (Claude) models it additionally needs research-preview access on your account. Whenever Fast mode can't be used, the prompt still returns a result at standard speed and Mutation announces why — whether you need to request access, pick a different model, or just try again later.

### Provisioning Azure Computer Vision

1. Sign in to the [Azure Portal](https://portal.azure.com).
2. **Create a resource** → search for **Computer Vision**.
3. Choose your subscription, resource group, region, name, and pricing tier.
4. After deployment, copy the **Key** and **Endpoint** into `AzureComputerVisionSettings` and restart Mutation.

### Provisioning Speech-to-Text Providers

* Follow each provider's portal to create an account and API key.
* Paste OpenAI/Whisper and Deepgram keys into the top-level `ApiKeys` section (`OpenAiApiKey`, `DeepgramApiKey`); they are shared with the rest of the app.
* A service's own `ApiKey` under `SpeechToTextSettings → Services` is an optional override — set it only when that service needs a different key (for example a Groq endpoint that reuses the OpenAI provider).

## Contribute

Pull requests are welcome—open an issue to discuss ideas first, then fork, commit, and PR.

## License

See [License.txt](License.txt) in the repository.


## Backstory.
So I got tired of having to learn the hotkeys of all the different online meeting applications that I use for toggling the microphone on and off mute. As a visually impaired computer user, finding the microphone icon visually and clicking on it is not really a viable option. I'm a very heavy AutoHotKey user, and I first tried to build a solution with that, but it was clunky. I then asked a buddy of mine if he has some experience with manipulating the microphone with C#. He didn't, but he quickly put together something in LINQPad to toggle the microphone using the audio switcher library. I then took that code and started a little WinForms application that had the microphone toggle functionality wired up to a global hotkey, and I called it Mutation. As in, I could mute the microphone at any time I wanted, no matter which application I was busy working in. This was incredibly useful, but I once had the situation where Microsoft Teams was using my second microphone and not the main one, and so when I thought I was muted with mutation, the second mic was still active and the person on the call heard while I was talking to someone locally. Luckily, it wasn't too embarrassing. I then updated mutation to list all the detected microphones and to mute and unmute them all on the toggle. In that way, I could be sure that when I wanted it muted, it was definitely muted across my system, across all the microphones. This capability became indispensable to me in my daily usage and meetings.

Being almost blind, I have the problem, like many others in the same situation, where I could not really read any screenshots or images containing text. And those come along more often than you realize in my kind of work. So, what I did was to provision myself a free Microsoft Computer Vision resource on my Azure subscription and wired up a hotkey that grabs an image from the clipboard, performs OCR on it, and puts the text back on the clipboard. Suddenly, Mutation became even more useful. This worked great for images that came our way over emails or instant messages, etc., but if I wanted to create my own screenshot of a portion of the screen, I still had to use a third-party application to put the screenshot on the clipboard. I decided, why can't mutation do that for me as well? So I extended it with the capability, again wired up to a hotkey, to take a screenshot of the entire application and then allow a rectangle selection with the mouse. At the end of the mouse drag, the image would be copied automatically onto the clipboard. I added a second hotkey that combined the screenshot and the OCR into an automated process. Now I could press a hotkey, select a rectangle on the screen, OCR was automatically performed and the text was placed on the clipboard. At which point I can just press another hotkey to read the contents of the clipboard with my screen reader.

Being extremely impressed with the OpenAI Whisper model's capability of speech-to-text while using the ChatGPT app on my iPhone, I wanted to start using it on my desktop as well. I tried using the OpenAI Whisper model on my local computer for dictation. I downloaded an application called Buzz that wrapped the model. Unfortunately, using the smaller models did not have very accurate transcription and using the larger models was unbelievably slow on my development workstation.
So I decided to wire up Mutation to record an MP3 when I press a hotkey, and then send that MP3 to the OpenAI Whisper API for transcription, and then to put the text back on the clipboard, at which point it's again available for my screen reader, or just to paste into a document. Typically, this is very fast for dictating a couple of sentences. It only takes one to three seconds to come back with the text.
In fact, I'm using mutation and whisper to dictate this entire backstory of mutation. This feature is quite the productivity booster. I find it saves me a lot of time, as for a lot of messages, even short messages, like on WhatsApp or Slack, it's much faster to speak them and then paste the resulting text than to type it out.

I don't think many people will use mutation, but I'm sure there will be a few that will find the kind of productivity boosting that it can give incredibly useful. and thus the open-source project was born.
For myself, it is absolutely indispensable, and I could not go a day without it anymore. I will add to it as I think of more tools to make my life easier.

Here's hoping it helps somebody else as well.
