# The Settings window

Everything you can change about Mutation lives in one window. This chapter walks
through it, category by category, so you know where to look.

## Opening it and finding your way around

Press **Ctrl+Comma** while Mutation's window is in front. You can also open the
menu (**Alt**, then **H**) and choose **Settings**.

The window has two halves. Down the left is a list of categories. On the right are
the settings for whichever category you have picked. Move between categories with
the arrow keys; the heading above the right-hand side always tells you which one
you are in.

At the bottom are **Save** and **Cancel**. Nothing you change takes effect until
you press **Save**. **Cancel** throws away everything you changed in that visit.
**Escape** closes the window too.

## The search box

Above the category list is a **Search settings** box. Type into it and two things
happen at once: the category list shrinks to the categories that match, and the
settings on the right hide any section that does not mention your word.

Non-matching sections are removed rather than greyed out, so they disappear from
the Tab order and from your screen reader as well as from the screen. A short line
under the search box tells you how many categories and sections matched, and it is
announced politely as you type — you never have to go hunting to find out whether
your search found anything.

Press **Escape** while you are in the search box to clear it and put everything
back.

## The Advanced switch

Bottom right there is an **Advanced** switch. It does one thing: turning it on
reveals an **Open Mutation.json** button, which opens the raw settings file in
whatever program your PC uses for that file type. Most people never need it.
Leaving Advanced off simply hides that button.

## The categories

### Audio

Your microphone and the little sounds Mutation makes.

| Setting | What it does |
|---|---|
| Microphone visualization | Shows a live waveform of the microphone input while recording. Turn it off to save a bit of CPU. |
| Toggle microphone mute | The shortcut that mutes and unmutes everything. |
| Use custom beeps | Play your own sound files instead of the built-in beeps for status cues. |
| Beep files | One file each for Success, Failure, Start, End, Mute and Unmute. **Browse...** picks a file, and the small play button lets you hear it. The low falling "waiting for the microphone" sound is built in and always plays as-is. |

### Screen capture & OCR

Reading text out of pictures. OCR just means "pulling the words out of an image".

| Setting | What it does |
|---|---|
| Azure Computer Vision API key | The long password that lets Mutation use the text-reading service on your behalf. |
| Endpoint | The web address of that service. Whoever gave you the key gives you this too. |
| Use free tier | Use the free OCR tier, which limits how many pages each document can contain. |
| Invert screenshot before OCR | Flips the image colours first, which helps a lot with light text on a dark background. |
| Max document size (MB) | Biggest file or page Mutation will send. Anything larger is skipped before upload, so you never accidentally send something huge. Set it to 0 for no limit. |
| Paste OCR text into the active app | On by default. Puts the recognised text straight into whatever you were working in. Turn it off and the text still reaches your clipboard and the **OCR result** box. |
| Send hotkey after OCR | Optional keystrokes sent to the app you are in once the text has been delivered — a screen reader command that reads it back, for example. It comes after the paste above. |
| Wiggle the mouse pointer after a capture | Off by default. Shakes the pointer one pixel back and forth when a capture finishes, then leaves it exactly where it started. Only useful if your magnifier keeps losing the pointer after a capture — see [Screen capture and OCR](screen-capture-and-ocr.md) for what that looks like. |
| Wiggle every (milliseconds) | How long between one shake and the next. 50 by default, and anything from 10 to 500. |
| Keep wiggling for (milliseconds) | How long the shaking goes on for. 500 by default, and anything from 50 to 5000. You always get at least one shake, so setting this shorter than the interval above still gives you one. |

The timeout and the "how many at a time" numbers have sensible defaults and hover
help. Leave them alone unless something is timing out on you.

### Speech to Text

Where your dictation gets turned into text.

