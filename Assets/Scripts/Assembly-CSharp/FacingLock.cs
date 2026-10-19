using UnityEngine;

// Keeps a character facing one way by fixing the sign of its x scale every frame (ActionTree FaceDirection /
// MoveToObject). The magnitude is left alone: pig rigs are their own ScaleController target, and ScaleMgr rewrites their
// scale (positive) whenever their depth changes.
[DefaultExecutionOrder(200)]
public class FacingLock : MonoBehaviour
{
	private float m_Sign = 1f;

	public static void Set(Transform target, bool faceRight, bool facesRightByDefault)
	{
		if (target == null)
		{
			return;
		}
		var lockComponent = target.GetComponent<FacingLock>();
		if (lockComponent == null)
		{
			lockComponent = target.gameObject.AddComponent<FacingLock>();
		}
		lockComponent.m_Sign = faceRight == facesRightByDefault ? 1f : -1f;
		lockComponent.LateUpdate();
	}

	private void LateUpdate()
	{
		var scale = transform.localScale;
		if (Mathf.Sign(scale.x) != m_Sign)
		{
			transform.localScale = new Vector3(m_Sign * Mathf.Abs(scale.x), scale.y, scale.z);
		}
	}
}
