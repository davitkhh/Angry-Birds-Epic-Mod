using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.Shared.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The equipment preset window (scene Window_EquipmentPresets), opened from any EquipmentPresetButton.
// List screen: a scrolling list of wooden bars, each showing the birds wearing that preset, with equip / edit / delete.
// Edit screen: the bird lineup from battle preparation (tap to include / focus) and the equipment picker from the bird manager.
// Editing works on preview copies of the birds; nothing touches the real birds until a preset is equipped.
public class EquipmentPresetWindowUI : MonoBehaviour
{
	public const string SceneName = "Window_EquipmentPresets";

	private const int BackButtonDepth = 3;

	// Unused by every other camera, so bar birds are only drawn by the list's clip camera.
	private const int ListBirdLayer = 26;

	// Menu transitions: top bars slide in from above the screen, everything else from below.
	private const float TopSlide = 260f;

	private const float BottomSlide = 800f;

	private const float SlideInTime = 0.35f;

	private const float SlideOutTime = 0.22f;

	// Half the width of the round item slot body (Button_Round_Large, 178).
	private const float ItemSlotHalfWidth = 89f;

	[Header("Shared")]
	[SerializeField]
	private GameObject m_HeaderRoot;

	[SerializeField]
	private Animation m_HeaderAnimation;

	[SerializeField]
	private UILabel m_HeaderLabel;

	[SerializeField]
	private UIInputTrigger m_CloseButton;

	[Header("Preset list")]
	[SerializeField]
	private GameObject m_ListScreen;

	[SerializeField]
	private UIScrollView m_ListScrollView;

	[SerializeField]
	private UIGrid m_ListGrid;

	[SerializeField]
	private EquipmentPresetBar m_BarTemplate;

	[SerializeField]
	private UIInputTrigger m_CreateButton;

	[SerializeField]
	private UISprite m_CreateButtonBody;

	[SerializeField]
	private UILabel m_EmptyLabel;

	[SerializeField]
	private GameObject m_CampBirdPrefab;

	[SerializeField]
	private float m_BarBirdScale = 0.42f;

	[SerializeField]
	private float m_BarBirdSpacing = 100f;

	// How far the list's clip reaches up under the header plank; bar birds (drawn above the interface) stop below it.
	[SerializeField]
	private float m_ListBirdTopInset;

	[Header("Preset editor")]
	[SerializeField]
	private GameObject m_EditScreen;

	[SerializeField]
	private UIGrid m_LineupGrid;

	[SerializeField]
	private UiSortBehaviour m_LineupSorting;

	[SerializeField]
	private GameObject m_CharacterSlotPrefab;

	[SerializeField]
	private Transform m_FocusMarker;

	[SerializeField]
	private Transform m_TopLeftAnchor;

	// The category tabs; kept against the right edge of the screen.
	[SerializeField]
	private Transform m_TabsRoot;

	// Distance from the tabs' pivot to their right edge (outer tab's edge, in the tabs' own units) plus a margin.
	[SerializeField]
	private float m_TabsRightExtent = 262f;

	[SerializeField]
	private UIInputTrigger m_BackButton;

	[SerializeField]
	private UIInputTrigger m_SaveButton;

	[SerializeField]
	private UIInput m_NameInput;

	// The name's text; centred on its plank while it fits, otherwise right-aligned just left of the pencil.
	[SerializeField]
	private UILabel m_NameLabel;

	// Usable text span on the plank (plank-local x): from the left edge margin to just before the pencil.
	[SerializeField]
	private float m_NameTextLeft = -226f;

	[SerializeField]
	private float m_NameTextRight = 178f;

	[SerializeField]
	private float m_NamePlankWidth = 476f;

	// Slid in from the bottom together with the equipment panel.
	[SerializeField]
	private Transform m_LineupRoot;

	[SerializeField]
	private Transform m_CategoriesRoot;

	[SerializeField]
	private List<OpenInventoryButton> m_TabButtons = new List<OpenInventoryButton>();

	[SerializeField]
	private UIScrollView m_ItemScrollView;

	[SerializeField]
	private UIGrid m_ItemGrid;

	// Gap between the screen's left edge and the first equipment slot.
	[SerializeField]
	private float m_ItemLeftMargin = 13f;

	[SerializeField]
	private InventoryItemSlot m_ItemSlotPrefab;

	[SerializeField]
	private InventoryItemSlot m_ClassSlotPrefab;

	[SerializeField]
	private InventoryItemSlot m_EquipmentSlotPrefab;

	private static EquipmentPresetWindowUI s_Instance;

