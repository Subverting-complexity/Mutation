# Screenshots and reading text from images

Sometimes the words you need are trapped inside a picture. A screenshot a colleague
sent you. A scanned invoice. A PDF that will not let you select anything.

OCR is the answer to that. It stands for optical character recognition, and it simply
means reading the words out of a picture so you can copy, search, or paste them.
Mutation does this for you, and puts the resulting text straight onto your clipboard.

Everything in this chapter lives in the **Visual Capture** card on the main window.

> Before any of the OCR buttons will work, Mutation needs an Azure Computer Vision key
> (a long password that lets Mutation send your image to Microsoft's text-reading
> service and get the words back). See [Getting started](getting-started.md) for how to
> set that up.

## Taking a screenshot of part of the screen

Press the screenshot shortcut — **Shift+Alt+K** by default. The screen freezes, and an
overlay appears over everything. You then pick a rectangle. As soon as you finish
picking, that rectangle is copied to your clipboard as an image, and you are back where
you were.

Press **Esc** at any point to cancel. Nothing is copied, and Mutation tells you so.

If you press the shortcut again while the overlay is still up — easy to do if you did
not hear the first press land — nothing new starts. Mutation brings the overlay you
already have back to the front and says "A screenshot is already in progress. Select a
region, or press Escape to cancel it." So carry on and pick your rectangle, or press
**Esc** if you would rather start over.

### Picking the rectangle with the mouse

Hold the left mouse button down at one corner of the area you want, drag to the
opposite corner, and let go. That is it. A right-click cancels instead.

### Picking the rectangle with the keyboard

You never need to see the screen to do this. The overlay talks you through it. When it
opens it says what it wants from you, then as you move it announces the position you
are at, the size of the rectangle you have built so far, and finally the size of what
was captured. Those numbers are all in pixels of the picture, so the size you hear
while sizing the rectangle matches the size you hear when it is captured.

Think of it as a moving pointer that you park at one corner, pin down, and then drag to
the other corner.

| Key | Action |
|---|---|
| Arrow keys | Move the pointer (hold **Ctrl** for fine steps, **Shift** for large steps) |
| **Home** / **End** | Jump the pointer to the left / right edge |
| **Page Up** / **Page Down** | Jump the pointer to the top / bottom edge |
| **Enter** or **Space** | First press pins one corner; second press captures the region |
| **Ctrl+A** | Select the whole screen — then **Enter** to capture it |
| **Backspace** | Clear the pinned corner and start the selection again |
| **Esc** | Cancel |

So the quickest possible capture is **Ctrl+A** then **Enter**: the whole screen, in two
keystrokes.

## The two reading orders

A page of text can be read in two sensible ways, and Mutation offers both.

- **Natural** follows the shape of the page. It reads down one column, then down the
  next, keeping sections together. Choose it for newspapers, magazines, brochures, and
  multi-column PDFs.
- **Basic** reads straight across each row, left to right, all the way down. The app
  labels this one **(L→R)**. Choose it for tables, spreadsheets, forms, and invoices.

Rule of thumb: if the thing you are reading has *columns*, use Natural. If it has
*rows*, use Basic.

If one order gives you a jumbled result, just try the other on the same image.

## The buttons and their shortcuts

| Button | Default shortcut | What it does |
|---|---|---|
| Screenshot to clipboard | **Shift+Alt+K** | Picks a rectangle and copies it to the clipboard as an image. No text reading. |
| OCR clipboard | **Alt+J** | Reads the text out of whatever image is already on your clipboard, in Natural order. |
| OCR clipboard (L→R) | **Alt+K** | The same, but in Basic left-to-right order. |
| Screenshot & OCR | **Shift+Alt+J** | Picks a rectangle *and* reads its text, in Natural order. One step. |
| Screenshot & OCR (L→R) | **Shift+Alt+E** | The same, in Basic left-to-right order. |

Every one of these shortcuts can be changed in **Settings** (**Ctrl+Comma**).

In all four OCR cases the text lands on your clipboard, ready to paste, and also
appears in the **OCR result** box at the bottom of the card so you can read it in
place.

Mutation also pastes it for you. Say you are writing an email, press **Shift+Alt+J**,
and pick a rectangle off a web page: a moment later that text is sitting in your email,
where the cursor was. You did not have to switch to Mutation or press **Ctrl+V**.

