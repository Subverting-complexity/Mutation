# Troubleshooting

Most problems in Mutation fall into a handful of buckets. Find the one that sounds like
yours below. Each entry says what is going on and what to do about it.

## Common problems

### A shortcut does nothing

**What's happening.** Windows only lets one app own a global shortcut at a time.
If another app grabbed your combination first, Mutation cannot have it. This is
usually the cause when one shortcut is dead but the others all work.

Mutation does not hide this. When it starts up, if any shortcut could not be claimed
you get a failure beep and a dialog titled "Some hotkeys could not be registered",
listing each one and the reason. A common reason reads "The shortcut is already
registered by another application."

**What to do.**

1. Read the list in that dialog — it names the shortcuts that failed.
2. Open **Settings** with **Ctrl+Comma** and go to the shortcuts section.
3. Change the failed shortcut to a combination nothing else uses. Adding **Shift** to
   an existing combination is often enough.
4. Save. Every default shortcut in Mutation can be changed, so nothing is stuck.

If a shortcut you just typed into Settings is rejected outright, the combination could
not be understood. Use the shortcut editor to record the keys rather than typing them
by hand.

### Dictation comes back empty, or says no speech was detected

**What's happening.** Mutation trims silence off your recording before sending it. If
what is left is too short to be worth transcribing, it skips the request entirely and
tells you "No speech detected — nothing to transcribe." That saves you the wait and
the cost of a request that would have returned nothing.

**What to do.**

1. Check the right microphone is selected on the **Microphone** card, and that it is
   not muted.
2. Watch the level indicator while you talk — if it does not move, the microphone is
   not picking you up.
3. Start speaking a beat after the start beep, not on top of it.
4. If your input level is very low, raise it on the **Microphone** card.

### Dictation says the recorder is still busy

**What's happening.** A previous recording is still being transcribed and the recorder
is not free yet. Mutation refuses to start a new one on top of it rather than losing
either recording.

**What to do.** Wait for the previous transcription to finish — the status area will
say when it is done — then press your dictation shortcut again.

### Dictation is slow, or fails with a timeout

**What's happening.** Transcription happens over the internet, so a slow connection or
a busy service makes it slow. Mutation retries a failed attempt, giving each retry a
longer window than the last, before it gives up. Long recordings naturally take longer
than short ones.

**What to do.**

1. Give it a moment — the retries are automatic and you will hear the result.
2. Record in shorter chunks. A two-minute note transcribes far faster than a
   twenty-minute one.
3. If it consistently times out, raise the timeout in **Settings**. There are separate
   timeouts for live dictation and for transcribing a file, plus a per-attempt timeout
   on each speech service.

### OCR returns nothing useful, or the wrong reading order

**What's happening.** OCR reads text out of a picture. If the picture is small,
blurry, or is light text on a dark background, there may be nothing recognisable in
it. Reading order is a separate matter: Mutation offers two layouts, and they suit
different pages.

**What to do.**

1. Capture a tighter region at a larger zoom level. More pixels per letter helps a lot.
2. If your text is light on a dark background, turn on the setting that inverts the
   image colours before OCR.
3. Try the other reading order. **Natural** layout follows the visual structure of the
   page, which is better for columns and tables. **Basic** layout reads strictly left
   to right, top to bottom, which is better for a plain block of text that Natural has
   scrambled. Each has its own shortcut, so you can just try the other one.

### OCR skipped a file for being too large

**What's happening.** There is a maximum size for a file or page sent for OCR, so a
huge file cannot be uploaded by accident. Anything over it is skipped before upload,
and the failure message names the file, its size, and the limit.

**What to do.** Open **Settings**, find the maximum document size under the OCR
settings, and raise it — or set it to 0 for no limit at all. Alternatively, capture a
smaller region or save the file at a lower resolution.

### OCR stops after a couple of pages

**What's happening.** The free OCR tier limits how many pages a single document can
contain. Mutation respects that limit by default, sending documents in small batches,
with the page limit set to 2 out of the box.

**What to do.** If you are on a paid tier, turn off the free-tier option in
**Settings** under the OCR settings. If you are staying on the free tier, raise the
page limit only as far as your tier allows.

### A batch of documents is taking far too long

**What's happening.** Every page of every file you picked is sent off to be read, one
request at a time. Forty PDFs is a lot of pages, and it can run for several minutes.

**What to do.** Listen for the per-file announcements — Mutation says "done" as each
file finishes, so you can tell it is still working. If you picked the wrong files, or
simply want it to stop, click **Cancel OCR** under the progress bar. Closing the
Mutation window stops a running batch too.

