# Having text read aloud

Mutation can read text out loud to you. Copy an email, a report, a long article —
then press a shortcut and listen while you do something else.

Once you know a handful of shortcuts you can drive it like a music player: play,
pause, skip ahead, jump back.

## Starting a reading

There are two ways to get text into Mutation's ears.

**Read the clipboard.** Copy some text anywhere (the usual **Ctrl+C**), then press
**Ctrl+Shift+Alt+Q**. Mutation starts reading it.

**Read what you have selected.** Highlight text in any app and press
**Ctrl+Shift+Q**. Mutation grabs the selection for you, so you don't need to copy
first.

Both shortcuts are global. That means they work no matter which app is in front —
you never have to switch back to Mutation. All of these shortcuts are just
defaults, and you can change any of them in **Settings** (**Ctrl+Comma**) on the
**Hotkeys** tab.

If you prefer buttons, the **Voice & Speech** card on the main window has **Speak
clipboard** and **Speak selection** buttons that do the same thing.

## Pausing and picking up again

There are two ways to interrupt a reading, and they behave slightly differently.

**Pause properly: Ctrl+Shift+Space.** This is the real pause button. Press it while
Mutation is reading and it says "Paused." out loud. Press it again and the reading
carries on, with "Resuming." in the status area. If nothing is being read, it says
"Nothing to resume." out loud.

Some of these replies are spoken in the reading voice and some appear in the status
area instead. Either way you hear them: the status area is announced to your screen
reader as it changes.

When it resumes, it rewinds a few words first — five by default — so you get your
bearings instead of being dropped mid-sentence. That rewind only happens if your
pause was long enough to lose the thread. A quick pause of a few seconds picks up
from exactly where it stopped. By default, "long enough" means more than 10 seconds.

Both of those numbers are yours to set, on the **Text to Speech** tab in
**Settings**: "Rewind on resume (words)" (0 to 20, where 0 means no rewind at all)
and "Rewind on resume after (seconds)" (set it to 0 to always rewind, however brief
the pause).

**Stop with the same key you started with: Ctrl+Shift+Alt+Q.** Pressing the speak
shortcut again while it is reading stops it, and "Stopped." appears in the status
area. Press it once more and it picks the same text up again from where it left off —
so in everyday use it works like a play/stop button on the one key.

One handy detail: if you copy something new before pressing it again, Mutation
notices and reads the *new* clipboard text instead of carrying on with the old one.
The status area says so: "Speaking new clipboard text."

## Moving around the text

You can move through the text sentence by sentence, the way you skip tracks on a
music player.

| What you want | Shortcut |
|---|---|
| Pause, then carry on again | **Ctrl+Shift+Space** |
| Skip forward one sentence | **Ctrl+Shift+K** |
| Skip back one sentence | **Ctrl+Shift+J** |
| Start again from the very beginning | **Ctrl+Shift+B** |
| Tell me where I am | **Ctrl+Shift+P** |

Skipping back is a little cleverer than it looks. If you have only just landed on a
sentence, pressing back jumps to the previous one — because you probably missed
that one. If you have been listening to the current sentence for a moment, pressing
back restarts the current sentence instead — because you probably want to hear that
bit again.

The dividing line is a short grace window, 1.5 seconds by default. You can adjust
it under "Skip-back grace window (milliseconds)" on the **Text to Speech** tab. A
larger value makes stepping back to the previous sentence easier; a smaller value
favours re-reading the sentence you are on.

If you skip past the end, Mutation says "End of text." If you skip back before the
start, it says "Beginning of text."

The **Position** button on the **Voice & Speech** card (or **Ctrl+Shift+P**) tells
you where you are in the text. You hear something like "Sentence 4 of 22, about 40%
through, about 2 minutes left." If nothing is being read, it simply says "Not
currently reading."

## Choosing a voice, speed, and volume

The **Voice & Speech** card on the main window holds three controls.

- **Voice** — a list of every voice installed on Windows, plus "(System default)".
  Pick one and Mutation immediately speaks a short sample in that voice, so you can
  hear it before committing.
