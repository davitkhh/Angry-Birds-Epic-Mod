using UnityEngine;

// Keeps a world map character turned towards a target (ActionTree FaceTarget node) by flipping its x scale.
// Stops when the target is destroyed; the last facing is kept.
public class FaceTargetTracker : MonoBehaviour
{
	// Ignore small x differences so the character doesn't flicker when the target passes straight above or below.
	private const float DeadZone = 5f;

	private Transform m_Target;

	// Birds face right at scale x = +1; pig rigs face left.
	private bool m_FacesRightByDefault;

	public void Track(Transform target, bool facesRightByDefault)
	{
		m_Target = target;
		m_FacesRightByDefault = facesRightByDefault;
		LateUpdate();
	}

	private void LateUpdate()
	{
		if (m_Target == null)
		{
			Destroy(this);
			return;
		}
		var dx = m_Target.position.x - transform.position.x;
		if (Mathf.Abs(dx) < DeadZone)
		{
			return;
		}
		var faceRight = dx > 0f;
		var sign = faceRight == m_FacesRightByDefault ? 1f : -1f;
		var scale = transform.localScale;
		transform.localScale = new Vector3(sign * Mathf.Abs(scale.x), scale.y, scale.z);
	}
}