| Setting | What it does |
|---|---|
| Service definitions | The transcription services you can pick from. Each one has a name, a provider (OpenAI Whisper or Deepgram), an optional key of its own, a model, and a prompt. |
| Per-service prompt | Optional priming text sent with each transcription to nudge the spelling of names and the punctuation. |
| Recording sessions to keep | How many past recordings stay on disk. Once you pass this, the oldest go first; the recording in progress is never deleted. Anything from 1 to 500, and 10 by default. |
| Temp directory | The folder your recordings are written to while they are being made. It has to be a full path that starts with a drive, like `D:\Recordings`. |
| Send hotkey after transcription | Optional keystrokes sent to the app you are in once your text arrives, for example **Ctrl+V** to paste. |
| Strip silent gaps from audio | Removes long silences before the audio is sent, so pauses while you think do not bloat the recording. |
| Maximum upload size (MB) | The biggest audio file Mutation sends in one go. Anything larger is broken into pieces and sent one after the other. 24 by default. |

> Adding, removing, or editing a service definition takes effect after you restart
> Mutation. The per-service prompt and your choice of active service apply straight
> away. You choose which service is active from the main window.

If the service you are using is Deepgram, the prompt can also carry a list of words you
want it to listen out for. Put them on a line of their own that starts with `keyterms:`
and separate them with commas, like this:

```
keyterms: Dr. Bosch, Mutation, WinUI
```

Everything to the end of that line is the list, so full stops inside a name are safe. A
full stop at the very end is optional and is not treated as part of the last word.

If you clear the **Temp directory** box, or type a folder name on its own like
`Recordings`, Mutation cannot save yet. It puts its own folder back in the box, plays
the failure beep, and tells you what it did. Press **Save** again to accept that
folder, or type a full path of your own. The **Browse...** button next to the box
always gives you a full path.

The three silence numbers underneath (minimum silence, threshold, edge guard) fine
tune that trimming. The defaults are good; each has hover help if you want to
experiment.

Under **Large recordings** is **Maximum upload size (MB)**. Transcription services
refuse anything over about 25 MB in one request, which a long meeting recording will
sail past. When a recording is bigger than the number in this box, Mutation splits it
into pieces, sends them one after the other, and joins the results back together in
order, so you still get one transcript. It picks the splitting points where a long
pause was taken out, so a break falls between sentences rather than in the middle of
a word.

24 is the default and suits OpenAI. Set it to 0 if you never want a recording split,
for example with a service that accepts larger uploads. Otherwise the box takes
anything from 1 to 1000. If you type something smaller than 1, Mutation puts 1 back
in the box, because a piece any smaller than that is no use to a transcription
service.

### API keys

Your keys in one place. An API key is a long password that lets Mutation talk to a
service on your behalf.

| Setting | What it does |
|---|---|
| OpenAI API key | Used for OpenAI's AI models and for OpenAI/Whisper dictation. |
| Anthropic API key | Used for Anthropic (Claude) AI models. |
| Deepgram API key | Used for Deepgram dictation. |

A speech service can override its key from the Speech to Text section. Otherwise
the key here is the one used.

### AI assistance

The AI models Mutation can send your text to.

| Setting | What it does |
|---|---|
| Request timeout (seconds) | How long to wait for the model to answer before giving up. |
| Retries | How many times to try again after a failed request. Retries help the very first request after a reboot succeed while the network warms up. Three by default. |
| Models | Your list of models. Each row has a name, a provider, and an optional temperature — which controls how random the answers are. Leave temperature empty to use the provider's own setting. |

Capital letters in a model name do not matter, so `GPT-4.1` and `gpt-4.1` are the same
model. Each name may only appear once in the list, though — if two rows have the same
name, Mutation tells you which one is duplicated instead of quietly picking one.

Your prompts themselves are edited on the main window, not here.

### Text to Speech

How Mutation reads text out loud. Voice, rate and volume live on the main window;
this page holds everything else.

| Setting | What it does |
|---|---|
| Enable speech preprocessing | Tidies text up before it is spoken — expanding abbreviations, dropping stray symbols. This is the master switch for all the clean-ups. |
| Individual cleanup rules | Seven switches you can flick on and off one at a time: remove code blocks, strip bold and italic symbols, strip heading marks, shorten web links, strip bullet markers, expand abbreviations, and normalise whitespace. They only apply while the master switch is on. |
| Rewind on resume (words) | When you carry on after a pause, rewind this many words so you regain your place. Set it to 0 for no rewind. |
| Rewind on resume after (seconds) | Only rewind if the pause lasted longer than this. A quick pause carries on from the exact word. |
| Announce progress every (percent) | How often your progress is spoken while reading something long. 25 means at 25%, 50% and 75%. |

