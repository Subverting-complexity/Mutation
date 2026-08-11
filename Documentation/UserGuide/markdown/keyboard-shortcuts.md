# Keyboard shortcuts

Keyboard shortcuts are how you actually use Mutation day to day. This chapter
lists every one of them, shows you how to change them, and explains what to do
when a combination refuses to work.

## What "global" means

Almost every Mutation shortcut is **global**. That means it works no matter which
app you are in. You can be halfway through an email, a spreadsheet, or a video
call, and the shortcut still fires. Mutation's own window does not need to be
open, in front, or even visible. It just sits quietly in the background and
listens.

There is one exception, and it is noted in the tables below: **Ctrl+Comma** opens
Settings, and that one only works when Mutation's own window is in front.

Every default shortcut listed here can be changed. Nothing is fixed.

A shortcut fires once per press. Holding the keys down a little too long does
nothing extra — you will not start a second recording, or flip the mute back and
forth, just because your fingers lingered. Let go and press again when you want it
to happen a second time.

## The full list

### Microphone

| What it does | Default shortcut | More about it |
|---|---|---|
| Mute or unmute every microphone at once | **Alt+Q** | [Muting your microphone](microphone.md) |

### Dictation

| What it does | Default shortcut | More about it |
|---|---|---|
| Start or stop recording your voice, then turn it into text | **Shift+Alt+U** | [Dictation](dictation.md) |
| Same, then send the text straight through an AI prompt | **Shift+Alt+I** | [Dictation](dictation.md) |
| Send a key to the app you are in, after the text is delivered | None — set your own | [Dictation](dictation.md) |

### Screenshots and OCR

| What it does | Default shortcut | More about it |
|---|---|---|
| Take a screenshot of part of the screen | **Shift+Alt+K** | [Screenshots and OCR](screen-capture-and-ocr.md) |
| Take a screenshot and pull the text out of it | **Shift+Alt+J** | [Screenshots and OCR](screen-capture-and-ocr.md) |
| Same, but read strictly left to right, top to bottom | **Shift+Alt+E** | [Screenshots and OCR](screen-capture-and-ocr.md) |
| Pull the text out of an image already on the clipboard | **Alt+J** | [Screenshots and OCR](screen-capture-and-ocr.md) |
| Same, but read strictly left to right, top to bottom | **Alt+K** | [Screenshots and OCR](screen-capture-and-ocr.md) |
| Send a key to the app you are in, after the text is delivered | None — set your own | [Screenshots and OCR](screen-capture-and-ocr.md) |

### Reading aloud

| What it does | Default shortcut | More about it |
|---|---|---|
| Read whatever is on the clipboard | **Ctrl+Shift+Alt+Q** | [Having text read aloud](read-aloud.md) |
| Read the text you have selected | **Ctrl+Shift+Q** | [Having text read aloud](read-aloud.md) |
| Pause, or carry on from where you paused | **Ctrl+Shift+Space** | [Having text read aloud](read-aloud.md) |
| Start again from the beginning | **Ctrl+Shift+B** | [Having text read aloud](read-aloud.md) |
| Go back a sentence | **Ctrl+Shift+J** | [Having text read aloud](read-aloud.md) |
| Go forward a sentence | **Ctrl+Shift+K** | [Having text read aloud](read-aloud.md) |
| Say how far through the text you are | **Ctrl+Shift+P** | [Having text read aloud](read-aloud.md) |
| Save the spoken audio to a file | None, and see the note below | [Having text read aloud](read-aloud.md) |

> **Speak to file** is the one action without a row on the Hotkeys page. Use the
> **Speak to file** button on the main window. If you really want a shortcut for it,
> it can only be added by editing the settings file by hand — see
> [The Settings window](settings.md) for where that file lives.

### AI prompts

| What it does | Default shortcut | More about it |
|---|---|---|
| Run one of your AI prompts | None — set your own, one per prompt | [Using AI prompts](ai-prompts.md) |

Each prompt you create can have its own shortcut. You set it in the prompt editor
on the main window, not on the Hotkeys page. None are filled in for you.

If the combination you type is already taken, the prompt editor tells you straight
away, under the **Hotkey** box, and says what has it — another prompt by name, one of
Mutation's own shortcuts, or a hotkey route. You can still save it, but only one of
the two will ever fire, so it is worth picking something else while you are there.

### App

| What it does | Default shortcut | More about it |
|---|---|---|
| Open Settings (only when Mutation's window is in front) | **Ctrl+Comma** | [The Settings window](settings.md) |
| Open Mutation's menu (only when Mutation's window is in front) | **Alt**, then **H** | [A tour of the main window](main-window.md) |

