using SmoothMoves;
using UnityEngine;

// A permanent golden pig cloud on the world map (hotspot_golden_pig_cloud_battleground): a normal battle hotspot (state,
// unlock via the hotspot graph, flags) dressed as the daily golden pig cloud. The birds reach it by jumping from the
// pirate hotspot by the pig prison (Path_GoldenPigCloud, see GoldenPigCloudJumpPath).
// While the level is unbeaten the pig laughs every few seconds as long as it is fully on screen; once beaten it holds the
// first frame of its "New Clip" pose (built into the scene rigs with SmoothMovesClipBuilder). Tapping the unbeaten pig
// startles it.
public class HotSpotWorldMapViewGoldenPigCloud : HotSpotWorldMapViewBattle
{
	private const string LaughAnimation = "Cheer";

	private const string ClickedAnimation = "Surprised";

	// Keeps the next laugh from cutting off the surprised reaction.
	private const float LaughPauseAfterClick = 2f;

	private const string BeatenPoseAnimation = "New Clip";

	private const string IdleAnimation = "Idle";

	// The pig sitting on the cloud; its model is set at runtime, like the daily cloud's.
	[SerializeField]
	private CharacterAssetController m_CloudGoldenPig;

	[SerializeField]
	private float m_FirstLaughDelay = 1f;

	[SerializeField]
	private float m_LaughIntervalMin = 4f;

	[SerializeField]
	private float m_LaughIntervalMax = 7f;

	private bool m_PigReady;

	private BoneAnimation m_PigAnimation;

	private Renderer m_PigRenderer;

	private bool m_PigOnScreen;

	private float m_NextLaughTime;

	private bool m_PigPosed;

	protected override void InitialSetupHotspot()
	{
		base.InitialSetupHotspot();
		TrySetUpGoldenPig();
	}

	private void OnEnable()
	{
		TrySetUpGoldenPig();
	}

	public override void HandleMouseButtonUp(bool directMove = false)
	{
		base.HandleMouseButtonUp(directMove);
		if (base.Model == null || base.Model.IsCompleted() || !FindPig() || !IsPointerOnPig() || !HasClip(ClickedAnimation))
		{
			return;
		}
		m_PigAnimation.Play(ClickedAnimation);
		m_PigAnimation.PlayQueued(IdleAnimation);
		m_NextLaughTime = Mathf.Max(m_NextLaughTime, Time.time + LaughPauseAfterClick);
	}

	// The hotspot's click collider also covers the cloud and the flag; only react to taps on the pig itself.
	private bool IsPointerOnPig()
	{
		var camera = DIContainerInfrastructure.LocationStateMgr != null ? DIContainerInfrastructure.LocationStateMgr.SceneryCamera : null;
		if (camera == null || m_PigRenderer == null)
		{
			return false;
		}
		var bounds = m_PigRenderer.bounds;
		var point = camera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, bounds.center.z - camera.transform.position.z));
		return point.x >= bounds.min.x && point.x <= bounds.max.x && point.y >= bounds.min.y && point.y <= bounds.max.y;
	}

	private void Update()
	{
		if (base.Model == null || !FindPig())
		{
			return;
		}
		if (base.Model.IsCompleted())
		{
			if (!m_PigPosed)
			{
				m_PigPosed = true;
				HoldBeatenPose();
			}
			return;
		}
		if (m_PigPosed)
		{
			m_PigPosed = false;
			m_PigAnimation.Play(IdleAnimation);
		}
		var onScreen = IsFullyOnScreen(m_PigRenderer);
		if (onScreen && !m_PigOnScreen)
		{
			m_NextLaughTime = Time.time + m_FirstLaughDelay;
		}
		m_PigOnScreen = onScreen;
		if (onScreen && Time.time >= m_NextLaughTime)
		{
			m_NextLaughTime = Time.time + Random.Range(m_LaughIntervalMin, m_LaughIntervalMax);
			if (HasClip(LaughAnimation))
			{
				m_PigAnimation.Play(LaughAnimation);
				m_PigAnimation.PlayQueued(IdleAnimation);
			}
		}
	}

	private bool FindPig()
	{
		if (m_PigAnimation == null)
		{
			m_PigAnimation = GetComponentInChildren<BoneAnimation>();
			m_PigRenderer = m_PigAnimation != null ? m_PigAnimation.GetComponent<Renderer>() : null;
		}
		return m_PigAnimation != null;
	}

	// Stops on the pose's first frame.
	private void HoldBeatenPose()
	{
		if (!HasClip(BeatenPoseAnimation))
		{
			DebugLog.Warn(GetType(), "golden pig cloud: the pig has no '" + BeatenPoseAnimation + "' clip; build it with SmoothMovesClipBuilder");
			return;
		}
		m_PigAnimation.Play(BeatenPoseAnimation);
		var state = m_PigAnimation[BeatenPoseAnimation];
		state.time = 0f;
		state.speed = 0f;
	}

	// SmoothMoves' indexer logs on a missing clip, so check the legacy Animation instead.
	private bool HasClip(string animationName)
	{
		var legacy = m_PigAnimation.GetComponent<Animation>();
		return legacy != null && legacy.GetClip(animationName) != null;
	}

	private void TrySetUpGoldenPig()
	{
		if (!m_PigReady && m_CloudGoldenPig != null && base.Model != null && base.isActiveAndEnabled)
		{
			StartCoroutine(SetUpGoldenPig());
		}
	}

	// This runs from WorldMapStateMgr.Awake, before the pig rig has built its mesh (SetModel then threw and aborted the
	// whole world map setup). Wait a frame, and never let the decoration break the map.
	private System.Collections.IEnumerator SetUpGoldenPig()
	{
		yield return null;
		try
		{
			m_CloudGoldenPig.SetModel("pig_golden_pig", true, true, true);
			m_PigReady = true;
		}
		catch (System.Exception e)
		{
			DebugLog.Error(GetType(), "golden pig cloud: could not set up the pig: " + e);
		}
	}
}
