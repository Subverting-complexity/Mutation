# Timers

A timer plays a sound every so often, as a reminder. For example, you can have Mutation play a click every 45 minutes to remind you to stand up and stretch. You can have as many timers as you like, and turn each one on and off.

When a timer is due, Mutation plays its sound and does nothing else. Nothing pops up, and nothing is read out.

---

## Setting up a timer

1. Press **Ctrl+Comma** to open Settings, and choose **Timers** in the list on the left.
2. Press **Add timer**. A new timer appears, called **Timer 1**. It is switched on.
3. Change the **Name** to something you will recognise, such as "Stretch".
4. Set how often it plays with **Hours** and **Minutes**. The shortest is 1 minute and the longest is 24 hours.
5. Leave the **Sound file** as it is, or press **Browse...** to pick a different .wav file. Press **Play** to hear it.
6. Press **Save**. The timer starts counting from that moment.

Each timer also has **Move up** and **Move down** buttons, which set the order the timers are listed in, and a **Remove** button. Nothing changes until you press **Save**, and **Cancel** throws your changes away.

A new timer plays the sound of a mouse click. Mutation comes with a few other short sounds you can pick instead. They are in the **CustomAudio** folder, in the folder where Mutation is installed.

## Seeing and switching timers on the main window

When you have at least one timer, a **Timers** strip appears near the top of the main window, just under the **Mutation Workspace** heading. It lists every timer in the same order as Settings. Each one has an on/off switch, its name, and how long until it plays next, for example:

- **next in 1 hour and 12 minutes**
- **next in 5 minutes**
- **next in 45 seconds**, in the last minute
- **off**, when the timer is switched off

You can switch a timer on or off right there. It is the same switch as the one in Settings, and Mutation remembers it straight away.

With a screen reader, tab to a timer's switch to hear its name, whether it is on, and when it plays next, for example "Stretch timer, on, next in 4 minutes". Mutation says the time once, when you arrive. It does not keep reading the countdown as it changes.

## How the timing works

- **Switching a timer on, or changing how often it plays, starts a full interval from that moment.** Changing only the name or the sound does not restart it.
- **Timers keep their place when you close Mutation.** When you open it again, each timer carries on where it was.
- **A timer that fell due while Mutation was closed, or while your PC was asleep, is skipped.** It does not play late. It plays next at its normal time, as if it had played.
- **Timers play while you dictate.** If the microphone is recording when a timer is due, the sound still plays on time.
- **Two timers due at the same moment play one after the other**, in the order they are listed, so you can hear that both played.

## If a timer's sound does not play

If the sound file is missing or is not a .wav file Mutation can read, the timer plays Mutation's normal beep instead, so you still get the reminder. A note then appears under that timer on the main window: "The sound file could not be played, so the built-in beep played instead."

To fix it, open **Settings**, choose **Timers**, and press **Play** next to the timer's sound file to check it. Pick another file with **Browse...** if needed, then press **Save**. Saving clears the note.

## Where to next

- [A tour of the main window](main-window.md) — where the Timers strip sits among everything else.
- [The Settings window](settings.md) — the other settings pages.
- [Screen reader and accessibility notes](accessibility.md) — what Mutation announces, and the sounds it uses.
