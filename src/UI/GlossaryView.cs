using System.Collections.Generic;
using System.Linq;
using DeepTip.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeepTip.UI;

/// <summary>
/// DeepTip's own window for a glossary entry, on a top-most overlay canvas scaled like Valheim's GUI. Layout is done in
/// code (no layout groups): header with title, fallback badge, pin and close; below it the body, "See also" and the
/// hint line, scrollable when the screen is too small.
/// </summary>
internal sealed class GlossaryView : IView
{
	private const float Pad = 10f;
	private const float IconSize = 18f;
	private const float Gap = 6f;
	private const float MinWidth = 180f;

	private static Canvas? canvas;
	private static CanvasScaler? scaler;
	private static GuiScaler? valheimScaler;

	private readonly Window window;
	private readonly GameObject root;
	private readonly RectTransform rect;
	private readonly Image background;
	private readonly Frame frame;
	private readonly RectTransform viewport;
	private readonly RectTransform content;
	private readonly TextMeshProUGUI topic;
	private readonly TextMeshProUGUI badge;
	private readonly TextMeshProUGUI body;
	private readonly TextMeshProUGUI see;
	private readonly TextMeshProUGUI hint;
	private readonly GameObject pinButton;
	private readonly Image pinIcon;
	private readonly GameObject closeButton;
	private readonly Image lockBar;
	private float contentHeight;
	private float headerHeight;
	private float scroll;
	private float widthLimit;
	private float heightLimit;
	private bool pinned;

	internal GlossaryView(Window window)
	{
		this.window = window;
		root = new GameObject("DeepTip " + window.Key, typeof(RectTransform));
		root.transform.SetParent(CanvasRoot(), false);
		rect = (RectTransform)root.transform;
		rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
		rect.pivot = new Vector2(0f, 1f);
		background = root.AddComponent<Image>();
		background.sprite = Theme.Background;
		background.type = Theme.Background != null ? Theme.BackgroundType : Image.Type.Simple;
		background.color = Theme.Background != null ? Theme.BackgroundColor : new Color(0f, 0f, 0f, 0.88f);
		background.raycastTarget = true; // clicks inside never reach the game UI behind the window
		Shade(rect);
		root.AddComponent<ClickCatcher>().Window = window;

		topic = Theme.Text(rect, "Topic", Theme.TopicSize, Theme.TopicColor);
		badge = Theme.Text(rect, "Badge", Theme.TextSize * 0.7f, new Color(0.7f, 0.7f, 0.7f));
		pinButton = Button(rect, "Pin", Theme.Pin, () => Manager.TogglePin(window), out pinIcon);
		closeButton = Button(rect, "Close", Theme.Close, () => Manager.Close(window), out _);

		GameObject vp = new("Viewport", typeof(RectTransform));
		vp.transform.SetParent(rect, false);
		viewport = (RectTransform)vp.transform;
		viewport.anchorMin = viewport.anchorMax = new Vector2(0f, 1f);
		viewport.pivot = new Vector2(0f, 1f);
		vp.AddComponent<RectMask2D>();
		GameObject ct = new("Content", typeof(RectTransform));
		ct.transform.SetParent(viewport, false);
		content = (RectTransform)ct.transform;
		content.anchorMin = content.anchorMax = new Vector2(0f, 1f);
		content.pivot = new Vector2(0f, 1f);
		body = Theme.Text(content, "Body", Theme.TextSize, Theme.TextColor);
		see = Theme.Text(content, "See", Theme.TextSize * 0.9f, Theme.TextColor);
		hint = Theme.Text(content, "Hint", Theme.TextSize * 0.75f, new Color(0.65f, 0.65f, 0.65f));

		GameObject bar = new("Lock", typeof(RectTransform));
		bar.transform.SetParent(rect, false);
		lockBar = bar.AddComponent<Image>();
		lockBar.color = Settings.PinnedColor.Value;
		lockBar.type = Image.Type.Filled;
		lockBar.fillMethod = Image.FillMethod.Horizontal;
		lockBar.raycastTarget = false;
		lockBar.sprite = Theme.Solid;
		RectTransform br = lockBar.rectTransform;
		br.anchorMin = new Vector2(0f, 0f);
		br.anchorMax = new Vector2(1f, 0f);
		br.pivot = new Vector2(0f, 0f);
		br.sizeDelta = new Vector2(0f, 3f);
		bar.SetActive(false);
		frame = new Frame(rect);
		Refresh();
	}

