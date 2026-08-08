using Mutation.Ui.Services;
using Windows.System;

namespace Mutation.Tests;

/// <summary>
/// Issue #329: the short key names lived in <c>SendKeysMapper</c> alone, so the same key had
/// one spelling that worked in the "send key after…" boxes and a different one that worked in
/// the shortcut editors. These pin the single table both now read.
/// </summary>
public class KeyNameAliasesTests
{
	[Theory]
	[InlineData("PgDn", VirtualKey.PageDown)]
	[InlineData("PgUp", VirtualKey.PageUp)]
	[InlineData("PageDn", VirtualKey.PageDown)]
	[InlineData("Esc", VirtualKey.Escape)]
	[InlineData("Bksp", VirtualKey.Back)]
	[InlineData("BS", VirtualKey.Back)]
	[InlineData("Backspace", VirtualKey.Back)]
	[InlineData("Del", VirtualKey.Delete)]
	[InlineData("Ins", VirtualKey.Insert)]
	[InlineData("Return", VirtualKey.Enter)]
	[InlineData("Spacebar", VirtualKey.Space)]
	[InlineData("Caps", VirtualKey.CapitalLock)]
	[InlineData("CapsLock", VirtualKey.CapitalLock)]
	[InlineData("NumLock", VirtualKey.NumberKeyLock)]
	[InlineData("ScrollLock", VirtualKey.Scroll)]
	[InlineData("Break", VirtualKey.Pause)]
	[InlineData("Apps", VirtualKey.Application)]
	[InlineData("ContextMenu", VirtualKey.Application)]
	[InlineData("ArrowUp", VirtualKey.Up)]
	[InlineData("UpArrow", VirtualKey.Up)]
	[InlineData("ArrowDown", VirtualKey.Down)]
	[InlineData("ArrowLeft", VirtualKey.Left)]
	[InlineData("ArrowRight", VirtualKey.Right)]
	public void AShortcutEditorNowTakesTheShortSpellingToo(string alias, VirtualKey expected)
	{
		// The case from the issue: "Alt+PgDn" used to be rejected with "Unsupported key
		// 'PGDN'" while a send-key box took it without complaint.
		var parsed = Hotkey.Parse("Alt+" + alias);

		Assert.Equal(expected, parsed.Key);
		Assert.True(parsed.Alt);
	}

	[Fact]
	public void EveryAliasMeansExactlyWhatItsFullSpellingMeans()
	{
		// One vocabulary means the two spellings are the same chord, not merely two spellings
		// that both happen to parse.
		foreach (var (alias, canonical) in KeyNameAliases.All)
			Assert.Equal(Hotkey.Parse("Ctrl+" + canonical), Hotkey.Parse("Ctrl+" + alias));
	}

	[Fact]
	public void NoAliasQuietlyRedefinesAKeyWindowsAlreadyNames()
	{
		// "Menu" is why this matters: VirtualKey.Menu is the Alt key, so aliasing it to the
		// context-menu key would change what an already-saved shortcut means. Nothing in the
		// table is allowed to shadow a name like that.
		foreach (string alias in KeyNameAliases.All.Keys)
			Assert.False(
				Enum.TryParse<VirtualKey>(alias, ignoreCase: true, out _),
				$"'{alias}' is already a VirtualKey name; aliasing it changes what it means.");
	}

	[Fact]
	public void EveryAliasResolvesToANameWindowsKnows()
	{
		foreach (string canonical in KeyNameAliases.All.Values)
			Assert.True(
				Enum.TryParse<VirtualKey>(canonical, ignoreCase: true, out _),
				$"'{canonical}' is not a VirtualKey name, so nothing can resolve to it.");
	}

	[Fact]
	public void ANameThatIsNotAnAliasComesBackUnchanged()
	{
		Assert.Equal("PageDown", KeyNameAliases.Resolve("PageDown"));
		Assert.Equal("NOSUCHKEY", KeyNameAliases.Resolve("NOSUCHKEY"));
	}

	[Theory]
	[InlineData("PgDn")]
	[InlineData("Esc")]
	[InlineData("Bksp")]
	[InlineData("ArrowUp")]
	[InlineData("Caps")]
	public void ASendKeyBoxStillTakesTheShortSpellingItAlwaysDid(string alias)
	{
		// The alias table moved; nothing was taken away from the boxes that had it.
		Assert.Equal(SendKeysMapper.Map("Alt+" + alias), SendKeysMapper.Map("Alt+" + KeyNameAliases.Resolve(alias)));
	}

	[Fact]
	public void ReadingASendKeysBraceNameBackAgreesWithTheAliasTable()
	{
		// SendKeysReader (issue #326) keeps its own closed list of the brace names Windows'
		// shorthand uses, and deliberately so — reading {SHIFT} back as a key is how an
		// innocent row gets a duplicate badge. But the half of that list which is short
		// spellings is this table again, so the two can drift. Where both know a name, they
		// have to mean the same key.
		foreach (var (alias, canonical) in KeyNameAliases.All)
		{
			var read = SendKeysReader.ChordsIn("^{" + alias + "}");
			if (read.Count == 0)
				continue; // Not one of SendKeys' brace names; nothing to agree about.

			Assert.Equal(Hotkey.Parse("Ctrl+" + canonical), Assert.Single(read));
		}
	}

	[Fact]
	public void MenuIsTheOneNameLeftMeaningTwoThings()
	{
		// Recorded rather than fixed: VirtualKey.Menu is Alt, and the send-key boxes have
		// typed the context-menu key for "Menu" since they were written. Agreeing would have
		// to break one of those, so neither was changed — see KeyNameAliases.
		Assert.Equal(VirtualKey.Menu, Hotkey.Parse("Ctrl+Menu").Key);
		Assert.Equal("^{APPS}", SendKeysMapper.Map("Ctrl+Menu"));
	}
}
