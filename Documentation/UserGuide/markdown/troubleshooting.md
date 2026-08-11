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

You can hear this happening. Each retry plays the end beep once more than the one
before — two beeps, then three, then four. A burst of end beeps part way through a
dictation means Mutation is still working, and how many you hear tells you how far into
its retries it has got.

Some services answer a busy moment by telling Mutation how long to wait before asking
again. When Mutation is told, it waits that long, up to a minute — so a gap between
beeps that is longer than usual is the service setting the pace, not something going
wrong. Deepgram does not tell Mutation, so its retries come at the usual short
intervals.

Not everything is worth another go. If the service turns the request down for a reason
that will not change — your key was rejected, or the recording is bigger than it
accepts — Mutation tells you straight away instead of working through the retries
first. You hear one set of beeps, not four.

**What to do.**

1. Give it a moment — the retries are automatic and you will hear the result.
2. Record in shorter chunks. A two-minute note transcribes far faster than a
   twenty-minute one.
3. If it consistently times out, raise the timeout in **Settings**. There are separate
   timeouts for live dictation and for transcribing a file, plus a per-attempt timeout
   on each speech service.

### A long recording takes several rounds to come back

**What's happening.** Transcription services refuse a file over about 25 MB in one
request. When a recording is bigger than **Maximum upload size (MB)** in **Settings**,
Mutation splits it and sends the pieces one after the other, then joins the results
into a single transcript. That is several trips over the internet instead of one, so a
long meeting recording takes noticeably longer than a short note. No audio is skipped,
and the pieces are joined back in the order you recorded them.

**What to do.**

1. Wait it out — the pieces are sent automatically and you get one transcript at the
   end.
2. Leave **Strip silent gaps from audio** switched on. It makes recordings smaller, so
   fewer of them need splitting at all, and it gives Mutation the pauses it uses to
   split between sentences rather than mid-word.
3. If a message says a piece is still over the upload limit, lower **Maximum upload
   size (MB)** and try again.
4. If your service accepts larger uploads than OpenAI does, raise the number, or set
   it to 0 to never split.

### The AI seems stuck on "Processing..."

**What's happening.** The AI runs over the internet too, and Mutation retries a failed
request, giving each attempt a longer window than the last. The retries are silent, so a
long wait sounds exactly like nothing happening. With the standard settings an AI service
that has gone down keeps Mutation waiting about **ten minutes** before it admits defeat —
and up to twenty if the prompt asked for Fast mode and the service turns it away for being
busy, because Mutation then tries the whole thing again at normal speed. Nothing is broken;
it is still trying.

If the AI service asks Mutation to wait a moment before asking again — which is what a
busy service usually does — Mutation waits as long as it was asked to, up to a minute,
rather than asking straight back.

A prompt you started yourself puts "Processing..." in the **Formatted Transcript** box. The
AI step after a dictation says "Processing with LLM..." in the **Raw Transcript** box
instead.

**What to do.**

1. Stop it. If the AI step followed a dictation, both **Record** and **Record and
   Format** are still available, renamed to **Stop LLM processing** — or press
   **Shift+Alt+U** or **Shift+Alt+I** again. For a prompt you started yourself, press
   its shortcut again, or **Run** on any row, or **Process with LLM**.
2. Listen for the two messages. "Cancelling LLM processing..." means your press landed;
   "LLM processing cancelled." means the request has actually let go. In between, a
   further press answers "Already stopping." rather than doing anything new.
3. Cancelling a dictation's AI step keeps the dictation. You hear the success beep and
   "LLM processing cancelled. Transcript ready.", and your words are in both boxes and on
   the clipboard — you lose the tidying up, never what you said.
4. Closing the Mutation window also stops an AI request, so you never have to wait for
   one to finish before you can quit.
5. If it happens often, lower **Request timeout** or **Retries** under **AI
   assistance** in **Settings** so Mutation gives up sooner and tells you.

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

### The screenshot could not be copied to the clipboard

**What's happening.** Only one program can have the clipboard open at a time, and the
moment a picture lands there is exactly when a clipboard manager or your screen reader
opens it to see what arrived. Mutation waits and tries again a few times, which almost
always gets in.