	private static bool s_Loading;

	private static Action s_PendingOnClosed;

	// Set when another window (e.g. battle preparation) opened this one: it keeps the window backdrop and gets called back.
	private Action m_OnClosed;

	private bool m_Hosted;

	private List<EquipmentPreset> m_Presets = new List<EquipmentPreset>();

	private readonly List<EquipmentPresetBar> m_Bars = new List<EquipmentPresetBar>();

	private EquipmentPresetClipCamera m_ClipCamera;

	private bool m_Leaving;

	private bool m_Transitioning;

	private Vector3 m_ListBase;

	private Vector3 m_LineupBase;

	private Vector3 m_CategoriesBase;

	private float m_TopOffset;

	private Vector3 m_ItemPanelStart;

	private Vector2 m_ItemClipStart;

	// Invisible widget at the screen's left edge so the scroll view's bounds keep that margin when it springs back.
	private Transform m_ItemSpacer;

	private string m_EditSignature;

	// Editor state. m_EditIndex is -1 while creating a new preset.
	private int m_EditIndex = -1;

	private List<BirdGameData> m_EditBirds;

	private readonly HashSet<string> m_Included = new HashSet<string>();

	private readonly List<BattlePrepCharacterButton> m_Lineup = new List<BattlePrepCharacterButton>();

	private BirdGameData m_FocusedBird;

	private InventoryItemType m_CurrentType = InventoryItemType.Class;

	private readonly List<InventoryItemSlot> m_ItemSlots = new List<InventoryItemSlot>();

	private InventoryItemSlot m_SelectedSlot;

	public static bool IsOpen
	{
		get
		{
			return s_Instance != null || s_Loading;
		}
	}

	private static bool InBattle
	{
		get
		{
			return ClientInfo.CurrentBattleGameData != null;
		}
	}

	public static void Open()
	{
		Open(null);
	}

	// onClosed: the caller already shows the window backdrop and wants to know when this window is gone.
	public static void Open(Action onClosed)
	{
		if (DIContainerInfrastructure.GetCurrentPlayer() == null)
		{
			if (onClosed != null)
			{
				onClosed();
			}
			return;
		}
		if (s_Instance != null)
		{
			s_Instance.m_OnClosed = onClosed;
			s_Instance.Enter();
			return;
		}
		if (s_Loading)
		{
			return;
		}
		s_Loading = true;
		s_PendingOnClosed = onClosed;
		DIContainerInfrastructure.GetCoreStateMgr().SceneLoadingMgr.AddUILevel(SceneName, delegate
		{
			s_Loading = false;
			if (s_Instance != null)
			{
				s_Instance.m_OnClosed = s_PendingOnClosed;
				s_Instance.Enter();
			}
			s_PendingOnClosed = null;
		});
	}

	// While a preset is being edited, item slots compare stats against the edited copy instead of the real bird.
	public static BirdGameData GetEditedBird(string birdNameId)
	{
		if (s_Instance == null || s_Instance.m_EditBirds == null)
		{
			return null;
		}
		return s_Instance.m_EditBirds.FirstOrDefault(b => b.BalancingData.NameId == birdNameId);
	}

	private void Awake()
	{
		s_Instance = this;
		base.transform.position += DIContainerInfrastructure.GetCoreStateMgr().m_WindowRoot.transform.position;
		m_BarTemplate.gameObject.SetActive(false);
		m_ListScreen.SetActive(false);
		m_EditScreen.SetActive(false);
		m_ListBase = m_ListScreen.transform.localPosition;
		m_LineupBase = m_LineupRoot.localPosition;
		m_CategoriesBase = m_CategoriesRoot.localPosition;
		m_ItemPanelStart = m_ItemScrollView.transform.localPosition;
		m_ItemClipStart = m_ItemScrollView.GetComponent<UIPanel>().clipOffset;
		var spacer = new GameObject("LeftMarginSpacer");
		spacer.layer = m_ItemScrollView.gameObject.layer;
		spacer.transform.parent = m_ItemScrollView.transform;
		spacer.transform.localScale = Vector3.one;
		var spacerWidget = spacer.AddComponent<UIWidget>();
		spacerWidget.width = 2;
		spacerWidget.height = 2;
		m_ItemSpacer = spacer.transform;

		var cameraObject = new GameObject("PresetBirdCamera");
		cameraObject.transform.parent = base.transform;
		cameraObject.AddComponent<Camera>();
		m_ClipCamera = cameraObject.AddComponent<EquipmentPresetClipCamera>();
		m_ClipCamera.m_Panel = m_ListScrollView.GetComponent<UIPanel>();
		m_ClipCamera.m_Layer = ListBirdLayer;
		m_ClipCamera.m_TopInset = m_ListBirdTopInset;
		cameraObject.SetActive(false);
	}