### Nothing happens at all — a key is missing or wrong

**What's happening.** Mutation talks to outside services to do dictation, OCR and AI
processing. Each needs an API key (a long password that lets Mutation use the service
on your behalf). Without one, the feature has nothing to talk to.

On a brand-new install Mutation shows a welcome dialog explaining which keys it needs,
then opens Settings on the API keys tab. If a speech service has no key, it is
disabled and Mutation tells you which ones and why. For OCR you need both an Azure
Computer Vision key **and** an endpoint, and Mutation says specifically which of the
two is missing.

**What to do.** Open **Settings** with **Ctrl+Comma**, go to the API keys tab, and
paste in the key. Azure Computer Vision has its own tab, because the key and the
endpoint live together there. Save, and try again.

### The text didn't paste into the other app

**What's happening.** Mutation delivers text through the clipboard, and only one app
can hold the clipboard at a time. If another app is holding it at that moment, the copy
fails. Mutation retries a few times before giving up. If it still cannot get through,
you get a failure beep and a message like "The clipboard is in use by another
application; the transcript could not be delivered. It is available in the Mutation
window."

**What to do.**

1. Your text is not lost. Open the main window and copy it from there.
2. Clipboard manager tools and remote-desktop sessions are the usual culprits. Closing
   or pausing one often fixes it for good.
3. If you rely on the text landing in another app automatically, set the "send keys
   after" option to **Ctrl+V** so Mutation pastes for you once the copy succeeds.

### Fast mode didn't actually run fast

**What's happening.** Fast mode is a faster setting for AI prompts. If it is not
available, Mutation does not throw your text away — it quietly runs the prompt again at
standard speed and then tells you why Fast mode could not be used. There are three
different reasons, and each needs a different response:

- Fast mode is not enabled for your account. You need to request access for it.
- Fast mode was rate limited or at capacity. Wait a moment and try again.
- The model you chose does not offer Fast mode. Choose a different model.

**What to do.** Listen to which of the three you got — the wording is deliberately
different for each. You can also turn Fast mode off for that prompt in the prompt
editor, which stops the notice appearing every time.

### The wrong microphone is being used, or the mute didn't take

**What's happening.** Mutation's mute shortcut mutes every microphone on the PC, not
just the one it is recording with. It re-checks the list of devices when one is plugged
in or unplugged, so a headset you just connected is covered too.

Mutation also verifies that a mute actually took effect at the operating-system level.
If it cannot confirm it, it will not pretend — you get "Could not change the microphone
mute state. The microphone may still be live — please try again."

**What to do.**

1. Treat that message seriously. Assume the microphone is still live until you have
   confirmed otherwise.
2. Press the mute shortcut again and listen for the mute beep.
3. If Mutation is recording from the wrong device, pick the right one on the
   **Microphone** card. Setting the input level can also fail if a device is busy or
   was just unplugged — you will be told, and can try again.

### No voices in the voice list, or the voice sounds wrong

**What's happening.** The voices Mutation offers for reading aloud come from Windows
itself, not from Mutation. An empty or very short list means Windows has few voices
installed.

**What to do.**

1. Install more voices through Windows Settings, under the speech options. Restart
   Mutation afterwards so it picks them up.
2. Choose the voice, rate and volume on the **Voice & Speech** section of the main
   window.
3. If the voice reads out stray symbols like asterisks and hash marks, turn on the
   text clean-up options in **Settings**. There are individual switches for stripping
   bold and italic marks, heading marks, bullet markers, code blocks, and for
   shortening long web links.

### Settings won't save

**What's happening.** Mutation writes your settings to a file. If that write fails —
the file is read-only, a backup tool has it open, or the folder is not writable — the
save cannot complete. Mutation plays the failure beep, announces the problem, and shows
"Save failed" along with the actual reason and the words "Your changes were not saved.
Fix the problem and press Save again, or press Cancel to discard the changes."

**What to do.**

1. Read the reason given — it is the real cause, not a generic message.
2. Close anything that might have the settings file open, including a text editor.
3. Press Save again. Your changes are still in the dialog until you press Cancel.

Some settings are saved for you a moment after you change them, without a Save
button — the read-aloud speed and volume sliders, for example. If one of those writes
fails you get the failure beep and a message titled "Settings not saved", which ends
with "Close anything that has the settings file open, then change the setting again."
Do that, then nudge the slider once more. Until you do, the setting works for now but
goes back to its old value the next time you start Mutation.

