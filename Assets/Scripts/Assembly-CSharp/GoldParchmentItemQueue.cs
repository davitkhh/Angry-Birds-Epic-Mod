using System.Collections.Generic;
using UnityEngine;

// The gold blueprint parchment is a UITexture, so NGUI gives it its own draw call with a render queue one above
// the slot background. The tinted 3D item drawn on the parchment (render queue 3000, like the original parchment's
// atlas draw call, where it won by z) would otherwise be drawn underneath it. This keeps every non-NGUI renderer
// under the slot at least on the gold draw call's queue, so it again sorts against the parchment by z.
public class GoldParchmentItemQueue : MonoBehaviour
{
	public GameObject SlotRoot;

	public UITexture Parchment;

	private readonly HashSet<Renderer> m_Raised = new HashSet<Renderer>();

	private void LateUpdate()
	{
		if (SlotRoot == null || Parchment == null || Parchment.drawCall == null)
		{
			return;
		}
		var queue = Parchment.drawCall.finalRenderQueue;
		foreach (var rend in SlotRoot.GetComponentsInChildren<Renderer>())
		{
			if (rend.GetComponent<UIDrawCall>() != null)
			{
				continue;
			}
			var shared = rend.sharedMaterial;
			if (shared == null || (shared.renderQueue >= queue && !m_Raised.Contains(rend)))
			{
				continue;
			}
			if (shared.renderQueue != queue)
			{
				// Also follows the gold draw call when NGUI renumbers the panel's queues
				rend.material.renderQueue = queue;
			}
			m_Raised.Add(rend);
		}
	}

	// Slots are reused for normal recipes, so hand the item back its shader's queue
	private void OnDestroy()
	{
		foreach (var rend in m_Raised)
		{
			if (rend != null && rend.sharedMaterial != null)
			{
				rend.material.renderQueue = -1;
			}
		}
		m_Raised.Clear();
	}
}
