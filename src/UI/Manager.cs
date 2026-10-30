using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using DeepTip.Core;
using DeepTip.Valheim;
using TMPro;
using UnityEngine;

namespace DeepTip.UI;

/// <summary>
/// The window tree and the input (SPEC §3, §5): opening after a rest, previews and pinned windows, the grace time and
/// mouse tendency that keep previews open on the way into them, suppression of crossed terms, the timer lock, Esc,
/// F1 and the mouse wheel. Valheim's own tooltip joins the tree as an adopted root window.
/// </summary>
internal static class Manager
{
	private const float TendencyMax = 1.5f;
	private const float HoldClose = 0.6f;

	private sealed class Pending
	{
		public string Id = "";
		public string Key = "";
		public Window? Parent;
		public float Since;
		public float Delay;
		public Func<Anchor> Anchor = null!;
		public string[]? Sides;
	}

	private static readonly List<Window> wins = new();
	private static readonly List<(Vector2 P, float T)> samples = new();
	private static readonly HashSet<string> warned = new();
	private static readonly List<TMP_Text> pages = new();
	private static int seq;
	private static Pending? pending;
	private static DeepTipAnchor? quiet;
	private static Window? lockWin;
	private static float lockSince;
	private static float escHeldSince = -1f;
	private static bool escHoldDone;
	private static int escFrame = -1;
	private static bool escConsumed;

	/// <summary>True while DeepTip reads keys itself: the input patches let those calls through.</summary>
	internal static bool Reading;

	/// <summary>The live Valheim tooltip as a window (null while none is shown or after it was pinned and copied).</summary>
	internal static Window? Adopted;

	/// <summary>Set while DeepTip itself hides Valheim's tooltip, so the HideTooltip patch does not react.</summary>
	internal static bool Detaching;

	/// <summary>A Valheim tooltip that waits while the mouse heads into the open one (SPEC §3.4 suppression).</summary>
	internal static (UITooltip Tip, GameObject Go, float Since)? Deferred;

	/// <summary>Mouse and keyboard in use. In the first frames after start Valheim's input system does not exist yet: no windows then.</summary>
	internal static bool MouseMode => ZInput.instance != null && !ZInput.IsExclusiveGamepadActive() && !ZInput.IsTouchActive();
	internal static bool Active => Plugin.HasUi && Settings.Enabled.Value && MouseMode;
	internal static bool IsOpen(Window w) => wins.Contains(w);
	internal static IReadOnlyList<Window> Windows => wins;

	private static string[] RootSides => Settings.Sides(Settings.RootSides, new[] { "bottom", "top", "right", "left" });
	internal static string[] TooltipSides => Settings.Sides(Settings.TooltipSides, new[] { "right", "left", "bottom", "top" });
	private static string[] ChildSides => Settings.Sides(Settings.ChildSides, new[] { "left", "right", "top", "bottom" });

	internal static void WarnOnce(string id, string message)
	{
		if (warned.Add(id))
		{
			Plugin.Log.LogWarning(message);
		}
	}

	/// <summary>A TMP text outside any window whose links open windows (Compendium page, other mods' panels).</summary>
	internal static void AttachPage(TMP_Text text)
	{
		if (text == null || pages.Contains(text))
		{
			return;
		}
		pages.Add(text);
		text.raycastTarget = true;
		ClickCatcher catcher = text.GetComponent<ClickCatcher>() ?? text.gameObject.AddComponent<ClickCatcher>();
		catcher.Window = null;
	}

	// ------------------------------------------------------------------ main loop