	/// <summary>
	/// Valheim's tooltip background is see-through, so item icons showed through the text (Jan, 30.10.2026): a second
	/// layer of the same sprite (same edges) covers by Settings.Opacity.
	/// </summary>
	private static void Shade(RectTransform parent)
	{
		if (Theme.Background == null || Settings.Opacity.Value <= 0f)
		{
			return;
		}
		GameObject go = new("Shade", typeof(RectTransform));
		go.transform.SetParent(parent, false);
		RectTransform rt = (RectTransform)go.transform;
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
		Image image = go.AddComponent<Image>();
		image.sprite = Theme.Background;
		image.type = Theme.BackgroundType;
		Color c = Theme.BackgroundColor;
		image.color = new Color(c.r, c.g, c.b, Settings.Opacity.Value);
		image.raycastTarget = false;
	}

	public GameObject Root => root;
	public RectTransform Panel => rect;
	public IEnumerable<TMP_Text> Texts => new TMP_Text[] { body, see };

	/// <summary>The overlay canvas above every Valheim panel, created once.</summary>
	internal static Transform CanvasRoot()
	{
		if (canvas == null)
		{
			GameObject go = new("DeepTip");
			Object.DontDestroyOnLoad(go);
			canvas = go.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 30000;
			scaler = go.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
			go.AddComponent<GraphicRaycaster>();
		}
		SyncScale();
		return canvas.transform;
	}

	/// <summary>Follow Valheim's GUI scale (Settings → GUI scale).</summary>
	internal static void SyncScale()
	{
		if (scaler == null)
		{
			return;
		}
		if (valheimScaler == null)
		{
			valheimScaler = Object.FindFirstObjectByType<GuiScaler>();
		}
		float s = valheimScaler != null && valheimScaler.m_canvasScaler != null ? valheimScaler.m_canvasScaler.scaleFactor : 1f;
		if (s > 0.01f && !Mathf.Approximately(scaler.scaleFactor, s))
		{
			scaler.scaleFactor = s;
		}
	}

	private static float Scale => scaler != null ? scaler.scaleFactor : 1f;

	private static GameObject Button(Transform parent, string name, Sprite sprite, UnityEngine.Events.UnityAction onClick, out Image icon)
	{
		GameObject go = new(name, typeof(RectTransform));
		go.transform.SetParent(parent, false);
		icon = go.AddComponent<Image>();
		icon.sprite = sprite;
		icon.color = Theme.TextColor;
		icon.raycastTarget = true;
		Button button = go.AddComponent<Button>();
		button.targetGraphic = icon;
		ColorBlock colors = button.colors;
		colors.highlightedColor = Settings.LinkColor.Value;
		colors.pressedColor = Settings.LinkColor.Value;
		button.colors = colors;
		button.onClick.AddListener(onClick);
		RectTransform rt = (RectTransform)go.transform;
		rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
		rt.pivot = new Vector2(0f, 1f);
		rt.sizeDelta = new Vector2(IconSize, IconSize);
		return go;
	}

	public void Refresh()
	{
		Markup.Style style = Theme.Style;
		bool found = Glossary.TryGet(window.Key, out Entry entry, out string from);
		if (!found)
		{
			Manager.WarnOnce(window.Key, $"No entry \"{window.Key}\"");
		}
		topic.text = Localize(Markup.Plain(found ? entry.Title : window.Key));
		badge.text = found && from != Glossary.Locale ? from.ToUpperInvariant() : "";
		body.text = Localize(Markup.ToTmp(found ? entry.Body ?? entry.Short ?? Glossary.Ui("missing") : Glossary.Ui("missing"), window.Key, style));
		if (found && entry.See.Length > 0)
		{
			see.text = Markup.Plain(Glossary.Ui("seeAlso")) + " " + string.Join(", ", entry.See.Select(k => Markup.Link(k, null, window.Key, style)));
		}
		else
		{
			see.text = "";
		}
		Layout();
	}

	private static string Localize(string text) => text.IndexOf('$') >= 0 && Localization.instance != null ? Localization.instance.Localize(text) : text;

