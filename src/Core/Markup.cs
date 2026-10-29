using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DeepTip.Core;

/// <summary>
/// DeepTip markup (SPEC §2.4) → small AST → TextMeshPro rich text. Port of parseText from web/deeptip.js; glossary text
/// can never inject TMP tags: plain text is wrapped in &lt;noparse&gt; when it contains '&lt;'.
/// </summary>
public static class Markup
{
	private const string Escapable = "\\*`[]{}|";
	private static readonly Regex VarPattern = new(@"^\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);
	private static readonly Regex Bullet = new(@"^\s*[-•]\s+(.*)$", RegexOptions.Compiled);
	private static readonly Regex Paragraphs = new(@"\n[ \t]*\n", RegexOptions.Compiled);

	public enum Kind { Text, Br, Bold, Italic, Code, Link, Var }

	public sealed class Node
	{
		public Kind Kind;
		public string Value = "";
		public string? LinkText;
		public List<Node>? Children;
	}

	public sealed class Block
	{
		/// <summary>Paragraph (Inline set) or bullet list (Items set).</summary>
		public List<Node>? Inline;
		public List<List<Node>>? Items;
	}

	public static List<Block> Parse(string? src)
	{
		List<Block> blocks = new();
		string text = (src ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
		foreach (string chunk in Paragraphs.Split(text))
		{
			List<string> para = new();
			Block? list = null;
			void EndPara()
			{
				if (para.Count > 0)
				{
					blocks.Add(new Block { Inline = ParseInline(string.Join("\n", para), 0) });
				}
				para.Clear();
			}
			foreach (string line in chunk.Split('\n'))
			{
				Match m = Bullet.Match(line);
				if (m.Success)
				{
					EndPara();
					if (list == null)
					{
						list = new Block { Items = new List<List<Node>>() };
						blocks.Add(list);
					}
					list.Items!.Add(ParseInline(m.Groups[1].Value, 0));
				}
				else if (line.Trim().Length > 0)
				{
					list = null;
					para.Add(line.Trim());
				}
			}
			EndPara();
		}
		return blocks;
	}

	private static List<Node> ParseInline(string s, int depth)
	{
		List<Node> output = new();
		StringBuilder buf = new();
		void Flush()
		{
			if (buf.Length > 0)
			{
				output.Add(new Node { Kind = Kind.Text, Value = buf.ToString() });
			}
			buf.Clear();
		}
		for (int i = 0; i < s.Length;)
		{
			char c = s[i];
			if (c == '\\' && i + 1 < s.Length && Escapable.IndexOf(s[i + 1]) >= 0)
			{
				buf.Append(s[i + 1]);
				i += 2;
				continue;
			}
			if (c == '\n')
			{
				Flush();
				output.Add(new Node { Kind = Kind.Br });
				i++;
				continue;
			}
			if (c == '[' && i + 1 < s.Length && s[i + 1] == '[')
			{
				int end = s.IndexOf("]]", i + 2, StringComparison.Ordinal);
				if (end > 0)
				{
					string inner = s.Substring(i + 2, end - i - 2);
					int bar = inner.IndexOf('|');
					string key = (bar < 0 ? inner : inner.Substring(0, bar)).Trim();
					if (Glossary.KeyPattern.IsMatch(key))
					{
						Flush();
						string? label = bar < 0 ? null : inner.Substring(bar + 1).Trim();
						output.Add(new Node { Kind = Kind.Link, Value = key, LinkText = string.IsNullOrEmpty(label) ? null : label });
						i = end + 2;
						continue;
					}
				}
			}
			if (c == '`')
			{
				int end = s.IndexOf('`', i + 1);
				if (end > i + 1)
				{
					Flush();
					output.Add(new Node { Kind = Kind.Code, Value = s.Substring(i + 1, end - i - 1) });
					i = end + 1;
					continue;
				}
			}
			if (c == '*' && depth < 3)
			{
				string mark = i + 1 < s.Length && s[i + 1] == '*' ? "**" : "*";
				int from = i + mark.Length;
				if (from < s.Length && !char.IsWhiteSpace(s[from]))
				{
					int end = FindClose(s, from, mark);
					if (end > 0)
					{
						Flush();
						output.Add(new Node { Kind = mark == "**" ? Kind.Bold : Kind.Italic, Children = ParseInline(s.Substring(from, end - from), depth + 1) });
						i = end + mark.Length;
						continue;
					}
				}
			}
			if (c == '{')
			{
				Match m = VarPattern.Match(s.Substring(i, Math.Min(64, s.Length - i)));
				if (m.Success)
				{
					Flush();
					output.Add(new Node { Kind = Kind.Var, Value = m.Groups[1].Value });
					i += m.Length;
					continue;
				}
			}
			buf.Append(c);
			i++;
		}
		Flush();
		return output;
	}

	private static int FindClose(string s, int from, string mark)
	{
		for (int j = from; j < s.Length; j++)
		{
			if (s[j] == '\\')
			{
				j++;
				continue;
			}
			if (s[j] == '`')
			{
				int e = s.IndexOf('`', j + 1);
				if (e > j)
				{
					j = e;
					continue;
				}
			}
			if (string.CompareOrdinal(s, j, "[[", 0, 2) == 0)
			{
				int e = s.IndexOf("]]", j + 2, StringComparison.Ordinal);
				if (e > j)
				{
					j = e + 1;
					continue;
				}
			}
			if (mark == "*" && string.CompareOrdinal(s, j, "**", 0, 2) == 0)
			{
				int e = FindClose(s, j + 2, "**");
				if (e > 0)
				{
					j = e + 1;
					continue;
				}
			}
			if (string.CompareOrdinal(s, j, mark, 0, mark.Length) == 0 && j > from && !char.IsWhiteSpace(s[j - 1]))
			{
				return j;
			}
		}
		return -1;
	}

	/// <summary>All link targets and live-value names used in a text.</summary>
	public static (List<string> Links, List<string> Vars) CollectRefs(string src)
	{
		List<string> links = new();
		List<string> vars = new();
		void Walk(List<Node> nodes)
		{
			foreach (Node n in nodes)
			{
				if (n.Kind == Kind.Link)
				{
					links.Add(n.Value);
				}
				else if (n.Kind == Kind.Var)
				{
					vars.Add(n.Value);
				}
				else if (n.Children != null)
				{
					Walk(n.Children);
				}
			}
		}
		foreach (Block b in Parse(src))
		{
			if (b.Items != null)
			{
				b.Items.ForEach(Walk);
			}
			else
			{
				Walk(b.Inline!);
			}
		}
		return (links, vars);
	}

	// ------------------------------------------------------------------ rendering to TextMeshPro rich text

	/// <summary>Link id prefix inside TMP &lt;link&gt; tags; other mods' links (EpicLoot "elrare…") stay theirs.</summary>
	public const string LinkPrefix = "dt:";

	/// <summary>Colors and the live-value source used while rendering.</summary>
	public sealed class Style
	{
		public string Link = "#F6C453";
		public string Missing = "#9A9A9A";
		public string Code = "#BFD7EA";
		public string Var = "#9FE0A8";
		public Func<string, string, string?>? Value;
	}

	/// <summary>Glossary markup → TMP rich text. <paramref name="selfKey"/>: links to the window's own entry become bold.</summary>
	public static string ToTmp(string? src, string? selfKey, Style style)
	{
		StringBuilder sb = new();
		bool first = true;
		foreach (Block b in Parse(src))
		{
			if (!first)
			{
				sb.Append("\n\n");
			}
			first = false;
			if (b.Items != null)
			{
				for (int i = 0; i < b.Items.Count; i++)
				{
					if (i > 0)
					{
						sb.Append('\n');
					}
					sb.Append("• ");
					Inline(sb, b.Items[i], selfKey, style);
				}
			}
			else
			{
				Inline(sb, b.Inline!, selfKey, style);
			}
		}
		return sb.ToString();
	}

	private static void Inline(StringBuilder sb, List<Node> nodes, string? selfKey, Style style)
	{
		foreach (Node n in nodes)
		{
			switch (n.Kind)
			{
				case Kind.Text:
					sb.Append(Plain(n.Value));
					break;
				case Kind.Br:
					sb.Append('\n');
					break;
				case Kind.Code:
					sb.Append("<color=").Append(style.Code).Append('>').Append(Plain(n.Value)).Append("</color>");
					break;
				case Kind.Var:
					string? value = style.Value?.Invoke(n.Value, selfKey ?? "");
					sb.Append("<color=").Append(style.Var).Append('>').Append(Plain(value ?? "{" + n.Value + "}")).Append("</color>");
					break;
				case Kind.Bold:
					sb.Append("<b>");
					Inline(sb, n.Children!, selfKey, style);
					sb.Append("</b>");
					break;
				case Kind.Italic:
					sb.Append("<i>");
					Inline(sb, n.Children!, selfKey, style);
					sb.Append("</i>");
					break;
				case Kind.Link:
					sb.Append(Link(n.Value, n.LinkText, selfKey, style));
					break;
			}
		}
	}

	/// <summary>One link as TMP rich text: clickable when the entry exists, bold for the own entry, grey when missing.</summary>
	public static string Link(string key, string? text, string? selfKey, Style style)
	{
		bool found = Glossary.TryGet(key, out Entry entry, out _);
		string label = Plain(text ?? (found ? entry.Title : key));
		if (key == selfKey)
		{
			return "<b>" + label + "</b>";
		}
		if (!found)
		{
			return "<color=" + style.Missing + ">" + label + "</color>";
		}
		return Wrap(key, label, style);
	}

	/// <summary>Wrap already safe TMP text in a DeepTip link.</summary>
	public static string Wrap(string key, string tmpLabel, Style style) => "<link=\"" + LinkPrefix + key + "\"><color=" + style.Link + ">" + tmpLabel + "</color></link>";

	/// <summary>Plain text that TMP must not parse as tags.</summary>
	public static string Plain(string text) => text.IndexOf('<') < 0 ? text : "<noparse>" + text.Replace("</noparse>", "</ noparse>") + "</noparse>";
}