	private void OnDestroy()
	{
		DeRegisterEventHandlers();
		if (DIContainerInfrastructure.BackButtonMgr != null)
		{
			DIContainerInfrastructure.BackButtonMgr.DeRegisterAction(BackButtonDepth);
		}
		if (s_Instance == this)
		{
			s_Instance = null;
		}
	}

	private void Enter()
	{
		m_Leaving = false;
		base.gameObject.SetActive(true);
		m_Hosted = m_OnClosed != null;
		if (!m_Hosted)
		{
			DIContainerInfrastructure.GetCoreStateMgr().m_WindowRoot.Enter();
		}
		DIContainerInfrastructure.BackButtonMgr.RegisterAction(BackButtonDepth, HandleBackButton);
		RegisterEventHandlers();
		ShowList();
		StartCoroutine(EnterList());
	}

	private void RegisterEventHandlers()
	{
		DeRegisterEventHandlers();
		m_CloseButton.Clicked += Close;
		m_CreateButton.Clicked += OnCreateClicked;
		m_BackButton.Clicked += OnBackClicked;
		m_SaveButton.Clicked += OnSaveClicked;
		foreach (var tab in m_TabButtons)
		{
			tab.OnButtonClicked += OnTabClicked;
		}
	}

	private void DeRegisterEventHandlers()
	{
		if (m_CloseButton)
		{
			m_CloseButton.Clicked -= Close;
		}
		if (m_CreateButton)
		{
			m_CreateButton.Clicked -= OnCreateClicked;
		}
		if (m_BackButton)
		{
			m_BackButton.Clicked -= OnBackClicked;
		}
		if (m_SaveButton)
		{
			m_SaveButton.Clicked -= OnSaveClicked;
		}
		foreach (var tab in m_TabButtons)
		{
			if (tab)
			{
				tab.OnButtonClicked -= OnTabClicked;
			}
		}
	}

	private void HandleBackButton()
	{
		if (m_EditScreen.activeSelf)
		{
			OnBackClicked();
		}
		else
		{
			Close();
		}
	}

	public void Close()
	{
		if (m_Leaving || m_Transitioning)
		{
			return;
		}
		StartCoroutine(LeaveRoutine());
	}

	private IEnumerator LeaveRoutine()
	{
		m_Leaving = true;
		DeRegisterEventHandlers();
		DIContainerInfrastructure.BackButtonMgr.DeRegisterAction(BackButtonDepth);
		yield return StartCoroutine(m_EditScreen.activeSelf ? LeaveEdit() : LeaveList());
		m_ClipCamera.gameObject.SetActive(false);
		ClearEditor();
		ClearBars();
		if (!m_Hosted)
		{
			DIContainerInfrastructure.GetCoreStateMgr().m_WindowRoot.Leave();
		}
		var onClosed = m_OnClosed;
		m_OnClosed = null;
		SceneManager.UnloadSceneAsync(base.gameObject.scene);
		if (onClosed != null)
		{
			onClosed();
		}
	}

	private void LateUpdate()
	{
		// Keep the edit screen's top-left cluster pinned to the screen corner (next to the level icon) on any aspect ratio.
		if (m_TopLeftAnchor != null && m_EditScreen.activeSelf)
		{
			var interfaceCamera = NGUITools.FindCameraForLayer(base.gameObject.layer);
			if (interfaceCamera != null)
			{
				var corner = m_TopLeftAnchor.parent.InverseTransformPoint(interfaceCamera.ViewportToWorldPoint(new Vector3(0f, 1f, 0f)));
				m_TopLeftAnchor.localPosition = new Vector3(corner.x, corner.y + m_TopOffset, m_TopLeftAnchor.localPosition.z);
			}
		}
		if (m_TabsRoot != null && m_EditScreen.activeSelf)
		{
			var interfaceCamera = NGUITools.FindCameraForLayer(base.gameObject.layer);
			if (interfaceCamera != null)
			{
				var edge = m_TabsRoot.parent.InverseTransformPoint(interfaceCamera.ViewportToWorldPoint(new Vector3(1f, 0f, 0f)));
				m_TabsRoot.localPosition = new Vector3(edge.x - m_TabsRightExtent * m_TabsRoot.localScale.x, m_TabsRoot.localPosition.y, m_TabsRoot.localPosition.z);
			}
		}
		LayoutNameText();
		if (m_FocusMarker != null && m_FocusedBird != null)
		{
			var focused = m_Lineup.FirstOrDefault(b => b.GetBirdGameData() == m_FocusedBird);
			if (focused != null)
			{
				var position = m_FocusMarker.parent.InverseTransformPoint(focused.transform.position);
				m_FocusMarker.localPosition = new Vector3(position.x, m_FocusMarker.localPosition.y, m_FocusMarker.localPosition.z);
			}
		}
	}