	private void Layout()
	{
		hint.text = Settings.Hints.Value && !pinned ? Markup.Plain(Glossary.Ui("hint")) : "";
		pinButton.SetActive(Settings.PinButton.Value);
		closeButton.SetActive(Settings.CloseButtons.Value);
		float maxW = widthLimit > 0f ? Mathf.Min(Settings.MaxWidth.Value, widthLimit / Scale) : Settings.MaxWidth.Value;
		maxW = Mathf.Max(maxW, MinWidth);
		float icons = (pinButton.activeSelf ? IconSize + 4f : 0f) + (closeButton.activeSelf ? IconSize + 4f : 0f);
		float badgeW = badge.text.Length > 0 ? badge.GetPreferredValues(badge.text).x + 6f : 0f;
		float innerMax = maxW - 2f * Pad;
		float need = topic.GetPreferredValues(topic.text, innerMax - icons - badgeW, 0f).x + icons + badgeW;
		foreach (TextMeshProUGUI t in new[] { body, see, hint })
		{
			if (t.text.Length > 0)
			{
				need = Mathf.Max(need, t.GetPreferredValues(t.text, innerMax, 0f).x);
			}
		}
		float inner = Mathf.Clamp(Mathf.Ceil(need) + 2f, MinWidth - 2f * Pad, innerMax);
		float width = inner + 2f * Pad;

		float topicW = inner - icons - badgeW;
		float topicH = Mathf.Max(IconSize, topic.GetPreferredValues(topic.text, topicW, 0f).y);
		Set(topic.rectTransform, Pad, Pad, topicW, topicH);
		Set(badge.rectTransform, Pad + Mathf.Min(topicW, topic.GetPreferredValues(topic.text, topicW, 0f).x) + 6f, Pad + 2f, badgeW, topicH);
		float bx = width - Pad;
		if (closeButton.activeSelf)
		{
			bx -= IconSize;
			Set((RectTransform)closeButton.transform, bx, Pad, IconSize, IconSize);
			bx -= 4f;
		}
		if (pinButton.activeSelf)
		{
			bx -= IconSize;
			Set((RectTransform)pinButton.transform, bx, Pad, IconSize, IconSize);
		}
		headerHeight = Pad + topicH + Gap;

		float y = 0f;
		foreach (TextMeshProUGUI t in new[] { body, see, hint })
		{
			t.gameObject.SetActive(t.text.Length > 0);
			if (t.text.Length == 0)
			{
				continue;
			}
			if (y > 0f)
			{
				y += Gap;
			}
			float h = t.GetPreferredValues(t.text, inner, 0f).y;
			Set(t.rectTransform, 0f, y, inner, h);
			y += h;
		}
		contentHeight = y;
		content.sizeDelta = new Vector2(inner, contentHeight);
		float maxView = heightLimit > 0f ? heightLimit / Scale - headerHeight - Pad : float.MaxValue;
		float viewH = Mathf.Max(20f, Mathf.Min(contentHeight, maxView));
		Set(viewport, Pad, headerHeight, inner, viewH);
		rect.sizeDelta = new Vector2(width, headerHeight + viewH + Pad);
		scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentHeight - viewH));
		content.anchoredPosition = new Vector2(0f, scroll);
	}

	private static void Set(RectTransform rt, float x, float y, float w, float h)
	{
		rt.anchoredPosition = new Vector2(x, -y);
		rt.sizeDelta = new Vector2(w, h);
	}

	public Vector2 ScreenSize() => rect.sizeDelta * Scale;

	public void Place(float x, float y, float maxW, float maxH)
	{
		if (!Mathf.Approximately(maxW, widthLimit) || !Mathf.Approximately(maxH, heightLimit))
		{
			widthLimit = maxW;
			heightLimit = maxH;
			Layout();
		}
		rect.anchoredPosition = new Vector2(x / Scale, -y / Scale);
		root.transform.SetAsLastSibling();
	}

	public void SetPinned(bool value)
	{
		pinned = value;
		frame.Show(value, Settings.PinnedColor.Value);
		pinIcon.sprite = value ? Theme.PinFilled : Theme.Pin;
		Layout();
	}

	public void SetLock(float progress)
	{
		bool on = progress > 0f && progress < 1f;
		if (lockBar.gameObject.activeSelf != on)
		{
			lockBar.gameObject.SetActive(on);
		}
		lockBar.fillAmount = progress;
	}

	public void Flash(float strength)
	{
		frame.Show(pinned || strength > 0f, strength > 0f ? Color.Lerp(Settings.PinnedColor.Value, Color.white, strength) : Settings.PinnedColor.Value);
	}

	public void Scroll(float wheel)
	{
		float viewH = viewport.sizeDelta.y;
		if (contentHeight <= viewH)
		{
			return;
		}
		scroll = Mathf.Clamp(scroll - wheel * 40f, 0f, contentHeight - viewH);
		content.anchoredPosition = new Vector2(0f, scroll);
	}

	public void Destroy() => Object.Destroy(root);
}
