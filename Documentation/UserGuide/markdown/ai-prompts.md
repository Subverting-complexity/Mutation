# Using AI prompts on your text

A prompt is a saved instruction that you can run on any piece of text. You write the instruction once — "fix the grammar", "make this shorter", "turn these rough notes into a polite email" — give it a name, and from then on it is one click or one keyboard shortcut away.

That is the whole idea. You are not chatting with anything. You hand Mutation some text, Mutation hands it to an AI along with your saved instruction, and you get the tidied-up version back.

Here are four that earn their keep in an ordinary working day.

| Name | The instruction you would write |
|---|---|
| Fix Grammar | Fix the grammar, spelling and punctuation in the following text. Do not change the meaning or the tone. |
| Shorten | Rewrite the following text so it says the same thing in about half the words. |
| Make It an Email | Turn the following rough notes into a short, polite work email. Keep it plain and friendly. |
| Key Points | Summarise the following text as a short bullet list of the main points. |

---

## Where the text comes from, and where the answer goes

When you run a prompt, Mutation uses **whatever text is on your clipboard**. So the normal routine is: select the text anywhere — in Word, in your browser, in an email — press **Ctrl+C**, then run the prompt.

When the answer comes back, three things happen at once. The answer appears in the **Formatted Transcript** box on the main window, it is copied onto your clipboard, and Mutation puts it into the app you were working in. You hear a success beep when all of that worked. If the clipboard was busy and the text could not be delivered, you get a failure beep and a message telling you the result is still sitting safely in the Mutation window.

---

## The prompt list

Your prompts live on the **LLM Prompts** card on the main window. Each row shows the prompt's name, its keyboard shortcut if it has one, the model it uses, and the word "(Auto-Run)" if it is the automatic one.

Each row has three buttons.

- **Run** — runs that prompt on the clipboard text, right now.
- **Edit** — opens the prompt so you can change it.
- **Delete** — removes it.

**Add Prompt**, at the top right of the card, creates a new one.

---

## Creating a prompt

Press **Add Prompt** and the **Edit Prompt** window opens. Two fields matter most.

**Name** is what you will see in the list. Keep it short — "Fix Grammar", "Shorten", "Draft Reply".

**Prompt Instructions** is the instruction itself. This is the part that does the work.

Three pointers for writing a good one:

- Say what you want done, plainly, as if you were asking a colleague. "Fix the grammar and punctuation" beats "improve this".
- Say what you want back. Adding "Reply with only the corrected text and nothing else" stops the AI from adding a chatty preamble.
- Say what must not change. "Keep my wording and tone" is worth adding to any tidy-up prompt.

There is a **Test Run** button at the bottom. It runs the prompt against your current clipboard content using the settings on screen, without saving anything. Use it to try an instruction before you commit to it.

If a test run is taking longer than you want to wait, press **Test Run** again. Mutation gives up on the request, you hear the failure beep, and a "Test run cancelled." box appears when it has let go. Pressing **Save** or **Cancel**, or closing the window, stops a test run too — but those also close the editor, so press **Save** first if you want to keep what you have typed.

Then press **Save**, or **Cancel** to throw it away.

---

## Giving a prompt its own shortcut

The **Hotkey** box in the prompt editor is optional. Fill it in and that key combination runs this prompt from anywhere — no need to switch to Mutation first. Copy some text in Outlook, press your shortcut, and the cleaned-up version lands back in Outlook.

Pick combinations that nothing else is using. **Ctrl+Alt** plus a letter is usually safe. If a shortcut cannot be claimed because another program already has it, Mutation tells you.

---

## The Auto-Run prompt

One prompt in your list can be marked **Run automatically after transcription**. That prompt then runs on its own as soon as a recording has been turned into text, without you pressing anything.

This is what the record-and-process dictation shortcut uses: you dictate, Mutation transcribes, and your Auto-Run prompt cleans the result up in the same breath. See [Dictation: turning speech into text](dictation.md) for that shortcut.

The **Process with LLM** button on the **Transcripts** card also reaches for the Auto-Run prompt.

Only one prompt can hold this role. Marking a new one automatically clears it from the old one.

---

## Choosing a model

A model is the particular AI that reads your text and writes the reply. Different models cost different amounts and are better at different jobs.

The **Model** box in the prompt editor lets you pin a prompt to a specific one. **If you leave it alone, that is completely fine** — the prompt just uses the standard model. Only change it if you have a reason to.

The list of available models is managed in **Settings** (**Ctrl+Comma**), under **AI assistance**. Each model belongs to one of two providers: **OpenAI** or **Anthropic** (whose models are called Claude). You need the matching key set up for the provider you choose — an OpenAI key for OpenAI models, an Anthropic key for Claude models. See [Getting started](getting-started.md) for how to add them.

---

## Fast mode

**Fast mode** is a check box in the prompt editor. It runs the same model faster. The model, and the quality of its answers, are unchanged — only the speed and the price differ.

The catch is the price. Fast mode is billed at roughly **twice** the standard rate, so turn it on only for prompts where waiting actually costs you something.

Two more things to know. Not every model offers Fast mode. And on Anthropic (Claude) models it also needs research-preview access on your account, which you have to request.

If Fast mode cannot be used for any reason, nothing breaks. The prompt still runs at normal speed, you still get your answer, and Mutation tells you why Fast mode was skipped — whether you need to request access, pick a different model, or simply try again later.

---

## Waiting, and when things go slowly

Under **AI assistance** in **Settings** there are two knobs worth knowing about.

**Request timeout** is how long Mutation waits for the model to respond before giving up. If you regularly send long documents and see things time out, raise it.

**Retries** is how many times Mutation quietly tries again after a failed request, before telling you it did not work. The default of three is there for a good reason: it helps the very first request after you reboot succeed while your network is still warming up.

Those two multiply. With the standard 60 seconds and three retries, a service that has gone down keeps Mutation waiting about **ten minutes** before it gives up — and up to twenty if the prompt asked for Fast mode, because Mutation then tries the whole thing again at normal speed. So it is worth knowing how to stop it.

### Stopping a prompt that is taking too long

Mutation runs one AI request at a time, so **starting any prompt while one is already running stops the one that is running** instead of queueing behind it. Press its shortcut again, press **Run** on any row, or press **Process with LLM** — all of them stop the request in flight.

You hear the failure beep and a message naming what stopped — "Cancelling LLM processing for 'Fix Grammar'..." — then "LLM processing cancelled." once the request has let go, and the **Formatted Transcript** box empties itself. Press again while it is still winding down and you hear "Already stopping."

Because any prompt stops the running one, the prompt you just pressed does **not** then run. Press it once more, after you hear that the first one has stopped, to actually run it.

Nothing is lost. Your original text is still on the clipboard where you copied it from, so you can try again whenever you like — or pick a different prompt.

Closing the Mutation window stops an AI request too, so you never have to wait for one before you can quit.

---

## Where to next

- [Dictation: turning speech into text](dictation.md) — the record-and-process shortcut that uses your Auto-Run prompt
- [Automatic find-and-replace rules](transcript-formatting.md) — fixing repeat offenders without involving an AI at all
- [Getting started](getting-started.md) — adding your OpenAI and Anthropic keys
- [Keyboard shortcuts](keyboard-shortcuts.md) — the full list, and how to change any of them