With a plain **Screenshot to clipboard**, when it does not get in you hear the failure
beep and see the message "Another program is using the clipboard, so the screenshot was
not copied. Try again in a moment." That picture is gone and you will need to pick the
region again. Your clipboard still holds whatever it held before — Mutation does not wipe
it on the way to failing.

With **Screenshot & OCR** it is gentler, because the reading does not need the clipboard.
Mutation reads from the picture it is holding, so you get your text as usual, you hear
the ordinary success beep, and the message tells you only that the picture itself did not
make it.

**What to do.** Take the screenshot again — the second attempt almost always gets in. If
it happens every time, a clipboard manager or clipboard-history tool is the usual
culprit; closing it or turning off its monitoring settles it.

### Mutation says a screenshot is already in progress

**What's happening.** You pressed a capture shortcut while a capture overlay was already
on screen. Only one capture can run at a time, so the second press starts nothing.
Mutation brings the overlay you already have back to the front and tells you so.

With **Screenshot to clipboard** you get the message "A screenshot is already in
progress. Select a region, or press Escape to cancel it." With **Screenshot & OCR** the
words "Screenshot already in progress" appear in the **OCR result** box instead, and
nothing else happens — in particular, any shortcut you set to run after an OCR is held
back, so it cannot land in the capture overlay.

Either way, the overlay has not gone anywhere. It is still there, still waiting for you
to pick a rectangle. This is not a cancellation, and nothing has been copied or lost.

**What to do.** Carry on with the capture: pick your rectangle with the mouse or the
keyboard as usual. If you would rather start again, press **Esc** to close the overlay
first, then press the shortcut once more.

### OCR read the text but could not copy it to the clipboard

**What's happening.** Only one program can have the clipboard open at a time. Just
after a screenshot, a clipboard manager or your screen reader often opens it to look at
the picture that arrived — and for that moment nothing else can write to it. Mutation
waits and tries again a few times, which is usually enough. When it is not, you hear
the message "The text was recognised, but it could not be copied to the clipboard. It
is in the OCR results box."

This is not a failed reading. The text is there, in the **OCR result** box on the
**Visual Capture** card, and any shortcut you set to run afterwards still fires, so a
screen reader can read it out of the box as usual.

Mutation does not paste it for you this time, though. Pasting takes the text off the
clipboard, and the clipboard is exactly what did not get it — you would end up with
whatever was there before, most likely the screenshot.

**What to do.** Copy the text out of the **OCR result** box, or press the OCR shortcut
again — the second attempt almost always gets in. If it happens every time, a clipboard
manager or clipboard-history tool is the usual culprit; closing it or turning off its
monitoring settles it.

### OCR text isn't landing in the app I'm working in

**What's happening.** Mutation normally pastes the recognised text where your cursor
was. There are four reasons it would not.

- The **Paste OCR text into the active app** setting is switched off. It lives under
  **Screen capture & OCR** in **Settings** (**Ctrl+Comma**), and is on by default.
- You started the reading with a button in the Mutation window instead of a shortcut.
  The app you were working in is then Mutation, so there is nowhere else to paste. Use
  the shortcut — **Shift+Alt+J** and friends — from the app you want the text in.