	private void LayoutNameText()
	{
		if (m_NameLabel == null || !m_EditScreen.activeSelf)
		{
			return;
		}
		var available = m_NameTextRight - m_NameTextLeft;
		var width = m_NameLabel.printedSize.x;
		// Very long names shrink to fit the span instead of running under the pencil.
		var scale = width > available ? available / width : 1f;
		m_NameLabel.transform.localScale = new Vector3(scale, scale, 1f);
		var centred = width * scale * 0.5f <= m_NameTextRight;
		m_NameLabel.pivot = centred ? UIWidget.Pivot.Center : UIWidget.Pivot.Right;
		var labelTransform = m_NameLabel.transform;
		labelTransform.localPosition = new Vector3(centred ? 0f : m_NameTextRight, labelTransform.localPosition.y, labelTransform.localPosition.z);
		// Keep the tap area on the whole plank wherever the text moved.
		var tapArea = m_NameLabel.GetComponent<BoxCollider>();
		if (tapArea != null)
		{
			tapArea.center = new Vector3(-labelTransform.localPosition.x / scale, 0f, 0f);
			tapArea.size = new Vector3(m_NamePlankWidth / scale, tapArea.size.y, tapArea.size.z);
		}
	}

	// ---------------------------------------------------------------- Transitions

	private static void SetOffset(Transform target, Vector3 basePosition, float offset)
	{
		target.localPosition = basePosition + new Vector3(0f, offset, 0f);
	}

	// Enter eases out with a small overshoot like the game's own window animations; leave accelerates away.
	private static float Ease(float t, bool enter)
	{
		if (!enter)
		{
			return t * t;
		}
		const float overshoot = 1.4f;
		t -= 1f;
		return t * t * ((overshoot + 1f) * t + overshoot) + 1f;
	}

	private IEnumerator Tween(float duration, bool enter, Action<float> apply)
	{
		for (var time = 0f; time < duration; time += Time.unscaledDeltaTime)
		{
			apply(Ease(time / duration, enter));
			yield return null;
		}
		apply(1f);
	}

	private void ApplyListOffset(float offset)
	{
		SetOffset(m_ListScreen.transform, m_ListBase, offset);
	}

	private void ApplyEditOffsets(float top, float bottom)
	{
		m_TopOffset = top;
		SetOffset(m_LineupRoot, m_LineupBase, bottom);
		SetOffset(m_CategoriesRoot, m_CategoriesBase, bottom);
	}

	private IEnumerator EnterList()
	{
		m_Transitioning = true;
		ApplyListOffset(-BottomSlide);
		PlayAnimation(m_HeaderAnimation, "Header_Enter");
		yield return StartCoroutine(Tween(SlideInTime, true, e => ApplyListOffset(-BottomSlide * (1f - e))));
		m_Transitioning = false;
	}

	private IEnumerator LeaveList()
	{
		m_Transitioning = true;
		var headerTime = PlayAnimation(m_HeaderAnimation, "Header_Leave");
		yield return StartCoroutine(Tween(SlideOutTime, false, e => ApplyListOffset(-BottomSlide * e)));
		if (headerTime > SlideOutTime)
		{
			yield return new WaitForSeconds(headerTime - SlideOutTime);
		}
	}

	private IEnumerator EnterEdit()
	{
		m_Transitioning = true;
		yield return StartCoroutine(Tween(SlideInTime, true, e => ApplyEditOffsets(TopSlide * (1f - e), -BottomSlide * (1f - e))));
		m_Transitioning = false;
	}

	private IEnumerator LeaveEdit()
	{
		m_Transitioning = true;
		yield return StartCoroutine(Tween(SlideOutTime, false, e => ApplyEditOffsets(TopSlide * e, -BottomSlide * e)));
	}

	private IEnumerator SwitchToEditor(int index)
	{
		yield return StartCoroutine(LeaveList());
		ShowEditor(index);
		yield return StartCoroutine(EnterEdit());
	}

	private IEnumerator SwitchToList()
	{
		yield return StartCoroutine(LeaveEdit());
		ShowList();
		yield return StartCoroutine(EnterList());
	}

