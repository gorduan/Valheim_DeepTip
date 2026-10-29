using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeepTip.Core;

/// <summary>One explanation (SPEC §2.2).</summary>
public sealed class Entry
{
	public string Key = "";
	public string Title = "";
	public string? Short;
	public string? Body;
	public string[] Aliases = Array.Empty<string>();
	public string[] See = Array.Empty<string>();
	/// <summary>Who provided it (file or mod), for the log and the lexicon.</summary>
	public string Owner = "";
}

/// <summary>
/// The glossary (SPEC §2): layers of JSON files per locale. A later layer overrides an entry with the same key; the
/// first locale of the chain that has a key provides the whole entry. Pure C#, no Unity.
/// </summary>
public static class Glossary
{
	/// <summary>Layer priorities: built-in data, files of other plugins, API calls, the user's own overrides.</summary>
	public const int BuiltIn = 0;
	public const int PluginFiles = 10;
	public const int Api = 20;
	public const int UserFiles = 30;

	public static readonly Regex KeyPattern = new(@"^[A-Za-z0-9_-]+(\.[A-Za-z0-9_-]+)*$", RegexOptions.Compiled);
	private static readonly Regex LocaleInName = new(@"(^|\.)(?<loc>[a-z]{2,3}(-[A-Za-z]{2,4})?)$", RegexOptions.Compiled);

	private sealed class Layer
	{
		public string Owner = "";
		public string Source = "";
		public string Locale = "";
		public int Priority;
		public int Order;
		public Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
		public Dictionary<string, string> Ui = new(StringComparer.Ordinal);
	}

	private static readonly List<Layer> layers = new();
	private static int nextOrder;
	private static Dictionary<string, Dictionary<string, Entry>> merged = new();
	private static Dictionary<string, Dictionary<string, string>> mergedUi = new();
	private static string[] chain = { "en" };

	/// <summary>Raised after any change of data or locale (windows re-render, the auto-linker rebuilds).</summary>
	public static event Action? Changed;

	/// <summary>Counts changes; caches compare it.</summary>
	public static int Version { get; private set; }

	public static string Locale => chain[0];
	public static IReadOnlyList<string> Chain => chain;

	private static readonly Dictionary<string, Dictionary<string, string>> DefaultUi = new()
	{
		["en"] = new()
		{
			["close"] = "Close",
			["pin"] = "Pin",
			["unpin"] = "Unpin",
			["missing"] = "No explanation for this term yet.",
			["fallback"] = "Not translated yet",
			["hint"] = "Click to pin · Esc closes",
			["seeAlso"] = "See also:",
			["search"] = "Search terms …",
			["mentionedIn"] = "Mentioned in",
			["empty"] = "No matches.",
			["lexicon"] = "DeepTip lexicon",
		},
		["de"] = new()
		{
			["close"] = "Schließen",
			["pin"] = "Festhalten",
			["unpin"] = "Loslassen",
			["missing"] = "Zu diesem Begriff gibt es noch keine Erklärung.",
			["fallback"] = "Noch nicht übersetzt",
			["hint"] = "Klick hält fest · Esc schließt",
			["seeAlso"] = "Siehe auch:",
			["search"] = "Begriffe suchen …",
			["mentionedIn"] = "Erwähnt in",
			["empty"] = "Keine Treffer.",
			["lexicon"] = "DeepTip-Lexikon",
		},
	};

	/// <summary>
	/// Add one glossary file. The locale comes from "$locale", else from the file name ("de", "mymod.de"), else from
	/// <paramref name="locale"/>. Returns the problems found (invalid keys, broken entries); throws on invalid JSON.
	/// </summary>
	public static List<string> Add(string owner, string json, string? locale = null, int priority = Api, string? fileName = null)
	{
		List<string> problems = new();
		if (Json.Parse(json) is not Json.JsonObject root)
		{
			problems.Add("a glossary file must be a JSON object");
			return problems;
		}
		string? loc = root.TryGetValue("$locale", out object? l) ? l as string : null;
		if (string.IsNullOrEmpty(loc) && fileName != null)
		{
			Match m = LocaleInName.Match(Path.GetFileNameWithoutExtension(fileName));
			if (m.Success)
			{
				loc = m.Groups["loc"].Value;
			}
		}
		loc ??= locale;
		if (string.IsNullOrEmpty(loc))
		{
			problems.Add("no locale: set \"$locale\" or name the file like de.json");
			return problems;
		}
		Layer layer = new() { Owner = owner, Locale = Normalize(loc!), Priority = priority, Order = nextOrder++ };
		if (root.TryGetValue("$ui", out object? ui) && ui is Json.JsonObject uiObj)
		{
			foreach (string k in uiObj.Order)
			{
				if (uiObj[k] is string text)
				{
					layer.Ui[k] = text;
				}
			}
		}
		layer.Source = fileName ?? "";
		Walk(root, "", layer, problems);
		if (fileName != null)
		{
			// the same file again (a reload) replaces its old layer; API calls without a file name add up
			layers.RemoveAll(x => x.Owner == owner && x.Source == fileName && x.Locale == layer.Locale && x.Priority == priority);
		}
		layers.Add(layer);
		Rebuild();
		return problems;
	}

	/// <summary>Remove everything an owner added (e.g. a mod that unloads its data).</summary>
	public static void Remove(string owner)
	{
		if (layers.RemoveAll(x => x.Owner == owner) > 0)
		{
			Rebuild();
		}
	}

