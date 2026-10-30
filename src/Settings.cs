using BepInEx.Configuration;
using DeepTip.Core;
using UnityEngine;

namespace DeepTip;

/// <summary>Configuration (BepInEx/config/gorduan.deeptip.cfg). Names follow SPEC §7 where they exist.</summary>
internal static class Settings
{
	internal static ConfigEntry<bool> Enabled = null!;
	internal static ConfigEntry<bool> ValheimTooltips = null!;
	internal static ConfigEntry<bool> AutoLinkTerms = null!;
	internal static ConfigEntry<float> Delay = null!;
	internal static ConfigEntry<float> LinkDelay = null!;
	internal static ConfigEntry<float> CloseDelay = null!;
	internal static ConfigEntry<float> LockAfter = null!;
	internal static ConfigEntry<bool> CloseButtons = null!;
	internal static ConfigEntry<bool> PinButton = null!;
	internal static ConfigEntry<bool> Hints = null!;
	internal static ConfigEntry<bool> ClickInsidePins = null!;
	internal static ConfigEntry<float> MaxWidth = null!;
	internal static ConfigEntry<float> Opacity = null!;
	internal static ConfigEntry<string> RootSides = null!;
	internal static ConfigEntry<string> TooltipSides = null!;
	internal static ConfigEntry<string> ChildSides = null!;
	internal static ConfigEntry<KeyboardShortcut> OpenKey = null!;
	internal static ConfigEntry<KeyboardShortcut> CloseAllKey = null!;
	internal static ConfigEntry<bool> Lexicon = null!;
	internal static ConfigEntry<bool> LexiconFirst = null!;
	internal static ConfigEntry<Color> LinkColor = null!;
	internal static ConfigEntry<Color> PinnedColor = null!;

	internal static void Bind(ConfigFile config)
	{
		Enabled = config.Bind("1 - General", "Enabled", true, "DeepTip windows on. Off: Valheim's tooltips behave as without the mod.");
		ValheimTooltips = config.Bind("1 - General", "ValheimTooltips", true, "Valheim's own tooltips (inventory, crafting, skills ...) become DeepTip windows: they open next to the element instead of following the mouse, you can move into them, and highlighted terms open further windows.");
		AutoLinkTerms = config.Bind("1 - General", "AutoLink", true, "Highlight known terms in tooltip texts of Valheim and other mods automatically (first occurrence per window).");
		Delay = config.Bind("2 - Timing", "Delay", 0.5f, new ConfigDescription("Seconds the mouse rests on an element before its window opens (Valheim: 0.5).", new AcceptableValueRange<float>(0f, 5f)));
		LinkDelay = config.Bind("2 - Timing", "LinkDelay", 0.08f, new ConfigDescription("Seconds on a highlighted term before its preview opens.", new AcceptableValueRange<float>(0f, 2f)));
		CloseDelay = config.Bind("2 - Timing", "CloseDelay", 0.3f, new ConfigDescription("Grace in seconds before an unpinned window closes once the mouse left it (longer while the mouse moves towards it).", new AcceptableValueRange<float>(0f, 3f)));
		LockAfter = config.Bind("2 - Timing", "LockAfter", 0f, new ConfigDescription("Timer lock: seconds resting inside an unpinned window pin it (a bar shows the progress). 0 = off.", new AcceptableValueRange<float>(0f, 10f)));
		CloseButtons = config.Bind("3 - Windows", "CloseButton", true, "X button in every window.");
		PinButton = config.Bind("3 - Windows", "PinButton", true, "Pin button in every window.");
		Hints = config.Bind("3 - Windows", "Hints", true, "Hint line \"Click to pin · Esc closes\" in unpinned windows.");
		ClickInsidePins = config.Bind("3 - Windows", "ClickInsidePins", true, "A click anywhere inside an unpinned window pins it.");
		MaxWidth = config.Bind("3 - Windows", "MaxWidth", 420f, new ConfigDescription("Largest window width (GUI units).", new AcceptableValueRange<float>(200f, 900f)));
		Opacity = config.Bind("3 - Windows", "Opacity", 0.9f, new ConfigDescription("Extra cover behind the text of DeepTip's own windows: 0 = Valheim's see-through tooltip look, 1 = solid. Applies to windows opened afterwards.", new AcceptableValueRange<float>(0f, 1f)));
		RootSides = config.Bind("3 - Windows", "RootSides", "bottom,top,right,left", "Sides of a marked element where its window may open, in order of preference.");
		TooltipSides = config.Bind("3 - Windows", "TooltipSides", "right,left,bottom,top", "Sides for Valheim's own tooltips (inventory slots, recipes ...).");
		ChildSides = config.Bind("3 - Windows", "ChildSides", "left,right,top,bottom", "Sides of the previous window where a term's window may open (never covering it).");
		OpenKey = config.Bind("4 - Keys", "Open", new KeyboardShortcut(KeyCode.F1), "Opens the window of the element under the mouse, pinned.");
		CloseAllKey = config.Bind("4 - Keys", "CloseAll", new KeyboardShortcut(KeyCode.Escape, KeyCode.LeftShift), "Closes every window (holding Esc does the same). Esc alone closes the newest window.");
		Lexicon = config.Bind("5 - Lexicon", "Enabled", true, "A searchable page with every entry in Valheim's Compendium.");
		LexiconFirst = config.Bind("5 - Lexicon", "First", true, "The lexicon is the first page of the Compendium (off: the last).");
		LinkColor = config.Bind("6 - Colors", "Link", new Color(0.965f, 0.769f, 0.325f), "Color of highlighted terms.");
		PinnedColor = config.Bind("6 - Colors", "Pinned", new Color(0.965f, 0.769f, 0.325f), "Frame color of pinned windows.");
	}

	internal static string[] Sides(ConfigEntry<string> entry, string[] fallback) => Placement.ParseSides(entry.Value, fallback);
}