	private void OpenEditor(int index)
	{
		if (!m_Transitioning && !m_Leaving)
		{
			StartCoroutine(SwitchToEditor(index));
		}
	}

	private static float PlayAnimation(Animation animation, string clip)
	{
		if (animation == null || animation[clip] == null)
		{
			return 0f;
		}
		animation.Play(clip);
		return animation[clip].length;
	}

	// ---------------------------------------------------------------- Preset list

	private void ShowList()
	{
		ClearEditor();
		m_EditScreen.SetActive(false);
		m_HeaderRoot.SetActive(true);
		m_ListScreen.SetActive(true);
		m_HeaderLabel.text = "Equipment Presets";
		m_Presets = EquipmentPresets.Load();
		RebuildBars();
		var canCreate = m_Presets.Count < EquipmentPresets.MaxPresets;
		m_CreateButton.GetComponent<Collider>().enabled = canCreate;
		if (m_CreateButtonBody)
		{
			m_CreateButtonBody.color = canCreate ? Color.white : new Color(0.5f, 0.5f, 0.5f);
		}
		m_ClipCamera.gameObject.SetActive(true);
	}

	private void RebuildBars()
	{
		ClearBars();
		for (var i = 0; i < m_Presets.Count; i++)
		{
			m_Bars.Add(CreateBar(m_Presets[i]));
		}
		if (m_EmptyLabel)
		{
			m_EmptyLabel.gameObject.SetActive(m_Presets.Count == 0);
		}
		StartCoroutine(RepositionList());
	}

	private IEnumerator RepositionList()
	{
		m_ListGrid.Reposition();
		yield return null;
		m_ListGrid.Reposition();
		m_ListScrollView.ResetPosition();
	}

	private EquipmentPresetBar CreateBar(EquipmentPreset preset)
	{
		var bar = Instantiate(m_BarTemplate);
		bar.name = "Bar_" + m_Bars.Count.ToString("00");
		bar.transform.parent = m_ListGrid.transform;
		bar.transform.localPosition = Vector3.zero;
		bar.transform.localScale = Vector3.one;
		bar.gameObject.SetActive(true);
		bar.m_NameLabel.text = preset.Name;
		if (bar.m_StatusLabel)
		{
			bar.m_StatusLabel.gameObject.SetActive(false);
		}
		if (bar.m_MissingMarker)
		{
			bar.m_MissingMarker.SetActive(EquipmentPresets.CountMissing(preset) > 0);
		}
		if (bar.m_EquipButtonBody)
		{
			bar.m_EquipButtonBody.color = InBattle ? new Color(0.5f, 0.5f, 0.5f) : Color.white;
		}

		var index = 0;
		foreach (var entry in preset.Birds)
		{
			var bird = EquipmentPresets.FindBird(entry.BirdNameId);
			if (bird == null)
			{
				continue;
			}
			var controller = SpawnBird(bar.m_BirdRoot, EquipmentPresets.CreatePreviewBird(bird, entry), ListBirdLayer);
			controller.transform.localPosition = new Vector3(index * m_BarBirdSpacing, 0f, -index);
			controller.transform.localScale = Vector3.one * m_BarBirdScale;
			bar.m_Birds.Add(controller);
			index++;
		}

		bar.m_EquipButton.Clicked += delegate
		{
			OnEquipClicked(preset, bar);
		};
		bar.m_EditButton.Clicked += delegate
		{
			OpenEditor(m_Presets.IndexOf(preset));
		};
		bar.m_DeleteButton.Clicked += delegate
		{
			OnDeleteClicked(preset);
		};
		return bar;
	}

