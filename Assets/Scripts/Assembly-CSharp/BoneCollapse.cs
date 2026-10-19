using UnityEngine;

// Collapses a rig sprite (scales it to almost nothing) every frame, e.g. a bird's hand item once it was dropped as a
// separate quad. Collapsing keeps the SmoothMoves quad count, so the rig's other parts stay in their submesh runs
// (hiding a bone would shift them).
[DefaultExecutionOrder(100)]
public class BoneCollapse : MonoBehaviour
{
	private static readonly Vector3 Collapsed = new Vector3(0.0001f, 0.0001f, 1f);

	public static void Add(Transform sprite)
	{
		if (sprite != null && sprite.GetComponent<BoneCollapse>() == null)
		{
			sprite.gameObject.AddComponent<BoneCollapse>();
		}
	}

	private void LateUpdate()
	{
		transform.localScale = Collapsed;
	}
}
