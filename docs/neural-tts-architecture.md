# Local neural text-to-speech: the shared design

This is the reference the neural-voice stories share. It says how the pieces
fit together and, just as importantly, which parts of today's reading engine
have to move. Read it before picking up any story in the local neural voices
feature.

It is adapted from an architecture guide written for *Cadence Reader*, a React
Native e-book app for phones. The core advice survives the move to a Windows
desktop app; a good deal of the detail does not. The last section says what we
dropped and why.

## The one rule

> Mutation owns the reading: the text, the position, the sentence navigation,
> the announcements, the speed, the hotkeys, the screen-reader behaviour. A
> speech engine has one job — turn a piece of text into audio.

Everything below follows from that rule. If a change makes the engine decide
something about *reading*, it is the wrong change.

## Why this matters, and why it is not free

Mutation today does the opposite of the rule. `TextToSpeechService` hands the
whole text to Windows SAPI and then *learns* where the reading is by listening
to SAPI's `SpeakProgress` event. The engine is the source of truth for
position; the service follows along.

That works, and it works well, because SAPI reports progress word by word. It
is also why we cannot simply add a second engine beside it. A local neural
model does not stream a running character offset. It takes a sentence and
hands back a block of audio. So adding neural voices means building the thing
SAPI was doing for us: a queue that generates sentences ahead of time, plays
them in order, and tracks where the reading has got to.

That is the real work in this feature. The engines themselves are the easy
part.

### One consequence to be honest about

