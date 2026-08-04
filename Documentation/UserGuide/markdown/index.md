# The Mutation User Guide

Welcome. Mutation is a small Windows app that sits quietly in the background and
gives you a set of keyboard shortcuts that work *everywhere* — in your email, in a
meeting, in a spreadsheet, in your browser. You don't have to switch to Mutation to
use it. You just press a key.

It does six main jobs for you:

- **Mutes every microphone on your PC at once**, so you always know for certain
  whether you are muted.
- **Grabs a piece of your screen and pulls the words out of it**, so text trapped in
  a picture becomes text you can copy, search and paste.
- **Turns what you say into typed text**, so you can dictate a message instead of
  typing it.
- **Runs your text past an AI assistant** to fix the grammar, shorten it, or turn
  rough notes into a proper email.
- **Reads text out loud**, with proper pause, resume and skip controls.
- **Turns one keyboard shortcut into another**, so an awkward shortcut in some other
  app becomes an easy one.

Mutation was built by a nearly-blind developer for his own daily work, so everything
in it can be done from the keyboard, and everything it does it also tells you about
out loud or with a sound.

---

## Where to start

If you are brand new, read these two, in this order:

1. **[Getting started](getting-started.md)** — what you need, what happens the first
   time you run Mutation, and a five-minute first try.
2. **[A tour of the main window](main-window.md)** — a quick map of what every part
   of the screen does.

After that, dip into whichever chapter covers the job you want to do.

---

## All the chapters

### Getting going

| Chapter | What's in it |
|---|---|
| [Getting started](getting-started.md) | What you need before you begin, setting up your account keys, where your settings are kept, and a quick first try. |
| [A tour of the main window](main-window.md) | Every section of the main screen, what each button does, and where to read more. |

### The things Mutation does

| Chapter | What's in it |
|---|---|
| [Muting your microphone](microphone.md) | One shortcut to mute and unmute every microphone at once, the confirmation beeps, the input level meter, and pinning your input level. |
| [Dictation: turning speech into text](dictation.md) | Recording your voice and getting text back, choosing a transcription service, where the text is delivered, replaying past recordings, and transcribing files you already have. |
| [Screenshots and reading text from images](screen-capture-and-ocr.md) | Capturing part of your screen with the mouse or the keyboard, pulling the text out of pictures and PDFs, and choosing the right reading order. |
| [Having text read aloud](read-aloud.md) | Reading the clipboard or your selection out loud, pausing and resuming, skipping around sentence by sentence, and picking a voice, speed and volume. |
| [Using AI prompts on your text](ai-prompts.md) | Saving your own instructions like "fix the grammar" or "turn this into an email", giving each one a shortcut, and choosing which AI model runs it. |
| [Automatic find-and-replace rules](transcript-formatting.md) | Teaching Mutation to fix the words it always gets wrong, and to strip out filler, automatically. |

### Settings and reference

| Chapter | What's in it |
|---|---|
| [Keyboard shortcuts](keyboard-shortcuts.md) | The full list of shortcuts and their defaults, how to change one, and how to make one shortcut stand in for another. |
| [The Settings window](settings.md) | A guided walk through every settings page, what each option does, and how to put things back the way they were. |
| [Screen reader and accessibility notes](accessibility.md) | How Mutation works with a screen reader and ZoomText, what it announces, and the sounds it uses to tell you what happened. |
| [Troubleshooting](troubleshooting.md) | When something doesn't work: the usual causes, the quick fixes, and where the log file lives. |

---

## A few things worth knowing up front

- **Every shortcut in this guide can be changed.** The ones printed here are just what
  Mutation starts with. If a combination clashes with something you already use, go to
  Settings and pick your own — see [Keyboard shortcuts](keyboard-shortcuts.md).
- **You only need to set up the parts you want.** Mutation asks for account keys for
  the online services it uses, but they are separate. If you only want the microphone
  mute, you don't need any of them.
- **Settings open with Ctrl+Comma** from anywhere in the app.
- **Most settings have hover help.** If a setting isn't clear, rest your mouse on it,
  or tab to it with a screen reader — the same explanation is available both ways.

---

## About this guide

The chapters live as Markdown files in the `markdown` folder, and the matching web
pages in the `html` folder are generated from them. The Markdown is always the
authoritative version. To rebuild the web pages after an edit, run
`Build-UserGuide.cmd` in the `Documentation` folder.
