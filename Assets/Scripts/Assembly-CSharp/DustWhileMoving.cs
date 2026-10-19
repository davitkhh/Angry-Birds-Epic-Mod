using UnityEngine;

// Kicks up a dust trail (FX_DustTrail) under a character while it moves, e.g. the intro raid's swarm pigs running and
// carrying; the trail stops emitting while it stands still. Jumps (a cutscene putting it somewhere) don't count.
public class DustWhileMoving : MonoBehaviour
{
	// Moving at least this fast (world units a second) raises dust; faster than the jump speed is a jump.
	private const float MinSpeed = 40f;

	private const float JumpSpeed = 4000f;

	private ParticleSystem m_Dust;

	private Vector3 m_LastPosition;

	private bool m_HasLastPosition;

	private void OnEnable()
	{
		m_HasLastPosition = false;
	}

	private void LateUpdate()
	{
		var position = transform.position;
		var speed = m_HasLastPosition && Time.deltaTime > 0f ? (position - m_LastPosition).magnitude / Time.deltaTime : 0f;
		m_LastPosition = position;
		m_HasLastPosition = true;
		var moving = speed > MinSpeed && speed < JumpSpeed;
		if (moving)
		{
			if (m_Dust == null)
			{
				m_Dust = DustTrail.Begin(transform, Vector3.zero);
			}
			else if (!m_Dust.isEmitting)
			{
				m_Dust.Play(true);
			}
		}
		else if (m_Dust != null && m_Dust.isEmitting)
		{
			m_Dust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
		}
	}
}
