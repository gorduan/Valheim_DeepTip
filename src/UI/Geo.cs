using DeepTip.Core;
using TMPro;
using UnityEngine;

namespace DeepTip.UI;

/// <summary>Screen geometry. DeepTip computes in screen pixels with y growing downwards (like the web reference).</summary>
internal static class Geo
{
	private static readonly Vector3[] Corners = new Vector3[4];

	internal static Vector2 Pointer => ZInput.pointerPosition;

	internal static float ScreenH => Screen.height;

	/// <summary>Camera of the canvas a UI element lives on (null for overlay canvases).</summary>
	internal static Camera? Cam(Component c)
	{
		Canvas? canvas = c.GetComponentInParent<Canvas>();
		canvas = canvas != null ? canvas.rootCanvas : null;
		return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
	}

	/// <summary>Screen box of a RectTransform (y down).</summary>
	internal static Box ScreenBox(RectTransform rt)
	{
		rt.GetWorldCorners(Corners);
		return FromWorld(Corners, Cam(rt));
	}

	internal static Box FromWorld(Vector3[] corners, Camera? cam)
	{
		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		foreach (Vector3 c in corners)
		{
			Vector2 s = RectTransformUtility.WorldToScreenPoint(cam, c);
			minX = Mathf.Min(minX, s.x);
			maxX = Mathf.Max(maxX, s.x);
			minY = Mathf.Min(minY, s.y);
			maxY = Mathf.Max(maxY, s.y);
		}
		return new Box(minX, ScreenH - maxY, maxX, ScreenH - minY);
	}

	internal static bool Contains(RectTransform rt, Vector2 screenPoint) => rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, Cam(rt));

	/// <summary>DeepTip link (key) of a TMP text under a screen point, else null. Other mods' links are ignored.</summary>
	internal static string? LinkAt(TMP_Text text, Vector2 screenPoint, out int index)
	{
		index = -1;
		if (text == null || !text.isActiveAndEnabled || text.textInfo == null || text.textInfo.linkCount == 0)
		{
			return null;
		}
		index = TMP_TextUtilities.FindIntersectingLink(text, screenPoint, Cam(text));
		if (index < 0 || index >= text.textInfo.linkCount)
		{
			index = -1;
			return null;
		}
		string id = text.textInfo.linkInfo[index].GetLinkID();
		return id.StartsWith(Markup.LinkPrefix, System.StringComparison.Ordinal) ? id.Substring(Markup.LinkPrefix.Length) : null;
	}

	/// <summary>Screen box of one link's characters (anchor of a window opened from a term on a page).</summary>
	internal static Box LinkBox(TMP_Text text, int index)
	{
		TMP_LinkInfo link = text.textInfo.linkInfo[index];
		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		for (int i = link.linkTextfirstCharacterIndex; i < link.linkTextfirstCharacterIndex + link.linkTextLength && i < text.textInfo.characterCount; i++)
		{
			TMP_CharacterInfo c = text.textInfo.characterInfo[i];
			if (!c.isVisible)
			{
				continue;
			}
			minX = Mathf.Min(minX, c.bottomLeft.x);
			minY = Mathf.Min(minY, c.descender);
			maxX = Mathf.Max(maxX, c.topRight.x);
			maxY = Mathf.Max(maxY, c.ascender);
		}
		if (minX > maxX)
		{
			return ScreenBox(text.rectTransform);
		}
		Transform t = text.transform;
		Corners[0] = t.TransformPoint(new Vector3(minX, minY));
		Corners[1] = t.TransformPoint(new Vector3(minX, maxY));
		Corners[2] = t.TransformPoint(new Vector3(maxX, maxY));
		Corners[3] = t.TransformPoint(new Vector3(maxX, minY));
		return FromWorld(Corners, Cam(text));
	}
}