- **Rate** — how fast it reads, from -10 (slow) to +10 (fast). The default is 8,
  which is brisk. Slide it down if that is too quick.
- **Volume** — 0 to 100%, separate from your Windows volume.

Your choices are saved automatically. There is no Save button to press.

Want more voices? Install them through Windows' own speech settings, and they turn
up in Mutation's Voice list next time.

## Being told how long it will take

Two switches on the **Voice & Speech** card keep you informed while you listen.

**Announce reading time at start** speaks a short estimate before it begins, such
as "Reading approximately 3 minutes of text."

**Announce progress while reading** speaks your progress at regular points along
the way, such as "50%, about 3 minutes left."

Short texts stay quiet. On the **Text to Speech** tab in **Settings** you can set a
minimum length for each announcement: "Announce reading time only above (minutes)"
and "Announce progress only above (minutes)". Set either to 0 to hear it for any
length of text. You can also set how often progress is spoken with "Announce
progress every (percent)" — 25 gives you 25%, 50%, and 75%.

## Tidier reading

Text copied from the web or from notes is often littered with symbols that sound
awful when read out. So before speaking, Mutation quietly cleans the text up. This
is on by default.

Here is what it tidies:

- **Remove code blocks** — drops chunks of code (text between triple backticks)
  rather than reading them out.
- **Strip bold, italic, and inline-code symbols** — removes asterisks, underscores,
  and backticks, keeping the words between them.
- **Strip heading marks** — removes leading `#` symbols so headings read as plain
  text.
- **Shorten web links** — reads "link to example.com" instead of the full address.
- **Strip bullet markers** — removes the `-`, `*`, or `+` at the start of list
  items.
- **Expand abbreviations** — "e.g." becomes "for example", "i.e." becomes "that
  is", "etc." becomes "et cetera", and "vs." becomes "versus".
- **Normalise paragraph breaks and whitespace** — collapses blank lines and runs of
  spaces so you don't sit through long silent gaps.

Each of those is its own switch on the **Text to Speech** tab in **Settings**, so
you can keep the ones you like and turn off the ones you don't. Above them is a
master switch, "Enable speech preprocessing". Turn that off and you hear the text
exactly as written, symbols and all.

> Every setting on the page has a small reset button next to it that puts it back
> to the default, so experimenting is safe.

## Saving a reading as an audio file

The **Speak to file** button on the **Voice & Speech** card takes whatever text is
on your clipboard and saves it as a spoken audio file (a `.wav` file) instead of
playing it. Mutation suggests a name with the date and time in it, and you choose
where to put it.

This is useful when you want to listen later — on your phone during a commute, for
instance — or when you want to keep a spoken copy of a document.

## When Mutation can't read something

If there is nothing to read, Mutation tells you out loud rather than sitting
silently. You will hear one of these:

- "No text on the clipboard." — nothing has been copied yet.
- "The clipboard contains an image, not text. Use OCR to extract text first." — see
  [Screenshots and reading text from images](screen-capture-and-ocr.md) for how to
  pull the words out of a picture.
- "The clipboard is in use by another application. Try again in a moment." — just
  press the shortcut again.

Each of these also appears as a message on the main window, and plays the failure
beep.

A reading can also fail once it has started — most often because the voice it was
told to use is no longer installed on Windows. Mutation plays the failure beep,
puts the reason on the main window, and opens a dialog with the details. If the
missing voice is the cause, the message names it, so you know which one to pick a
replacement for on the **Voice & Speech** card. A shortcut that fails this way
never just goes quiet on you.

## Where to next

- [Screenshots and reading text from images](screen-capture-and-ocr.md) — get text
  out of a picture so it can be read aloud.
- [The Settings window](settings.md) — where the **Text to Speech** tab lives.
- [Keyboard shortcuts](keyboard-shortcuts.md) — the full list, and how to change
  them.
- [A tour of the main window](main-window.md) — where the **Voice & Speech** card
  sits.
