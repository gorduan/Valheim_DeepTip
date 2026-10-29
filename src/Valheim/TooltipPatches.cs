using DeepTip.UI;
using HarmonyLib;
using UnityEngine;

namespace DeepTip.Valheim;

/// <summary>
/// Valheim's UITooltip as the first DeepTip window (setting ValheimTooltips): it opens next to its element after the
/// delay instead of following the mouse, stays while the mouse moves into it, and its terms open further windows.
/// Valheim, EpicLoot and other mods still create and fill it; gamepad and touch keep Valheim's behaviour.
/// </summary>
internal static class TooltipPatches
{
	/// <summary>DeepTip itself starts a hover that it had held back.</summary>
	internal static bool Bypass;

	internal static bool On => Manager.Active && Settings.ValheimTooltips.Value;
}

[HarmonyPatch(typeof(UITooltip), nameof(UITooltip.OnHoverStart))]
internal static class TooltipHoverStartPatch
{
	[HarmonyPriority(Priority.First)]
	private static bool Prefix(UITooltip __instance, GameObject go)
	{
		if (!TooltipPatches.On || TooltipPatches.Bypass)
		{
			return true;
		}
		bool hasText = __instance.m_text.Length > 0 || __instance.m_topic.Length > 0;
		if (hasText && Manager.ShouldDefer(__instance))
		{
			Manager.Deferred = (__instance, go, Time.unscaledTime); // the mouse is on its way into the open tooltip
			return false;
		}
		Manager.Deferred = null;
		Window? open = Manager.Adopted;
		if (open != null && open.Source != __instance && !open.Pinned)
		{
			Manager.DropAdopted(); // Valheim reuses its tooltip object for this element
		}
		return true;
	}
}

[HarmonyPatch(typeof(UITooltip), "LateUpdate")]
internal static class TooltipLateUpdatePatch
{
	[HarmonyPriority(Priority.First)]
	private static bool Prefix(UITooltip __instance)
	{
		if (!TooltipPatches.On || UITooltip.m_current != __instance)
		{
			return true; // other tooltips do nothing in mouse mode
		}
		GameObject? tooltip = UITooltip.m_tooltip;
		if (tooltip == null)
		{
			return false;
		}
		if (!tooltip.activeSelf)
		{
			if (!Geo.Contains((RectTransform)__instance.transform, ZInput.pointerPosition))
			{
				UITooltip.HideTooltip(); // left before the delay was over
				return false;
			}
			__instance.m_showTimer += Time.unscaledDeltaTime;
			if (__instance.m_showTimer >= Settings.Delay.Value)
			{
				tooltip.SetActive(true);
				Manager.Adopt(__instance, tooltip);
			}
		}
		else if (Manager.Adopted == null || Manager.Adopted.Source != __instance)
		{
			Manager.Adopt(__instance, tooltip); // shown by someone else in between
		}
		return false; // no following the mouse, no hiding on leave: DeepTip's grace time decides
	}
}

[HarmonyPatch(typeof(UITooltip), "LateUpdate")]
[HarmonyAfter("randyknapp.mods.epicloot")]
internal static class TooltipPlacePatch
{
	[HarmonyPriority(Priority.Last)]
	private static void Postfix(UITooltip __instance)
	{
		if (TooltipPatches.On && UITooltip.m_current == __instance)
		{
			Manager.KeepAdoptedPlaced(); // after EpicLoot placed it beside the cursor
		}
	}
}

[HarmonyPatch(typeof(UITooltip), nameof(UITooltip.OnPointerExit))]
internal static class TooltipPointerExitPatch
{
	private static bool Prefix() => !TooltipPatches.On;
}

[HarmonyPatch(typeof(UITooltip), "UpdateTextElements")]
internal static class TooltipTextPatch
{
	private static void Postfix()
	{
		if (TooltipPatches.On && UITooltip.m_tooltip != null)
		{
			Manager.AdoptedTextChanged(UITooltip.m_tooltip);
		}
	}
}

[HarmonyPatch(typeof(UITooltip), nameof(UITooltip.HideTooltip))]
internal static class TooltipHidePatch
{
	private static void Postfix()
	{
		if (!Manager.Detaching && Manager.Adopted != null)
		{
			Manager.DropAdopted();
		}
	}
}
