using UnityEngine;

// Opens the equipment preset window (EquipmentPresetWindowUI).
//  - The ready-made button is Resources/EquipmentPresets/Button_EquipmentPresets: yellow round button with the sword and
//    shield icon. Drop it into any scene; it pins itself to a screen corner (m_Anchor + m_Offset) and, like the camp's own
//    buttons, swings out and stops taking taps while a window (anvil, cauldron, bird manager...) is open.
//  - Added to any other GameObject (no m_Trigger), it spawns that ready-made button and passes on its anchor settings.
public class EquipmentPresetButton : MonoBehaviour
{
	public const string PrefabResource = "EquipmentPresets/Button_EquipmentPresets";

	[Tooltip("The button's UIInputTrigger. Leave empty to spawn the ready-made button instead.")]
	public UIInputTrigger m_Trigger;

	[Tooltip("Plays BackButton_Enter / BackButton_Leave when the button shows / hides (the game's round-button swing).")]
	public Animation m_Animation;

	[Tooltip("Keep the button at m_Anchor + m_Offset on screen.")]
	public bool m_PinToScreen = true;

	[Tooltip("Swing out while a window is open. Off: stay put like the camp's world map button; windows cover it and take the taps.")]
	public bool m_HideWhenWindowOpen;

	[Tooltip("Screen corner/edge the button sits against.")]
	public TextAnchor m_Anchor = TextAnchor.LowerRight;

	[Tooltip("Gap between the screen edge and the button, in interface units (the screen is 720 tall).")]
	public Vector2 m_Offset = new Vector2(16f, 16f);

	// Radius of the button (Button_Round_Medium is 126 across).
	private const float ButtonRadius = 63f;

	private const int InterfaceLayer = 8;

	private const string EnterClip = "BackButton_Enter";

	private const string LeaveClip = "BackButton_Leave";

	private GameObject m_Spawned;

	private bool m_Shown;

	private void Start()
	{
		if (m_Trigger == null)
		{
			Spawn();
		}
		else if (m_Animation != null && m_Animation[LeaveClip] != null)
		{
			// Start tucked away; the first visible frame swings it in.
			m_Animation.Play(LeaveClip);
			m_Animation[LeaveClip].normalizedTime = 1f;
			m_Animation.Sample();
			m_Animation.Stop();
		}
	}

	private void OnEnable()
	{
		if (m_Trigger != null)
		{
			m_Trigger.Clicked -= OpenMenu;
			m_Trigger.Clicked += OpenMenu;
		}
		if (m_Spawned != null)
		{
			m_Spawned.SetActive(true);
		}
	}

	private void OnDisable()
	{
		if (m_Trigger != null)
		{
			m_Trigger.Clicked -= OpenMenu;
		}
		if (m_Spawned != null)
		{
			m_Spawned.SetActive(false);
		}
	}

	private void OnDestroy()
	{
		if (m_Spawned != null)
		{
			Destroy(m_Spawned);
		}
	}

	public void OpenMenu()
	{
		if (!m_Shown && m_Trigger != null && m_HideWhenWindowOpen)
		{
			return;
		}
		var coreStateMgr = DIContainerInfrastructure.GetCoreStateMgr();
		if (coreStateMgr != null && coreStateMgr.SceneLoadingMgr != null && coreStateMgr.SceneLoadingMgr.IsLoading())
		{
			return;
		}
		EquipmentPresetWindowUI.Open();
	}

	private void Spawn()
	{
		var prefab = Resources.Load<GameObject>(PrefabResource);
		if (prefab == null)
		{
			Debug.LogWarning("[EquipmentPresetButton] Missing Resources/" + PrefabResource + " (Tools > Equipment Presets > Rebuild Window Scene).");
			return;
		}
		m_Spawned = Instantiate(prefab);
		m_Spawned.name = prefab.name;
		var spawned = m_Spawned.GetComponent<EquipmentPresetButton>();
		if (spawned != null)
		{
			spawned.m_Anchor = m_Anchor;
			spawned.m_Offset = m_Offset;
			spawned.m_PinToScreen = true;
			spawned.m_HideWhenWindowOpen = m_HideWhenWindowOpen;
		}
	}

	private void LateUpdate()
	{
		if (m_Trigger == null)
		{
			return;
		}
		var coreStateMgr = DIContainerInfrastructure.GetCoreStateMgr();
		var windowRoot = coreStateMgr != null ? coreStateMgr.m_WindowRoot : null;
		var interfaceCamera = NGUITools.FindCameraForLayer(InterfaceLayer);
		if (m_PinToScreen && interfaceCamera != null && windowRoot != null)
		{
			transform.position = GetScreenPosition(interfaceCamera, windowRoot.transform.position.z);
		}
		var windowOpen = windowRoot != null && windowRoot.gameObject.activeInHierarchy;
		var loading = coreStateMgr != null && coreStateMgr.SceneLoadingMgr != null && coreStateMgr.SceneLoadingMgr.IsLoading();
		var show = DIContainerInfrastructure.GetCurrentPlayer() != null && (!m_HideWhenWindowOpen || (!loading && !EquipmentPresetWindowUI.IsOpen && !windowOpen));
		if (show != m_Shown)
		{
			SetShown(show);
		}
	}

	private void SetShown(bool show)
	{
		m_Shown = show;
		var tapArea = m_Trigger.GetComponent<Collider>();
		if (tapArea != null)
		{
			tapArea.enabled = show;
		}
		var clip = show ? EnterClip : LeaveClip;
		if (m_Animation != null && m_Animation[clip] != null)
		{
			m_Animation.Play(clip);
		}
		else
		{
			m_Trigger.gameObject.SetActive(show);
		}
	}

	// Interface objects sit at scale 1 under an orthographic camera, so one world unit is one interface unit.
	private Vector3 GetScreenPosition(Camera interfaceCamera, float z)
	{
		float x;
		float y;
		float dx;
		float dy;
		switch (m_Anchor)
		{
		case TextAnchor.UpperCenter:
		case TextAnchor.MiddleCenter:
		case TextAnchor.LowerCenter:
			x = 0.5f;
			dx = m_Offset.x;
			break;
		case TextAnchor.UpperRight:
		case TextAnchor.MiddleRight:
		case TextAnchor.LowerRight:
			x = 1f;
			dx = -(m_Offset.x + ButtonRadius);
			break;
		default:
			x = 0f;
			dx = m_Offset.x + ButtonRadius;
			break;
		}
		switch (m_Anchor)
		{
		case TextAnchor.MiddleLeft:
		case TextAnchor.MiddleCenter:
		case TextAnchor.MiddleRight:
			y = 0.5f;
			dy = -m_Offset.y;
			break;
		case TextAnchor.LowerLeft:
		case TextAnchor.LowerCenter:
		case TextAnchor.LowerRight:
			y = 0f;
			dy = m_Offset.y + ButtonRadius;
			break;
		default:
			y = 1f;
			dy = -(m_Offset.y + ButtonRadius);
			break;
		}
		var world = interfaceCamera.ViewportToWorldPoint(new Vector3(x, y, 0f));
		return new Vector3(world.x + dx, world.y + dy, z);
	}
}
