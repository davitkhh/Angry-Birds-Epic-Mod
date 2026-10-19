using UnityEngine;

// Extra depth scaling for a scene character on top of ScaleMgr's (which rewrites its scale every frame): from 1 at
// m_StartDepth to m_EndFactor at m_EndDepth, by the battle plane depth of where it stands (world y + 384, so it holds
// even when its z is kept in front of scenery). E.g. the intro raid's rogues shrink to half by the time they reach
// the egg nest far back.
[DefaultExecutionOrder(150)]
public class ExtraDepthScale : MonoBehaviour
{
	private const float PlaneDepthOffset = 384f;

	public float m_StartDepth;

	public float m_EndDepth;

	public float m_EndFactor = 0.5f;

	private void LateUpdate()
	{
		var depth = transform.position.y + PlaneDepthOffset;
		var factor = Mathf.Lerp(1f, m_EndFactor, Mathf.InverseLerp(m_StartDepth, m_EndDepth, depth));
		var scale = transform.localScale;
		transform.localScale = new Vector3(scale.x * factor, scale.y * factor, scale.z);
	}
}
