using UnityEngine;
using UnityEngine.EventSystems;

namespace DeepTip.UI;

/// <summary>Clicks inside a window or on a page with links: a term pins its window, elsewhere the window pins itself.</summary>
internal sealed class ClickCatcher : MonoBehaviour, IPointerClickHandler
{
	internal Window? Window;

	public void OnPointerClick(PointerEventData eventData)
	{
		if (eventData.button == PointerEventData.InputButton.Left)
		{
			Manager.Click(Window, eventData.position);
		}
	}
}