This is the **Paste OCR text into the active app** setting, and it starts switched on.
Turn it off if you would rather paste it yourself — see
[A few settings worth knowing](#a-few-settings-worth-knowing) below.

You can follow all of this by ear.

With **Screenshot & OCR**, the start beep says the overlay is ready for you to pick a
rectangle. The end beep says your picture has been captured and sent off to be read.

With **OCR clipboard**, there is no rectangle to pick, so there is no start beep. The
end beep is the first thing you hear, and it says your picture has been sent off to be
read.

Either way, the success beep then says the text is on your clipboard — or the failure
beep says something went wrong, and the status area tells you what.

Now and then another program has the clipboard open at the moment Mutation wants to
copy to it — a clipboard manager, or your screen reader taking a look at the
screenshot that went there a second earlier. Mutation waits a moment and tries again a
few times, for the picture and for the text alike. That is usually enough and you never
notice it.

When it is not enough, what happens depends on which one you used.

A plain **Screenshot to clipboard** tells you the clipboard was busy and asks you to try
again. That picture is gone, so you do need to pick the region a second time. Your
clipboard still holds whatever it held before.

A **Screenshot & OCR** still reads the text. The reading works from the picture Mutation
is holding, and never needed the clipboard for it, so you get your text on the clipboard
and in the **OCR result** box as usual, with a message telling you that only the picture
did not make it. You still hear the success beep, because the reading itself worked. And
if the text is the part that could not be copied, it is still in the **OCR result** box
for you to copy out yourself.

## Reading a batch of files at once

The **OCR documents** button handles whole files rather than screenshots. Click it,
pick one or more files, and Mutation reads them all.

It accepts PDFs and image files: `.pdf`, `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tif` and
`.tiff`. You can select several at once.

A progress bar tells you which file and page it is on, and Mutation keeps you posted out
loud as it goes.

You hear it once per file, not once per page — "Finished invoice.pdf. 3 of 12
documents." — so a long batch does not talk over you. Inside a long PDF you also get an
occasional page count, every ten pages, just so you know it is still going. The last file
is not announced this way; the summary at the end covers it.

When it finishes, all the text comes back as one combined block in the **OCR result**
box — each file introduced by its name in square brackets, and each page of a PDF
marked with its page number. That combined text is copied to the clipboard too.

If you want to keep it, the **Download OCR result** button saves the whole thing to a
plain text file.

### Stopping a batch part way

Picked forty files by mistake? The **Cancel OCR** button sits just below the progress
bar while a batch is running.

Click it and Mutation stops straight away — the pages still being read are dropped too,
so nothing lingers. It then tells you how many files finished before it stopped. Nothing
is copied to your clipboard when you cancel, and the **OCR result** box is left as it
was. If you want those files read after all, start the batch again.

Closing the Mutation window also stops a batch that is still running.

## A few settings worth knowing

These live under **Screen capture & OCR** in **Settings** (**Ctrl+Comma**).

- **Invert screenshot before OCR** — flips the colours before reading. Turn this on if
  you work with light text on a dark background; it usually improves accuracy a lot.
- **Free-tier page limit** — if you are on Microsoft's free plan, this caps how many
  pages of each document get sent. The default is 2, so long PDFs are cut short unless
  you turn **Use free tier** off.
- **Max document size (MB)** — any file or page bigger than this is skipped instead of
  being uploaded, so you never accidentally send something enormous. Set it to 0 to
  remove the limit.
- **Paste OCR text into the active app** — on by default. Puts the text straight into
  whatever you were working in when the reading finishes. Switch it off and the text
  still goes to your clipboard and the **OCR result** box; you just paste it yourself.

Mutation holds the paste back when there is nothing worth pasting: a reading that found
no text, and a reading whose text could not reach the clipboard. In both of those the
clipboard still holds the picture, and pasting would put that picture in your document
instead. It also holds back when
you started the job from a button in the Mutation window rather than by shortcut, since
"the app you were working in" is Mutation itself. That is why a batch of documents is
copied but not pasted.

There is also an optional **Send hotkey after OCR** setting, which fires a keystroke of
your choosing once the text is ready — handy for kicking off your screen reader
automatically. It is sent whether the run found text or failed, so a screen-reader
shortcut in that box reads out the error just as readily as the result.

It always comes after the paste, never before, so a screen-reader shortcut in that box
reads the text that has just arrived rather than whatever was there a moment earlier.

It is sent after a batch of documents as well, not only after a screenshot or a
clipboard image. It is not sent if you cancel a batch, since nothing is copied then.

Two more times it holds back rather than firing:

- **You press an OCR shortcut while a capture is already on screen.** Nothing has
  happened, so there is nothing new to read. Mutation brings the capture you already
  have back to the front and sends nothing — otherwise the keystroke would land in the
  capture overlay.
- **You cancel a capture.** The keystroke is still sent, so a screen-reader shortcut
  reads out the "cancelled" message. It just waits until the window you were in has
  the keyboard back, so it goes where you meant it to.

The rest of the options are covered in [The Settings window](settings.md).

## Where to next

- [Getting started](getting-started.md) — setting up your Azure Computer Vision key
- [A tour of the main window](main-window.md) — where the **Visual Capture** card sits
- [Having text read aloud](read-aloud.md) — listen to the text you just captured
- [Keyboard shortcuts](keyboard-shortcuts.md) — the full list, and how to change them
