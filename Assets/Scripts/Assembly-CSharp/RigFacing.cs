using UnityEngine;

// Holds a battle rig turned around (King DJ's ChargeTurnAround) by rewriting the sign and width of its x scale every
// Update and LateUpdate. Needed because battlegrounds with a ScaleMgr rewrite a ScaleController rig's scale (positive x) every
// Update. The width comes from the y scale and the x/y ratio the rig had when it was first turned, so repeated squeezes
// never compound. Facing 1 = the rig's own direction, -1 = turned around, values in between squeeze through the turn.
[DefaultExecutionOrder(300)]
public class RigFacing : MonoBehaviour
{
	public float Facing = 1f;

	private float m_Sign = 1f;

	private float m_Ratio = 1f;

	public static RigFacing Get(Transform rig)
	{
		var facing = rig.GetComponent<RigFacing>();
		if (facing == null)
		{
			facing = rig.gameObject.AddComponent<RigFacing>();
			var scale = rig.localScale;
			facing.m_Sign = scale.x < 0f ? -1f : 1f;
			facing.m_Ratio = Mathf.Abs(scale.y) > 1e-5f ? Mathf.Abs(scale.x / scale.y) : 1f;
		}
		return facing;
	}

	// The scale the rig would have without the turn (for placing bubbles and texts beside it).
	public static Vector3 UnturnedScale(Transform rig)
	{
		var scale = rig.localScale;
		var facing = rig.GetComponent<RigFacing>();
		if (facing != null)
		{
			scale.x = Mathf.Abs(scale.y) * facing.m_Ratio * facing.m_Sign;
		}
		return scale;
	}

	public void Apply()
	{
		var scale = transform.localScale;
		var magnitude = Mathf.Max(Mathf.Abs(Facing), 0.05f) * (Facing < 0f ? -1f : 1f);
		scale.x = Mathf.Abs(scale.y) * m_Ratio * m_Sign * magnitude;
		transform.localScale = scale;
	}

	// Back to the rig's own direction; the component goes away.
	public void Release()
	{
		Facing = 1f;
		Apply();
		Destroy(this);
	}

	// Update runs after ScaleMgr's (execution order 300), so coroutines and skill code that read bone positions this
	// frame (e.g. the cannon muzzle) already see the turned rig; LateUpdate covers anything later in the frame.
	private void Update()
	{
		Apply();
	}

	private void LateUpdate()
	{
		Apply();
	}
}