SAPI knows the position to the word. A chunked neural reader knows it to the
sentence, plus an offset in samples into the sentence currently playing. For
the "speak position" announcement ("Sentence 4 of 22, 35 percent, about 6
minutes left") that is no loss — it already reports sentences. For the
rewind-by-words behaviour on resume it is a real change: the neural path
rewinds to a sentence boundary, not to a word.

Do not paper over this. The system voice path keeps its word-level behaviour
exactly as it is. The neural path documents the sentence-level behaviour
plainly in the user guide.

## The layers

```text
Hotkeys, buttons, Settings  (Mutation.Ui)
        |
        v
Reading service  — owns text, sentence list, position, navigation,
        |          announcements, pause and resume
        v
Speech queue     — generates a few sentences ahead, cancels work nobody
        |          wants any more, rejects stale results
        v
Speech engine contract   (ISpeechSynthesisEngine)
        |
        +--> System voices (SAPI, what we ship today)
        +--> Kokoro          \
        +--> KittenTTS        >  all three through one sherpa-onnx engine
        +--> whatever is next /
        |
        v
PCM audio  ->  pitch-preserving speed change  ->  speakers
```

The reading service and the queue never name an engine. No `if (kokoro)`
anywhere above the contract.

## The contract

A sketch, not a specification. The contract story settles the final shape.

```csharp
public interface ISpeechSynthesisEngine : IDisposable
{
	string Id { get; }            // "sapi", "kokoro", "kitten"
	string DisplayName { get; }   // "Windows voices", "Kokoro"

	SpeechEngineCapabilities Capabilities { get; }

	IReadOnlyList<SpeechVoice> GetVoices();

	bool IsInstalled { get; }
	long InstalledSizeBytes { get; }

	// Turn one chunk of text into audio. Throws SpeechSynthesisException,
	// which carries a SpeechFailureReason. Must honour the token promptly.
	ValueTask<SynthesisResult> SynthesiseAsync(
		SynthesisRequest request,
		CancellationToken cancellationToken);
}
```

`SynthesisResult` carries mono PCM at a sample rate the engine states, plus
the request id it answers. The engine converts its own output format; the
player never learns what a model natively produced.

Voices are described in the terms a person chooses one by: name, language,
quality tier, whether it is installed, how big the download is. The engine
behind a voice is a detail we can show in a "technical details" line, not the
thing the list is organised around.

## Buffering and cancellation

While sentence 12 plays, the queue should already hold 13, 14 and 15, and be
generating 16. Two to four sentences ahead is the starting point; make the
depth a setting so it can be tuned on a slow machine.

Every generation carries a session id and a sentence id. When the user skips,
stops, changes voice or reads something else, the session id changes: work in
flight is cancelled, queued audio is thrown away, and any result that arrives
late for the old session is dropped on the floor rather than played.

This is not a nicety. Without it, a skip from sentence 12 to sentence 400
plays sentence 13 a second later, because it was already in the oven.

`SupersedingOperation` in `CognitiveSupport` already does exactly this kind of
handover for the current service, and the comment on it explains a bug we have
already paid for once. Reuse it rather than inventing a second mechanism.

## Speed

Generate at the model's natural rate. Change speed on the way out, with
pitch preserved.

We already own this part. `SoundTouchSampleProvider` and
`PlaybackSpeedOptions` give the recorded-audio player 0.5x to 3.0x without the
voice going squeaky. The neural reading path uses the same stage.

Do not ask a neural model to speak at 3x. Model-level rate controls fall apart
well before the speeds an experienced screen-reader user actually reads at.

This leaves the settings in an awkward spot worth planning for: the current
`Rate` setting is a SAPI number from -10 to 10, and the default is 8. A
multiplier means nothing to SAPI and a SAPI rate means nothing to a neural
voice. The speed story settles how the two are presented so that switching
voice does not silently change how fast the reading goes.

## Models on disk

Neural models are downloads, not build output. They are far too big to ship
inside an MSIX package, and a user who never turns neural voices on should
never pay for them.

- Store under `%LOCALAPPDATA%\Mutation\`, beside the settings file and the
  error log. Not in a temp folder Windows may clear.
- Track for each installed model: version, files, total size, checksum.
- Verify the checksum before the model is ever loaded. A half-finished
  download must not be handed to the engine.
- Check free disk space before starting, and say the number out loud in the UI.
- Deleting a voice from Settings must actually free the disk space.

## When it fails

The contract exposes one set of reasons, whichever engine failed:
model not installed, download failed, model corrupted, not enough storage,
synthesis failed, cancelled, unsupported language, unsupported voice, out of
memory, engine unavailable. The UI turns those into sentences a person can act
on.

If a neural read fails mid-way: retry the sentence once, and if it fails
again, tell the user and offer the system voice — keeping the position. Never
skip silently past text the user has not heard. Silently jumping ahead is the
one failure a blind user cannot detect.

## Accessibility

The reading voice and the screen reader are different things and must stay
that way. Narrator and ZoomText announce the buttons; the neural voice reads
the text. A user must never have to listen to the neural voice to find out
what a control is called.

Everything already true of the reading controls stays true: every new control
has a real label, help text, a reset button where it has a default, and is
reachable by keyboard. Download progress has to be announced, not just drawn.

## What we took from the guide, and what we left

Taken, more or less whole:

- The one rule at the top of this page.
- The engine contract, and the refusal to build around one model.
- Sentence-sized generation with a small look-ahead buffer.
- Cancellation as a first-class feature, with generation ids against stale
  results.
- Natural-rate generation plus time-stretched playback.
- Models as versioned, checksummed, optional downloads.
- Voices presented as voices, not as machine-learning models.
- A licence record per engine, and the ability to drop one.
- Position advances on playback, not on generation.

Left behind, because Mutation is not a phone e-book reader:

- **The React Native bridge.** sherpa-onnx publishes an official .NET binding
  (`org.k2fsa.sherpa.onnx`, with `org.k2fsa.sherpa.onnx.runtime.win-x64`
  carrying the Windows native library). We are a .NET 10 app on Windows. There
  is no bridge to build and no tensor marshalling to worry about — it is a
  package reference.
- **iOS and Android.** Along with background playback, lock-screen controls,
  Bluetooth reconnection, VoiceOver and TalkBack, and thermal throttling.
- **Books.** No chapters, no bookmarks, no implicit bookmarks, no reading
  percentage across a volume. Mutation reads what is on the clipboard or what
  you have selected.
- **Reading profiles.** The guide assumes a per-profile voice and speed.
  Mutation has one set of speech settings. If profiles ever arrive they are
  their own feature.
- **Automatic per-language engine routing.** Worth having eventually; it is not
  in this feature. One selected voice, predictable, until someone changes it.

## Order of work

The guide's implementation order is right, and it contains the test that
matters: if adding the *second* engine needs changes above the contract, the
contract is wrong.

1. The contract and its types. No engine, no behaviour change.
2. The queue that owns position and buffers ahead.
3. The audio path that plays PCM chunks at the chosen speed.
4. A reading service built on those three, still behind the existing interface.
5. Model download, verification, storage, deletion.
6. The first engine: Kokoro through sherpa-onnx.
7. Voice selection in Settings.
8. Speed, and how the old rate setting maps onto it.
9. Failure handling and the fall back to a system voice.
10. The second engine — the abstraction's real exam.
11. Everything after that: caching, export to file, licence records.
