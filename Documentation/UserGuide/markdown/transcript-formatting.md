# Automatic find-and-replace rules

Some corrections you end up making every single day. Dictation keeps hearing your company name "Ardvark Holdings" as "aardvark holdings". Or you say "um" more than you would like, and there it is in the text every time.

Formatting rules fix those once and for all. A rule is a plain find-and-replace instruction, and Mutation applies your rules to every transcript automatically. No AI involved, nothing to press, no cost.

---

## Setting up rules

Open **Settings** with **Ctrl+Comma** and go to **Transcript formatting**. Press **Add rule** to create one.

A rule has three parts.

- **Find** — the text you want Mutation to look for.
- **Replace with** — what to put in its place. Leave it empty to simply delete the found text.
- **Case sensitive** — when this is on, capital letters have to match exactly. When it is off, case is ignored. Off is the usual choice.

Rules run from top to bottom, so the order can matter. The up and down arrow buttons on each row let you rearrange them, and **Delete** removes a rule.

Mutation starts you off with a set of ready-made rules the first time it runs. They are yours to change or throw away. If you delete every rule and save, the list stays empty next time you open Mutation — that is how you turn find-and-replace off altogether.

---

## The three match types

Each rule also has a **Match type**, which decides how the Find text is interpreted.

**Plain** swaps the exact text wherever it turns up, including in the middle of longer words.

**Smart** matches the word properly, tidying up the spaces and punctuation around it as it goes. This is the one most people want, and it is especially good for removing a spoken word cleanly without leaving a double space or a stranded comma behind.

**RegEx** treats the Find box as a pattern written in a special pattern language for advanced users. If you do not know what this is, you do not need it. (If a pattern is malformed, Mutation shows the error in red under the row so you can fix it.)

---

## Some worked examples

| Find | Replace with | Match type | What it does |
|---|---|---|---|
| aardvark holdings | Ardvark Holdings | Smart | Fixes a company name that dictation always mishears |
| um | *(leave empty)* | Smart | Strips out "um" and closes the gap neatly |
| ASAP | as soon as possible | Plain | Spells out an abbreviation everywhere it appears |

---

## When rules run

Rules run automatically on every transcript, and they run **before** any AI prompt does. That order is deliberate: your names and jargon are already correct by the time the AI sees the text, so it has less chance of "correcting" them into something wrong.

Mutation also does a small tidy-up pass of its own at the same time, collapsing doubled punctuation like "word.." down to a single mark.

---

## Running rules on demand

You do not have to wait for a dictation. The **Format with rules** button sits next to the **Raw Transcript** box on the main window. Press it and Mutation applies your rules to whatever is in that box, puts the result in the **Formatted Transcript** box, copies it to your clipboard, and delivers it into the app you were working in.

It is the same treatment a transcript gets automatically, just triggered by you — and it never contacts an AI, so it is instant and free.

---

## Where to next

- [Dictation: turning speech into text](dictation.md) — where most transcripts come from
- [Using AI prompts on your text](ai-prompts.md) — the AI step that runs after your rules
- [The Settings window](settings.md) — finding your way around Settings
