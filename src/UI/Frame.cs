using UnityEngine;
using UnityEngine.UI;

namespace DeepTip.UI;

/// <summary>
/// Frame of a pinned or flashing window: four thin strips along the edges of the panel. An Outline effect does not work
/// here: it repeats the whole graphic in the frame color, so the transparent click blocker on top of Valheim's tooltip
/// became a solid yellow box, and a see-through background got tinted.
/// </summary>
internal sealed class Frame
{
	private const string Name = "DeepTip Frame";
	private const float Thickness = 2f;

	private readonly GameObject root;
	private readonly Image[] edges;

	/// <summary>The frame inside this panel, built once (a pinned copy of Valheim's tooltip brings its own along).</summary>
	internal Frame(Transform parent)
	{
		Transform? existing = parent.Find(Name);
		if (existing != null)
		{
			root = existing.gameObject;
			edges = root.GetComponentsInChildren<Image>(true);
			return;
		}
		root = new GameObject(Name, typeof(RectTransform));
		root.transform.SetParent(parent, false);
		root.AddComponent<LayoutElement>().ignoreLayout = true;
		RectTransform rt = (RectTransform)root.transform;
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
		edges = new[]
		{
			Edge(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, Thickness)),
			Edge(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, Thickness)),
			Edge(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(Thickness, 0f)),
			Edge(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(Thickness, 0f)),
		};
		root.SetActive(false);
	}

	private Image Edge(Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
	{
		GameObject go = new("Edge", typeof(RectTransform));
		go.transform.SetParent(root.transform, false);
		RectTransform rt = (RectTransform)go.transform;
		rt.anchorMin = anchorMin;
		rt.anchorMax = anchorMax;
		rt.pivot = pivot;
		rt.sizeDelta = size;
		rt.anchoredPosition = Vector2.zero;
		Image image = go.AddComponent<Image>();
		image.sprite = Theme.Solid;
		image.raycastTarget = false;
		return image;
	}

	internal void Show(bool on, Color color)
	{
		if (root.activeSelf != on)
		{
			root.SetActive(on);
		}
		foreach (Image edge in edges)
		{
			edge.color = color;
		}
	}

	/// <summary>Above everything else in the panel.</summary>
	internal void ToFront() => root.transform.SetAsLastSibling();
}