	private CharacterControllerCamp SpawnBird(Transform parent, BirdGameData bird, int layer)
	{
		var go = Instantiate(m_CampBirdPrefab);
		go.transform.parent = parent;
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		var controller = go.GetComponent<CharacterControllerCamp>();
		controller.SetModel(bird);
		foreach (var scaleController in go.GetComponentsInChildren<ScaleController>())
		{
			if (ScaleMgr.Instance != null)
			{
				ScaleMgr.Instance.RemoveScaleController(scaleController);
			}
			Destroy(scaleController);
		}
		foreach (var skinned in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
		{
			skinned.updateWhenOffscreen = true;
		}
		foreach (var collider in go.GetComponentsInChildren<Collider>())
		{
			collider.enabled = false;
		}
		SetLayerRecursively(go, layer);
		return controller;
	}

	private static void SetLayerRecursively(GameObject go, int layer)
	{
		go.layer = layer;
		foreach (Transform child in go.transform)
		{
			SetLayerRecursively(child.gameObject, layer);
		}
	}

	private void ClearBars()
	{
		foreach (var bar in m_Bars)
		{
			if (bar != null)
			{
				Destroy(bar.gameObject);
			}
		}
		m_Bars.Clear();
	}

	private void OnEquipClicked(EquipmentPreset preset, EquipmentPresetBar bar)
	{
		if (InBattle)
		{
			bar.ShowStatus("Can't change gear in battle");
			return;
		}
		var missing = EquipmentPresets.Apply(preset);
		bar.ShowStatus(missing == 0 ? "Equipped!" : "Equipped - " + missing + " missing item(s) skipped");
		foreach (var bird in bar.m_Birds)
		{
			bird.PlayCheerCharacter();
		}
	}

	private void OnDeleteClicked(EquipmentPreset preset)
	{
		// The popup is drawn by the interface camera, so hide the bar birds that would otherwise sit on top of it.
		m_ClipCamera.gameObject.SetActive(false);
		DIContainerInfrastructure.GetCoreStateMgr().ShowConfirmationPopup("Delete the preset \"" + preset.Name + "\"?", delegate
		{
			m_Presets.Remove(preset);
			EquipmentPresets.Save(m_Presets);
			ShowList();
		}, delegate
		{
			m_ClipCamera.gameObject.SetActive(m_ListScreen.activeSelf);
		});
	}

	private void OnCreateClicked()
	{
		if (m_Presets.Count < EquipmentPresets.MaxPresets)
		{
			OpenEditor(-1);
		}
	}

	// ---------------------------------------------------------------- Preset editor

	private void ShowEditor(int index)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		m_EditIndex = index;
		var preset = index >= 0 && index < m_Presets.Count ? m_Presets[index] : null;

		ClearBars();
		m_ClipCamera.gameObject.SetActive(false);
		m_ListScreen.SetActive(false);
		m_HeaderRoot.SetActive(false);
		ApplyEditOffsets(TopSlide, -BottomSlide);
		m_EditScreen.SetActive(true);

		m_EditBirds = player.Birds.Select(b => EquipmentPresets.CreatePreviewBird(b, EquipmentPresets.GetEntry(preset, b.BalancingData.NameId))).ToList();
		m_Included.Clear();
		foreach (var bird in m_EditBirds)
		{
			if (preset == null || EquipmentPresets.GetEntry(preset, bird.BalancingData.NameId) != null)
			{
				m_Included.Add(bird.BalancingData.NameId);
			}
		}
		if (m_Included.Count == 0)
		{
			foreach (var bird in m_EditBirds)
			{
				m_Included.Add(bird.BalancingData.NameId);
			}
		}
		m_NameInput.characterLimit = EquipmentPresets.MaxNameLength;
		m_NameInput.value = preset != null ? preset.Name : "Preset " + (m_Presets.Count + 1);

		foreach (var bird in m_EditBirds)
		{
			var slot = Instantiate(m_CharacterSlotPrefab);
			slot.transform.parent = m_LineupGrid.transform;
			slot.transform.localPosition = Vector3.zero;
			slot.transform.localScale = Vector3.one;
			var button = slot.GetComponent<BattlePrepCharacterButton>();
			button.InitExternal(bird);
			button.Tapped += OnLineupTapped;
			button.Select(m_Included.Contains(bird.BalancingData.NameId), true);
			m_Lineup.Add(button);
		}
		StartCoroutine(RepositionLineup());
		m_EditSignature = EditSignature();

		Focus(m_EditBirds.FirstOrDefault(b => m_Included.Contains(b.BalancingData.NameId)));
		foreach (var tab in m_TabButtons)
		{
			tab.Activate(true);
			if (tab.m_ItemType == m_CurrentType)
			{
				tab.Select();
			}
		}
	}

	private IEnumerator RepositionLineup()
	{
		m_LineupGrid.Reposition();
		yield return null;
		m_LineupGrid.Reposition();
		for (var i = 0; i < m_Lineup.Count; i++)
		{
			m_Lineup[i].transform.localPosition += new Vector3(0f, 0f, i);
		}
		if (m_LineupSorting)
		{
			m_LineupSorting.ClearAndAutoSearchRenderers();
		}
	}

