# A tour of the main window

Most of the time you will not look at Mutation at all. You press a keyboard shortcut in whatever app you are using, and Mutation does its job in the background. But there is a main window, and it is worth knowing your way around it.

The window is titled **Mutation Workspace**. Think of it as the control panel. Everything on it is also available as a keyboard shortcut that works from any other app, so the window is where you go to change a setting, check a result, or run something once without remembering a shortcut.

The window is laid out as a set of cards, one per feature. On a wide window the cards sit two to a row. Narrow the window and they stack into a single column, so nothing is ever hidden off to the side.

> Everything here is reachable from the keyboard. **Tab** moves forward through the controls, **Shift+Tab** moves back, **Space** or **Enter** presses a button, and the arrow keys open and move through dropdowns. Many buttons also have an access key: hold **Alt** and the underlined letter.

---

## The menu at the top

Top left is a single **Menu** button (the three-line "hamburger" icon). Press **Alt** then **H** to open it, or just click it.

| Item | What it does |
|---|---|
| **Settings** | Opens the Settings window. Same as pressing **Ctrl+Comma** from anywhere in Mutation. |
| **Debug** | A submenu with three "Simulate ... Crash" items. These exist so the developer can test error handling. Ordinary users can ignore them entirely. |

See [The Settings window](settings.md) for what is inside Settings.

## The User guide button

Top right, opposite the menu, is a **User guide** button with the subtext "Read the
documentation". Click it — or press **Alt** then **G** — and this guide opens in your
usual web browser, at the contents page.

The guide is installed along with Mutation and lives on your own computer, so it works
with no internet connection.

---

## The Microphone card

This is the mute switch. It turns every microphone on your PC on or off at once, so you never have to hunt for the mute button in whichever meeting app you happen to be in.

| Control | What it does |
|---|---|
| **Microphone selection** (dropdown) | Chooses the input microphone. |
| **Toggle microphone** (button) | Mutes or unmutes. The icon shows a line through the microphone when you are muted. |
| **Toggle microphone visualization** | The big waveform panel is itself a button. Press it to turn the live waveform drawing on or off. When it is off it simply reads "Off". |
| Level meter | The thin bar beside the waveform. It rises and falls with how loud you are. Nothing to press. |
| **Pin input level** (on/off switch) | Keeps the Windows recording level fixed at the value you choose. Mutation re-applies it every time you record, change microphone, or start the app. |
| **Input level** (slider) | Sets the Windows recording level from 0 to 100. The change takes effect straight away, even when you are not recording. |

Full details: [Muting your microphone](microphone.md).

---

## The Speech to Text card

This is dictation. You press record, you talk, you press stop, and a few seconds later your words appear as text.

| Control | What it does |
|---|---|
| **Speech-to-text service selection** (dropdown) | Picks which transcription service does the listening. |
| **Record** (button) | Starts or stops speech capture. The transcript appears in the **Raw Transcript** box. |
| **Record and Format** (button) | Starts or stops speech capture, then automatically sends the transcript to the AI model for tidying up. |
| **Play selected session** | Plays back the recording you currently have selected. |
| **Go to older session** / **Go to newer session** | Step backwards and forwards through your recent recordings. |
| **Retry transcription** | Transcribes the selected recording again. Useful if the first attempt failed. |
| **Upload audio for transcription** | Pick an audio file from your PC and have it transcribed. |
| **Speed** (dropdown) | How fast recordings play back. Voices stay at their natural pitch at every speed. |
| **Prompt** (text box) | Optional. A few words of context or a list of names, to help the service spell things the way you want. |

Full details: [Dictation: turning speech into text](dictation.md).

---

## The LLM Prompts card

An LLM is an AI writing assistant. This card is where you keep your list of instructions for it — "make this into a polite email", "summarise this in three bullets", and so on. The card's own description says it plainly: configure multiple prompts for LLM processing.

| Control | What it does |
|---|---|
| **Add Prompt** (button) | Creates a new prompt. |
| The list | One row per prompt. Each row shows the prompt's name, its shortcut, its model, and the words **(Auto-Run)** if it is set to run by itself. |
| **(Auto-Run)** label | Means that prompt runs automatically after a recording finishes, instead of waiting for you to trigger it. |
| **Run** | Runs that prompt now, on the text in the **Raw Transcript** box. |
| **Edit** | Opens the prompt so you can change its wording, model or shortcut. |
| **Delete** | Removes the prompt. |

Full details: [Using AI prompts on your text](ai-prompts.md).

---

## The transcript boxes

This card has no heading of its own — just two labelled boxes stacked one above the other. It is where your dictated words land, and where the tidied-up version appears.