## Changing a shortcut

Open **Settings** with **Ctrl+Comma**, then pick **Hotkeys** from the list on the
left. Every shortcut in the tables above is on that one page, in roughly the same
order — the only exception is **Speak to file**, noted above.

Each row has the name of the action, a box holding the current combination, a
**Record** button, a **Clear** button, and a small reset button. Hover over the
reset button and it tells you what the default is, for example "Reset to default
(ALT+Q)".

To record a new combination:

1. Press the **Record** button. Its label changes to "Press keys...".
2. Hold down the modifiers you want and press the final key. Holding Ctrl, Shift,
   Alt or the Windows key on their own does nothing — Mutation waits for a real
   key before it decides you are finished.
3. The box fills in with what you pressed, in Mutation's own style, like
   `CTRL+ALT+J`. Recording stops on its own.

Changed your mind mid-recording? Press **Escape** and nothing is captured.

You can also just type into the box if you prefer. Write the keys separated by
plus signs. When you move away from the box, Mutation tidies up what you typed: it
puts the capitals right, and it puts the keys in its own order, always Ctrl, then
Shift, then Alt, then the Windows key, then the key itself. So if you type
**Shift+Ctrl+A**, the box shows `CTRL+SHIFT+A` afterwards. It is the same shortcut
either way — Mutation just writes it one way everywhere, so two boxes holding the
same combination look the same. That tidied version is what gets saved, so a
shortcut you typed into the settings file by hand comes back tidied once Mutation
has opened that page.

When a box does change what you typed, a short note appears under it and a screen
reader reads it out: "Speak clipboard now reads CTRL+SHIFT+A." You get that only
when something actually changed, so tabbing down a page of shortcuts that are
already written Mutation's way stays quiet. The note goes away when you come back
to that box. The **From** and **To** boxes in the shortcut router further down the
page do the same, and name themselves when they do: "Shortcut to listen for now
reads CTRL+SHIFT+A."

Short names for keys are fine. Write **Alt+PgDn** or **Alt+PageDown**, **Ctrl+Esc**
or **Ctrl+Escape**, **Bksp** or **Backspace**, **ArrowUp** or **Up** — Mutation
takes either, in any box on the page, and saves the full name. The one word to
avoid is **Menu**, which means the Alt key in a shortcut but the context-menu key
in a "send key after" box.

Two boxes are the exception: **Send key after OCR (optional)** and **Send key
after transcription (optional)**. They keep whatever you type, exactly as you
typed it, because what goes in them is not always a plain combination. There is
more on them below. The **Clear** button empties a box, which is what you want for
those two.

Your changes are held until you press **Save** at the bottom of Settings. Press
**Cancel** and nothing you changed is kept.

## When a combination is taken or not allowed

Mutation checks as you go, and shows the problem in red just under the box. Screen
readers announce it straight away.

- **Left blank when a shortcut is required** — "Enter a hotkey."
- **Only modifier keys** — "Hotkey must include a non-modifier key." Ctrl+Shift on
  its own is not a shortcut; it needs a letter, number, or other key on the end.
- **A key Mutation does not recognise** — "Unsupported key", followed by the key
  you typed.
- **The same combination used twice inside Mutation** — an orange **Duplicate
  hotkey** note appears under both rows, and is read out the moment it turns up while
  you are editing. Give one of them a different combination, otherwise only one of the
  two will ever fire.
  Mutation compares the keys, not the spelling, so writing one as
  **Ctrl+Shift+A** and the other as **Shift+Ctrl+A** is still caught. The check covers
  the whole page at once, so a shortcut at the top and a router mapping's "From" box at
  the bottom claiming the same keys are both flagged.
- **The same combination as one of your prompt shortcuts** — a note appears under
  the row naming the prompt, for example **Also used by the LLM prompt "Summarize"**.
  Prompt shortcuts are set in the prompt window rather than on this page, so there is
  no second row here to flag — the note names it instead, so you know which prompt to
  open.
- **The same combination in a "send key after" box and a real shortcut** —
  also flagged, and worth fixing. Windows hands that key straight back to Mutation,
  so the action would set itself off again and again. Putting the same key in *both*
  "send key after" boxes is fine, though, and is not flagged: those keys go to
  whatever app you are in, so there is nothing for them to clash over.
- **Another app already owns it** — Mutation cannot tell until it actually tries to
  claim the combination. When it fails, you get a beep and a message titled "Some
  hotkeys could not be registered", listing each action, its combination, and the
  reason, usually "The shortcut is already registered by another application."
  Go back to the Hotkeys page and pick something else.

