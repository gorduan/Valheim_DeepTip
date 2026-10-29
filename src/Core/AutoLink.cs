using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DeepTip.Core;

/// <summary>
/// Links terms inside existing TextMeshPro rich text (Valheim's and other mods' tooltips, Compendium pages):
/// explicit DeepTip markup <c>[[key]]</c> / <c>[[key|text]]</c> becomes a link, and with auto-linking the first
/// occurrence of every entry's <c>aliases</c> (whole words, case-insensitive) as well. Tags, existing links and
/// &lt;noparse&gt; regions stay untouched. Pure C#.
/// </summary>
public static class AutoLink
{
	private static readonly Regex Explicit = new(@"\[\[([^\[\]|]+?)(?:\|([^\[\]]*))?\]\]", RegexOptions.Compiled);

	private static Regex? aliasPattern;
	private static Dictionary<string, string> aliasKeys = new(StringComparer.OrdinalIgnoreCase);
	private static int builtFor = -1;

	/// <summary>Rebuild the alias index when the glossary or the locale changed.</summary>
	private static void Ensure()
	{
		if (builtFor == Glossary.Version)
		{
			return;
		}
		builtFor = Glossary.Version;
		Dictionary<string, string> keys = new(StringComparer.OrdinalIgnoreCase);
		foreach (Entry e in Glossary.All())
		{
			foreach (string alias in e.Aliases)
			{
				string a = alias.Trim();
				if (a.Length >= 3 && !keys.ContainsKey(a))
				{
					keys[a] = e.Key;
				}
			}
		}
		aliasKeys = keys;
		if (keys.Count == 0)
		{
			aliasPattern = null;
			return;
		}
		// longest first: at one position the regex takes the first alternative that matches
		string alternatives = string.Join("|", keys.Keys.OrderByDescending(a => a.Length).Select(Regex.Escape));
		aliasPattern = new Regex(@"(?<![\p{L}\p{N}_])(?:" + alternatives + @")(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	/// <summary>Number of aliases known in the current locale chain.</summary>
	public static int AliasCount
	{
		get
		{
			Ensure();
			return aliasKeys.Count;
		}
	}

	/// <summary>
	/// Process TMP rich text. <paramref name="auto"/>: also link aliases. <paramref name="exclude"/>: a key never linked
	/// (the entry a window shows). Returns the text unchanged when there is nothing to do.
	/// </summary>
	public static string Process(string tmp, bool auto, Markup.Style style, string? exclude = null)
	{
		if (string.IsNullOrEmpty(tmp))
		{
			return tmp;
		}
		bool hasMarkup = tmp.IndexOf("[[", StringComparison.Ordinal) >= 0;
		if (auto)
		{
			Ensure();
		}
		Regex? aliases = auto ? aliasPattern : null;
		if (!hasMarkup && aliases == null)
		{
			return tmp;
		}
		HashSet<string> linked = new(StringComparer.Ordinal);
		if (exclude != null)
		{
			linked.Add(exclude);
		}
		foreach (Match m in Regex.Matches(tmp, "<link=\"?" + Regex.Escape(Markup.LinkPrefix) + "([^\">]+)"))
		{
			linked.Add(m.Groups[1].Value); // already linked earlier (text processed twice)
		}
		StringBuilder output = new(tmp.Length + 64);
		int linkDepth = 0;
		bool noparse = false;
		int i = 0;
		while (i < tmp.Length)
		{
			int lt = tmp.IndexOf('<', i);
			int textEnd = lt < 0 ? tmp.Length : lt;
			if (textEnd > i)
			{
				string segment = tmp.Substring(i, textEnd - i);
				output.Append(linkDepth > 0 || noparse ? segment : Segment(segment, aliases, linked, style));
			}
			if (lt < 0)
			{
				break;
			}
			int gt = tmp.IndexOf('>', lt + 1);
			int nextLt = tmp.IndexOf('<', lt + 1);
			if (gt < 0 || (nextLt >= 0 && nextLt < gt) || gt - lt > 160)
			{
				// a lone '<' is text
				output.Append('<');
				i = lt + 1;
				continue;
			}
			string tag = tmp.Substring(lt, gt - lt + 1);
			string name = TagName(tag);
			if (noparse)
			{
				if (name == "/noparse")
				{
					noparse = false;
				}
			}
			else if (name == "noparse")
			{
				noparse = true;
			}
			else if (name == "link")
			{
				linkDepth++;
			}
			else if (name == "/link" && linkDepth > 0)
			{
				linkDepth--;
			}
			output.Append(tag);
			i = gt + 1;
		}
		return output.ToString();
	}

	private static string TagName(string tag)
	{
		int start = 1;
		int end = start;
		while (end < tag.Length && (char.IsLetter(tag[end]) || tag[end] == '/' || tag[end] == '-'))
		{
			end++;
		}
		return tag.Substring(start, end - start).ToLowerInvariant();
	}

	/// <summary>One stretch of plain text between tags.</summary>
	private static string Segment(string text, Regex? aliases, HashSet<string> linked, Markup.Style style)
	{
		if (text.IndexOf("[[", StringComparison.Ordinal) >= 0)
		{
			// explicit markup first; the parts in between can still get automatic links
			StringBuilder sb = new();
			int last = 0;
			foreach (Match m in Explicit.Matches(text))
			{
				string key = m.Groups[1].Value.Trim();
				if (!Glossary.KeyPattern.IsMatch(key))
				{
					continue;
				}
				sb.Append(Aliases(text.Substring(last, m.Index - last), aliases, linked, style));
				string? label = m.Groups[2].Success ? m.Groups[2].Value.Trim() : null;
				sb.Append(Markup.Link(key, string.IsNullOrEmpty(label) ? null : label, null, style));
				linked.Add(key);
				last = m.Index + m.Length;
			}
			sb.Append(Aliases(text.Substring(last), aliases, linked, style));
			return sb.ToString();
		}
		return Aliases(text, aliases, linked, style);
	}

	private static string Aliases(string text, Regex? aliases, HashSet<string> linked, Markup.Style style)
	{
		if (aliases == null || text.Length < 3)
		{
			return text;
		}
		return aliases.Replace(text, m =>
		{
			if (!aliasKeys.TryGetValue(m.Value, out string? key) || linked.Contains(key) || !Glossary.Has(key))
			{
				return m.Value;
			}
			linked.Add(key);
			return Markup.Wrap(key, m.Value, style);
		});
	}
}