| Control | What it does |
|---|---|
| **Raw Transcript** (text box) | Exactly what came back from the transcription service. You can edit it by hand. |
| **Format with rules** (button) | Applies your own find-and-replace rules to the raw transcript. No AI involved, so it is instant. |
| **Process with LLM** (button) | Sends the raw transcript to the AI model for formatting. |
| **Formatted Transcript** (text box) | The result, ready to copy or share. |

Full details: [Automatic find-and-replace rules](transcript-formatting.md) and [Using AI prompts on your text](ai-prompts.md).

---

## The Automation card

One small but important setting: it controls where your finished transcripts are delivered. The card's own subtitle is "Control where transcripts are delivered."

| Control | What it does |
|---|---|
| **Third-party interaction mode** (dropdown) | Chooses how the text reaches the app you were working in. |

There are three choices, and the line of text underneath the dropdown explains whichever one you have picked:

- **Paste into 3rd party application** — copies the transcript and pastes it into the active application.
- **Send keys to 3rd party application** — types the transcript into the active app as if you entered it yourself.
- **Don't insert into 3rd party application** — keeps the transcript inside Mutation without sending it anywhere.

Full details: [Dictation: turning speech into text](dictation.md).

---

## The Visual Capture card

Screenshots, and pulling the text out of pictures. OCR simply means reading the words in an image so you can copy them as text.

Each button in this card shows its current keyboard shortcut in small print underneath it, so you never have to go looking.

| Control | What it does |
|---|---|
| **Screenshot to clipboard** | Grab part of the screen and put the picture on the clipboard. |
| **OCR clipboard** | Read the text out of whatever image is on the clipboard. |
| **OCR clipboard (L→R)** | The same, but reading strictly left to right and top to bottom. Use this when the normal order jumbles columns or tables. |
| **Screenshot & OCR** | Grab part of the screen and read its text, in one step. |
| **Screenshot & OCR (L→R)** | The same, in strict left-to-right, top-to-bottom order. |
| **OCR documents** | Pick several PDFs or images and read them all into one result. A progress bar appears while it works, with a **Cancel OCR** button below it. |
| **OCR result** (text box) | Where the extracted text appears. |
| **Download OCR result** | Saves that text to a document. Stays greyed out until there is something to save. |

Full details: [Screenshots and reading text from images](screen-capture-and-ocr.md).

---

## The Voice & Speech card

This is the read-aloud feature. Its subtitle says it well: speak the current clipboard contents and tune the voice.

| Control | What it does |
|---|---|
| **Voice** (dropdown) | Which voice reads to you. |
| **Rate** (slider) | How fast it speaks. |
| **Volume** (slider) | How loud, independent of your system volume. |
| **Announce reading time at start** (on/off) | Speaks an estimate of how long the text will take before it starts reading. |
| **Announce progress while reading** (on/off) | Speaks your progress at regular points through a long text. |
| **Speak clipboard** | Reads whatever you last copied. |
| **Speak selection** | Copies whatever is highlighted in the app you were in, and reads that. |
| **Restart** | Starts again from the beginning, ignoring the saved position. |
| **Skip sentence backward** / **Skip sentence forward** | Jump one sentence back or forward. |
| **Position** | Speaks where you are: the sentence, the percentage, and the time left. |
| **Speak to file** | Saves the spoken version as a WAV audio file. |

Full details: [Having text read aloud](read-aloud.md).

---

## Status messages

Mutation tells you what it is doing through a status bar that appears in the **top right** of the window, not at the bottom. It stays out of the way until there is something to say — "Transcribing", "OCR complete", an error — then slides into view. It is announced to screen readers politely, so it will not interrupt you mid-sentence. Close it with its own close button, or leave it; it goes away on its own.

---

## Two useful habits

**You rarely need this window.** Every button here has an equivalent shortcut that works from any app. The window is for setting things up and checking results. See [Keyboard shortcuts](keyboard-shortcuts.md) for the full list, and remember that every shortcut can be changed in Settings.

**The window remembers itself.** Mutation saves the window's position and size when you close it, and puts it back exactly there next time. The very first time you run it, with no saved size yet, it opens at three-quarters of your screen, centred. If you later unplug a monitor, Mutation nudges the window back onto a screen you can actually see.

---

## Where to next

- [Getting started](getting-started.md) — set up your keys and take Mutation for its first run.
- [Keyboard shortcuts](keyboard-shortcuts.md) — the shortcuts behind every button on this window.
- [The Settings window](settings.md) — everything the main window does not show.
- [Screen reader and accessibility notes](accessibility.md) — navigating Mutation without looking at it.
