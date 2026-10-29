using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeepTip.Core;
using DeepTip.UI;
using TMPro;
using UnityEngine;

namespace DeepTip;

/// <summary>
/// Public API for other mods. Without code a mod only ships <c>deeptip/de.json</c>, <c>deeptip/en.json</c> … in its
/// plugin folder and writes <c>[[key]]</c> into its tooltip texts. With code (soft dependency via reflection or a
/// reference to GC_Valheim_DeepTip.dll) it can register glossaries, mark UI elements, open windows and provide live
/// values. All methods are safe to call on a dedicated server (no windows there).
/// </summary>
public static class Api
{
	/// <summary>BepInEx GUID of DeepTip (for [BepInDependency]).</summary>
	public const string Guid = Plugin.Guid;
	/// <summary>Raised when the API gains methods.</summary>
	public const int Version = 1;

	private static readonly Dictionary<string, Func<string?>> values = new(StringComparer.Ordinal);

	/// <summary>Current glossary locale (e.g. "de"), following Valheim's language.</summary>
	public static string Locale => Glossary.Locale;

	/// <summary>The glossary or the language changed.</summary>
	public static event Action? Changed;
	/// <summary>A window for this key opened, closed or got pinned.</summary>
	public static event Action<string>? Opened;
	public static event Action<string>? Closed;
	public static event Action<string>? Pinned;

	/// <summary>Add a glossary (JSON text, SPEC §2). Locale from "$locale" or <paramref name="locale"/>. Problems go to the log.</summary>
	public static bool Register(string owner, string json, string? locale = null)
	{
		try
		{
			foreach (string problem in Glossary.Add(owner, json, locale, Glossary.Api))
			{
				Plugin.Log.LogWarning($"{owner}: {problem}");
			}
			return true;
		}
		catch (Exception e)
		{
			Plugin.Log.LogError($"{owner}: glossary is not valid JSON: {e.Message}");
			return false;
		}
	}

	/// <summary>Add every *.json in a folder (locale from the file name, e.g. de.json or mymod.de.json). Returns the files loaded.</summary>
	public static int RegisterFolder(string owner, string folder)
	{
		if (!Directory.Exists(folder))
		{
			return 0;
		}
		return Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Count(f => Loader.Load(owner, File.ReadAllText(f), Path.GetFileName(f), Glossary.Api));
	}

	/// <summary>Remove everything an owner registered.</summary>
	public static void Unregister(string owner) => Glossary.Remove(owner);

	/// <summary>Resting the mouse on this element opens the entry's window. Sides like "top,bottom"; delay in seconds (negative = setting).</summary>
	public static void Mark(GameObject element, string key, string? sides = null, float delay = -1f)
	{
		if (element == null)
		{
			return;
		}
		DeepTipAnchor anchor = element.GetComponent<DeepTipAnchor>() ?? element.AddComponent<DeepTipAnchor>();
		anchor.Key = key;
		anchor.Sides = sides ?? "";
		anchor.Delay = delay;
	}

	public static void Unmark(GameObject element)
	{
		DeepTipAnchor? anchor = element != null ? element.GetComponent<DeepTipAnchor>() : null;
		if (anchor != null)
		{
			UnityEngine.Object.Destroy(anchor);
		}
	}

	/// <summary>Open the window for a key next to a UI element.</summary>
	public static bool Open(string key, RectTransform anchor, bool pinned = false)
	{
		if (!Plugin.HasUi || anchor == null)
		{
			return false;
		}
		return Manager.Open(key, new ElementAnchor(anchor), null, pinned) != null;
	}

	public static void CloseAll()
	{
		if (Plugin.HasUi)
		{
			Manager.CloseAll();
		}
	}

	/// <summary>Glossary markup → TextMeshPro rich text with DeepTip links (for a mod's own panels; then call <see cref="AttachLinks"/>).</summary>
	public static string Render(string markup) => Markup.ToTmp(markup, null, Theme.Style);

	/// <summary>Turn [[key]] markup and known terms inside existing TMP text into DeepTip links.</summary>
	public static string Link(string tmpText) => AutoLink.Process(tmpText, Settings.AutoLinkTerms.Value, Theme.Style);

	/// <summary>Links inside this TMP text open DeepTip windows (hover previews, click pins).</summary>
	public static void AttachLinks(TMP_Text text)
	{
		if (Plugin.HasUi)
		{
			Manager.AttachPage(text);
		}
	}

	/// <summary>Live value for {name} in glossary texts (SPEC §2.5). Call <see cref="Refresh"/> after it changed.</summary>
	public static void SetValue(string name, Func<string?> provider) => values[name] = provider;

	public static void RemoveValue(string name) => values.Remove(name);

	/// <summary>Render open windows again (live values changed).</summary>
	public static void Refresh()
	{
		if (Plugin.HasUi)
		{
			Manager.RefreshAll();
		}
	}

	public static bool Has(string key) => Glossary.Has(key);

	public static string? Title(string key) => Glossary.TryGet(key, out Entry e, out _) ? e.Title : null;

	/// <summary>The one-line text of an entry, e.g. for a mod's simple tooltip.</summary>
	public static string? Short(string key) => Glossary.TryGet(key, out Entry e, out _) ? e.Short ?? e.Title : null;

	internal static string? ValueOf(string name, string key)
	{
		if (!values.TryGetValue(name, out Func<string?>? provider))
		{
			return null;
		}
		try
		{
			return provider();
		}
		catch (Exception e)
		{
			Manager.WarnOnce("value:" + name, $"Live value {{{name}}} failed: {e.Message}");
			return null;
		}
	}

	internal static void RaiseChanged() => Changed?.Invoke();
	internal static void RaiseOpened(string key) => Opened?.Invoke(key);
	internal static void RaiseClosed(string key) => Closed?.Invoke(key);
	internal static void RaisePinned(string key) => Pinned?.Invoke(key);
}
