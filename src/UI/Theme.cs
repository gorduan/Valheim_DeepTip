using DeepTip.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeepTip.UI;

/// <summary>
/// Valheim look: background sprite, font and colors come from Valheim's own tooltip prefab (any UITooltip's
/// m_tooltipPrefab), so DeepTip windows look like the game's tooltips. Pin and close icons are drawn in code.
/// </summary>
internal static class Theme
{
	internal static Sprite? Background;
	internal static Image.Type BackgroundType = Image.Type.Sliced;
	internal static Color BackgroundColor = new(0f, 0f, 0f, 0.85f);
	internal static TMP_FontAsset? Font;
	internal static Material? FontMaterial;
	internal static float TextSize = 16f;
	internal static float TopicSize = 19f;
	internal static Color TextColor = new(0.92f, 0.92f, 0.92f);
	internal static Color TopicColor = new(1f, 0.74f, 0.36f);
	internal static GameObject? TooltipPrefab;
	internal static Sprite Pin = null!;
	internal static Sprite PinFilled = null!;
	internal static Sprite Close = null!;
	/// <summary>Plain white sprite for bars (Filled images need a sprite).</summary>
	internal static Sprite Solid = null!;
	private static bool ready;

	/// <summary>Render style; safe before DeepTip's Awake (another mod calling the API early).</summary>
	internal static Markup.Style Style => new()
	{
		Link = "#" + ColorUtility.ToHtmlStringRGB(Settings.LinkColor != null ? Settings.LinkColor.Value : new Color(0.965f, 0.769f, 0.325f)),
		Value = Api.ValueOf,
	};

	/// <summary>Read the look from Valheim once its UI exists (main menu or game); false while nothing is loaded yet.</summary>
	internal static bool Ensure()
	{
		if (ready)
		{
			return true;
		}
		if (Pin == null)
		{
			Pin = Icons.Pin(false);
			PinFilled = Icons.Pin(true);
			Close = Icons.Cross();
			Solid = Icons.Solid();
		}
		foreach (UITooltip tip in Resources.FindObjectsOfTypeAll<UITooltip>())
		{
			if (tip != null && tip.m_tooltipPrefab != null)
			{
				TooltipPrefab = tip.m_tooltipPrefab;
				break;
			}
		}
		if (TooltipPrefab == null)
		{
			return false;
		}
		Transform? bkg = TooltipPrefab.transform.Find("Bkg");
		Image? image = bkg != null ? bkg.GetComponent<Image>() : TooltipPrefab.GetComponentInChildren<Image>(true);
		if (image != null)
		{
			Background = image.sprite;
			BackgroundType = image.type;
			BackgroundColor = image.color;
		}
		TMP_Text? text = Find(TooltipPrefab.transform, "Text");
		if (text != null)
		{
			Font = text.font;
			FontMaterial = text.fontSharedMaterial;
			TextSize = text.fontSize > 1f ? text.fontSize : TextSize;
			TextColor = text.color;
		}
		TMP_Text? topic = Find(TooltipPrefab.transform, "Topic");
		if (topic != null)
		{
			Font ??= topic.font;
			TopicSize = topic.fontSize > 1f ? topic.fontSize : TopicSize;
			TopicColor = topic.color;
		}
		ready = true;
		Plugin.Log.LogInfo($"Look taken from Valheim's tooltip: font {(Font != null ? Font.name : "default")}, size {TextSize}.");
		return true;
	}

	internal static TMP_Text? Find(Transform root, string name)
	{
		Transform? t = Utils.FindChild(root, name);
		return t != null ? t.GetComponent<TMP_Text>() : null;
	}

	/// <summary>A TMP text in Valheim's font.</summary>
	internal static TextMeshProUGUI Text(Transform parent, string name, float size, Color color)
	{
		GameObject go = new(name, typeof(RectTransform));
		go.transform.SetParent(parent, false);
		TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
		if (Font != null)
		{
			t.font = Font;
		}
		if (FontMaterial != null)
		{
			t.fontSharedMaterial = FontMaterial;
		}
		t.fontSize = size;
		t.color = color;
		t.richText = true;
		t.textWrappingMode = TextWrappingModes.Normal;
		t.overflowMode = TextOverflowModes.Overflow;
		t.raycastTarget = false;
		RectTransform rt = t.rectTransform;
		rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
		rt.pivot = new Vector2(0f, 1f);
		return t;
	}
}

/// <summary>Pin and close icons, drawn as anti-aliased shapes (no font glyph needed).</summary>
internal static class Icons
{
	private const int Size = 32;

	internal static Sprite Pin(bool filled)
	{
		return Draw((x, y) =>
		{
			// round head on top, needle below
			float head = Vector2.Distance(new Vector2(x, y), new Vector2(16f, 21f)) - 7f;
			float headShape = filled ? head : Mathf.Abs(head + 1.25f) - 1.25f;
			float needle = Segment(new Vector2(x, y), new Vector2(16f, 14f), new Vector2(16f, 3f)) - 1.4f;
			return Mathf.Min(headShape, needle);
		});
	}

	internal static Sprite Solid() => Draw((x, y) => -1f);

	internal static Sprite Cross()
	{
		return Draw((x, y) => Mathf.Min(Segment(new Vector2(x, y), new Vector2(8f, 8f), new Vector2(24f, 24f)), Segment(new Vector2(x, y), new Vector2(8f, 24f), new Vector2(24f, 8f))) - 1.8f);
	}

	private static float Segment(Vector2 p, Vector2 a, Vector2 b)
	{
		Vector2 ab = b - a;
		float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
		return Vector2.Distance(p, a + t * ab);
	}

	/// <summary>White shape from a signed distance function (negative inside), tinted by the Image color.</summary>
	private static Sprite Draw(System.Func<float, float, float> sdf)
	{
		Texture2D tex = new(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
		Color32[] pixels = new Color32[Size * Size];
		for (int y = 0; y < Size; y++)
		{
			for (int x = 0; x < Size; x++)
			{
				float d = sdf(x + 0.5f, y + 0.5f);
				byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
				pixels[y * Size + x] = new Color32(255, 255, 255, a);
			}
		}
		tex.SetPixels32(pixels);
		tex.Apply(false, true);
		return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
	}
}
