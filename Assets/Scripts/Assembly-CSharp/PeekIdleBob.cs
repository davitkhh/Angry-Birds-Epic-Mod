using UnityEngine;

// Minion-pig style idle bob for a character built from flat pieces (the shop's Piggy McCool peek).
// Offsets are applied on top of each piece's rest pose, so the pieces can be positioned freely in the scene.
public class PeekIdleBob : MonoBehaviour
{
	// 1 = the MinionPig Idle loop (36 frames at 24 fps = 1.5 s); 2 bobs twice as fast, 0.5 half as fast.
	[SerializeField]
	public float m_Speed = 1f;

	[SerializeField]
	private Transform m_Body;

	[SerializeField]
	private Transform m_Face;

	[SerializeField]
	private Transform m_Nose;

	[Header("MinionPig Idle values")]
	[SerializeField]
	private float m_LoopSeconds = 1.5f;

	// Body imageScale at frame 18; it squashes from its own origin (keep the body widget's pivot at the bottom).
	[SerializeField]
	private Vector2 m_BodySquash = new Vector2(1.05f, 0.92f);

	// Face moves (-20,42) -> (-19,38) at frame 18.
	[SerializeField]
	private Vector2 m_FaceDip = new Vector2(1f, -4f);

	// Snout x -2 and imageScale (1.05, 0.9) at frame 21.
	[SerializeField]
	private Vector2 m_NoseShift = new Vector2(-2f, 0f);

	[SerializeField]
	private Vector2 m_NoseSquash = new Vector2(1.05f, 0.9f);

	private const float BodyPeak = 18f / 36f;

	private const float NosePeak = 21f / 36f;

	private Vector3 m_BodyScale;

	private Vector3 m_FacePosition;

	private Vector3 m_NosePosition;

	private Vector3 m_NoseScale;

	private bool m_HasRestPose;

	private float m_Phase;

	private void Awake()
	{
		CaptureRestPose();
	}

	private void OnDisable()
	{
		ApplyPose(0f, 0f);
		m_Phase = 0f;
	}

	private void CaptureRestPose()
	{
		if (m_Body != null)
		{
			m_BodyScale = m_Body.localScale;
		}
		if (m_Face != null)
		{
			m_FacePosition = m_Face.localPosition;
		}
		if (m_Nose != null)
		{
			m_NosePosition = m_Nose.localPosition;
			m_NoseScale = m_Nose.localScale;
		}
		m_HasRestPose = true;
	}

	private void Update()
	{
		if (!m_HasRestPose)
		{
			CaptureRestPose();
		}
		m_Phase = Mathf.Repeat(m_Phase + Time.deltaTime * m_Speed / Mathf.Max(0.01f, m_LoopSeconds), 1f);
		ApplyPose(Bump(m_Phase, BodyPeak), Bump(m_Phase, NosePeak));
	}

	private void ApplyPose(float body, float nose)
	{
		if (!m_HasRestPose)
		{
			return;
		}
		if (m_Body != null)
		{
			m_Body.localScale = Vector3.Scale(m_BodyScale, new Vector3(Mathf.Lerp(1f, m_BodySquash.x, body), Mathf.Lerp(1f, m_BodySquash.y, body), 1f));
		}
		if (m_Face != null)
		{
			m_Face.localPosition = m_FacePosition + (Vector3)(m_FaceDip * body);
		}
		if (m_Nose != null)
		{
			m_Nose.localPosition = m_NosePosition + (Vector3)(m_NoseShift * nose);
			m_Nose.localScale = Vector3.Scale(m_NoseScale, new Vector3(Mathf.Lerp(1f, m_NoseSquash.x, nose), Mathf.Lerp(1f, m_NoseSquash.y, nose), 1f));
		}
	}

	// 0 -> 1 at peak -> 0 with flat tangents at every key, like the rig's smooth keyframes.
	private static float Bump(float phase, float peak)
	{
		var x = phase < peak ? phase / peak : (1f - phase) / (1f - peak);
		return x * x * (3f - 2f * x);
	}
}