	private static void Walk(Json.JsonObject node, string prefix, Layer layer, List<string> problems)
	{
		foreach (string k in node.Order)
		{
			if (k.StartsWith("$", StringComparison.Ordinal))
			{
				continue; // metadata; only top level counts
			}
			string key = prefix.Length > 0 ? prefix + "." + k : k;
			if (node[k] is not Json.JsonObject obj)
			{
				problems.Add($"{key}: not an entry object");
				continue;
			}
			if (obj.TryGetValue("title", out object? t) && t is string || obj.TryGetValue("short", out object? s) && s is string || obj.TryGetValue("body", out object? b) && b is string)
			{
				if (!KeyPattern.IsMatch(key))
				{
					problems.Add($"{key}: invalid key (letters, digits, _ - separated by dots)");
					continue;
				}
				Entry e = new()
				{
					Key = key,
					Title = obj.TryGetValue("title", out object? title) && title is string ts ? ts : key,
					Short = obj.TryGetValue("short", out object? sh) ? sh as string : null,
					Body = obj.TryGetValue("body", out object? bo) ? bo as string : null,
					Aliases = Strings(obj, "aliases"),
					See = Strings(obj, "see"),
					Owner = layer.Owner,
				};
				layer.Entries[key] = e;
			}
			else
			{
				Walk(obj, key, layer, problems);
			}
		}
	}

	private static string[] Strings(Json.JsonObject obj, string field)
	{
		if (!obj.TryGetValue(field, out object? v) || v is not List<object?> list)
		{
			return Array.Empty<string>();
		}
		return list.OfType<string>().Where(x => x.Length > 0).ToArray();
	}

	/// <summary>"de-DE" → "de-de"; locales compare case-insensitively.</summary>
	public static string Normalize(string locale) => locale.Trim().Replace('_', '-').ToLowerInvariant();

	/// <summary>Current locale and the ones tried after it (SPEC §2.3).</summary>
	public static void SetLocale(string locale, IEnumerable<string> fallback)
	{
		List<string> list = new() { Normalize(locale) };
		foreach (string f in fallback)
		{
			string n = Normalize(f);
			if (!list.Contains(n))
			{
				list.Add(n);
			}
		}
		if (list.SequenceEqual(chain))
		{
			return;
		}
		chain = list.ToArray();
		Version++;
		Changed?.Invoke();
	}

	private static void Rebuild()
	{
		Dictionary<string, Dictionary<string, Entry>> m = new();
		Dictionary<string, Dictionary<string, string>> u = new();
		foreach (Layer layer in layers.OrderBy(x => x.Priority).ThenBy(x => x.Order))
		{
			if (!m.TryGetValue(layer.Locale, out Dictionary<string, Entry>? entries))
			{
				m[layer.Locale] = entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
			}
			foreach (KeyValuePair<string, Entry> e in layer.Entries)
			{
				entries[e.Key] = e.Value;
			}
			if (!u.TryGetValue(layer.Locale, out Dictionary<string, string>? labels))
			{
				u[layer.Locale] = labels = new Dictionary<string, string>(StringComparer.Ordinal);
			}
			foreach (KeyValuePair<string, string> l in layer.Ui)
			{
				labels[l.Key] = l.Value;
			}
		}
		merged = m;
		mergedUi = u;
		Version++;
		Changed?.Invoke();
	}

	/// <summary>The entry for a key in the current chain, and the locale it came from.</summary>
	public static bool TryGet(string key, out Entry entry, out string fromLocale)
	{
		foreach (string loc in chain)
		{
			if (merged.TryGetValue(loc, out Dictionary<string, Entry>? entries) && entries.TryGetValue(key, out Entry? e))
			{
				entry = e;
				fromLocale = loc;
				return true;
			}
		}
		entry = null!;
		fromLocale = "";
		return false;
	}

	public static bool Has(string key) => TryGet(key, out _, out _);

	/// <summary>A window label ($ui, SPEC §2.6) in the current chain, with built-in German and English defaults.</summary>
	public static string Ui(string name)
	{
		foreach (string loc in chain)
		{
			if (mergedUi.TryGetValue(loc, out Dictionary<string, string>? labels) && labels.TryGetValue(name, out string? text))
			{
				return text;
			}
			string lang = loc.Split('-')[0];
			if (DefaultUi.TryGetValue(lang, out Dictionary<string, string>? defaults) && defaults.TryGetValue(name, out string? def))
			{
				return def;
			}
		}
		return DefaultUi["en"].TryGetValue(name, out string? en) ? en : name;
	}

	/// <summary>Every key with its resolved entry (the first locale of the chain that has it), sorted by title.</summary>
	public static List<Entry> All()
	{
		Dictionary<string, Entry> result = new(StringComparer.Ordinal);
		foreach (string loc in chain)
		{
			if (!merged.TryGetValue(loc, out Dictionary<string, Entry>? entries))
			{
				continue;
			}
			foreach (KeyValuePair<string, Entry> e in entries)
			{
				if (!result.ContainsKey(e.Key))
				{
					result[e.Key] = e.Value;
				}
			}
		}
		return result.Values.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	/// <summary>Number of entries per owner (for the log).</summary>
	public static string Summary()
	{
		return string.Join(", ", layers.GroupBy(l => l.Locale).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Sum(l => l.Entries.Count)}"));
	}
}
