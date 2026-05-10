using System;
using System.Collections.Generic;
using System.Linq;

namespace Mutation.Ui.Views;

public sealed class SettingsCategoryItem
{
	private readonly string[] _keywords;

	public SettingsCategoryItem(string key, string displayName, string description, string iconGlyph)
		: this(key, displayName, description, iconGlyph, Array.Empty<string>())
	{
	}

	public SettingsCategoryItem(string key, string displayName, string description, string iconGlyph, IEnumerable<string> keywords)
	{
		Key = key;
		DisplayName = displayName;
		Description = description;
		IconGlyph = iconGlyph;
		_keywords = (keywords ?? Array.Empty<string>())
			.Where(k => !string.IsNullOrWhiteSpace(k))
			.Select(k => k.ToLowerInvariant())
			.ToArray();
	}

	public string Key { get; }
	public string DisplayName { get; }
	public string Description { get; }
	public string IconGlyph { get; }

	public bool MatchesSearch(string query)
	{
		if (string.IsNullOrWhiteSpace(query))
			return true;
		string q = query.ToLowerInvariant();
		if (DisplayName.ToLowerInvariant().Contains(q)) return true;
		if (Description.ToLowerInvariant().Contains(q)) return true;
		return _keywords.Any(k => k.Contains(q));
	}
}
