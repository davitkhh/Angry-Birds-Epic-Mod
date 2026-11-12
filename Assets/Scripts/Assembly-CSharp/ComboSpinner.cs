using UnityEngine;

// Temporary spin for combo moves: turns a battle rig about its vertical axis by squeezing its x scale through cos()
// (RigFacing does the writing, so the battleground's ScaleMgr can't undo it). Runs just before RigFacing (300).
[DefaultExecutionOrder(290)]
public class ComboSpinner : MonoBehaviour
{
	public float TurnsPerSecond;

	private float m_Phase;

	private RigFacing m_Facing;

	// phase in turns (0.25 = a quarter turn ahead), so two stacked spinners don't move in lockstep.
	public static ComboSpinner Begin(Transform rig, float turnsPerSecond, float phase = 0f)
	{
		var spinner = rig.GetComponent<ComboSpinner>();
		if (spinner == null)
		{
			spinner = rig.gameObject.AddComponent<ComboSpinner>();
		}
		spinner.m_Facing = RigFacing.Get(rig);
		spinner.TurnsPerSecond = turnsPerSecond;
		spinner.m_Phase = phase;
		spinner.Update();
		return spinner;
	}

	// Back to the rig's own direction.
	public void Stop()
	{
		Destroy(this);
	}

	private void Update()
	{
		if (m_Facing == null)
		{
			return;
		}
		m_Phase += TurnsPerSecond * Time.deltaTime;
		m_Facing.Facing = Mathf.Cos(m_Phase * 2f * Mathf.PI);
		m_Facing.Apply();
	}

	private void OnDestroy()
	{
		if (m_Facing != null)
		{
			m_Facing.Release();
			m_Facing = null;
		}
	}
}