	private void ClearEditor()
	{
		foreach (var button in m_Lineup)
		{
			if (button != null)
			{
				button.Tapped -= OnLineupTapped;
				Destroy(button.gameObject);
			}
		}
		m_Lineup.Clear();
		ClearItemSlots();
		foreach (var tab in m_TabButtons)
		{
			if (tab)
			{
				tab.Activate(false);
			}
		}
		m_EditBirds = null;
		m_FocusedBird = null;
		m_Included.Clear();
	}

	// Tap an excluded bird to add it; tap an included bird to show its gear; tap the bird already shown to drop it.
	private void OnLineupTapped(BattlePrepCharacterButton button)
	{
		var bird = button.GetBirdGameData();
		var id = bird.BalancingData.NameId;
		if (!m_Included.Contains(id))
		{
			m_Included.Add(id);
			button.Select(true, true);
			button.PlaySelectionReaction(true);
			Focus(bird);
		}
		else if (m_FocusedBird != bird)
		{
			Focus(bird);
		}
		else if (m_Included.Count > 1)
		{
			m_Included.Remove(id);
			button.Select(false, true);
			button.PlaySelectionReaction(false);
			Focus(m_EditBirds.FirstOrDefault(b => m_Included.Contains(b.BalancingData.NameId)));
		}
	}

	private void Focus(BirdGameData bird)
	{
		m_FocusedBird = bird;
		if (m_FocusMarker)
		{
			m_FocusMarker.gameObject.SetActive(bird != null);
		}
		RefreshItemList();
	}

	private void OnTabClicked(InventoryItemType type)
	{
		m_CurrentType = type;
		RefreshItemList();
	}

	private void ClearItemSlots()
	{
		foreach (var slot in m_ItemSlots)
		{
			if (slot != null)
			{
				slot.OnUsed -= OnSlotUsed;
				Destroy(slot.gameObject);
			}
		}
		m_ItemSlots.Clear();
		m_SelectedSlot = null;
	}

	private void RefreshItemList()
	{
		ClearItemSlots();
		if (m_FocusedBird == null)
		{
			return;
		}
		List<IInventoryItemGameData> owned;
		if (!DIContainerInfrastructure.GetCurrentPlayer().InventoryGameData.Items.TryGetValue(m_CurrentType, out owned))
		{
			return;
		}
		var items = owned.Where(i => i.IsValidForBird(m_FocusedBird));
		items = m_CurrentType == InventoryItemType.Class ? items.OrderBy(i => i.ItemBalancing.SortPriority) : items.OrderByDescending(i => i.ItemMainStat);
		var index = 0;
		foreach (var item in items)
		{
			var prefab = m_CurrentType == InventoryItemType.Class ? m_ClassSlotPrefab : (m_CurrentType == InventoryItemType.MainHandEquipment || m_CurrentType == InventoryItemType.OffHandEquipment ? m_EquipmentSlotPrefab : m_ItemSlotPrefab);
			var slot = Instantiate(prefab);
			// Same naming as BirdWindowUI.SetItemListContent, which the grid uses for order.
			slot.name = (++index).ToString("000") + item.ItemBalancing.SortPriority.ToString("00") + "_" + slot.name;
			slot.transform.parent = m_ItemGrid.transform;
			slot.transform.localPosition = Vector3.zero;
			slot.transform.localScale = Vector3.one;
			slot.SetModel(item, false);
			slot.OnUsed += OnSlotUsed;
			m_ItemSlots.Add(slot);
			if (m_SelectedSlot == null && EquipmentPresets.IsEquipped(m_FocusedBird, m_CurrentType, EquipmentPresets.ItemRef(item)))
			{
				m_SelectedSlot = slot;
			}
		}
		if (m_SelectedSlot != null)
		{
			m_SelectedSlot.Select();
		}
		foreach (var slot in m_ItemSlots)
		{
			slot.RefreshStat();
		}
		StartCoroutine(RepositionItems());
	}

	// Always start the strip at the same spot: put the scroll panel back exactly where the scene had it.
	private IEnumerator RepositionItems()
	{
		RestoreItemPanel();
		// Like BirdWindowUI.RestorePosition: wait a frame so the destroyed slots are gone before laying out the new ones.
		yield return null;
		m_ItemGrid.Reposition();
		RestoreItemPanel();
		AnchorItemStrip();
	}

