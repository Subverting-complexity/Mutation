# Muting your microphone

This is the feature Mutation was built for in the first place, and it is still the
one most people use every day.

Press one shortcut and every microphone on your PC goes quiet. Press it again and
they all come back. It works no matter which app you are in — you do not have to
find the meeting window first, and you do not have to learn a different mute
shortcut for Teams, Zoom, Slack and everything else.

## Why it mutes *every* microphone

Most PCs have more than one microphone. There is the one in your headset, maybe one
in your webcam, and maybe one built into the laptop lid. Meeting apps do not always
pick the one you expect, and they can quietly switch to a different one.

That is the trap. You mute what you think is "the" microphone, carry on talking to
someone in the room, and the call has been listening through a different mic the
whole time.

Mutation avoids that by muting all of them together. When it says muted, every
microphone on the machine is muted.

## The shortcut

The default shortcut is **Alt+Q**.

You can change it. Open **Settings** with **Ctrl+Comma**, go to the **Audio** page,
and set a new shortcut in the "Toggle microphone mute" box. Every shortcut in
Mutation can be changed this way.

You can also click the microphone button on the **Microphone** card on the main
window. It does exactly the same thing as the shortcut.

## How you know it worked

Two things tell you the new state.

- **A beep.** One sound for muted, a different one for unmuted.
- **A message** on the main window: "Microphone muted." or "Microphone is live."

If the change could not be confirmed, you get the failure beep instead and a
message saying the microphone may still be live. That is deliberate. Mutation will
never tell you that you are muted unless it has checked each microphone and
confirmed it.

> Tip: tapping the shortcut twice very quickly is safe. The second press is ignored
> while the first is still working, so you cannot accidentally end up back where you
> started.

### Using your own sounds

If you do not like the built-in beeps, you can swap in your own sound files — one
for mute, one for unmute, and for Mutation's other status sounds too. Turn on
"Custom beeps" on the **Audio** page in **Settings**, then browse for a sound file
for each one. There is a play button next to each so you can hear it before you
commit. See [The Settings window](settings.md) for the full walkthrough.

## Choosing the active microphone

The **Microphone** card has a dropdown listing the microphones Mutation can see.
The one you pick is the *active* microphone — the one Mutation records from for
dictation, and the one the level meter and the level controls apply to.

This choice does not affect muting. Muting always covers every microphone,
whichever one is selected. Your choice is remembered the next time you start
Mutation.

## The live level display

Underneath the dropdown is a live picture of what your microphone is hearing: a
waveform that moves as you speak, and a vertical bar next to it that rises and falls
with how loud you are. It is a quick way to check that the mic is actually picking
you up before you start talking.

To turn it off, click on the waveform itself — it is a toggle. Click it again to
turn it back on. When it is off it simply shows the word "Off".

It is reachable from the keyboard too: tab to "Toggle microphone visualization" and
press Space or Enter. There is also a "Microphone visualization" switch on the
**Audio** page in **Settings** if you prefer to set it there. Either way, the
setting is remembered.

## Pin input level

Windows lets any app change your microphone's input volume, and some of them do it
without telling you. You set your mic to a comfortable level, join a call a few days
later, and suddenly nobody can hear you.

"Pin input level" stops that. It works like this:

1. Use the **Input level** slider to set the volume you want, from 0 to 100. The
   change applies to Windows straight away, even when you are not recording.
2. Turn **Pin input level** on. Mutation remembers that number as your target.

From then on, Mutation puts the level back to your number whenever it matters: when
you start recording, when you switch microphones, and when the app starts up. If
something moved it in the meantime, it gets moved back.

Turning the pin off stops the re-checking. It does not change the level you are
currently on — it just stops Mutation from correcting it.

Some microphones have a fixed input level that software cannot change at all. On
those, the switch and the slider are greyed out, and Mutation tells you why.

## Plugging microphones in and out

You can plug in a headset or unplug one while Mutation is running. It notices and
updates the list on its own.

- **A mic appears or disappears somewhere else in the list** — your selected
  microphone is untouched, and Mutation stays quiet about it. Nothing about your
  audio changed, so there is nothing to interrupt you with.
- **Your selected microphone is unplugged** — Mutation switches to the first
  microphone in the list and tells you: "The selected microphone was disconnected.
  Now using ...".
- **No microphones at all** — you get a message saying none are available.

There is one more nicety. If you are muted and then plug in a new microphone, the
new one comes up muted too. Connecting a headset mid-call will not quietly put you
back on air.

## Where to next

- [A tour of the main window](main-window.md) — where the **Microphone** card sits
  and what else shares the screen with it.
- [The Settings window](settings.md) — changing the shortcut, and using your own
  sound files instead of the beeps.
- [Dictation: turning speech into text](dictation.md) — the other half of what your
  microphone does in Mutation.
- [Troubleshooting](troubleshooting.md) — what to try when a mute does not take or a
  mic goes missing.
