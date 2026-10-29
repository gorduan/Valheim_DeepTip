using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DeepTip.Core;
using DeepTip.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeepTip.Valheim;

/// <summary>
/// SPEC §2.7 in Valheim's Compendium: a page with every entry (title as a term, the short line), grouped by namespace
/// and searchable. All Compendium pages get their terms linked, and terms there open windows like anywhere else.
/// </summary>
internal static class Lexicon
{
	internal const string Marker = "\u0001deeptip.lexicon";

	private static TMP_InputField? search;
	private static TextsDialog? dialog;
	private static string query = "";

	/// <summary>The search field has the keyboard: Valheim must not react to the keys.</summary>
	internal static bool Typing => search != null && search.isFocused;

	internal static void AddPage(List<TextsDialog.TextInfo> texts)
	{
		if (!Settings.Lexicon.Value || !Plugin.HasUi || Glossary.All().Count == 0)
		{
			return;
		}
		TextsDialog.TextInfo page = new(Glossary.Ui("lexicon"), Marker);
		if (Settings.LexiconFirst.Value)
		{
			texts.Insert(0, page);
		}
		else
		{
			texts.Add(page);
		}
	}

	internal static void Shown(TextsDialog d, TextsDialog.TextInfo text)
	{
		dialog = d;
		Theme.Ensure();
		if (text.m_text == Marker)
		{
			// the page is readable in every mode (gamepad too); its terms react only with the mouse
			if (Manager.Active)
			{
				Manager.AttachPage(d.m_textArea);
			}
			EnsureSearch(d);
			search?.gameObject.SetActive(true);
			Render();
			return;
		}
		if (search != null)
		{
			search.gameObject.SetActive(false);
		}
		if (!Manager.Active)
		{
			return;
		}
		Manager.AttachPage(d.m_textArea);
		d.m_textArea.text = AutoLink.Process(d.m_textArea.text, Settings.AutoLinkTerms.Value, Theme.Style);
	}

	private static void Render()
	{
		TextsDialog? d = dialog;
		if (d == null)
		{
			return;
		}
		Markup.Style style = Theme.Style;
		string q = query.Trim();
		List<Entry> all = Glossary.All();
		List<Entry> matches = q.Length == 0 ? all : all.Where(e => Has(e.Title, q) || Has(e.Short, q) || Has(e.Key, q) || e.Aliases.Any(a => Has(a, q))).ToList();
		StringBuilder sb = new();
		if (matches.Count == 0)
		{
			sb.Append(Markup.Plain(Glossary.Ui("empty")));
		}
		foreach (IGrouping<string, Entry> group in matches.GroupBy(Group).OrderBy(g => g.Key == "deeptip" ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
		{
			if (sb.Length > 0)
			{
				sb.Append('\n');
			}
			sb.Append("<b>").Append(Markup.Plain(GroupTitle(group.Key))).Append("</b>\n");
			foreach (Entry e in group)
			{
				sb.Append("• ").Append(Markup.Wrap(e.Key, Markup.Plain(e.Title), style));
				if (!string.IsNullOrEmpty(e.Short))
				{
					sb.Append(" – ").Append(Markup.Plain(e.Short!));
				}
				sb.Append('\n');
			}
		}
		string text = sb.ToString();
		d.m_textAreaTopic.text = Glossary.Ui("lexicon");
		d.m_textArea.richText = true;
		d.m_textArea.text = text.IndexOf('$') >= 0 && Localization.instance != null ? Localization.instance.Localize(text) : text;
	}

	private static bool Has(string? text, string q) => text != null && text.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;

	private static string Group(Entry e)
	{
		int dot = e.Key.IndexOf('.');
		return dot > 0 ? e.Key.Substring(0, dot) : e.Key;
	}

	private static string GroupTitle(string group) => group switch
	{
		"valheim" => "Valheim",
		"deeptip" => "DeepTip",
		_ => group.Length <= 3 ? group.ToUpperInvariant() : char.ToUpperInvariant(group[0]) + group.Substring(1),
	};

	private static void EnsureSearch(TextsDialog d)
	{
		if (search != null)
		{
			return;
		}
		try
		{
			GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
			go.name = "DeepTip Search";
			go.transform.SetParent(d.m_textAreaTopic.transform, false);
			RectTransform rt = (RectTransform)go.transform;
			rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
			rt.pivot = new Vector2(1f, 0.5f);
			rt.anchoredPosition = Vector2.zero;
			rt.sizeDelta = new Vector2(260f, 30f);
			Image background = go.GetComponent<Image>();
			background.sprite = Theme.Background;
			background.type = Theme.Background != null ? Theme.BackgroundType : Image.Type.Simple;
			background.color = new Color(0f, 0f, 0f, 0.6f);
			search = go.GetComponent<TMP_InputField>();
			foreach (TextMeshProUGUI t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
			{
				if (Theme.Font != null)
				{
					t.font = Theme.Font;
				}
				t.fontSize = Theme.TextSize;
				t.color = t.name == "Placeholder" ? new Color(0.6f, 0.6f, 0.6f) : Theme.TextColor;
				if (t.name == "Placeholder")
				{
					t.text = Glossary.Ui("search");
				}
			}
			search.onValueChanged.AddListener(value =>
			{
				query = value ?? "";
				Render();
			});
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"Lexicon search field not created: {e.Message}");
		}
	}
}

[HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
internal static class CompendiumListPatch
{
	private static void Postfix(TextsDialog __instance) => Lexicon.AddPage(__instance.m_texts);
}

[HarmonyPatch(typeof(TextsDialog), "ShowText", new[] { typeof(TextsDialog.TextInfo) })]
internal static class CompendiumShowPatch
{
	private static void Postfix(TextsDialog __instance, TextsDialog.TextInfo text) => Lexicon.Shown(__instance, text);
}
