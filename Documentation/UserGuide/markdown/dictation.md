# Dictation: turning speech into text

Dictation is the feature most people end up using every day. You press a shortcut,
talk, press it again, and a second or two later your words are sitting there as
text — ready to paste into an email, a chat message, or a document.

## The basic loop

1. Press **Shift+Alt+U**. Mutation plays a short beep and starts recording.
2. Say what you want to write. Take your time — pauses are fine.
3. Press **Shift+Alt+U** again. The beep tells you recording has stopped.
4. A moment later — usually one to three seconds for a few sentences — the text
   appears, and a success beep plays.

That's it. The shortcut is global, so it works no matter which app you are in. You
never have to switch to Mutation first.

If you prefer clicking, the **Speech to Text** card on the main window has a
**Record** button that does exactly the same thing.

> **Shift+Alt+U** is just the default. Every shortcut in Mutation can be changed.
> Open **Settings** with **Ctrl+Comma** and look on the **Hotkeys** tab.

## Changed your mind while it is transcribing

While Mutation is turning your recording into text, the **Raw Transcript** box
reads "Transcribing..." and the buttons are greyed out. Press **Shift+Alt+U** once
more and it gives up on that recording.

You hear two things, a second or so apart. First the failure beep and "Cancelling
transcription..." — that is Mutation telling you your keypress arrived. Then, once the
transcription has actually let go, "Transcription cancelled." The box empties itself
and everything is yours to use again.

If you press again in between, you hear "Already stopping." Nothing has gone wrong —
your first press landed, and Mutation is waiting for the transcription service to let
go of the request.

Your recording is not lost. It is still the session Mutation has selected, so you
can press **Retry transcription** to send it off again — or step back to it later
with the **older** and **newer** arrow buttons.

## Recording and cleaning up in one step

There is a second shortcut, **Shift+Alt+I** by default, that records *and* then
runs your chosen AI prompt on the result — all from the one press. So you can
ramble a bit, and what comes back is already tidied into proper sentences.

On the main window this is the **Record and Format** button, next to **Record**.

Which prompt runs is up to you: it's the one you have marked as **Auto-Run**. See
[Using AI prompts on your text](ai-prompts.md) for how to set that up.

## Changed your mind while the AI is tidying up

The AI step happens after the transcription, and it can take a lot longer. If the
service is down, Mutation keeps trying — giving each attempt a longer window than the
last — for about **ten minutes** before it gives up. So this is the wait you are most
likely to want out of.

While it runs, the **Raw Transcript** box reads "Processing with LLM..." — LLM is just
the app's short name for the AI. Both **Record** and **Record and Format** stay
available, renamed to **Stop LLM processing**. Press either button, or press
**Shift+Alt+I** or **Shift+Alt+U** again. Any of those gives up on the AI step.

You hear the failure beep and "Cancelling LLM processing..." Then, once the request has
let go, the success beep and "LLM processing cancelled. Transcript ready." Press again
while it is still winding down and you hear "Already stopping." — the first press
did register; the AI is just taking a moment to let go.

Your dictation is not thrown away with it. Everything you said is still transcribed,
still in both boxes, and still on your clipboard — just without the AI's tidying up.
Cancelling costs you the polish, never the words.

## Where the text ends up

Once a recording is transcribed, the text lands in three places at once:

- The **Raw Transcript** box on the main window — exactly what the transcription
  service heard.
- The **Formatted Transcript** box, just below it — the same text after your
  find-and-replace rules have been applied. If you haven't set any rules up, the
  two boxes will look the same. See
  [Automatic find-and-replace rules](transcript-formatting.md).
- Your **clipboard**. The formatted version is copied automatically, so you can
  paste it anywhere with **Ctrl+V**.

Both boxes are ordinary text boxes. You can read them with your screen reader,
edit them, or select and copy from them.

## Getting the text into the app you were using

This is the part that trips people up, so it's worth reading slowly.

On the **Automation** card there is a dropdown called **Third-party interaction**.
It decides what Mutation does with your transcript *after* it has copied it to the
clipboard. There are three choices:

| Choice in the list | What happens |
|---|---|
| **Paste into 3rd party application** | Mutation copies the text and then presses **Ctrl+V** for you, so it appears in whatever app was in front. |
| **Send keys to 3rd party application** | Mutation types the text out, one keystroke at a time, as if you had typed it yourself. |
| **Don't insert into 3rd party application** | Mutation does nothing further. The text stays in the Mutation window and on the clipboard, and you paste it yourself. |

> A line of explanation appears underneath the dropdown as soon as you pick one, and
> your screen reader reads it out.

**Which should you pick?**

Start with **Paste into 3rd party application**. It is the fastest and it handles
long text without trouble.

Switch to **Send keys to 3rd party application** when an app refuses to accept a
paste, or mangles it. Some older apps, some remote-desktop sessions, and some web
forms behave that way. Typing is slower and you will see the letters appear one by
one, but it works almost everywhere.

Choose **Don't insert into 3rd party application** when you want to look at the text
before it goes anywhere — or when you are dictating notes into Mutation itself and
don't want stray text landing in another window.

One thing worth knowing: if the Mutation window itself is the one in front, nothing
is inserted anywhere. Mutation won't paste into itself.

