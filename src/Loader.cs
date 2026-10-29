using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using DeepTip.Core;

namespace DeepTip;

/// <summary>
/// Loads glossary files: DeepTip's own (Valheim + help, embedded), every "deeptip" folder inside BepInEx/plugins
/// (any mod can ship deeptip/de.json, deeptip/en.json … without code), then BepInEx/config/DeepTip/*.json, the user's
/// own overrides (they win over everything).
/// </summary>
internal static class Loader
{
	internal static void LoadAll()
	{
		int files = 0;
		Assembly assembly = typeof(Loader).Assembly;
		foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("DeepTip.glossary.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
		{
			using Stream? stream = assembly.GetManifestResourceStream(name);
			if (stream == null)
			{
				continue;
			}
			using StreamReader reader = new(stream);
			string fileName = name.Substring("DeepTip.glossary.".Length);
			files += Load("DeepTip", reader.ReadToEnd(), fileName, Glossary.BuiltIn) ? 1 : 0;
		}
		int mods = 0;
		try
		{
			foreach (string folder in Directory.GetDirectories(Paths.PluginPath, "deeptip", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
			{
				string owner = Owner(folder);
				int before = files;
				foreach (string file in Directory.GetFiles(folder, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
				{
					files += Load(owner, File.ReadAllText(file), Path.GetFileName(file), Glossary.PluginFiles) ? 1 : 0;
				}
				mods += files > before ? 1 : 0;
			}
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"Could not search the plugin folders for glossaries: {e.Message}");
		}
		string user = Path.Combine(Paths.ConfigPath, "DeepTip");
		if (Directory.Exists(user))
		{
			foreach (string file in Directory.GetFiles(user, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
			{
				files += Load("config/DeepTip", File.ReadAllText(file), Path.GetFileName(file), Glossary.UserFiles) ? 1 : 0;
			}
		}
		Plugin.Log.LogInfo($"Glossary: {files} files ({mods} mod folders), entries per locale {Glossary.Summary()}.");
	}

	/// <summary>Plugin folder name of a "deeptip" folder (r2modman: Author-Package), else its parent's name.</summary>
	private static string Owner(string folder)
	{
		string relative = folder.Substring(Paths.PluginPath.Length).TrimStart('\\', '/');
		string[] parts = relative.Split('\\', '/');
		return parts.Length > 1 ? parts[0] : "plugins";
	}

	internal static bool Load(string owner, string json, string fileName, int priority)
	{
		try
		{
			foreach (string problem in Glossary.Add(owner, json, null, priority, fileName))
			{
				Plugin.Log.LogWarning($"{owner}/{fileName}: {problem}");
			}
			return true;
		}
		catch (Exception e)
		{
			Plugin.Log.LogError($"{owner}/{fileName} is not valid JSON and was skipped: {e.Message}");
			return false;
		}
	}
}