	// Stretch the strip's clip to the full screen width (no side fade) and start the first slot m_ItemLeftMargin in.
	private void AnchorItemStrip()
	{
		var interfaceCamera = NGUITools.FindCameraForLayer(base.gameObject.layer);
		if (interfaceCamera == null || m_ItemSlots.Count == 0)
		{
			return;
		}
		var panel = m_ItemScrollView.GetComponent<UIPanel>();
		var panelTransform = panel.transform;
		var left = panelTransform.InverseTransformPoint(interfaceCamera.ViewportToWorldPoint(new Vector3(0f, 0.5f, 0f))).x;
		var right = panelTransform.InverseTransformPoint(interfaceCamera.ViewportToWorldPoint(new Vector3(1f, 0.5f, 0f))).x;
		var clip = panel.baseClipRegion;
		panel.baseClipRegion = new Vector4((left + right) * 0.5f - panel.clipOffset.x, clip.y, right - left, clip.w);
		panel.clipSoftness = new Vector2(0f, panel.clipSoftness.y);
		// Measured from the slot's own center: its visible parts (enchantment badge, set icon, new marker) differ per item,
		// so their bounds would shift the strip depending on which item happens to come first.
		// The grid (top-left pivot) lays the first slot out at its own origin. Slots glide there over a few frames like in
		// the bird manager, so place the grid from that layout rather than from where the slots happen to be mid-glide.
		var gridTransform = m_ItemGrid.transform;
		gridTransform.localPosition = new Vector3(left + m_ItemLeftMargin + ItemSlotHalfWidth, gridTransform.localPosition.y, gridTransform.localPosition.z);
		m_ItemSpacer.localPosition = new Vector3(left + 1f, gridTransform.localPosition.y, 0f);
		m_ItemScrollView.InvalidateBounds();
	}

	private void RestoreItemPanel()
	{
		m_ItemScrollView.DisableSpring();
		m_ItemScrollView.currentMomentum = Vector3.zero;
		m_ItemScrollView.transform.localPosition = m_ItemPanelStart;
		m_ItemScrollView.GetComponent<UIPanel>().clipOffset = m_ItemClipStart;
	}

	private void OnSlotUsed(InventoryItemSlot slot)
	{
		var item = slot.GetModel();
		if (m_FocusedBird == null || item == null || EquipmentPresets.IsEquipped(m_FocusedBird, m_CurrentType, EquipmentPresets.ItemRef(item)))
		{
			return;
		}
		EquipmentPresets.EquipPreview(m_FocusedBird, m_CurrentType, item);
		if (m_SelectedSlot != null)
		{
			m_SelectedSlot.Deselect();
		}
		m_SelectedSlot = slot;
		slot.Select();
		foreach (var other in m_ItemSlots)
		{
			other.RefreshStat();
		}
	}

	private void OnBackClicked()
	{
		if (m_Transitioning || m_Leaving)
		{
			return;
		}
		if (EditSignature() == m_EditSignature)
		{
			StartCoroutine(SwitchToList());
			return;
		}
		// Leaving without saving throws the edited copies away, so ask first.
		DIContainerInfrastructure.GetCoreStateMgr().ShowConfirmationPopup("Discard your changes to this preset?", delegate
		{
			if (!m_Transitioning && !m_Leaving && m_EditScreen.activeSelf)
			{
				StartCoroutine(SwitchToList());
			}
		}, delegate
		{
		});
	}

	// Everything the editor can change: name, which birds are in, and what each included bird wears.
	private string EditSignature()
	{
		if (m_EditBirds == null)
		{
			return string.Empty;
		}
		var birds = m_EditBirds.Where(b => m_Included.Contains(b.BalancingData.NameId)).Select(b => b.BalancingData.NameId + "=" + string.Join(",", EquipmentPresets.Slots.Select(t => EquipmentPresets.ItemRef(GetEquipped(b, t))).ToArray()));
		return EquipmentPresets.SanitizeName(m_NameInput.value) + "|" + string.Join(";", birds.ToArray());
	}

	private static IInventoryItemGameData GetEquipped(BirdGameData bird, InventoryItemType type)
	{
		List<IInventoryItemGameData> items;
		return bird.InventoryGameData.Items.TryGetValue(type, out items) ? items.FirstOrDefault() : null;
	}

	private void OnSaveClicked()
	{
		if (m_Transitioning || m_Leaving)
		{
			return;
		}
		var birds = m_EditBirds.Where(b => m_Included.Contains(b.BalancingData.NameId)).ToList();
		if (birds.Count == 0)
		{
			return;
		}
		var preset = EquipmentPresets.Capture(m_NameInput.value, birds);
		if (m_EditIndex >= 0 && m_EditIndex < m_Presets.Count)
		{
			m_Presets[m_EditIndex] = preset;
		}
		else
		{
			m_Presets.Add(preset);
		}
		EquipmentPresets.Save(m_Presets);
		StartCoroutine(SwitchToList());
	}
}