Whichever choice you make, Mutation waits for the typing or pasting to finish before
it tells you the result, and checks that Windows accepted the keystrokes. If Windows
turned them down, you get the failure beep and a message saying the text could not be
sent, instead of a success beep for text that never landed. The transcript stays in
the Mutation window either way, so nothing is lost.

Windows does not always admit that it refused, so this catch is not a guarantee. If a
success beep is ever followed by an empty window in the other app, the text is still
in Mutation — paste it yourself with **Ctrl+V**.

### Sending a shortcut afterwards

In **Settings**, on the **Speech to Text** page, there is an optional box called
**Send hotkey after transcription**. Whatever shortcut you put there is sent to the
active app once the transcript has been delivered.

It exists for apps that need one extra keypress to finish the job — for example
sending **Ctrl+V** to paste, or a **Tab** or **Enter** to move on. Leave it blank
if you don't need it.

## Choosing a transcription service

At the top of the **Speech to Text** card is a dropdown listing your transcription
services.

A transcription service is simply an online service that receives your recording,
listens to it, and sends the text back. Mutation doesn't do the listening itself —
it hands the audio over and waits for the answer. Different services differ in how
fast they are, how accurate they are, and what they charge, so it's worth trying a
couple and settling on the one you like.

Out of the box, Mutation knows about:

- **OpenAI gpt-4o-transcribe** (and its smaller, cheaper sibling
  gpt-4o-mini-transcribe)
- **Groq Whisper 3**
- **Deepgram Nova3**
- Any other service that speaks the same language as OpenAI's Whisper

Each service needs an API key — a long password that lets Mutation talk to the
service on your behalf. See [Getting started](getting-started.md) for where those go.

> Switching which service is *active* takes effect straight away — just pick a
> different one from the dropdown. But if you **add**, **remove**, or **edit** a
> service definition in Settings, you need to restart Mutation before the change
> takes hold.

## The Prompt box

Under the session buttons there is a box simply labelled **Prompt**. It's optional
and easy to overlook, but it can noticeably improve your results.

Whatever you type here is sent along with each recording as a hint about what kind
of words to expect. It is especially good at getting unusual spellings right.

Say you work with a colleague called Siobhán, a product called Kestrel Nine, and
you keep saying "EBITDA". Type those into the Prompt box and the service will
spell them properly instead of guessing:

```
Siobhán, Kestrel Nine, EBITDA, Bloemfontein, Anthropic
```

The Prompt box belongs to whichever service is currently selected, so you can keep
a different list of terms for each one. It saves itself as you type.

## Working with past recordings

Mutation keeps your recent recordings so you can go back to them.

- The **older** and **newer** arrow buttons step back and forward through your
  recent sessions.
- **Play selected session** plays the one you've landed on, so you can hear what
  you actually said. A long recording takes a moment to get ready before the sound
  starts; if it takes more than a blink, Mutation says "Decoding" and the name of
  the file, so you know it is working on it. The window stays usable throughout, and
  pressing the button again stops it — even while it is still getting ready.
- The **Speed** dropdown changes how fast it plays back, from half speed up to
  three times normal. Voices stay at their natural pitch at every speed — nobody
  turns into a chipmunk.
- **Retry transcription** sends the selected recording off to be transcribed again.
  This is the useful one: if a transcript came back wrong, pick a different service
  or add some terms to the Prompt box, then press Retry.

By default Mutation keeps the last **10** recordings, and quietly deletes the
oldest ones beyond that. You can set this anywhere from 1 to 500 in **Settings**,
under **Recording sessions to keep**. A recording that is still in progress is
never deleted.

The recording you have selected is never one of the ones cleared away, nor is one
that is playing.

If the selected recording goes missing some other way — you deleted the file
yourself, say, or something else tidied the folder — **Play selected session** says
"Audio file not found", and names the recording it has selected for you instead. It
picks the newest one. That way the next button you press acts on a recording that is
really there, and you know which one it is. If there are no recordings left at all,
you just get "Audio file not found", because there is nothing to move to.

## Transcribing a file you already have

You don't have to record inside Mutation. If someone sends you a voice note, or you
have a recording of a meeting, use the **Upload audio for transcription** button on
the **Speech to Text** card. Pick the file and Mutation transcribes it the same way
it would a live recording.

It accepts audio files — MP3, WAV, M4A, AAC, FLAC, OGG, OPUS, WMA — and video
files — WEBM, MP4, AVI, MKV, MOV, WMV, M4V. With a video, Mutation pulls out the
soundtrack and transcribes that. Long files take longer, naturally; Mutation allows
a much more generous wait for them than for a quick live recording.

## Trimming the quiet bits

Mutation trims long silent gaps out of your audio before sending it off. This means
the pauses while you think don't make the recording bigger and slower than it needs
to be. It applies to recordings you make and to files you upload, and it is on by
default — leave it on unless you have a reason not to.

If you want to fine-tune how aggressive the trimming is, the dials live in
[The Settings window](settings.md).

---

## Where to next

- [Using AI prompts on your text](ai-prompts.md) — clean up, rewrite, or summarise
  what you dictated.
- [Automatic find-and-replace rules](transcript-formatting.md) — fix recurring
  mistakes automatically.
- [Keyboard shortcuts](keyboard-shortcuts.md) — the full list, and how to change
  them.
- [Muting your microphone](microphone.md) — choosing which microphone Mutation
  records from.