- The reading found no text, or its text never reached the clipboard. Either way there
  is no new text to paste, and pasting would put whatever the clipboard does hold — most
  likely the screenshot — into your document. See
  [OCR returns nothing useful](#ocr-returns-nothing-useful-or-the-wrong-reading-order)
  and
  [OCR read the text but could not copy it](#ocr-read-the-text-but-could-not-copy-it-to-the-clipboard).
- It was a batch of documents. Those are always started from the Mutation window, so
  the point above applies.

**What to do.** Check the setting first. If it is on and you used a shortcut, your
text is still on the clipboard and in the **OCR result** box — paste it with
**Ctrl+V**, and see [A shortcut does nothing](#a-shortcut-does-nothing) if keystrokes
Mutation sends never seem to arrive.

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

**What's happening.** Every page of every file you picked is sent off to be read.
Mutation works on two documents at a time by default, with up to four pages in the air
at once, so forty PDFs still adds up to a lot of waiting.

A page that does not get through first time is sent again, and each retry plays the end
beep once more than the one before. Extra beeps during a batch are normal on a busy
connection; they mean a page is being retried, not that anything has failed.

Each try also waits longer than the one before it, up to a minute, so a page that is
just being slow gets the room it needs. A page that never gets through is tried four
times, which is why one stubborn page can hold things up for a few minutes before
Mutation gives up on it and moves on.

**What to do.** Listen for the announcements — Mutation names each file as it finishes,
and calls out the page count every ten pages inside a long PDF, so you can tell it is
still working. If you picked the wrong files, or simply want it to stop, click **Cancel
OCR** under the progress bar. Closing the Mutation window stops a running batch too.

To speed things up, open **Settings** (**Ctrl+Comma**) and find **Max parallel
documents** and **Max parallel requests** under the OCR settings. Raising them sends
more at once. Do not go beyond what your Azure plan allows, or the service starts
refusing requests and the batch ends up slower.

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
3. If you rely on the text landing in another app automatically, check **Insert
   preference** under **Interface** in **Settings**. Set to *Paste into 3rd party
   application*, Mutation pastes for you whenever the copy succeeds.

### Mutation says it could not send the text to the other app

**What's happening.** This is a different problem from the clipboard one above, and it
has its own message: "The transcript could not be sent to the other application; it may
be running with higher privileges than Mutation. It is available in the Mutation
window." You get a failure beep with it, not the success beep.

Windows will not let an ordinary program type into a window that is running with
administrator privileges. It simply throws the keystrokes away, and it does not tell
the program that sent them. So Mutation asks first: before typing or pasting, it
compares how much privilege the app in front is running with against its own, and stops
with this message when the other app is higher. That is why you hear about it instead of
getting a success beep for text that never arrived.

It is not a cast-iron guarantee. When Windows will not tell Mutation anything about the
app in front, Mutation sends the text rather than refusing to — so a success beep
followed by an empty window in the other app is still possible. Your text is in the
Mutation window either way.

**What to do.**

1. Your text is not lost. It is in the main window and, unless the clipboard also
   failed, on your clipboard — paste it with **Ctrl+V**.
2. Check the other app before you paste, so you don't end up with the text twice.
3. The usual cause is an app started with "Run as administrator", or a system dialog
   that has taken the foreground. Close it, or click into an ordinary window, and
   dictate again.
4. Task Manager, the Windows security prompt, and some installer windows behave this
   way too. Nothing is wrong with Mutation or your microphone.

### The shortcut I set to run afterwards doesn't happen

**What's happening.** This is the **Send hotkey after transcription** and **Send hotkey
after OCR** boxes in **Settings**. There are two reasons one may not arrive.

The first is that dictation only sends it when the text was delivered. If you heard the
failure beep, the shortcut is deliberately skipped — it is usually something that acts
on the text, and running it when the text never landed would aim it at the wrong thing.
OCR is different: it sends the shortcut either way, so a screen-reader command in that
box reads out the error as readily as the result. There are two exceptions. A batch of
documents you cancelled sends nothing, because nothing is copied then. And an OCR
shortcut pressed while a capture is already on screen sends nothing either — nothing
happened, so there is nothing new to read, and the keystroke would land in the capture
overlay rather than where you aimed it.

Cancelling a capture does still send it, so a screen-reader shortcut reads out the
"cancelled" message. It waits a moment first, until the window you were in has the
keyboard back.

The second is how the shortcut is written. Write it the ordinary way — modifier names
joined with plus signs, like **Ctrl+V**, **Alt+F4** or **Ctrl+Shift+Delete**. Mutation
still understands the older shorthand some settings files hold, such as `^{DEL}` for
**Ctrl+Delete**, but the ordinary spelling is the one to reach for.

**What to do.**

1. Deal with the failure beep first. Whatever stopped the text arriving is stopping
   the shortcut too, and the two entries above cover the usual causes.
2. Retype the shortcut the plain way — **Ctrl+Delete** rather than a shorthand.
3. You can put more than one in, separated by commas: **Ctrl+A, Ctrl+C** sends the two
   in turn.

### Something went wrong while delivering the transcript

**What's happening.** Very occasionally the delivery itself fails in a way Mutation did
not expect — a beep that cannot play, for instance. You hear the failure beep and read
"Something went wrong while delivering the transcript. It is available in the Mutation
window."

**What to do.** Copy the text from the main window. The transcript box goes back to
being editable straight away, so you can carry on dictating. If it keeps happening,
the log file has the details — see **Finding the log file** below.

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

### The microphone list says "Finding microphones..." and stays that way

**What's happening.** Mutation asks Windows which microphones the PC has as soon as it
starts, but it does not hold the window shut while it waits. On most PCs the answer
comes back before you notice. A microphone whose driver has got stuck, or a Bluetooth
headset that never finishes connecting, can leave the question unanswered.

**What to do.**

1. Give it a few seconds. A USB microphone that has just been plugged in, or a headset
   still pairing, is often simply slow.
2. If it is still saying it after that, close Mutation and start it again. While the
   question is unanswered Mutation is not yet watching for microphones being plugged
   in or out, so unplugging and re-plugging will not shake it loose.
3. Before restarting, check the microphone works elsewhere — Windows Settings has a
   sound page that shows a test bar moving when you speak. If it does not move there
   either, the problem is the microphone or its driver, not Mutation.
4. "Could not read the microphones on this computer" means the question came back with
   an error rather than an answer. Close Mutation, reconnect your microphone, and start
   it again.

### Dictation says it is waiting for the microphone

**What's happening.** You pressed the dictation shortcut while Mutation was still
opening a microphone. Rather than record from the one you have just switched away
from, it holds your press until the right one is ready. You hear a low two-tone sound
that falls, and see "Waiting for the microphone to be ready".

**What to do.**

1. Wait a beat. Normally the start beep follows within a second or two and recording
   begins as usual.
2. After eight seconds Mutation stops waiting. If it has a working microphone to fall
   back on, it records from that one and tells you which: "The microphone you chose is
   still not ready. Recording from ... instead." Your words are not lost — just check
   the recording came from a mic you are actually speaking into.
3. If instead you get "The microphone is still not ready, so nothing was recorded",
   there was nothing to fall back on. Press the shortcut again to try once more.
4. If it keeps happening, restart Mutation. A microphone whose driver has stopped
   answering holds Mutation's microphone switching until the app is restarted, so
   picking a different one in the dropdown will not clear it. A Bluetooth headset that
   is half-connected is the usual culprit — turn it off, or unpair it, before you start
   Mutation again.

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

### Reading aloud fails, or the voice you chose has gone

**What's happening.** The voice Mutation was told to use is no longer installed —
a Windows update removed it, or the settings came from another computer that had it.
Reading can also fail if the audio device it was playing to has been unplugged.

Mutation plays the failure beep, puts the reason on the main window, and opens a
dialog with the details. When the chosen voice is the problem, the message names
it. This happens whether you started the reading with the button or the shortcut,
so a shortcut press never simply does nothing.

**What to do.**

1. Open the **Voice** list on the **Voice & Speech** card and pick a voice that is
   there now. If the list is short, install more through Windows Settings and
   restart Mutation.
2. If the voice is fine, check the speakers or headphones you were listening
   through are still connected, then press the shortcut again.

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

### A speech service came back set to OpenAI

**What's happening.** Every speech service in the settings file records which company
does the transcribing — OpenAI or Deepgram. If that entry is missing, blank, or
misspelt, Mutation cannot tell which one you meant. Rather than refuse to start, it
sets that service to OpenAI while starting up and keeps everything else you had
configured. This normally only happens after editing the file by hand — the
**Advanced** switch in **Settings** reveals an **Open Mutation.json** button that
opens it.

**What to do.** Open **Settings** with **Ctrl+Comma**, go to **Speech to Text**, and
pick the service you actually wanted from the list. If you use Deepgram, check the
service is set back to Deepgram before you dictate again.

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
share. The same applies to anything Mutation shows you on screen or reads out — an error
message never contains one of your keys.

### When an error message appears

An error message tells you what went wrong in up to four short lines, then points at the
log file. The lines run from the thing you were doing down to the underlying cause, so a
failed dictation might read "Speech to text failed." and then "401 (Unauthorized)" — the
second line being the one that tells you to go and check your API key.

The long technical detail — the part only a developer can use — goes to the log and not to
the screen, so the message stays short enough to read or listen to. If you are reporting
the problem, copy the message and then open the log for the rest.

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
