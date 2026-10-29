using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using DeepTip.Core;
using DeepTip.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeepTip;

/// <summary>
/// DeepTip for Valheim: explanation windows with depth (Paradox-style nested tooltips, see SPEC.md). Standalone; other
/// mods add glossaries through deeptip folders or <see cref="Api"/>.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BaseUnityPlugin
{
	public const string Guid = "gorduan.deeptip";
	public const string Name = "DeepTip";
	public const string Version = "1.0.0";

	/// <summary>Usable before Awake (another mod registering a glossary early).</summary>
	internal static ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(Name);
	internal static Plugin? Instance;

	/// <summary>False on a dedicated server: the glossary and the API work, windows do not exist.</summary>
	internal static bool HasUi;

	private readonly HashSet<string> errors = new();

	private void Awake()
	{
		Instance = this;
		Settings.Bind(Config);
		Loader.LoadAll();
		HasUi = !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
		Glossary.Changed += () =>
		{
			Api.RaiseChanged();
			if (HasUi)
			{
				Manager.RefreshAll();
			}
		};
		if (!HasUi)
		{
			Log.LogInfo("No graphics (dedicated server): glossary loaded, no windows.");
			return;
		}
		Harmony harmony = new(Guid);
		int failed = 0;
		foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
		{
			try
			{
				harmony.CreateClassProcessor(type).Patch();
			}
			catch (Exception e)
			{
				failed++;
				Log.LogError($"Patch {type.Name} failed, that part of DeepTip is off: {e.GetBaseException().Message}");
			}
		}
		Settings.Enabled.SettingChanged += (_, _) => Manager.CloseAll();
		Settings.LinkColor.SettingChanged += (_, _) => Manager.RefreshAll();
		Log.LogInfo($"DeepTip {Version} ready ({failed} patch errors).");
	}

	private void Update()
	{
		if (!HasUi)
		{
			return;
		}
		try
		{
			Manager.Update();
		}
		catch (Exception e)
		{
			// one log line per distinct problem, not one per frame
			if (errors.Add(e.GetType().Name + e.Message))
			{
				Log.LogError($"DeepTip update failed: {e}");
			}
		}
	}
}
