using System.Collections.Generic;
using System.Linq;
using DeepTip.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeepTip.UI;

/// <summary>
/// Valheim's own tooltip object as a DeepTip window: the live one (UITooltip.m_tooltip) while it is a preview, a copy
/// once it is pinned. Valheim, EpicLoot and other mods keep drawing it as usual; DeepTip only adds an invisible click
/// blocker, the pin and close buttons and the lock bar on top, and moves it next to its element.
/// </summary>
internal sealed class TooltipView : IView
{
	private const string DecorName = "DeepTip Decor";
	private const float IconSize = 16f;

	private readonly Window window;
	private readonly GameObject go;
	private readonly RectTransform panel;
	private readonly Image pinIcon;
	private readonly Image lockBar;
	private readonly Frame frame;
	private bool pinned;

	internal TooltipView(Window window, GameObject go)
	{
		this.window = window;
		this.go = go;
		panel = (go.transform.childCount > 0 ? go.transform.GetChild(0) : go.transform) as RectTransform ?? (RectTransform)go.transform;
		Transform? decor = panel.Find(DecorName);
		if (decor == null)
		{
			decor = BuildDecor();
		}
		decor.SetAsLastSibling();
		// listeners added in code are not copied with the object: wire them again for a pinned copy
		Button(decor, "Pin").onClick.RemoveAllListeners();
		Button(decor, "Pin").onClick.AddListener(() => Manager.TogglePin(window));
		Button(decor, "Close").onClick.RemoveAllListeners();
		Button(decor, "Close").onClick.AddListener(() => Manager.Close(window));
		pinIcon = decor.Find("Pin").GetComponent<Image>();
		lockBar = decor.Find("Lock").GetComponent<Image>();
		decor.Find("Blocker").GetComponent<ClickCatcher>().Window = window;
		frame = new Frame(decor);
		decor.Find("Pin").gameObject.SetActive(Settings.PinButton.Value);
		decor.Find("Close").gameObject.SetActive(Settings.CloseButtons.Value);
	}

	private static Button Button(Transform decor, string name) => decor.Find(name).GetComponent<Button>();

	private Transform BuildDecor()
	{
		GameObject decor = new(DecorName, typeof(RectTransform));
		decor.transform.SetParent(panel, false);
		decor.AddComponent<LayoutElement>().ignoreLayout = true;
		Stretch((RectTransform)decor.transform);

		GameObject blocker = new("Blocker", typeof(RectTransform));
		blocker.transform.SetParent(decor.transform, false);
		Stretch((RectTransform)blocker.transform);
		Image image = blocker.AddComponent<Image>();
		image.color = new Color(0f, 0f, 0f, 0f);
		image.raycastTarget = true; // clicks inside the tooltip must not reach the slot behind it
		blocker.AddComponent<ClickCatcher>();

		Icon(decor.transform, "Close", Theme.Close, 0);
		Icon(decor.transform, "Pin", Theme.Pin, 1);

		GameObject bar = new("Lock", typeof(RectTransform));
		bar.transform.SetParent(decor.transform, false);
		Image fill = bar.AddComponent<Image>();
		fill.sprite = Theme.Solid;
		fill.type = Image.Type.Filled;
		fill.fillMethod = Image.FillMethod.Horizontal;
		fill.color = Settings.PinnedColor.Value;
		fill.raycastTarget = false;
		RectTransform br = (RectTransform)bar.transform;
		br.anchorMin = new Vector2(0f, 0f);
		br.anchorMax = new Vector2(1f, 0f);
		br.pivot = new Vector2(0f, 0f);
		br.sizeDelta = new Vector2(0f, 3f);
		bar.SetActive(false);
		return decor.transform;
	}

	private static void Icon(Transform parent, string name, Sprite sprite, int slot)
	{
		GameObject go = new(name, typeof(RectTransform));
		go.transform.SetParent(parent, false);
		Image image = go.AddComponent<Image>();
		image.sprite = sprite;
		image.color = Theme.TextColor;
		Button button = go.AddComponent<Button>();
		button.targetGraphic = image;
		ColorBlock colors = button.colors;
		colors.highlightedColor = Settings.LinkColor.Value;
		colors.pressedColor = Settings.LinkColor.Value;
		button.colors = colors;
		RectTransform rt = (RectTransform)go.transform;
		rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
		rt.pivot = new Vector2(1f, 1f);
		rt.sizeDelta = new Vector2(IconSize, IconSize);
		rt.anchoredPosition = new Vector2(-6f - slot * (IconSize + 4f), -6f);
	}

	private static void Stretch(RectTransform rt)
	{
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
	}

	public GameObject Root => go;
	public RectTransform Panel => panel;

	/// <summary>Every TMP text of the tooltip (main text, EpicLoot's comparison box …), not DeepTip's own icons.</summary>
	public IEnumerable<TMP_Text> Texts => go.GetComponentsInChildren<TMP_Text>().Where(t => t.name != "Topic");

	/// <summary>Link the terms in Valheim's tooltip text (after Valheim or a mod set it).</summary>
	internal static void LinkTexts(GameObject tooltip)
	{
		Markup.Style style = Theme.Style;
		foreach (TMP_Text t in tooltip.GetComponentsInChildren<TMP_Text>())
		{
			if (t.name == "Topic" || string.IsNullOrEmpty(t.text))
			{
				continue;
			}
			string linked = AutoLink.Process(t.text, Settings.AutoLinkTerms.Value, style);
			if (!ReferenceEquals(linked, t.text) && linked != t.text)
			{
				t.richText = true;
				t.text = linked;
			}
		}
	}

	public Vector2 ScreenSize()
	{
		LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
		Box b = Geo.ScreenBox(panel);
		return new Vector2(b.Width, b.Height);
	}

	public void Place(float x, float y, float maxW, float maxH)
	{
		Box b = Geo.ScreenBox(panel);
		RectTransform? parent = go.transform.parent as RectTransform;
		Camera? cam = Geo.Cam(go.transform);
		Vector2 from = new(b.Left, Geo.ScreenH - b.Top);
		Vector2 to = new(x, Geo.ScreenH - y);
		if (parent != null && RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, from, cam, out Vector3 wFrom) && RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, to, cam, out Vector3 wTo))
		{
			go.transform.position += wTo - wFrom;
		}
		else
		{
			go.transform.position += (Vector3)(to - from);
		}
	}

	public void SetPinned(bool value)
	{
		pinned = value;
		pinIcon.sprite = value ? Theme.PinFilled : Theme.Pin;
		frame.Show(value, Settings.PinnedColor.Value);
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
		// EpicLoot's tooltip scrolls itself; vanilla tooltips are not scrollable
	}

	public void Refresh() => LinkTexts(go);

	public void Destroy() => Object.Destroy(go);
}
