using System.Collections.Generic;
using DeepTip.Core;

namespace DeepTip;

/// <summary>Valheim's language names → locale codes of the glossary files; checked once a second.</summary>
internal static class Locale
{
	private static readonly Dictionary<string, string> Codes = new()
	{
		["English"] = "en",
		["German"] = "de",
		["French"] = "fr",
		["Spanish"] = "es",
		["Spanish_Latam"] = "es",
		["Italian"] = "it",
		["Portuguese_European"] = "pt",
		["Portuguese_Brazilian"] = "pt-br",
		["Russian"] = "ru",
		["Polish"] = "pl",
		["Czech"] = "cs",
		["Slovak"] = "sk",
		["Hungarian"] = "hu",
		["Romanian"] = "ro",
		["Bulgarian"] = "bg",
		["Greek"] = "el",
		["Turkish"] = "tr",
		["Ukrainian"] = "uk",
		["Swedish"] = "sv",
		["Norwegian"] = "no",
		["Danish"] = "da",
		["Finnish"] = "fi",
		["Icelandic"] = "is",
		["Dutch"] = "nl",
		["Estonian"] = "et",
		["Latvian"] = "lv",
		["Lithuanian"] = "lt",
		["Croatian"] = "hr",
		["Serbian"] = "sr",
		["Slovenian"] = "sl",
		["Japanese"] = "ja",
		["Korean"] = "ko",
		["Chinese"] = "zh",
		["Chinese_Trad"] = "zh-tw",
		["Thai"] = "th",
		["Vietnamese"] = "vi",
		["Indonesian"] = "id",
		["Arabic"] = "ar",
		["Hindi"] = "hi",
	};

	private static string? last;
	private static float nextCheck;

	/// <summary>Code of a Valheim language name; unknown names become lower case ("Klingon" → "klingon").</summary>
	internal static string CodeOf(string language) => Codes.TryGetValue(language, out string? code) ? code : language.ToLowerInvariant();

	/// <summary>Follow the game's language; English, then German as fallbacks (most mods ship English).</summary>
	internal static void Update(float now)
	{
		if (now < nextCheck)
		{
			return;
		}
		nextCheck = now + 1f;
		string? language = Localization.instance?.GetSelectedLanguage();
		if (string.IsNullOrEmpty(language) || language == last)
		{
			return;
		}
		last = language;
		string code = CodeOf(language!);
		List<string> fallback = new();
		if (code.Contains("-"))
		{
			fallback.Add(code.Split('-')[0]);
		}
		fallback.Add("en");
		fallback.Add("de");
		Glossary.SetLocale(code, fallback);
		Plugin.Log.LogInfo($"Language {language} → glossary chain {string.Join(", ", Glossary.Chain)}");
	}
}
