# Screen reader and accessibility notes

Mutation was written by a developer who is nearly blind, for his own daily use. That
shows up all through the app. This chapter gathers the things that matter most if you
work with a screen reader, a magnifier, or both.

## Everything works from the keyboard

There is no part of Mutation that needs a mouse. The global shortcuts do the real
work, so most of the time you never open the app at all. When you do open the main
window, you can Tab through every card, and every button, slider, list and text box
can be reached and operated from the keyboard.

Settings opens with **Ctrl+Comma** and is keyboard-driven too, including the search
box at the top and the "Advanced" toggle.

## Controls are named, and the help text is the same text you see

Every control tells your screen reader what it is. Buttons that have a shortcut
attached announce the shortcut as part of their name, so you hear something like
"Mute, Ctrl+Shift+M". When a button changes state, the announced name changes with it
— a button that becomes Unmute announces itself as Unmute, not as the name it had at
startup.

Where a list repeats the same buttons on every row, the announced name says which row
it belongs to. In the **LLM Prompts** list you hear "Delete prompt 'Summarise'" rather
than just "Delete", so you know what you are about to remove before you press it.

Dropdowns read out proper wording, not shorthand. The **Third-party interaction**
dropdown announces "Paste into 3rd party application", and when you pick a different
one, the line of explanation underneath is read out straight after.

The help text for settings is written once and used twice. The sentence your screen
reader reads out for a setting is exactly the sentence that appears in the tooltip
when you hover it with the mouse. There is no short visual label with a longer hidden
explanation, or the other way around. What you hear is what is there.

## Sounds tell you what happened

Mutation uses short beeps so you know an action landed without having to check the
screen. There are distinct sounds for:

- **Start** — a recording has begun, or the screenshot overlay is ready for you to pick a region.
- **End** — a recording has stopped, or your screenshot has been captured and sent off to be read.
- **Success** — a two-note rising chirp when something completed.
- **Failure** — a low tone, repeated, when something went wrong.
- **Mute** — a low tone when microphones are muted.
- **Unmute** — a short high tone when they are live.

You can replace all of them with your own sound files. Open **Settings** with
**Ctrl+Comma**, go to the audio settings, turn on the option to play your own sound
files instead of the built-in beeps, and pick a `.wav` file for each of the six cues.
There is a preview button so you can hear a file before committing to it.

## Spoken status announcements

The main window has a status area at the bottom, named "Status notifications" for your
screen reader. Everything that appears there is also announced out loud, even when the
status area was already open — so a run of updates like "Recording…" then
"Transcribing…" then the final result all reach you.

Errors and warnings interrupt whatever your screen reader is currently saying, because
they need you now. Routine progress updates wait their turn politely. If several
updates arrive quickly, you hear the most recent one rather than a backlog of stale
messages.

## The screenshot overlay talks you through it

Grabbing part of a screen you cannot see sounds impossible. It is not.

When the screen-capture overlay opens, it announces itself and tells you the keys:
move with the arrow keys, press **Enter** to set the first corner, move again, then
**Enter** to capture. **Ctrl+A** selects the whole screen, which means any capture can
be finished in two keystrokes. **Backspace** clears the corner you set. **Escape**
cancels.

As you move, it reads your position out. Before you set a corner you hear the caret
position. After you set one you hear the size of the region and where its top-left
corner sits, so you always know where on the screen you are. Hold **Ctrl** for
one-pixel steps or **Shift** for big hundred-pixel jumps. When the capture finishes it
tells you the size of what it grabbed, in pixels. If you press Enter twice without
moving, it tells you the region is empty rather than silently doing nothing.

## Reading aloud is a first-class feature

Mutation can read the clipboard out loud, with its own voice, rate and volume, and its
own pause, skip and rewind controls. It can announce roughly how long a piece of text
will take to read before it starts, and read your progress out at intervals — "50%,
about 3 minutes left". You can ask where you are at any moment and hear "Sentence 4 of
22, about 40% through, about 2 minutes left".

See [Having text read aloud](read-aloud.md) for the full picture.

## Handing the result to your screen reader automatically

This one is worth setting up. After an OCR run or a dictation finishes, Mutation can
send a keystroke of your choosing to whatever app you were in.

Say you dictate a note into an email. Mutation puts the transcript on the clipboard,
then sends **Ctrl+V** to the email window for you, so the text lands where your cursor
was without you touching anything. Or set it to your screen reader's own "read from
here" shortcut, and the OCR result gets read to you the moment it is ready.

You set it in **Settings**, under the shortcuts section: one box for "after OCR" and
one for "after transcription". Leave a box empty and nothing is sent.

## Alongside a screen reader and ZoomText

Mutation is designed to sit next to your existing tools, not replace them. Its global
shortcuts are registered with Windows, so they work whichever app has focus. If your
screen reader or magnifier already owns a combination you want, Windows will refuse to
give it to Mutation — you will get a message at startup listing the shortcuts that
could not be registered and why, and you can pick different ones. See
[Troubleshooting](troubleshooting.md) if that happens.

Every default shortcut in Mutation can be changed, so you can fit it around the
shortcuts you already have in your fingers.

## Where to next

- [Keyboard shortcuts](keyboard-shortcuts.md) — the full list, and how to change them.
- [Having text read aloud](read-aloud.md) — voices, speed, and the reading controls.
- [Screenshots and reading text from images](screen-capture-and-ocr.md) — the capture
  overlay in context.
- [Troubleshooting](troubleshooting.md) — when something does not behave.