	internal static void Update()
	{
		float now = Time.unscaledTime;
		Locale.Update(now);
		if (!Active)
		{
			if (wins.Count > 0)
			{
				CloseAll();
			}
			pending = null;
			return;
		}
		GlossaryView.SyncScale();
		Vector2 p = Geo.Pointer;
		Sample(p, now);
		foreach (Window w in wins.ToArray())
		{
			if (IsOpen(w) && (w.View.Root == null || !w.Anchor.Alive))
			{
				Close(w); // menu closed or element gone
			}
		}
		pages.RemoveAll(t => t == null);

		// what is under the mouse: the newest window, a term in it (or on a page)
		Window? hoverWin = WindowAt(p);
		TMP_Text? linkText = null;
		string? linkKey = null;
		int linkIndex = -1;
		foreach (TMP_Text t in hoverWin != null ? hoverWin.View.Texts : pages)
		{
			if (t == null || !t.isActiveAndEnabled || (hoverWin == null && !Geo.Contains(t.rectTransform, p)))
			{
				continue;
			}
			linkKey = Geo.LinkAt(t, p, out linkIndex);
			if (linkKey != null)
			{
				linkText = t;
				break;
			}
		}

		// hot: the hovered window with its chain, and every window whose anchor is under the mouse
		HashSet<Window> hot = new();
		for (Window? x = hoverWin; x != null; x = x.Parent)
		{
			hot.Add(x);
		}
		foreach (Window w in wins)
		{
			bool anchored = w.Anchor switch
			{
				ElementAnchor e => hoverWin == null && e.Hovered(p),
				TermAnchor t => linkText != null && t.Text == linkText && t.Key == linkKey,
				_ => false,
			};
			if (anchored)
			{
				for (Window? x = w; x != null; x = x.Parent)
				{
					hot.Add(x);
				}
			}
		}
		foreach (Window w in wins.ToArray())
		{
			if (!IsOpen(w) || w.Pinned)
			{
				continue;
			}
			if (hot.Contains(w))
			{
				w.LeaveSince = -1f;
				continue;
			}
			if (w.LeaveSince < 0f)
			{
				w.LeaveSince = now;
			}
			float gone = now - w.LeaveSince;
			if (gone < Settings.CloseDelay.Value || (gone < Settings.CloseDelay.Value + TendencyMax && Heading(w)))
			{
				continue;
			}
			Close(w);
		}

		UpdateDeferred(p, now, hoverWin);

		// arm the window of the term or marked element under the mouse
		if (linkKey != null && linkText != null)
		{
			Window? parent = hoverWin;
			TMP_Text text = linkText;
			string key = linkKey;
			int index = linkIndex;
			string id = "t" + (parent != null ? "w" + parent.Id : "p" + text.GetInstanceID()) + ":" + key;
			Arm(id, key, parent, Settings.LinkDelay.Value, () => new TermAnchor(parent, text, key, Geo.LinkBox(text, index)), null, now);
		}
		else if (hoverWin == null && DeepTipAnchor.Hovered is DeepTipAnchor a && a != quiet && a.isActiveAndEnabled && a.Key.Length > 0 && !HasValheimTooltip(a))
		{
			RectTransform rt = (RectTransform)a.transform;
			string[]? sides = a.Sides.Length > 0 ? Placement.ParseSides(a.Sides, RootSides) : null;
			Arm("e" + rt.GetInstanceID(), a.Key, null, a.Delay >= 0f ? a.Delay : Settings.Delay.Value, () => new ElementAnchor(rt), sides, now);
		}
		else
		{
			pending = null;
		}
		if (pending != null && now - pending.Since >= pending.Delay)
		{
			bool wait = now - pending.Since < pending.Delay + TendencyMax && HeadingAway(pending.Parent);
			if (!wait)
			{
				Pending fire = pending;
				pending = null;
				Open(fire.Key, fire.Anchor(), fire.Parent, false, fire.Sides);
			}
		}

		UpdateLock(hoverWin, now);
		foreach (Window w in wins)
		{
			if (w.FlashUntil > 0f)
			{
				float left = w.FlashUntil - now;
				if (left <= 0f)
				{
					w.FlashUntil = 0f;
					w.View.Flash(0f);
				}
				else
				{
					w.View.Flash(Mathf.PingPong(left * 4f, 1f));
				}
			}
		}
		if (hoverWin != null)
		{
			Reading = true;
			float wheel = ZInput.GetMouseScrollWheel();
			Reading = false;
			if (Mathf.Abs(wheel) > 0.0001f)
			{
				hoverWin.View.Scroll(Mathf.Sign(wheel));
			}
		}
		Keys(p, now, hoverWin, linkText, linkKey, linkIndex);
		foreach (Window w in wins.ToArray())
		{
			if (IsOpen(w) && w.View is GlossaryView && (w.View.ScreenSize() - w.PlacedSize).sqrMagnitude > 4f)
			{
				Place(w); // content changed size (live values, language)
			}
		}
	}