Those two "send key after" boxes are a little more relaxed than the rest. They
hold more than one plain combination can say:

- **A run of keys, one after the other.** Separate them with commas, like
  `Ctrl+V, Enter` to paste and then press Enter.
- **Windows' own shorthand for typing keys.** That is the style which writes Ctrl
  as `^` and puts named keys in curly brackets, so Ctrl+F5 becomes `^{F5}`. You
  never need it — **Ctrl+F5** does the same job and reads better — but it works if
  you already know it.

Because of those two, Mutation leaves these boxes exactly as you type them rather
than tidying them up.

Both spellings are checked against your other shortcuts, so writing `^{F5}` while
some Mutation shortcut is **Ctrl+F5** is flagged just as writing **Ctrl+F5** would
be. Two things are not checked, and neither is a mistake. Plain words are not a
shortcut at all — type `hello` and Mutation types the word for you. And anything
in the shorthand that Mutation cannot read with certainty is passed over in
silence rather than guessed at, because a wrong warning is worse than none. So no
warning is good news, not a guarantee. If you want to be sure, write the key the
plain way — **Ctrl+F5** rather than `^{F5}` — and it is always checked.

## Choosing combinations that won't clash

1. **Use three modifiers.** Combinations like **Ctrl+Shift+Alt+Q** are almost never
   claimed by anything else. That is exactly why Mutation uses one for reading the
   clipboard.
2. **Stay away from the famous ones.** Ctrl+C, Ctrl+V, Ctrl+S, Ctrl+Z, Ctrl+F,
   Alt+Tab and Alt+F4 are used everywhere. So are most combinations that include
   the Windows key, which Windows itself reserves.
3. **Test it in the app you use most.** Set the shortcut, switch to that app, and
   press it. If Mutation responds and the other app does not, you are fine. If
   nothing happens at all, something else grabbed it first — try another.

## The shortcut router

The router does one thing: you press one combination, and Mutation presses a
different one for you. It never runs a Mutation feature. It just passes a
different set of keys along to whatever app you are in.

Two reasons people use it:

- **To make an awkward shortcut easy.** Some other app has a shortcut that is a
  stretch for one hand. Give yourself a comfortable one instead.
- **To get around a clash.** A combination you want is blocked or occupied, so you
  reach the same action a different way.

Here is a worked example. Your video call app mutes with **Ctrl+Shift+M**, which is
a fiddly reach while you are holding a coffee. Add a mapping with **Ctrl+Alt+1** in
the "From" box and **Ctrl+Shift+M** in the "To" box. From then on, pressing
**Ctrl+Alt+1** anywhere makes Mutation send **Ctrl+Shift+M** to whatever app you
are in, and the call mutes.

To set one up, go to **Settings** (**Ctrl+Comma**), pick **Hotkeys**, and scroll
down to the **Hotkey Router** section. Press **Add mapping**, fill in the "From"
and "To" boxes, and press **Save**. If something is wrong with a row, a marker
appears beside the "From" box and the reason is written underneath. Once you have
changed anything on the page, a screen reader reads that reason out as soon as it
turns up. Problems the page had already spotted when you opened it are written but
not read out at you — see
[Screen reader and accessibility notes](accessibility.md) for why.

Each row also says whether it is actually working. A mapping Mutation is listening
for reads "CTRL+ALT+1 is live." A mapping you have changed and not yet saved reads
"CTRL+ALT+1 is not active yet — press Save to apply." — because mappings only start
working when you save. The line names the shortcut, so you can tell which row it
belongs to when you hear it. It is written in the row, not tucked into a tooltip, so
your screen reader reads it along with the rest of the row and you do not have to
press the shortcut to find out whether it took.

While you are still typing in a box, the line goes away — Mutation cannot say
anything useful about half a shortcut. It comes back when you leave the box.

A mapping whose "From" combination is already taken gets a "Duplicate 'From'
hotkey" message — again comparing the keys rather than the spelling. That covers
another mapping, any shortcut higher up the Hotkeys page, and any of your prompt
shortcuts. **Delete** removes a mapping.

> As the in-app help puts it: map one shortcut to another so a single key press can
> trigger a more complex shortcut. Changes apply when you save settings.

## Where to next

- [The Settings window](settings.md) — where the Hotkeys page lives, and everything else you can change.
- [A tour of the main window](main-window.md) — the buttons behind these shortcuts.
- [Troubleshooting](troubleshooting.md) — what to do when a shortcut stops responding.
- [Screen reader and accessibility notes](accessibility.md) — how Mutation announces what it is doing.
