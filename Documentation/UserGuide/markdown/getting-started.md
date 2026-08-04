# Getting started

Welcome. Mutation is a small Windows program that sits quietly in the background and gives your keyboard some extra powers. Press one key combination to mute every microphone on your PC at once. Press another to dictate a sentence instead of typing it. Press another to pull the text out of a screenshot, or to have whatever you copied read out loud.

The clever part is that these shortcuts work everywhere. You do not have to switch to Mutation first. Whether you are in Outlook, Teams, Word or your browser, the same key combination does the same thing. Mutation was built by a developer who is almost blind, for his own daily work, so it is designed to be usable without hunting for tiny icons on screen.

---

## What you need before you start

**Windows.** Mutation is a Windows desktop app.

**The .NET 10 runtime.** This is a free piece of Microsoft plumbing that Mutation needs in order to run. Install it once, then start **Mutation.exe**. If you already have it, nothing more to do.

**A key or two for the online services.** Some of Mutation's features do their thinking on the internet rather than on your PC. Turning speech into text, or asking an AI to tidy up your writing, happens on someone else's servers. To use those servers you need an API key.

An API key is simply a long password that lets Mutation talk to a service on your behalf. You create one on that company's website, copy it, and paste it into Mutation. Treat it exactly like a password: do not email it, do not paste it into a chat, and do not put it in a shared document. Anyone who has your key can spend money on your account.

You only need the keys for the features you actually want. Here is what each one unlocks.

| Key | What it gives you |
|---|---|
| OpenAI | Dictation (speech to text) and AI prompts |
| Anthropic | AI prompts using Claude models |
| Deepgram | An alternative dictation service, if you prefer it to OpenAI |
| Azure Computer Vision (key *and* endpoint) | Reading text out of screenshots and images |

If all you want is the microphone mute, you need no keys at all. That feature works straight away.

> The Azure Computer Vision service needs two things, not one: a key, and an "endpoint", which is just the web address of your own Azure service. You get both from the same place when you set the service up.

---

## Your very first launch

The first time you run Mutation, it creates its settings file with sensible defaults, and every keyboard shortcut is already set up and ready to change later.

If you have not yet added an OpenAI or Anthropic key, Mutation greets you with a short welcome message titled **Welcome to Mutation**. It explains that at least one key is needed, and lists which key does what. Press **Continue**.

The **Settings** window then opens on its own, already on the **API keys** page, so you can paste your keys in right away. That is the whole of first-run setup. If you would rather do it later, just close Settings — you can reopen it at any time with **Ctrl+Comma**.

One other message you might see later: if you have set up a dictation service but its key is missing, Mutation tells you which service it has switched off and opens Settings on the API keys page. Add the key, save, then restart Mutation so the service becomes available again.

---

## Entering your keys

1. Press **Ctrl+Comma** to open **Settings**.
2. Choose **API keys** in the list on the left.
3. Paste your key into the matching box: **OpenAI API key**, **Anthropic API key** or **Deepgram API key**.
4. Save.

The OpenAI key covers both AI prompts and OpenAI dictation, so one key does double duty there. The Deepgram key is only for Deepgram dictation.

The Azure Computer Vision key lives somewhere else, because it belongs to the screenshot features. You will find it under **Screen capture & OCR** in Settings, together with the **Endpoint** box.

---

## Where your settings are kept

Everything you configure is stored in a single file called **Mutation.json**, which sits in the same folder as **Mutation.exe**.

You do not need to open it. Everything in it can be changed from the Settings window. But it is worth knowing where it is for one reason: if you copy that file somewhere safe now and then, you have a backup of all your shortcuts, prompts and settings. Copy it back into the folder and Mutation picks up exactly where you left off.

Because your API keys are stored in that file, keep any copies of it somewhere private.

---

## Try it in five minutes

Give these a go, in any app you like. These are the shortcuts Mutation starts with — you can change every one of them later.

1. **Mute your microphone.** Press **Alt+Q**. Every microphone on the PC mutes at once, and you hear a beep confirming which way it went. Press it again to unmute. See [Muting your microphone](microphone.md).
2. **Dictate a sentence.** Press **Shift+Alt+U**, say something, then press it again to stop. A few seconds later your words come back as text. See [Dictation: turning speech into text](dictation.md).
3. **Have something read aloud.** Copy any text, then press **Ctrl+Shift+Alt+Q** to hear it spoken. See [Having text read aloud](read-aloud.md).
4. **Read text off the screen.** Press **Shift+Alt+J**, drag a rectangle over some text in an image, and the text lands on your clipboard. This one needs the Azure key. See [Screenshots and reading text from images](screen-capture-and-ocr.md).

That is the core of it. Everything else builds on those four ideas.

> **Lost at any point?** Click **User guide** in the top right of Mutation's main
> window, or press **Alt** then **G**, and this guide opens in your browser.

---

## Leave it running

Mutation does its job from the background. Once it is started you can minimise the main window and forget about it — the shortcuts keep working no matter which app you are using. Closing the window shuts the app down, and the shortcuts stop with it, so minimise rather than close if you want them to stay live.

---

## Where to next

- [A tour of the main window](main-window.md) — what each part of the screen does.
- [Keyboard shortcuts](keyboard-shortcuts.md) — the full list, and how to change any of them.
- [The Settings window](settings.md) — every option, explained.
- [Troubleshooting](troubleshooting.md) — if something is not behaving.
