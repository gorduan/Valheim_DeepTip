using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepTip.Core;

/// <summary>Rectangle in a top-left coordinate system (y grows downwards), like the web reference.</summary>
public struct Box
{
	public float Left;
	public float Top;
	public float Right;
	public float Bottom;

	public Box(float left, float top, float right, float bottom)
	{
		Left = left;
		Top = top;
		Right = right;
		Bottom = bottom;
	}

	public float Width => Right - Left;
	public float Height => Bottom - Top;

	public bool Contains(float x, float y, float pad = 0f) => x >= Left - pad && x <= Right + pad && y >= Top - pad && y <= Bottom + pad;
}

/// <summary>Result of <see cref="Placement.Compute"/>: top-left corner, chosen side, and the size limit when nothing fit.</summary>
public struct Placed
{
	public string Side;
	public float X;
	public float Y;
	public bool Fits;
	public float MaxW;
	public float MaxH;
}

/// <summary>
/// SPEC §4, port of computePlacement from web/deeptip.js (same unit tests). Tries the sides in order and takes the first
/// where the window fits between anchor and screen edge, so it never covers the anchor. On the other axis it is centred
/// on the anchor and pushed back on screen. Nothing fits: the side with the most room, the window shrunk to that room.
/// </summary>
public static class Placement
{
	public static readonly string[] Sides = { "left", "right", "top", "bottom" };

	public static string[] ParseSides(string? text, string[] fallback)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return fallback;
		}
		string[] sides = text!.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToLowerInvariant()).Where(s => Sides.Contains(s)).ToArray();
		return sides.Length > 0 ? sides : fallback;
	}

	public static Placed Compute(float width, float height, Box a, IReadOnlyList<string> sides, float viewW, float viewH, float gap = 8f)
	{
		static bool Horiz(string s) => s == "left" || s == "right";
		Dictionary<string, float> room = new()
		{
			["left"] = a.Left - 2 * gap,
			["right"] = viewW - a.Right - 2 * gap,
			["top"] = a.Top - 2 * gap,
			["bottom"] = viewH - a.Bottom - 2 * gap,
		};
		float Need(string s) => Horiz(s) ? width : height;
		List<string> order = sides.Where(s => Sides.Contains(s)).ToList();
		if (order.Count == 0)
		{
			order.Add("bottom");
		}
		string? side = order.FirstOrDefault(s => room[s] >= Need(s));
		bool fits = side != null;
		float maxW = 0f;
		float maxH = 0f;
		if (side == null)
		{
			side = order.Aggregate((best, s) => room[s] / Need(s) > room[best] / Need(best) ? s : best);
			if (room[side] >= 120f)
			{
				if (Horiz(side))
				{
					maxW = room[side];
				}
				else
				{
					maxH = room[side];
				}
			}
		}
		float w = maxW > 0f ? Math.Min(width, maxW) : width;
		float h = maxH > 0f ? Math.Min(height, maxH) : height;
		static float Clamp(float v, float lo, float hi) => Math.Max(lo, Math.Min(v, Math.Max(lo, hi)));
		float x;
		float y;
		if (Horiz(side))
		{
			x = side == "left" ? a.Left - gap - w : a.Right + gap;
			y = a.Top + a.Height / 2f - h / 2f;
		}
		else
		{
			y = side == "top" ? a.Top - gap - h : a.Bottom + gap;
			x = a.Left + a.Width / 2f - w / 2f;
		}
		x = Clamp(x, gap, viewW - gap - w);
		y = Clamp(y, gap, viewH - gap - h);
		return new Placed { Side = side, X = x, Y = y, Fits = fits, MaxW = maxW, MaxH = maxH };
	}
}