The skip-back grace window and the two "only announce above this many minutes"
numbers have sensible defaults and hover help.

### Transcript formatting

Your own find-and-replace rules, applied to a transcript when you press **Format**.
Rules run from top to bottom, so the order matters — use the up and down arrows to
move them.

| Setting | What it does |
|---|---|
| Find / Replace with | The text to look for, and what to put in its place. |
| Match type | How Find is read. **Plain** replaces the literal text. **RegEx** treats it as a search pattern. **Smart** matches the word with the surrounding spaces and punctuation tidied up, which is the friendly choice for replacing a spoken word or phrase. |
| Case sensitive | When on, capitals must match exactly. When off, case is ignored. |

See [Automatic find-and-replace rules](transcript-formatting.md) for the full story.

### Interface

Small comforts for the main window.

| Setting | What it does |
|---|---|
| Max text-box visible lines | Caps how tall the transcript and OCR boxes grow on the main window. |
| Dictation insert preference | How your text reaches the app you are in. **Paste into 3rd party application** puts it in via the clipboard, **Send keys to 3rd party application** types it out one key at a time, and **Don't insert into 3rd party application** leaves the text in Mutation only. |
| Reset window position | Puts Mutation's window back to its starting position and size. Handy if it has ended up off-screen. |

### Hotkeys

Every keyboard shortcut in one place, plus the shortcut router. This one has a
chapter of its own: [Keyboard shortcuts](keyboard-shortcuts.md).

## The reset buttons

Next to most settings sits a small circular-arrow button. Your screen reader calls
it "Reset to default", and hovering shows the same thing — on the Hotkeys page it
even names the value, like "Reset to default (ALT+Q)".

Each button resets **only the setting it sits beside**. There is no button that
wipes the whole page or the whole app back to factory settings. If you want to
undo a whole visit, press **Cancel** instead of **Save**.

## Saving, and when saving goes wrong

Nothing is written down until you press **Save**. That is the moment your changes
reach the running app and get written to disk.

If the save fails — the file is locked, the disk is full, something like that —
Mutation tells you rather than failing quietly. A red bar appears at the bottom of
the Settings window titled **Save failed**, with the actual reason, followed by:
"Your changes were not saved. Fix the problem and press Save again, or press Cancel
to discard the changes." You also hear the failure beep, and screen readers get an
urgent announcement. Your changes are still sitting there in the window, so you can
fix the problem and press Save again.

The same treatment applies if the **Open Mutation.json** button cannot open the
file: a bar titled "Could not open settings file", with the reason.

## Where your settings live

Everything is kept in a single file called **Mutation.json**, in the folder
Mutation itself is installed in. Turn on the **Advanced** switch and press **Open
Mutation.json** to see it.

Two things worth knowing:

- **Back it up.** Copy that one file somewhere safe and you have your shortcuts,
  prompts, formatting rules and preferences saved. Dropping it back in place
  restores the lot.
- **Do not share it.** Your API keys are in there in plain text. Anyone who has the
  file can spend money on your accounts. Never email it, paste it into a chat, or
  attach it to a support ticket without stripping the keys out first.

## One last thing

You do not have to memorise any of this. Nearly every setting in the window has a
small help icon beside it, and hovering over it — or landing on the setting with a
screen reader — gives you the same plain explanation you have just read here.

## Where to next

- [Keyboard shortcuts](keyboard-shortcuts.md) — the Hotkeys page in full, including the shortcut router.
- [Getting started](getting-started.md) — the handful of settings worth filling in first.
- [A tour of the main window](main-window.md) — the settings that live on the main screen instead.
- [Troubleshooting](troubleshooting.md) — when something is not behaving.
