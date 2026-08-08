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
plus signs. Mutation tidies up the capitals for you when you move away from the
box. The **Clear** button empties a box, which is what you want for the two
optional "send a key afterwards" fields.

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
  hotkey** note appears under both rows, and is read out as soon as it appears. Give
  one of them a different combination, otherwise only one of the two will ever fire.
  Mutation compares the keys, not the spelling, so writing one as
  **Ctrl+Shift+A** and the other as **Shift+Ctrl+A** is still caught.
- **The same combination in a "send a key afterwards" box and a real shortcut** —
  also flagged, and worth fixing. Windows hands that key straight back to Mutation,
  so the action would set itself off again and again. Putting the same key in *both*
  "send a key afterwards" boxes is fine, though, and is not flagged: those keys go to
  whatever app you are in, so there is nothing for them to clash over.
- **Another app already owns it** — Mutation cannot tell until it actually tries to
  claim the combination. When it fails, you get a beep and a message titled "Some
  hotkeys could not be registered", listing each action, its combination, and the
  reason, usually "The shortcut is already registered by another application."
  Go back to the Hotkeys page and pick something else.

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
and "To" boxes, and press **Save**. Each row shows a small status marker: a tick
once the mapping is live, or an error marker with a message if something is wrong.
Two mappings listening for the same "From" combination get a "Duplicate 'From'
hotkey" message — again comparing the keys rather than the spelling. **Delete**
removes a mapping.

> As the in-app help puts it: map one shortcut to another so a single key press can
> trigger a more complex shortcut. Changes apply when you save settings.

## Where to next

- [The Settings window](settings.md) — where the Hotkeys page lives, and everything else you can change.
- [A tour of the main window](main-window.md) — the buttons behind these shortcuts.
- [Troubleshooting](troubleshooting.md) — what to do when a shortcut stops responding.
- [Screen reader and accessibility notes](accessibility.md) — how Mutation announces what it is doing.