	private static void Sample(Vector2 p, float now)
	{
		if (samples.Count == 0 || samples[samples.Count - 1].P != p)
		{
			samples.Add((p, now));
		}
		while (samples.Count > 2 && now - samples[0].T > 0.12f)
		{
			samples.RemoveAt(0);
		}
	}

	private static Window? WindowAt(Vector2 p)
	{
		for (int i = wins.Count - 1; i >= 0; i--)
		{
			Window w = wins[i];
			if (w.View.Root != null && w.View.Root.activeInHierarchy && Geo.Contains(w.View.Panel, p))
			{
				return w;
			}
		}
		return null;
	}

	private static bool HasValheimTooltip(DeepTipAnchor a) => a.GetComponent<UITooltip>() is UITooltip t && (t.m_text.Length > 0 || t.m_topic.Length > 0);

	private static void Arm(string id, string key, Window? parent, float delay, Func<Anchor> anchor, string[]? sides, float now)
	{
		if (pending != null && pending.Id == id)
		{
			return;
		}
		if (wins.Exists(w => w.Anchor.Id == id))
		{
			pending = null;
			return;
		}
		pending = new Pending { Id = id, Key = key, Parent = parent, Since = now, Delay = Mathf.Max(0f, delay), Anchor = anchor, Sides = sides };
	}

	/// <summary>A click on the element means it is being used: no window until the mouse leaves it (SPEC §3.3).</summary>
	internal static void Hush(DeepTipAnchor a)
	{
		quiet = a;
		if (pending != null && pending.Id == "e" + a.transform.GetInstanceID())
		{
			pending = null;
		}
	}

	internal static void Unhush(DeepTipAnchor a)
	{
		if (quiet == a)
		{
			quiet = null;
		}
	}

	// ------------------------------------------------------------------ mouse tendency (EU5) and suppression

	/// <summary>Is the mouse currently moving towards this window (ray of the last ~120 ms hits its box + 12 px)?</summary>
	private static bool Heading(Window w)
	{
		if (samples.Count < 2 || w.View.Root == null)
		{
			return false;
		}
		(Vector2 P, float T) a = samples[0];
		(Vector2 P, float T) b = samples[samples.Count - 1];
		if (Time.unscaledTime - b.T > 0.08f)
		{
			return false;
		}
		Vector2 v = b.P - a.P;
		if (v.magnitude < 3f)
		{
			return false;
		}
		Box r = w.ScreenBox();
		const float pad = 12f;
		float t0 = 0f;
		float t1 = float.MaxValue;
		return Axis(b.P.x, v.x, r.Left - pad, r.Right + pad, ref t0, ref t1) && Axis(Geo.ScreenH - b.P.y, -v.y, r.Top - pad, r.Bottom + pad, ref t0, ref t1);
	}

	private static bool Axis(float p, float v, float lo, float hi, ref float t0, ref float t1)
	{
		if (Mathf.Abs(v) < 1e-6f)
		{
			return p >= lo && p <= hi;
		}
		float ta = (lo - p) / v;
		float tb = (hi - p) / v;
		if (ta > tb)
		{
			(ta, tb) = (tb, ta);
		}
		t0 = Mathf.Max(t0, ta);
		t1 = Mathf.Min(t1, tb);
		return t0 <= t1;
	}

	/// <summary>On the way into a preview, terms crossed on the way must not replace it (the Victoria 3 problem).</summary>
	private static bool HeadingAway(Window? parent) => wins.Exists(w => !w.Pinned && !InChain(parent, w) && Heading(w));

