using UnityEngine;

// Offsets and scales one rig bone on top of its animation (e.g. Chuck's shadow while he lies knocked out), easing both
// in. Re-applied only when the animation has rewritten the bone, so a stopped clip doesn't make them add up.
public class BoneNudge : MonoBehaviour
{
	private Transform m_Bone;

	private Vector3 m_Offset;

	private float m_Scale;

	private float m_EaseTime;

	private float m_Elapsed;

	private Vector3 m_AppliedPosition;

	private Vector3 m_AppliedScale;

	private bool m_HasApplied;

	private float m_LastWeight;

	public static void Apply(Transform bone, Vector3 offset, float scale, float easeTime)
	{
		if (bone == null)
		{
			return;
		}
		var nudge = bone.gameObject.AddComponent<BoneNudge>();
		nudge.m_Bone = bone;
		nudge.m_Offset = offset;
		nudge.m_Scale = scale;
		nudge.m_EaseTime = easeTime;
	}

	private void LateUpdate()
	{
		m_Elapsed += Time.deltaTime;
		var weight = m_EaseTime > 0f ? Mathf.Clamp01(m_Elapsed / m_EaseTime) : 1f;
		var position = m_Bone.localPosition;
		var scale = m_Bone.localScale;
		if (m_HasApplied && position == m_AppliedPosition && scale == m_AppliedScale)
		{
			// Not rewritten since last frame: undo last frame's part before applying this frame's.
			if (weight >= 1f && m_LastWeight >= 1f)
			{
				return;
			}
			position -= m_Offset * m_LastWeight;
			scale /= Mathf.Lerp(1f, m_Scale, m_LastWeight);
		}
		m_AppliedPosition = position + m_Offset * weight;
		m_AppliedScale = scale * Mathf.Lerp(1f, m_Scale, weight);
		m_Bone.localPosition = m_AppliedPosition;
		m_Bone.localScale = m_AppliedScale;
		m_LastWeight = weight;
		m_HasApplied = true;
	}
}
