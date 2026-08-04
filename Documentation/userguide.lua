--[[
	userguide.lua — pandoc filter for the Mutation user guide.

	Two jobs:

	1. Rewrite relative links that point at a .md chapter so they point at the
	   generated .html page instead. The Markdown stays correct on its own terms
	   (chapters link to chapters), and the built site stays self-consistent.

	2. Mark every table header cell with scope="col". Pandoc does not emit scope
	   itself, and screen readers rely on it to say which column a value is under.
	   Every table in this guide is column-headed, so this is safe across the set.
]]

--- Rewrite chapter.md -> chapter.html, leaving absolute URLs alone.
function Link(el)
	local target = el.target

	-- Anything with a scheme (http:, https:, mailto:) is external: leave it.
	if target:match("^%a[%w+.-]*:") then
		return nil
	end

	-- chapter.md#anchor -> chapter.html#anchor
	-- Lua patterns have no alternation, so the two cases are matched separately.
	local base, anchor = target:match("^(.-)%.md(#.*)$")
	if base then
		el.target = base .. ".html" .. anchor
		return el
	end

	-- chapter.md -> chapter.html
	base = target:match("^(.-)%.md$")
	if base then
		el.target = base .. ".html"
		return el
	end

	return nil
end

--- Add scope="col" to the header cells of every table, and wrap the table in a
--- scrollable div.
---
--- The wrapper matters for accessibility, not just looks. A wide table has to be
--- able to scroll sideways on a narrow window, but doing that with
--- `table { display: block }` drops the element's implicit table semantics in
--- several browser and screen-reader combinations, so the rows and columns stop
--- being announced as a table. Scrolling the wrapper instead leaves the table a
--- table.
function Table(tbl)
	for _, row in ipairs(tbl.head.rows) do
		for _, cell in ipairs(row.cells) do
			cell.attr.attributes["scope"] = "col"
		end
	end

	return {
		pandoc.RawBlock("html", '<div class="table-wrap">'),
		tbl,
		pandoc.RawBlock("html", "</div>"),
	}
end
