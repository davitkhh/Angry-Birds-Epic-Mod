using UnityEngine;

// Moves a character so that one of its bones (not its root) follows a path along x, after a hold: e.g. Red pushed by
// the corporal, whose Hit clip knocks his body back on the ground and then hops it forward. Pinning the body keeps it
// still until the hop and moves it only while it is in the air. Runs after the animation (LateUpdate).
[DefaultExecutionOrder(50)]
public class BonePinnedPath : MonoBehaviour
{
	private Transform m_Bone;

	private ActionTree.SmoothPath m_Path;

	private float m_Start;

	private float m_Hold;

	private float m_Duration;

	public static void Begin(Transform mover, Transform bone, ActionTree.SmoothPath path, float hold, float duration)
	{
		var existing = mover.GetComponent<BonePinnedPath>();
		if (existing != null)
		{
			Destroy(existing);
		}
		var pinned = mover.gameObject.AddComponent<BonePinnedPath>();
		pinned.m_Bone = bone;
		pinned.m_Path = path;
		pinned.m_Start = Time.time;
		pinned.m_Hold = hold;
		pinned.m_Duration = Mathf.Max(duration, 0.01f);
	}

	private void LateUpdate()
	{
		if (m_Bone == null)
		{
			Destroy(this);
			return;
		}
		var t = Time.time - m_Start;
		var target = m_Path.At(Mathf.Clamp01((t - m_Hold) / m_Duration));
		var boneOffset = m_Bone.position.x - transform.position.x;
		transform.position = new Vector3(target.x - boneOffset, target.y, target.z);
		// Done once the path is (he has landed): from here his own animations move his body as usual, his root stays.
		if (t >= m_Hold + m_Duration)
		{
			Destroy(this);
		}
	}
}
