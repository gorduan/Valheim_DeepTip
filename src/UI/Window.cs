using System.Collections.Generic;
using DeepTip.Core;
using TMPro;
using UnityEngine;

namespace DeepTip.UI;

/// <summary>What a window belongs to (SPEC §3.1): a UI element or a term inside a window or on a page.</summary>
internal abstract class Anchor
{
	/// <summary>Identity for invariant 5 (the same anchor never has two windows).</summary>
	internal abstract string Id { get; }
	internal abstract bool Alive { get; }
	internal abstract Box Box();
}

internal sealed class ElementAnchor : Anchor
{
	internal readonly RectTransform Rect;

	internal ElementAnchor(RectTransform rect) => Rect = rect;

	internal override string Id => "e" + (Rect != null ? Rect.GetInstanceID() : 0);
	internal override bool Alive => Rect != null && Rect.gameObject.activeInHierarchy;
	internal override Box Box() => Geo.ScreenBox(Rect);
	internal bool Hovered(Vector2 p) => Alive && Geo.Contains(Rect, p);
}

internal sealed class TermAnchor : Anchor
{
	internal readonly Window? Host;
	internal readonly TMP_Text Text;
	internal readonly string Key;
	private readonly Box box;

	internal TermAnchor(Window? host, TMP_Text text, string key, Box box)
	{
		Host = host;
		Text = text;
		Key = key;
		this.box = box;
	}

	internal override string Id => "t" + (Host != null ? "w" + Host.Id : "p" + (Text != null ? Text.GetInstanceID() : 0)) + ":" + Key;
	internal override bool Alive => Host != null ? Manager.IsOpen(Host) : Text != null && Text.isActiveAndEnabled;
	internal override Box Box() => box;
}

/// <summary>Presentation of a window: DeepTip's own (glossary entry) or Valheim's tooltip object (adopted or pinned copy).</summary>
internal interface IView
{
	GameObject Root { get; }
	RectTransform Panel { get; }
	IEnumerable<TMP_Text> Texts { get; }
	/// <summary>Size in screen pixels as laid out now.</summary>
	Vector2 ScreenSize();
	/// <summary>Move so that the panel's top-left corner sits at (x, y) in screen pixels, y down; limit the size if asked.</summary>
	void Place(float x, float y, float maxW, float maxH);
	void SetPinned(bool pinned);
	void SetLock(float progress);
	void Flash(float strength);
	void Scroll(float wheel);
	void Refresh();
	void Destroy();
}

/// <summary>One open window (SPEC §3.2): previews and pinned windows form a forest.</summary>
internal sealed class Window
{
	internal int Id;
	/// <summary>Glossary key; empty for an adopted Valheim tooltip.</summary>
	internal string Key = "";
	internal Window? Parent;
	internal readonly List<Window> Children = new();
	internal bool Pinned;
	internal Anchor Anchor = null!;
	internal IView View = null!;
	internal string[] Sides = Placement.Sides;
	/// <summary>The live Valheim tooltip (UITooltip.m_tooltip) until it is pinned and copied.</summary>
	internal bool Adopted;
	internal UITooltip? Source;
	internal float LeaveSince = -1f;
	internal float FlashUntil;
	internal Vector2 PlacedSize;
	internal float PlacedX = float.NaN;
	internal float PlacedY;

	internal int Depth => Parent == null ? 0 : Parent.Depth + 1;

	internal Box ScreenBox() => Geo.ScreenBox(View.Panel);
}