	private static bool InChain(Window? x, Window w)
	{
		for (; x != null; x = x.Parent)
		{
			if (x == w)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>Called when Valheim wants to show the tooltip of another element while one is open.</summary>
	internal static bool ShouldDefer(UITooltip tip)
	{
		Window? open = Adopted;
		return open != null && IsOpen(open) && !open.Pinned && open.Source != tip && Heading(open);
	}

	private static void UpdateDeferred(Vector2 p, float now, Window? hoverWin)
	{
		if (Deferred is not { } d)
		{
			return;
		}
		if (d.Tip == null || !d.Tip.isActiveAndEnabled)
		{
			Deferred = null;
			return;
		}
		Window? open = Adopted;
		if (open != null && hoverWin != null && InChain(hoverWin, open))
		{
			Deferred = null; // arrived in the open tooltip
			return;
		}
		if (open != null && IsOpen(open) && !open.Pinned && now - d.Since < TendencyMax && Heading(open))
		{
			return;
		}
		Deferred = null;
		if (Geo.Contains((RectTransform)d.Tip.transform, p))
		{
			if (open != null && IsOpen(open) && !open.Pinned)
			{
				Close(open);
			}
			TooltipPatches.Bypass = true;
			try
			{
				d.Tip.OnHoverStart(d.Go);
			}
			finally
			{
				TooltipPatches.Bypass = false;
			}
		}
	}

	// ------------------------------------------------------------------ windows

	internal static Window? Open(string key, Anchor anchor, Window? parent, bool pinned, string[]? sides = null)
	{
		Theme.Ensure();
		if (parent != null && !IsOpen(parent))
		{
			return null;
		}
		Window? same = wins.Find(w => w.Anchor.Id == anchor.Id);
		if (same != null)
		{
			if (pinned)
			{
				Pin(same);
			}
			return same;
		}
		for (Window? x = parent; x != null; x = x.Parent)
		{
			if (x.Key == key)
			{
				x.FlashUntil = Time.unscaledTime + 0.6f; // already open further up: point at it
				return x;
			}
		}
		foreach (Window w in wins.Where(w => w.Parent == parent && !w.Pinned).ToList())
		{
			Close(w); // one preview per level
		}
		Window win = new() { Id = ++seq, Key = key, Parent = parent, Anchor = anchor, Sides = sides ?? (parent != null ? ChildSides : RootSides) };
		win.View = new GlossaryView(win);
		wins.Add(win);
		parent?.Children.Add(win);
		if (pinned)
		{
			Pin(win);
		}
		else
		{
			win.View.SetPinned(false);
		}
		Place(win);
		Api.RaiseOpened(key);
		return win;
	}

	internal static void Close(Window w)
	{
		if (!IsOpen(w))
		{
			return;
		}
		foreach (Window c in w.Children.ToArray())
		{
			Close(c);
		}
		wins.Remove(w);
		w.Parent?.Children.Remove(w);
		if (lockWin == w)
		{
			lockWin = null;
		}
		if (w.Adopted)
		{
			if (Adopted == w)
			{
				Adopted = null;
			}
			Detaching = true;
			try
			{
				UITooltip.HideTooltip();
			}
			finally
			{
				Detaching = false;
			}
		}
		else if (w.View.Root != null)
		{
			w.View.Destroy();
		}
		if (w.Key.Length > 0)
		{
			Api.RaiseClosed(w.Key);
		}
	}

	internal static void CloseAll()
	{
		foreach (Window w in wins.Where(x => x.Parent == null).ToList())
		{
			Close(w);
		}
		pending = null;
	}

	/// <summary>Pin a window and every window it came from (a pinned window never hangs off a vanishing one).</summary>
	internal static void Pin(Window w)
	{
		for (Window? x = w; x != null && !x.Pinned; x = x.Parent)
		{
			if (x.Adopted)
			{
				Detach(x);
			}
			x.Pinned = true;
			x.LeaveSince = -1f;
			x.View.SetPinned(true);
			if (x.Key.Length > 0)
			{
				Api.RaisePinned(x.Key);
			}
		}
		if (lockWin != null && lockWin.Pinned)
		{
			lockWin.View.SetLock(0f);
			lockWin = null;
		}
	}

	/// <summary>Back to a preview, with everything opened from it; it closes once the mouse is elsewhere.</summary>
	internal static void Unpin(Window w)
	{
		w.Pinned = false;
		w.LeaveSince = -1f;
		w.View.SetPinned(false);
		foreach (Window c in w.Children)
		{
			Unpin(c);
		}
	}

	internal static void TogglePin(Window w)
	{
		if (!IsOpen(w))
		{
			return;
		}
		if (w.Pinned)
		{
			Unpin(w);
		}
		else
		{
			Pin(w);
		}
	}

	private static void Place(Window w)
	{
		Vector2 size = w.View.ScreenSize();
		Box anchor = w.Parent != null ? w.Parent.ScreenBox() : w.Anchor.Box();
		Placed placed = Placement.Compute(size.x, size.y, anchor, w.Sides, Screen.width, Screen.height);
		w.View.Place(placed.X, placed.Y, placed.MaxW, placed.MaxH);
		w.PlacedSize = w.View.ScreenSize();
		w.PlacedX = placed.X;
		w.PlacedY = placed.Y;
	}

	// ------------------------------------------------------------------ Valheim's tooltip as a window

	/// <summary>Valheim showed its tooltip: it becomes the root window of its element.</summary>
	internal static void Adopt(UITooltip source, GameObject tooltip)
	{
		Theme.Ensure();
		if (Adopted != null)
		{
			if (Adopted.View.Root == tooltip && Adopted.Source == source)
			{
				return;
			}
			DropAdopted();
		}
		foreach (Window w in wins.Where(x => x.Parent == null && !x.Pinned).ToList())
		{
			Close(w); // one root preview
		}
		Window win = new() { Id = ++seq, Key = "", Anchor = new ElementAnchor((RectTransform)source.transform), Adopted = true, Source = source, Sides = TooltipSides };
		win.View = new TooltipView(win, tooltip);
		win.View.SetPinned(false);
		wins.Add(win);
		Adopted = win;
		TooltipView.LinkTexts(tooltip);
		Place(win);
	}

	/// <summary>Valheim hid its tooltip or reuses it for another element: the window ends without destroying it.</summary>
	internal static void DropAdopted()
	{
		Window? w = Adopted;
		if (w == null)
		{
			return;
		}
		Adopted = null;
		foreach (Window c in w.Children.ToArray())
		{
			Close(c);
		}
		wins.Remove(w);
		if (lockWin == w)
		{
			lockWin = null;
		}
	}

	/// <summary>Every frame after Valheim and EpicLoot moved the tooltip: back to its place next to the element.</summary>
	internal static void KeepAdoptedPlaced()
	{
		Window? w = Adopted;
		if (w == null || !IsOpen(w) || w.View.Root == null)
		{
			return;
		}
		Vector2 size = w.View.ScreenSize();
		if (float.IsNaN(w.PlacedX) || (size - w.PlacedSize).sqrMagnitude > 4f)
		{
			Place(w);
		}
		else
		{
			w.View.Place(w.PlacedX, w.PlacedY, 0f, 0f);
		}
	}

	/// <summary>Valheim changed the text of its tooltip (e.g. durability): link the terms again, keep the size right.</summary>
	internal static void AdoptedTextChanged(GameObject tooltip)
	{
		if (Adopted != null && Adopted.View.Root == tooltip)
		{
			foreach (Window c in Adopted.Children.Where(c => !c.Pinned).ToList())
			{
				Close(c);
			}
		}
		TooltipView.LinkTexts(tooltip);
	}

	/// <summary>Pinning Valheim's tooltip: it is copied, so Valheim can show the next one while this one stays.</summary>
	private static void Detach(Window w)
	{
		GameObject live = w.View.Root;
		GameObject copy = UnityEngine.Object.Instantiate(live, live.transform.parent);
		copy.name = "DeepTip pinned tooltip";
		copy.transform.position = live.transform.position;
		copy.SetActive(true);
		w.View = new TooltipView(w, copy);
		w.Adopted = false;
		w.Source = null;
		if (Adopted == w)
		{
			Adopted = null;
		}
		Detaching = true;
		try
		{
			UITooltip.HideTooltip();
		}
		finally
		{
			Detaching = false;
		}
	}

	// ------------------------------------------------------------------ clicks, keys, timer lock

	internal static void Click(Window? w, Vector2 position)
	{
		if (!Active)
		{
			return;
		}
		IEnumerable<TMP_Text> texts = w != null ? w.View.Texts : pages.Where(t => t != null && t.isActiveAndEnabled);
		foreach (TMP_Text t in texts)
		{
			string? key = Geo.LinkAt(t, position, out int index);
			if (key == null)
			{
				continue;
			}
			Window? parent = w != null && IsOpen(w) ? w : null;
			Open(key, new TermAnchor(parent, t, key, Geo.LinkBox(t, index)), parent, true);
			pending = null;
			return;
		}
		if (w != null && IsOpen(w) && !w.Pinned && Settings.ClickInsidePins.Value)
		{
			Pin(w);
		}
	}

	private static void UpdateLock(Window? hoverWin, float now)
	{
		float after = Settings.LockAfter.Value;
		Window? target = after > 0f && hoverWin != null && !hoverWin.Pinned ? hoverWin : null;
		if (target != lockWin)
		{
			lockWin?.View.SetLock(0f);
			lockWin = target;
			lockSince = now;
		}
		if (lockWin == null)
		{
			return;
		}
		float progress = (now - lockSince) / after;
		if (progress >= 1f)
		{
			Window w = lockWin;
			w.View.SetLock(0f);
			lockWin = null;
			Pin(w);
		}
		else
		{
			lockWin.View.SetLock(progress);
		}
	}

	private static void Keys(Vector2 p, float now, Window? hoverWin, TMP_Text? linkText, string? linkKey, int linkIndex)
	{
		Reading = true;
		try
		{
			if (Down(Settings.OpenKey.Value))
			{
				OpenHere(p, hoverWin, linkText, linkKey, linkIndex);
			}
			if (Down(Settings.CloseAllKey.Value) && wins.Count > 0)
			{
				CloseAll();
				escFrame = Time.frameCount;
				escConsumed = true;
			}
			if (!ZInput.GetKey(KeyCode.Escape))
			{
				escHeldSince = -1f;
				escHoldDone = false;
			}
			else if (escHeldSince < 0f)
			{
				escHeldSince = now;
			}
			else if (!escHoldDone && now - escHeldSince >= HoldClose && wins.Count > 0)
			{
				escHoldDone = true;
				CloseAll();
			}
		}
		finally
		{
			Reading = false;
		}
	}

	private static bool Down(KeyboardShortcut shortcut)
	{
		if (shortcut.MainKey == KeyCode.None || !ZInput.GetKeyDown(shortcut.MainKey))
		{
			return false;
		}
		foreach (KeyCode modifier in shortcut.Modifiers)
		{
			if (!ZInput.GetKey(modifier))
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// Esc pressed (asked by Valheim or by DeepTip): closes the newest window. Valheim does not see this Esc while a pinned
	/// window was open; with previews only, Esc also reaches the game (closing the inventory closes them anyway).
	/// </summary>
	internal static bool Escape()
	{
		int frame = Time.frameCount;
		if (escFrame == frame)
		{
			return escConsumed;
		}
		escFrame = frame;
		escConsumed = false;
		if (!Active || wins.Count == 0)
		{
			return false;
		}
		bool hadPinned = wins.Exists(w => w.Pinned);
		Close(wins[wins.Count - 1]);
		escConsumed = hadPinned;
		return escConsumed;
	}

	/// <summary>F1: the window of what is under the mouse, pinned (a term, a window, Valheim's tooltip, a marked element).</summary>
	private static void OpenHere(Vector2 p, Window? hoverWin, TMP_Text? linkText, string? linkKey, int linkIndex)
	{
		if (linkKey != null && linkText != null)
		{
			Open(linkKey, new TermAnchor(hoverWin, linkText, linkKey, Geo.LinkBox(linkText, linkIndex)), hoverWin, true);
			pending = null;
			return;
		}
		if (hoverWin != null)
		{
			Pin(hoverWin);
			return;
		}
		UITooltip? current = UITooltip.m_current;
		GameObject? tooltip = UITooltip.m_tooltip;
		if (current != null && tooltip != null && Geo.Contains((RectTransform)current.transform, p))
		{
			if (!tooltip.activeSelf)
			{
				tooltip.SetActive(true);
			}
			Adopt(current, tooltip);
			if (Adopted != null)
			{
				Pin(Adopted);
			}
			return;
		}
		if (DeepTipAnchor.Hovered is DeepTipAnchor a && a.isActiveAndEnabled && a.Key.Length > 0)
		{
			RectTransform rt = (RectTransform)a.transform;
			Open(a.Key, new ElementAnchor(rt), null, true, a.Sides.Length > 0 ? Placement.ParseSides(a.Sides, RootSides) : null);
			pending = null;
		}
	}

	/// <summary>Re-render every window (glossary or language changed, live values updated).</summary>
	internal static void RefreshAll()
	{
		foreach (Window w in wins.ToArray())
		{
			if (IsOpen(w) && w.View.Root != null)
			{
				w.View.Refresh();
			}
		}
	}
}
