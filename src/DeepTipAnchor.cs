using DeepTip.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DeepTip;

/// <summary>
/// Marks a UI element with a glossary key (<see cref="Api.Mark"/>): resting the mouse on it opens the entry's window.
/// Needs a raycast target on the element (Image, TMP text …) like any hover in Unity UI.
/// </summary>
public sealed class DeepTipAnchor : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
	/// <summary>Glossary key shown for this element.</summary>
	public string Key = "";
	/// <summary>Own side order, e.g. "top,bottom" (empty = the configured order).</summary>
	public string Sides = "";
	/// <summary>Own delay in seconds (negative = the configured delay).</summary>
	public float Delay = -1f;

	internal static DeepTipAnchor? Hovered;

	public void OnPointerEnter(PointerEventData eventData) => Hovered = this;

	public void OnPointerExit(PointerEventData eventData)
	{
		if (Hovered == this)
		{
			Hovered = null;
		}
		Manager.Unhush(this);
	}

	/// <summary>A click on the element means it is being used, not asked about: no window until the mouse leaves it.</summary>
	public void OnPointerDown(PointerEventData eventData) => Manager.Hush(this);

	private void OnDisable()
	{
		if (Hovered == this)
		{
			Hovered = null;
		}
	}
}
