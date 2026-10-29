using System.Collections.Generic;
using System.Reflection;
using DeepTip.UI;
using HarmonyLib;
using UnityEngine;

namespace DeepTip.Valheim;

/// <summary>
/// Esc closes the newest DeepTip window first: the first call of ZInput.GetKeyDown(Escape) in a frame lets DeepTip
/// react, and Valheim's own calls in that frame see no Esc while a pinned window was open. While the lexicon's search
/// field has the focus, Valheim sees no keys at all (typing "i" must not close the inventory).
/// </summary>
[HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyDown), new[] { typeof(KeyCode), typeof(bool) })]
internal static class EscapePatch
{
	[HarmonyPriority(Priority.First)]
	private static bool Prefix(KeyCode key, ref bool __result)
	{
		if (Manager.Reading)
		{
			return true;
		}
		if (Lexicon.Typing)
		{
			__result = false;
			return false;
		}
		if (key != KeyCode.Escape || !Manager.Active || Manager.Windows.Count == 0)
		{
			return true;
		}
		bool down;
		Manager.Reading = true;
		try
		{
			down = ZInput.GetKeyDown(KeyCode.Escape);
		}
		finally
		{
			Manager.Reading = false;
		}
		__result = down && !Manager.Escape();
		return false;
	}
}

[HarmonyPatch]
internal static class TypingPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		MethodInfo?[] methods =
		{
			AccessTools.Method(typeof(ZInput), nameof(ZInput.GetKey), new[] { typeof(KeyCode), typeof(bool) }),
			AccessTools.Method(typeof(ZInput), nameof(ZInput.GetKeyUp), new[] { typeof(KeyCode), typeof(bool) }),
			AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButtonDown), new[] { typeof(string) }),
			AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButtonUp), new[] { typeof(string) }),
			AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButton), new[] { typeof(string) }),
		};
		foreach (MethodInfo? m in methods)
		{
			if (m != null)
			{
				yield return m;
			}
		}
	}

	private static bool Prefix(ref bool __result)
	{
		if (Lexicon.Typing && !Manager.Reading)
		{
			__result = false;
			return false;
		}
		return true;
	}
}