### The temp directory box won't accept what I typed

**What's happening.** The **Temp directory** on the **Speech to Text** page is the
folder your recordings are written to. It has to be a full path that starts with a
drive, like `D:\Recordings`. If you clear the box, or type just a folder name like
`Recordings`, Mutation would have nowhere sensible to put your recordings — they would
end up next to the program itself, or fail outright.

So when you press Save, Mutation puts its own folder back in the box, plays the
failure beep, and tells you what was wrong and where recordings will go instead.

**What to do.** Press **Save** again to accept the folder Mutation filled in, or type
the full path you want and save. The **Browse...** button next to the box always gives
you a full path.

If the settings file itself has an unusable folder in it — after editing it by hand,
say — Mutation fixes it while starting up and shows a message titled "Recording Folder
Changed" telling you where your recordings will go instead.

### A hotkey router mapping came up blank

**What's happening.** The hotkey router lets you press one shortcut and have Mutation
send a different one. If a mapping in the settings file has lost one of its two
shortcuts, Mutation no longer refuses to start — it leaves that side blank, keeps
every other setting, and shows a message titled "Hotkey Router Settings Issues"
naming the mapping.

**What to do.** Open **Settings** with **Ctrl+Comma**, go to **Hotkeys**, and either
fill in the missing shortcut or delete the row. A mapping with a blank side simply
does nothing until you finish it.

### Screen capture appears to be blocked

**What's happening.** Some managed work PCs disable screen capture by policy, and some
privacy and DRM-protected windows cannot be captured at all. Mutation tests this before
it tries, and tells you when the test fails, with the technical detail included.

**What to do.** If you are on a work device, this is a policy set by your IT
department and Mutation cannot work around it — ask them. Otherwise, check for privacy
or screen-recording-blocker software, and make sure the window you are capturing is not
playing protected video.

### Your own beep sounds stopped playing

**What's happening.** At startup Mutation checks every sound file you picked for the
custom beeps. Rather than leave you with a silent cue, it falls back to its built-in
beeps and shows a message titled "Custom Beep Settings Issues", naming each file and
what is wrong with it. The message also tells you how far the fallback went, because
that depends on the problem:

- If a file has been moved, renamed, deleted, or is not a `.wav` file, Mutation turns
  **Use custom beeps** off altogether. All six sounds go back to the built-in ones.
- If a file is there and is a `.wav` but still cannot be played — a truncated or
  damaged file, say — only that one sound falls back. Your other custom sounds carry
  on, and **Use custom beeps** stays on.

**What to do.**

1. Read the list — it names each sound and the file it could not use.
2. Put the file back, or pick a new one: open **Settings** with **Ctrl+Comma**, go to
   **Audio**, and browse for a `.wav` file for that cue. The small play button lets you
   hear it before you commit.
3. If the message told you **Use custom beeps** was turned off, turn it back on.
4. Save.

Until you do, everything still works — you just hear Mutation's own beeps.

## Finding the log file

When something goes wrong, Mutation writes a timestamped entry to a log file called
`Mutation_Errors.log`. It lives here:

```
%LOCALAPPDATA%\Mutation\logs\Mutation_Errors.log
```

To open the folder, press **Windows+R**, paste `%LOCALAPPDATA%\Mutation\logs` into the
box, and press Enter. Mutation also writes a copy of the log next to its own program
file, in case the first location is unavailable.

The log is capped in size. When it gets too big, the old contents are moved to
`Mutation_Errors.log.old` and a fresh file is started — so if the problem happened a
while back, check the `.old` file too.

Your API keys are stripped out of the log before anything is written, so it is safe to
share.

## Reporting a problem

If none of the above fixes it, please report it. The project's issues page is at
[github.com/Subverting-complexity/Mutation](https://github.com/Subverting-complexity/Mutation/issues).

Include as much of this as you can:

- What you did, step by step, and what you expected to happen instead.
- The exact wording of any message you saw or heard.
- Which shortcut you pressed.
- The relevant entries from `Mutation_Errors.log`, with their timestamps.
- Your Windows version, and the name of your screen reader or magnifier if one was
  running.

## Where to next

- [The Settings window](settings.md) — where most of the fixes above live.
- [Keyboard shortcuts](keyboard-shortcuts.md) — changing a shortcut another app owns.
- [Getting started](getting-started.md) — first-run setup and API keys.
- [Screen reader and accessibility notes](accessibility.md) — beeps, announcements,
  and the keyboard-driven capture overlay.
