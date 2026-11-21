using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// One bird's unlock tutorial (see BirdUnlockSequence).
public class BirdUnlockDefinition
{
	public string BirdNameId;

	// Battle (BattleBalancingData) fought on the expanded popup; its id must start with BirdUnlockSequence.BattlePrefix.
	public string BattleNameId;

	// Clip under Resources/Sound/Music played instead of the popup jingle until the whole sequence ends.
	public string ThemeClip;

	// The ordered tutorial steps; each one waits for its wave (0-based) and for the steps before it.
	public List<BirdUnlockStep> Steps = new List<BirdUnlockStep>();

	// A wave's pigs were placed (offscreen, before they walk in).
	public Action<BirdUnlockSequence, BattleMgr, int> OnWavePigsPlaced;

	// A wave's pigs have walked in; before the birds' first turn. The last wave's is where the pigs make the bird mad.
	public Func<BirdUnlockSequence, BattleMgr, int, IEnumerator> OnWaveReady;

	// The bird's rage has started (the jump towards the pigs).
	public Action<BirdUnlockSequence, BattleMgr> OnBirdRageMove;

	// After the last pig fell: the bird does something, then cheers. Returns once the cheer has started.
	public Func<BirdUnlockSequence, BattleMgr, IEnumerator> Celebrate;
}

public enum BirdUnlockStepType
{
	Support,
	Attack,
	Rage
}

public class BirdUnlockStep
{
	public BirdUnlockStepType Type;

	public int Wave;

	public bool Done;
}

// Bird unlock sequence: when the yellow "bird unlocked" popup is confirmed, its yellow background expands into the
// background of a small battle fought on top of the darkened world map. The battle scene is loaded additively (its
// scenery switched off, the whole thing moved far away from the world map's content); a backdrop camera draws the
// dark overlay, the yellow sunburst and the white slot, and the battle camera is scissored to the yellow box so the
// combatants are only seen inside it. The battle runs on the normal BattleMgr with hooks for the scripted parts
// (BattleMgr.m_BirdUnlock). At the end the box closes like the popup does and the bird plays out its last clip
// over the world map before everything is unloaded.
[DefaultExecutionOrder(10000)]
public class BirdUnlockSequence : MonoBehaviour
{
	public const string BattlePrefix = "battle_birdunlock_";

	private static readonly Dictionary<string, Func<BirdUnlockDefinition>> s_Definitions = new Dictionary<string, Func<BirdUnlockDefinition>>
	{
		{ "bird_black", BombUnlock.Create }
	};

	public static BirdUnlockSequence Active { get; private set; }

	public static bool IsUnlockBattle(string battleNameId)
	{
		return battleNameId != null && battleNameId.StartsWith(BattlePrefix);
	}

	// The battle's camera (for code that looks up "the" SceneryCamera): while a sequence runs, the world map's camera is
	// tagged SceneryCamera too and would come first.
	public static Camera SceneryCamera()
	{
		var active = Active;
		if (active != null && active.BattleCamera != null)
		{
			return active.BattleCamera;
		}
		return Camera.allCameras.FirstOrDefault(c => c.CompareTag("SceneryCamera"));
	}

	private static readonly Dictionary<ICombatant, ICombatant> s_StatsStandIns = new Dictionary<ICombatant, ICombatant>();

	// The tutorial pigs (<pig>_birdunlock, made weak so the tutorial always goes the same way) show their <pig>_bird3
	// variant's stats in their tap-and-hold info: a display-only combatant built from it, or null for any other combatant.
	public static ICombatant StatsStandIn(ICombatant combatant)
	{
		if (combatant == null || !(combatant is PigCombatant) || combatant.CombatantNameId == null || !combatant.CombatantNameId.EndsWith("_birdunlock"))
		{
			return null;
		}
		ICombatant standIn;
		if (s_StatsStandIns.TryGetValue(combatant, out standIn))
		{
			return standIn;
		}
		var nameId = combatant.CombatantNameId.Substring(0, combatant.CombatantNameId.Length - "_birdunlock".Length) + "_bird3";
		var battle = ClientInfo.CurrentBattleGameData;
		if (battle == null || DIContainerBalancing.Service.GetBalancingData<PigBalancingData>(nameId) == null)
		{
			return null;
		}
		standIn = new PigCombatant(new PigGameData(nameId).SetDifficulties(battle.GetPlayerLevelForHotSpot(), battle.Balancing));
		if (s_StatsStandIns.Count > 32)
		{
			s_StatsStandIns.Clear();
		}
		s_StatsStandIns[combatant] = standIn;
		return standIn;
	}

	public static bool HasDefinition(string birdNameId)
	{
		return birdNameId != null && s_Definitions.ContainsKey(birdNameId);
	}

	// Popup layout (Popup_FeatureUnlocked.unity).
	private const string ColumnPath = "1_Body/Column/Animation";
	private const string OverlayPath = "1_Body/Overlay";
	private const string FeaturePath = "2_Content/2_Feature";
	private const string SlotPath = "2_Content/2_Feature/Animation/Slot";
	private const string ContentPath = "2_Content";

	// Where the battle is moved to, away from anything on the world map (the cameras share one world).
	private static readonly Vector3 BattleOffset = new Vector3(0f, 50000f, 0f);

	private static readonly Vector3 BackdropOrigin = new Vector3(0f, -60000f, 0f);

	private const float ExpandDuration = 1.2f;

	private const float OverlayAlpha = 0.75f;

	// Space left around the battle inside the box (battle units) and the least dark margin kept on each side (pixels).
	private const float BoxPadding = 50f;

	private const float MinScreenMargin = 24f;

	// The rays' light and dark yellow trade places every few seconds, cross-fading slowly.
	private const float ColorSwapInterval = 4f;

	private const float ColorFadeTime = 1.6f;

	// The tap-and-hold descriptions of the bird's hands after the drift (off for now).
	private const bool ShowsEquipmentPapers = false;

	public BirdUnlockDefinition Definition { get; private set; }

	public BattleMgr Battle { get; private set; }

	// The battle's own camera: kept full-screen (taps, health bars and the guide map through it) but draws nothing.
	public Camera BattleCamera { get; private set; }

	// Child of BattleCamera that draws the battle through a viewport cut down to the yellow box.
	private Camera m_ViewCamera;

	// Full-screen camera above the interface for OverlayLayer (the rage tutorial's hand, which passes over the meter).
	private Camera m_OverlayCamera;

	// An unnamed, otherwise unused layer.
	public const int OverlayLayer = 30;

	private PopupFeatureUnlockedStateMgr m_Popup;

	private GameObject m_PopupBird;

	private Scene m_WorldScene;

	private Scene m_BattleScene;

	private bool m_LoadFailed;

	private bool m_ContinueRequested;

	private bool m_DriftDone;

	private bool m_Finished;

	private bool m_Cancelled;

	private bool m_Expanding;

	private float m_ContinueTime;

	private AsyncOperation m_Load;

	private AudioSource m_Theme;

	private float m_ThemeFade = 1f;

	private BirdUnlockBackdrop m_Backdrop;

	private List<TouchInputHandler> m_RemovedInputHandlers = new List<TouchInputHandler>();

	private string m_PreviousLastBattle;

	// Combatant sounds are 3D and play at the battle's far-away position: the battle's listener takes over meanwhile.
	private AudioListener m_BattleListener;

	private readonly List<AudioListener> m_SilencedListeners = new List<AudioListener>();

	private AudioListener m_PreviousSceneryListener;

	// The bird being unlocked in the battle, its slot and its rig's scale handling during the drift.
	public CharacterControllerBattleGroundBase BirdView { get; private set; }

	public Vector3 BirdSlot { get; private set; }

	// When the clip the bird plays at the end (its cheer) is over: it stays on the world map until then.
	public float BirdClipEndsAt;

	private bool m_Scissor;

	// Where the circle rests while the bird stands on its slot; it moves along as the bird steps to its turn's spot.
	private Vector2 m_CircleRestCenter;

	private bool m_CircleFollows;

	private Rect m_BoxRect;

	private readonly List<EquipmentPaper> m_Papers = new List<EquipmentPaper>();

	// Things the bird's own scripts keep (wheels, laughing pigs...).
	public readonly Dictionary<string, object> Props = new Dictionary<string, object>();

	public static bool TryBegin(string birdNameId, PopupFeatureUnlockedStateMgr popup, GameObject popupBird)
	{
		Func<BirdUnlockDefinition> create;
		if (Active != null || popup == null || popupBird == null || !s_Definitions.TryGetValue(birdNameId, out create))
		{
			return false;
		}
		var definition = create();
		var bird = DIContainerInfrastructure.GetCurrentPlayer().GetBird(birdNameId, true);
		if (bird == null || DIContainerBalancing.Service.GetBalancingData<BattleBalancingData>(definition.BattleNameId) == null)
		{
			DebugLog.Error(typeof(BirdUnlockSequence), "No bird or battle for the unlock sequence of " + birdNameId);
			return false;
		}
		var go = new GameObject("BirdUnlockSequence_" + birdNameId);
		DontDestroyOnLoad(go);
		var sequence = go.AddComponent<BirdUnlockSequence>();
		sequence.Definition = definition;
		sequence.m_Popup = popup;
		sequence.m_PopupBird = popupBird;
		Active = sequence;
		sequence.StartTheme();
		sequence.StartCoroutine(sequence.LoadBattle(bird));
		return true;
	}

	// The popup's checkmark: false if the battle could not be set up (the popup then closes as usual).
	public bool Continue()
	{
		if (m_LoadFailed || m_ContinueRequested)
		{
			return false;
		}
		m_ContinueRequested = true;
		m_ContinueTime = Time.time;
		return true;
	}

	// Stops the sequence before it took over (the popup closes as usual).
	public void Cancel()
	{
		if (m_Cancelled)
		{
			return;
		}
		m_Cancelled = true;
		m_Finished = true;
		StartCoroutine(Cleanup(false));
	}

	private void OnDestroy()
	{
		if (Active == this)
		{
			Active = null;
		}
	}

	// ---------------------------------------------------------------------------------------------------- music

	private void StartTheme()
	{
		var clip = Resources.Load<AudioClip>("Sound/Music/" + Definition.ThemeClip);
		if (clip == null)
		{
			DebugLog.Error(GetType(), "Theme not found: Sound/Music/" + Definition.ThemeClip);
			return;
		}
		WorldMapAreaMusic.Silenced = true;
		m_Theme = gameObject.AddComponent<AudioSource>();
		m_Theme.clip = clip;
		m_Theme.loop = true;
		m_Theme.playOnAwake = false;
		m_Theme.spatialBlend = 0f;
		m_Theme.ignoreListenerPause = true;
		MusicLoopPointOverrides.ExtraSources.Add(m_Theme);
		UpdateThemeVolume();
		m_Theme.Play();
	}

	private void UpdateThemeVolume()
	{
		if (m_Theme == null)
		{
			return;
		}
		var audio = DIContainerInfrastructure.AudioManager;
		var cue = audio != null ? audio.GetSound("music_worldmap") as Sound : null;
		m_Theme.volume = (cue != null ? cue.Volume : 1f) * m_ThemeFade;
		m_Theme.mute = audio != null && audio.IsMuted(0);
	}

	private void Update()
	{
		UpdateThemeVolume();
		// The checkmark was pressed but the battle never got going: give the popup back.
		if (m_ContinueRequested && !m_Cancelled && !m_Expanding && (m_LoadFailed || Time.time - m_ContinueTime > 10f))
		{
			DebugLog.Error(GetType(), "The unlock battle did not start; closing the popup instead.");
			Cancel();
			m_Popup.CloseAfterBirdUnlockFailed();
		}
	}

	// ---------------------------------------------------------------------------------------------------- loading

	private IEnumerator LoadBattle(BirdGameData bird)
	{
		var battleId = Definition.BattleNameId;
		var balancing = DIContainerBalancing.Service.GetBalancingData<BattleBalancingData>(battleId);
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		m_PreviousLastBattle = player.m_LastBattle;
		var start = new BattleStartGameData();
		start.m_BackgroundAssetId = balancing.BackgroundAssetId;
		start.m_RageAvailiable = true;
		start.m_Birds = new List<BirdGameData> { bird };
		start.m_BattleBalancingNameId = battleId;
		start.callback = null;
		start.m_Inventory = player.InventoryGameData;
		start.m_InvokerLevel = player.Data.Level;
		start.m_InjectableParticipantTable = null;
		start.m_BattleRandomSeed = 0;
		start.m_PossibleFollowUpBattles = new List<string>();
		start.m_EnvironmentalEffects = null;
		start.m_SponsoredEnvironmentalEffect = string.Empty;
		start.m_FactionBuffs = new Dictionary<Faction, Dictionary<string, float>>();
		start.m_IsHardMode = false;
		start.m_IsDungeon = false;
		start.m_IsChronicleCave = false;
		ClientInfo.CurrentBattleStartGameData = start;
		m_WorldScene = SceneManager.GetActiveScene();
		var load = SceneManager.LoadSceneAsync(balancing.BackgroundAssetId, LoadSceneMode.Additive);
		m_Load = load;
		if (load == null)
		{
			m_LoadFailed = true;
			ClientInfo.CurrentBattleStartGameData = null;
			yield break;
		}
		while (!load.isDone)
		{
			yield return null;
		}
		m_BattleScene = SceneManager.GetSceneByName(balancing.BackgroundAssetId);
		// Objects the battle creates without a parent belong to its scene (and go with it).
		if (m_BattleScene.IsValid() && m_BattleScene.isLoaded && !m_Cancelled)
		{
			SceneManager.SetActiveScene(m_BattleScene);
		}
		if (Battle != null && m_BattleScene.IsValid() && m_BattleScene.isLoaded)
		{
			foreach (var root in m_BattleScene.GetRootGameObjects().Where(r => r.transform != Battle.transform.root))
			{
				root.SetActive(false);
			}
		}
		if (Battle == null)
		{
			DebugLog.Error(GetType(), "The unlock battle did not start in " + balancing.BackgroundAssetId);
			m_LoadFailed = true;
		}
	}

	// BattleMgr.Awake (before it looks for its cameras): moves the battle away from the world map, switches the scenery
	// off and sets up the battle camera to draw over the world map, inside the yellow box only.
	public void PrepareBattleScene(BattleMgr battle)
	{
		Battle = battle;
		// The scene is still loading here (no SetActiveScene / GetRootGameObjects yet): find its roots by object.
		var scene = battle.gameObject.scene;
		var setupRoot = battle.transform.root;
		setupRoot.position += BattleOffset;
		foreach (var root in FindObjectsOfType<Transform>(true).Where(r => r.parent == null && r.gameObject.scene == scene && r != setupRoot))
		{
			root.gameObject.SetActive(false);
		}
		foreach (var listener in setupRoot.GetComponentsInChildren<AudioListener>(true))
		{
			if (m_BattleListener == null)
			{
				m_BattleListener = listener;
			}
			listener.enabled = false;
		}
		foreach (var source in setupRoot.GetComponentsInChildren<AudioSource>(true).Where(s => s.playOnAwake && s.loop))
		{
			source.Stop();
			source.enabled = false;
		}
		BattleCamera = setupRoot.GetComponentsInChildren<Camera>(true).FirstOrDefault(c => c.CompareTag("SceneryCamera"));
		if (BattleCamera != null)
		{
			var view = new GameObject("BirdUnlockView");
			view.layer = BattleCamera.gameObject.layer;
			view.transform.SetParent(BattleCamera.transform, false);
			m_ViewCamera = view.AddComponent<Camera>();
			m_ViewCamera.CopyFrom(BattleCamera);
			// CopyFrom fixes the aspect; the viewport changes, so let it follow.
			m_ViewCamera.ResetAspect();
			m_ViewCamera.clearFlags = CameraClearFlags.Depth;
			m_ViewCamera.depth = 0.9f;
			// Like every other camera in the game: HDR on a depth-only camera drew nothing over the world map.
			m_ViewCamera.allowHDR = false;
			m_ViewCamera.transparencySortMode = TransparencySortMode.Orthographic;
			m_ViewCamera.enabled = false;
			var overlay = new GameObject("BirdUnlockOverlay");
			overlay.transform.SetParent(BattleCamera.transform, false);
			m_OverlayCamera = overlay.AddComponent<Camera>();
			m_OverlayCamera.CopyFrom(BattleCamera);
			m_OverlayCamera.ResetAspect();
			overlay.transform.localPosition = Vector3.zero;
			overlay.transform.localRotation = Quaternion.identity;
			m_OverlayCamera.clearFlags = CameraClearFlags.Depth;
			m_OverlayCamera.cullingMask = 1 << OverlayLayer;
			m_OverlayCamera.depth = 1.05f;
			m_OverlayCamera.allowHDR = false;
			m_OverlayCamera.enabled = false;
			BattleCamera.clearFlags = CameraClearFlags.Depth;
			BattleCamera.cullingMask = 0;
			BattleCamera.depth = 0.89f;
			BattleCamera.allowHDR = false;
			BattleCamera.enabled = false;
		}
	}

	// ---------------------------------------------------------------------------------------------------- battle intro

	// Replaces BattleMgr's start tree and initial placement: the bird waits (offscreen camera) until the popup is
	// confirmed, then drifts from the popup onto its slot while the box expands; then the first wave walks in.
	public IEnumerator RunBattleIntro(BattleMgr battle)
	{
		yield return battle.StartCoroutine(battle.PlaceCharacter(battle.m_BirdCenterPosition, Faction.Birds));
		var bird = battle.Model.m_CombatantsPerFaction[Faction.Birds].FirstOrDefault(b => b.CombatantNameId == Definition.BirdNameId) ?? battle.Model.m_CombatantsPerFaction[Faction.Birds].First();
		BirdView = bird.CombatantView;
		BirdSlot = battle.m_BirdCenterPosition.position + BirdView.CachedUnfocusedPos;
		BirdView.transform.position = BirdSlot;
		BirdView.gameObject.SetActive(true);
		bird.Entered = true;
		// A cancelled sequence keeps the battle waiting here until it is unloaded.
		while (!m_ContinueRequested || m_Cancelled)
		{
			yield return null;
		}
		m_Expanding = true;
		StartCoroutine(Expand(battle));
		while (!m_DriftDone)
		{
			yield return null;
		}
		if (ShowsEquipmentPapers)
		{
			ShowEquipmentPapers();
		}
		yield return battle.StartCoroutine(battle.PlaceCharacter(battle.m_PigCenterPosition, Faction.Pigs));
		NotifyWavePigsPlaced(battle);
		var pacing = DIContainerLogic.GetPacingBalancing();
		var summedEnterDelay = battle.Model.m_CombatantsPerFaction[Faction.Pigs].Count * pacing.PerCombatantEnterDelay + pacing.BaseBattleEnterDelay;
		battle.StartCoroutine(battle.EnterPigs());
		yield return new WaitForSeconds(summedEnterDelay);
		battle.SpawnHealthBars();
		yield return new WaitForSeconds(Mathf.Max(0f, pacing.AfterEnterBattleGroundDelay - summedEnterDelay));
		if (Definition.OnWaveReady != null)
		{
			yield return battle.StartCoroutine(Definition.OnWaveReady(this, battle, battle.Model.CurrentWaveIndex));
		}
	}

	public void NotifyWavePigsPlaced(BattleMgr battle)
	{
		if (Definition.OnWavePigsPlaced != null)
		{
			Definition.OnWavePigsPlaced(this, battle, battle.Model.CurrentWaveIndex);
		}
	}

	// SetupNextWave, after the pigs walked in.
	public IEnumerator OnWaveReady(BattleMgr battle)
	{
		if (Definition.OnWaveReady != null)
		{
			yield return battle.StartCoroutine(Definition.OnWaveReady(this, battle, battle.Model.CurrentWaveIndex));
		}
	}

	private IEnumerator Expand(BattleMgr battle)
	{
		var uiCamera = DIContainerInfrastructure.GetCoreStateMgr().m_InterfaceCamera;
		var popup = m_Popup.transform;
		var column = popup.Find(ColumnPath);
		var slot = popup.Find(SlotPath);
		var columnRect = ScreenRect(column, uiCamera);
		var slotRect = ScreenRect(slot, uiCamera);
		Vector2 rayCenter0 = uiCamera.WorldToScreenPoint(column.position);
		var circleCenter0 = slotRect.center;
		var circleRadius0 = slotRect.width * 0.5f;
		// UI units -> pixels (the column is 560 units wide).
		var uiPixel = columnRect.width / 560f;

		// The popup bird and the battle bird, measured on screen.
		var popupRig = m_PopupBird.GetComponentInChildren<CharacterAssetController>();
		var popupRoot = popupRig != null ? popupRig.transform : m_PopupBird.transform;
		// The two rigs' roots sit differently: line them up by their body centres.
		var popupAnchor = popupRig != null && popupRig.BodyCenter != null ? popupRig.BodyCenter : popupRoot;
		Vector2 birdScreen0 = uiCamera.WorldToScreenPoint(popupAnchor.position);
		var popupPixels = PixelsPerUnit(popupRoot, uiCamera);
		var rig = BirdView.m_AssetController.transform;
		var anchor = BirdView.m_AssetController.BodyCenter != null ? BirdView.m_AssetController.BodyCenter : rig;
		BirdView.transform.position = BirdSlot;
		var battlePixels = PixelsPerUnit(rig, BattleCamera);
		Vector2 birdScreen1 = BattleCamera.WorldToScreenPoint(anchor.position);
		var startScale = battlePixels > 0.0001f ? popupPixels / battlePixels : 1f;
		var circleOffset0 = circleCenter0 - birdScreen0;

		// The box: as tall as the column, wide enough for the bird on the left and the pigs' area on the right with the
		// same dark margin on both sides.
		var targetRect = BattleBoxRect(battle, columnRect);

		m_Backdrop = BirdUnlockBackdrop.Create(BackdropOrigin, m_ViewCamera.depth - 0.05f, uiPixel, BattleCamera);
		m_Backdrop.SetSlotSprites(slot.GetComponentsInChildren<UISprite>(true), uiCamera);
		m_Backdrop.OverlayAlpha = OverlayAlpha;
		m_Backdrop.Box = columnRect;
		m_Backdrop.RayCenter = rayCenter0;
		m_Backdrop.CircleCenter = circleCenter0;
		m_Backdrop.CircleRadius = circleRadius0;
		m_Backdrop.ColorSwapInterval = ColorSwapInterval;
		m_Backdrop.ColorFadeTime = ColorFadeTime;

		// Hand-over in this frame: the popup's column, slot, bird and overlay go, the backdrop and battle take over.
		var scale = new RigScale(BirdView, rig);
		scale.Set(startScale);
		PlaceRigAt(anchor, birdScreen0);
		BattleCamera.enabled = true;
		m_ViewCamera.enabled = true;
		m_OverlayCamera.enabled = true;
		m_Scissor = true;
		m_BoxRect = columnRect;
		ApplyScissor();
		m_Popup.BeginBirdUnlockHandover();
		m_PopupBird.SetActive(false);
		RemoveWorldInput();
		SwitchListener(true);
		var generic = DIContainerInfrastructure.GetCoreStateMgr().m_GenericUI;
		generic.DeRegisterBar(5u);
		generic.UpdateAllBars();

		var t = 0f;
		while (t < ExpandDuration)
		{
			t += Time.deltaTime;
			var e = EaseInOut(Mathf.Clamp01(t / ExpandDuration));
			var s = Mathf.Exp(Mathf.Lerp(Mathf.Log(startScale), 0f, e));
			scale.Set(s);
			var birdScreen = Vector2.Lerp(birdScreen0, birdScreen1, e);
			PlaceRigAt(anchor, birdScreen);
			var r = s / startScale;
			var circleCenter = birdScreen + circleOffset0 * r;
			m_Backdrop.CircleCenter = circleCenter;
			m_Backdrop.CircleRadius = circleRadius0 * r;
			m_Backdrop.RayCenter = circleCenter + (rayCenter0 - circleCenter0) * (1f - e);
			m_BoxRect = LerpRect(columnRect, targetRect, e);
			m_Backdrop.Box = m_BoxRect;
			yield return null;
		}
		scale.Release();
		BirdView.transform.position = BirdSlot;
		m_BoxRect = targetRect;
		m_Backdrop.Box = targetRect;
		m_CircleRestCenter = m_Backdrop.CircleCenter;
		m_CircleFollows = true;
		m_DriftDone = true;
	}

	private Rect BattleBoxRect(BattleMgr battle, Rect columnRect)
	{
		var center = Screen.width * 0.5f;
		var halfWidth = columnRect.width * 0.5f;
		var birdLeft = BirdSlot - new Vector3(BirdView.Size.x * 0.5f + BoxPadding, 0f, 0f);
		halfWidth = Mathf.Max(halfWidth, center - BattleCamera.WorldToScreenPoint(birdLeft).x);
		var pigArea = battle.m_PigCenterPosition.GetComponent<ContainerControl>();
		var pigRightX = battle.m_PigCenterPosition.position.x + (pigArea != null ? pigArea.m_Size.x * 0.5f : 300f) + BoxPadding;
		var pigRight = new Vector3(pigRightX, battle.m_PigCenterPosition.position.y, battle.m_PigCenterPosition.position.z);
		halfWidth = Mathf.Max(halfWidth, BattleCamera.WorldToScreenPoint(pigRight).x - center);
		halfWidth = Mathf.Min(halfWidth, center - MinScreenMargin);
		return Rect.MinMaxRect(center - halfWidth, Mathf.Min(columnRect.yMin, 0f), center + halfWidth, Mathf.Max(columnRect.yMax, Screen.height));
	}

	private void PlaceRigAt(Transform rig, Vector2 screen)
	{
		var current = BattleCamera.WorldToScreenPoint(rig.position);
		var target = BattleCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, current.z));
		var delta = target - rig.position;
		delta.z = 0f;
		BirdView.transform.position += delta;
	}

	// ---------------------------------------------------------------------------------------------------- camera scissor

	private void LateUpdate()
	{
		ApplyScissor();
		FollowBirdWithCircle();
	}

	// The circle goes with the bird between its slot and its turn's spot (further left when it isn't its turn), and stays
	// at the turn's spot while the bird is further out (attacking, raging).
	private void FollowBirdWithCircle()
	{
		if (!m_CircleFollows || m_Backdrop == null || BirdView == null || BattleCamera == null)
		{
			return;
		}
		var focus = BirdSlot + BirdView.BirdFocusPosOffset;
		var segment = focus - BirdSlot;
		var along = segment.sqrMagnitude > 0.01f ? Mathf.Clamp01(Vector3.Dot(BirdView.transform.position - BirdSlot, segment) / segment.sqrMagnitude) : 0f;
		Vector2 slotScreen = BattleCamera.WorldToScreenPoint(BirdSlot);
		Vector2 focusScreen = BattleCamera.WorldToScreenPoint(focus);
		m_Backdrop.CircleCenter = m_CircleRestCenter + (focusScreen - slotScreen) * along;
	}

	// The view camera shows exactly what the full-screen battle camera would show in the box: same scale, its viewport
	// and centre moved onto the box (plain orthographic settings, so everything Unity computes from it stays right).
	private void ApplyScissor()
	{
		if (BattleCamera == null || m_ViewCamera == null)
		{
			return;
		}
		var fullSize = BattleCamera.orthographicSize;
		if (m_OverlayCamera != null)
		{
			m_OverlayCamera.orthographicSize = fullSize;
			m_OverlayCamera.nearClipPlane = BattleCamera.nearClipPlane;
			m_OverlayCamera.farClipPlane = BattleCamera.farClipPlane;
		}
		m_ViewCamera.nearClipPlane = BattleCamera.nearClipPlane;
		m_ViewCamera.farClipPlane = BattleCamera.farClipPlane;
		if (!m_Scissor)
		{
			m_ViewCamera.rect = new Rect(0f, 0f, 1f, 1f);
			m_ViewCamera.orthographicSize = fullSize;
			m_ViewCamera.transform.localPosition = Vector3.zero;
			return;
		}
		var width = Mathf.Max(1f, Screen.width);
		var height = Mathf.Max(1f, Screen.height);
		var box = Rect.MinMaxRect(Mathf.Clamp(m_BoxRect.xMin, 0f, width), Mathf.Clamp(m_BoxRect.yMin, 0f, height), Mathf.Clamp(m_BoxRect.xMax, 0f, width), Mathf.Clamp(m_BoxRect.yMax, 0f, height));
		if (box.width < 1f || box.height < 1f)
		{
			box = new Rect(box.x, box.y, Mathf.Max(box.width, 1f), Mathf.Max(box.height, 1f));
		}
		m_ViewCamera.rect = new Rect(box.x / width, box.y / height, box.width / width, box.height / height);
		var unitsPerPixel = 2f * fullSize / height;
		m_ViewCamera.orthographicSize = fullSize * box.height / height;
		m_ViewCamera.transform.localPosition = new Vector3((box.center.x - width * 0.5f) * unitsPerPixel, (box.center.y - height * 0.5f) * unitsPerPixel, 0f);
	}

	// ---------------------------------------------------------------------------------------------------- tutorial steps

	public BirdUnlockStep CurrentStep(BattleMgr battle)
	{
		var step = Definition.Steps.FirstOrDefault(s => !s.Done);
		return step != null && step.Wave == battle.Model.CurrentWaveIndex ? step : null;
	}

	// CharacterControllerBattleGround: a bird is waiting for the player's command.
	public void OnBirdAwaitingInput(BattleMgr battle, CharacterControllerBattleGroundBase view)
	{
		if (view != BirdView || m_Finished)
		{
			return;
		}
		var step = CurrentStep(battle);
		if (step == null)
		{
			return;
		}
		StartCoroutine(RunStep(battle, view, step));
	}

	private IEnumerator RunStep(BattleMgr battle, CharacterControllerBattleGroundBase bird, BirdUnlockStep step)
	{
		var tutorial = DIContainerInfrastructure.TutorialMgr;
		var scenery = LayerMask.NameToLayer("Scenery");
		var tutorialScenery = LayerMask.NameToLayer("TutorialScenery");
		var done = false;
		switch (step.Type)
		{
		case BirdUnlockStepType.Support:
		{
			Action<ICombatant> clicked = c => done = true;
			bird.gameObject.layer = tutorialScenery;
			bird.m_AllowDrag = false;
			tutorial.SetTutorialCameras(true);
			tutorial.ShowHelp(bird.m_AssetController.BodyCenter, TutorialStepType.SupportSelf.ToString(), -60f);
			bird.Clicked += clicked;
			while (!done && bird != null && !m_Finished)
			{
				yield return null;
			}
			if (bird != null)
			{
				bird.Clicked -= clicked;
				bird.gameObject.layer = scenery;
				bird.m_AllowDrag = true;
			}
			tutorial.HideHelp(TutorialStepType.SupportSelf.ToString(), done);
			tutorial.SetTutorialCameras(false);
			HideEquipmentPapers();
			break;
		}
		case BirdUnlockStepType.Attack:
		{
			var target = battle.Model.m_CombatantsPerFaction[Faction.Pigs].Where(p => p.IsParticipating && p.CombatantView != null).Select(p => p.CombatantView).FirstOrDefault();
			if (target == null)
			{
				yield break;
			}
			Action<ICombatant> clicked = c => done = true;
			bird.gameObject.layer = tutorialScenery;
			target.gameObject.layer = tutorialScenery;
			bird.m_AllowClick = false;
			tutorial.SetTutorialCameras(true);
			tutorial.ShowFromToHelp(battle, bird.transform, bird.m_AssetController.BodyCenter, target.m_AssetController.BodyCenter, TutorialStepType.AttackPig.ToString(), -60f);
			target.Clicked += clicked;
			while (!done && bird != null && target != null && !m_Finished)
			{
				yield return null;
			}
			if (target != null)
			{
				target.Clicked -= clicked;
				target.gameObject.layer = scenery;
			}
			if (bird != null)
			{
				bird.gameObject.layer = scenery;
				bird.m_AllowClick = true;
			}
			tutorial.HideHelp(TutorialStepType.AttackPig.ToString(), done);
			tutorial.SetTutorialCameras(false);
			break;
		}
		case BirdUnlockStepType.Rage:
		{
			var meter = battle.m_BattleUI.m_RageMeter;
			Action<float, ICombatant> used = (v, c) => done = true;
			bird.gameObject.layer = tutorialScenery;
			meter.m_DragTrigger.gameObject.layer = LayerMask.NameToLayer("TutorialInterface");
			bird.m_AllowClick = false;
			bird.m_AllowDrag = false;
			tutorial.SetTutorialCameras(true);
			// The meter is interface; the battle is far from the interface's world space, so the hand starts from a point in
			// the battle at the meter's screen position, on the overlay layer (drawn above the meter).
			var start = RageHandStart(battle, meter.m_RageMeterPosInScene.transform);
			tutorial.ShowFromToHelp(battle, start, start, bird.m_AssetController.BodyCenter, TutorialStepType.UseRage.ToString(), -60f);
			battle.Model.RageUsed += used;
			while (!done && bird != null && !m_Finished)
			{
				yield return null;
			}
			battle.Model.RageUsed -= used;
			meter.m_DragTrigger.gameObject.layer = LayerMask.NameToLayer("Interface");
			if (bird != null)
			{
				bird.gameObject.layer = scenery;
				bird.m_AllowClick = true;
				bird.m_AllowDrag = true;
			}
			tutorial.HideHelp(TutorialStepType.UseRage.ToString(), done);
			if (start != null)
			{
				Destroy(start.gameObject, 1f);
			}
			tutorial.SetTutorialCameras(false);
			if (done)
			{
				StartCoroutine(WatchRageMove(battle));
			}
			break;
		}
		}
		if (done)
		{
			step.Done = true;
		}
	}

	private Transform RageHandStart(BattleMgr battle, Transform meter)
	{
		var uiCamera = DIContainerInfrastructure.GetCoreStateMgr().m_InterfaceCamera;
		Vector2 screen = uiCamera.WorldToScreenPoint(meter.position);
		var depth = BattleCamera.WorldToScreenPoint(BirdView.m_AssetController.BodyCenter.position).z;
		var start = new GameObject("BirdUnlock_RageHandStart").transform;
		start.SetParent(battle.m_BattleArea, true);
		start.position = BattleCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
		// Drawn above the interface (the hand starts over the meter).
		start.gameObject.layer = OverlayLayer;
		return start;
	}

	// The rage's jump: the bird leaves its slot.
	private IEnumerator WatchRageMove(BattleMgr battle)
	{
		var start = BirdView.transform.position;
		var timeout = 5f;
		while (BirdView != null && timeout > 0f && (BirdView.transform.position - start).magnitude < 25f)
		{
			timeout -= Time.deltaTime;
			yield return null;
		}
		// ...and lands next to the pigs (stops moving), just before the blast.
		var last = BirdView != null ? BirdView.transform.position : Vector3.zero;
		var still = 0;
		timeout = 3f;
		while (BirdView != null && timeout > 0f && still < 3)
		{
			yield return null;
			timeout -= Time.deltaTime;
			var now = BirdView.transform.position;
			still = (now - last).magnitude < 0.5f ? still + 1 : 0;
			last = now;
		}
		if (Definition.OnBirdRageMove != null)
		{
			Definition.OnBirdRageMove(this, battle);
		}
	}

	// ---------------------------------------------------------------------------------------------------- equipment papers

	// The tap-and-hold descriptions of the bird's main and off hand, next to the hands: the main hand's grows from its
	// bottom-left corner (just above the hand), the off hand's from its top-right corner (just below the hand); each
	// shrinks into the opposite corner.
	private void ShowEquipmentPapers()
	{
		var bird = BirdView.GetModel().CharacterModel as BirdGameData;
		var asset = BirdView.m_AssetController;
		if (bird == null || asset == null)
		{
			return;
		}
		if (bird.MainHandItem != null && asset.MainHandBone != null)
		{
			var paper = EquipmentPaper.Show(bird.MainHandItem, asset.MainHandBone, BattleCamera, true);
			if (paper != null)
			{
				m_Papers.Add(paper);
			}
		}
		if (bird.OffHandItem != null && asset.OffHandBone != null)
		{
			var paper = EquipmentPaper.Show(bird.OffHandItem, asset.OffHandBone, BattleCamera, false);
			if (paper != null)
			{
				m_Papers.Add(paper);
			}
		}
		foreach (var paper in m_Papers)
		{
			StartCoroutine(paper.Enter());
		}
	}

	private void HideEquipmentPapers()
	{
		foreach (var paper in m_Papers)
		{
			StartCoroutine(paper.Leave());
		}
		m_Papers.Clear();
	}

	// ---------------------------------------------------------------------------------------------------- ending

	// BattleMgr: the last pig fell.
	public IEnumerator RunOutro(BattleMgr battle)
	{
		HideEquipmentPapers();
		if (Definition.Celebrate != null && BirdView != null)
		{
			yield return battle.StartCoroutine(Definition.Celebrate(this, battle));
		}
		else if (BirdView != null)
		{
			BirdClipEndsAt = Time.time + BirdView.PlayCheerCharacter();
		}
		StartCoroutine(Close(battle));
	}

	// BattleMgr: the bird lost (should not happen with the tutorial pigs): close without the celebration.
	public IEnumerator RunAbort(BattleMgr battle)
	{
		HideEquipmentPapers();
		StartCoroutine(Close(battle));
		yield break;
	}

	// The box closes the way the popup's Leave clip closes its column (and its slot pops and shrinks); the bird stays on
	// over the world map until its clip ends, then everything goes.
	private IEnumerator Close(BattleMgr battle)
	{
		m_Finished = true;
		var tutorial = DIContainerInfrastructure.TutorialMgr;
		tutorial.HideHelp();
		tutorial.SetTutorialCameras(false);
		foreach (var combatant in battle.Model.m_CombatantsPerFaction.Values.SelectMany(f => f))
		{
			if (combatant.CombatantView != null)
			{
				foreach (var bar in combatant.CombatantView.GetComponentsInChildren<CharacterHealthBar>(true))
				{
					bar.gameObject.SetActive(false);
				}
			}
		}
		var menu = SceneManager.GetSceneByName("Menu_Battleground");
		if (menu.IsValid())
		{
			foreach (var root in menu.GetRootGameObjects())
			{
				root.SetActive(false);
			}
		}
		// The bird must stay visible while the box closes around it.
		m_Scissor = false;
		ApplyScissor();
		var box = m_BoxRect;
		var circleRadius = m_Backdrop != null ? m_Backdrop.CircleRadius : 0f;
		var t = 0f;
		const float closeTime = 0.6666667f;
		while (t < closeTime)
		{
			t += Time.deltaTime;
			if (m_Backdrop != null)
			{
				// Popup_FeatureUnlocked_Leave: column x scale 1 (0.333 s) -> 0.001 (0.583 s) -> 0 (0.667 s); overlay alpha
				// 0.75 (0.333 s) -> 0 (0.583 s); the slot 1 (0.25 s) -> 1.2 (0.333 s) -> 0.001 (0.5 s).
				var columnScale = t < 0.3333333f ? 1f : t < 0.5833333f ? Mathf.Lerp(1f, 0.001f, Smooth((t - 0.3333333f) / 0.25f)) : Mathf.Lerp(0.001f, 0f, (t - 0.5833333f) / 0.0833333f);
				var halfWidth = box.width * 0.5f * columnScale;
				m_Backdrop.Box = Rect.MinMaxRect(box.center.x - halfWidth, box.yMin, box.center.x + halfWidth, box.yMax);
				m_Backdrop.OverlayAlpha = t < 0.3333333f ? OverlayAlpha : Mathf.Lerp(OverlayAlpha, 0f, Mathf.Clamp01((t - 0.3333333f) / 0.25f));
				var slotScale = t < 0.25f ? 1f : t < 0.3333333f ? Mathf.Lerp(1f, 1.2f, Smooth((t - 0.25f) / 0.0833333f)) : Mathf.Lerp(1.2f, 0.001f, Smooth(Mathf.Clamp01((t - 0.3333333f) / 0.1666667f)));
				m_Backdrop.CircleRadius = circleRadius * slotScale;
			}
			yield return null;
		}
		if (m_Backdrop != null)
		{
			Destroy(m_Backdrop.gameObject);
			m_Backdrop = null;
		}
		// The world map's own bird carries on with the clip the battle bird is playing; without one, the battle bird stays
		// until its clip ends.
		if (!TransferBirdClipToWorldMap())
		{
			yield return StartCoroutine(WaitForBirdClip());
		}
		SwitchListener(false);
		if (BirdView != null)
		{
			BirdView.gameObject.SetActive(false);
		}
		yield return StartCoroutine(Cleanup(true));
	}

	private bool TransferBirdClipToWorldMap()
	{
		var location = DIContainerInfrastructure.LocationStateMgr;
		var battleRig = BirdView != null ? BirdView.m_AssetController : null;
		if (location == null || location.m_Birds == null || battleRig == null || battleRig.m_BoneAnimation == null)
		{
			return false;
		}
		CharacterControllerWorldMap target = null;
		foreach (var bird in location.m_Birds.Where(b => b != null))
		{
			var view = bird.GetComponentInChildren<CharacterControllerWorldMap>(true);
			if (view != null && view.GetModel() != null && view.GetModel().Name == Definition.BirdNameId)
			{
				target = view;
				break;
			}
		}
		if (target == null || target.m_AssetController == null)
		{
			return false;
		}
		var source = battleRig.m_BoneAnimation.GetComponent<Animation>();
		AnimationState playing = null;
		foreach (AnimationState state in source)
		{
			if (state.enabled && state.weight > 0f && state.name != "Blink" && (playing == null || state.layer > playing.layer || (state.layer == playing.layer && state.weight > playing.weight)))
			{
				playing = state;
			}
		}
		if (playing == null || playing.name == "Idle")
		{
			return true;
		}
		var rig = target.m_AssetController;
		if (!rig.HasClipSilently(playing.name) || !rig.HasAnimation(playing.name))
		{
			return true;
		}
		rig.PlayAnimation(playing.name);
		var clips = rig.m_BoneAnimation != null ? rig.m_BoneAnimation.GetComponent<Animation>() : null;
		if (clips != null && clips[playing.name] != null)
		{
			clips[playing.name].normalizedTime = playing.normalizedTime;
		}
		rig.PlayAnimationQueued("Idle");
		CarrySounds(BirdView.gameObject, target.gameObject);
		return true;
	}

	// Sounds the battle bird is still playing (its cheer) go on from the world map bird.
	private static void CarrySounds(GameObject from, GameObject to)
	{
		foreach (var source in from.GetComponentsInChildren<AudioSource>(true).Where(s => s.isPlaying && s.clip != null))
		{
			var carried = to.AddComponent<AudioSource>();
			carried.clip = source.clip;
			carried.volume = source.volume;
			carried.pitch = source.pitch;
			carried.outputAudioMixerGroup = source.outputAudioMixerGroup;
			carried.spatialBlend = 0f;
			carried.loop = false;
			carried.playOnAwake = false;
			carried.timeSamples = Mathf.Min(source.timeSamples, source.clip.samples - 1);
			carried.Play();
			Destroy(carried, Mathf.Max(0.1f, (source.clip.length - source.time) / Mathf.Max(0.01f, Mathf.Abs(source.pitch))) + 0.1f);
		}
	}

	private IEnumerator WaitForBirdClip()
	{
		while (Time.time < BirdClipEndsAt && BirdView != null)
		{
			yield return null;
		}
	}

	private IEnumerator Cleanup(bool completed)
	{
		var core = DIContainerInfrastructure.GetCoreStateMgr();
		SwitchListener(false);
		while (m_Load != null && !m_Load.isDone)
		{
			yield return null;
		}
		if (!m_BattleScene.IsValid() && Battle != null)
		{
			m_BattleScene = Battle.gameObject.scene;
		}
		RestoreWorldInput();
		HideEquipmentPapers();
		if (m_WorldScene.IsValid() && m_WorldScene.isLoaded)
		{
			SceneManager.SetActiveScene(m_WorldScene);
		}
		if (m_BattleScene.IsValid() && m_BattleScene.isLoaded)
		{
			var unload = SceneManager.UnloadSceneAsync(m_BattleScene);
			while (unload != null && !unload.isDone)
			{
				yield return null;
			}
		}
		// The battle loads its UI scenes itself (possibly still loading when cancelled early): unload them once they are in.
		for (var pass = 0; pass < 2; pass++)
		{
			foreach (var sceneName in new[] { "Menu_Battleground", "Popup_BattlePaused" })
			{
				var scene = SceneManager.GetSceneByName(sceneName);
				if (scene.IsValid() && scene.isLoaded)
				{
					SceneManager.UnloadSceneAsync(scene);
				}
			}
			yield return new WaitForSeconds(0.5f);
		}
		ClientInfo.CurrentBattleStartGameData = null;
		ClientInfo.CurrentBattleGameData = null;
		DIContainerInfrastructure.GetCurrentPlayer().m_LastBattle = m_PreviousLastBattle;
		core.m_IsWithinPvP = false;
		core.m_GenericUI.IsInBattle = false;
		core.m_GenericUI.RegisterBar(new BarRegistry
		{
			Depth = 0u,
			showFriendshipEssence = true,
			showLuckyCoins = true,
			showSnoutlings = true
		}, true);
		foreach (var reason in new[] { "battle_init", "battle_end", "battle_wave_change", "battle_consumable_enter" })
		{
			DIContainerInfrastructure.BackButtonMgr.DeRegisterBlockReason(reason);
		}
		core.m_InfoOverlays.HideAllTooltipsInstant();
		if (completed)
		{
			m_Popup.CompleteBirdUnlock();
		}
		// The theme stops looping and plays to its end (its fade-out), then the world map's music comes back.
		if (Active == this)
		{
			Active = null;
		}
		if (m_Theme != null && m_Theme.isPlaying && completed)
		{
			m_Theme.loop = false;
			while (m_Theme != null && m_Theme.isPlaying)
			{
				yield return null;
			}
		}
		if (m_Theme != null)
		{
			MusicLoopPointOverrides.ExtraSources.Remove(m_Theme);
			m_Theme.Stop();
		}
		WorldMapAreaMusic.Silenced = false;
		Destroy(gameObject);
	}

	private void SwitchListener(bool toBattle)
	{
		var core = DIContainerInfrastructure.GetCoreStateMgr();
		if (toBattle)
		{
			if (m_BattleListener == null)
			{
				return;
			}
			m_SilencedListeners.Clear();
			foreach (var listener in FindObjectsOfType<AudioListener>())
			{
				if (listener != m_BattleListener && listener.enabled)
				{
					listener.enabled = false;
					m_SilencedListeners.Add(listener);
				}
			}
			m_BattleListener.enabled = true;
			m_PreviousSceneryListener = core.m_SceneryAudioListener;
			core.m_SceneryAudioListener = m_BattleListener;
			return;
		}
		if (m_BattleListener != null)
		{
			m_BattleListener.enabled = false;
		}
		foreach (var listener in m_SilencedListeners.Where(l => l != null))
		{
			listener.enabled = true;
		}
		m_SilencedListeners.Clear();
		if (core.m_SceneryAudioListener == m_BattleListener)
		{
			core.m_SceneryAudioListener = m_PreviousSceneryListener;
		}
	}

	// The world map's touch handlers would also see taps on the battle.
	private void RemoveWorldInput()
	{
		var primary = DIContainerInfrastructure.GetCoreStateMgr().GetComponent<TouchInputHandler>();
		if (primary == null)
		{
			return;
		}
		var coreScene = primary.gameObject.scene;
		m_RemovedInputHandlers = primary.m_SecondaryInputHandlers.Where(h => h != null && h != primary && h.gameObject.scene != coreScene && h.gameObject.scene != m_BattleScene && (Battle == null || h.gameObject.scene != Battle.gameObject.scene)).ToList();
		foreach (var handler in m_RemovedInputHandlers)
		{
			primary.m_SecondaryInputHandlers.Remove(handler);
		}
	}

	private void RestoreWorldInput()
	{
		var primary = DIContainerInfrastructure.GetCoreStateMgr().GetComponent<TouchInputHandler>();
		if (primary == null)
		{
			return;
		}
		primary.m_SecondaryInputHandlers.RemoveAll(h => h == null);
		foreach (var handler in m_RemovedInputHandlers.Where(h => h != null && !primary.m_SecondaryInputHandlers.Contains(h)))
		{
			primary.m_SecondaryInputHandlers.Add(handler);
		}
		m_RemovedInputHandlers.Clear();
	}

	// ---------------------------------------------------------------------------------------------------- helpers

	public static Rect ScreenRect(Transform widgetRoot, Camera camera)
	{
		var bounds = NGUIMath.CalculateAbsoluteWidgetBounds(widgetRoot);
		var a = camera.WorldToScreenPoint(bounds.min);
		var b = camera.WorldToScreenPoint(bounds.max);
		return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
	}

	// Screen pixels per local unit of the transform (along its y axis).
	public static float PixelsPerUnit(Transform t, Camera camera)
	{
		var p0 = camera.WorldToScreenPoint(t.position);
		var p1 = camera.WorldToScreenPoint(t.position + t.up * Mathf.Abs(t.lossyScale.y));
		return ((Vector2)(p1 - p0)).magnitude;
	}

	private static Rect LerpRect(Rect a, Rect b, float t)
	{
		return Rect.MinMaxRect(Mathf.Lerp(a.xMin, b.xMin, t), Mathf.Lerp(a.yMin, b.yMin, t), Mathf.Lerp(a.xMax, b.xMax, t), Mathf.Lerp(a.yMax, b.yMax, t));
	}

	private static float EaseInOut(float t)
	{
		return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
	}

	public static float Smooth(float t)
	{
		t = Mathf.Clamp01(t);
		return t * t * (3f - 2f * t);
	}

	// Plays the first clip the rig has; returns its length (0 if none).
	public static float PlayFirstClip(CharacterControllerBattleGroundBase view, params string[] clips)
	{
		var asset = view != null ? view.m_AssetController : null;
		if (asset == null)
		{
			return 0f;
		}
		foreach (var clip in clips)
		{
			if (asset.HasClipSilently(clip) && asset.HasAnimation(clip))
			{
				asset.PlayAnimation(clip);
				return asset.GetAnimationLength(clip);
			}
		}
		return 0f;
	}

	// Scales a battle rig on top of what ScaleMgr gives it (ScaleMgr rewrites the rig's scale every Update).
	private class RigScale
	{
		private readonly Transform m_Rig;

		private readonly ScaleController m_Controller;

		private readonly Vector3 m_Base;

		public RigScale(CharacterControllerBattleGroundBase view, Transform rig)
		{
			m_Rig = rig;
			m_Controller = rig.GetComponent<ScaleController>() ?? view.GetComponentInChildren<ScaleController>(true);
			if (m_Controller != null && m_Controller.m_ScaleTransform == null)
			{
				m_Controller = null;
			}
			m_Base = m_Controller != null ? m_Controller.m_BaseScale : rig.localScale;
		}

		public void Set(float scale)
		{
			if (m_Controller != null)
			{
				m_Controller.m_BaseScale = m_Base * scale;
				var factor = ScaleMgr.Instance != null ? ScaleMgr.Instance.GetScaleFactor(m_Controller.m_UseFixedDepth ? m_Controller.m_FixedDepth : m_Controller.transform.position.z) : 1f;
				var applied = m_Base * scale;
				applied.x *= factor;
				applied.y *= factor;
				m_Controller.m_ScaleTransform.localScale = applied;
			}
			else
			{
				m_Rig.localScale = m_Base * scale;
			}
		}

		public void Release()
		{
			Set(1f);
			if (m_Controller != null)
			{
				m_Controller.m_BaseScale = m_Base;
			}
		}
	}
}

// The yellow sunburst box, the dark overlay around it and the white slot, drawn by their own camera (under the battle
// camera, over the world map) in screen pixels.
public class BirdUnlockBackdrop : MonoBehaviour
{
	// Ray boundaries in each quarter of the popup's sunburst, in degrees from the vertical, measured on the popup's
	// Popup_FeatureUnlock quarter sprite as drawn (244x384 texels stretched to 280x516): light from 0 deg, then dark.
	private static readonly float[] QuarterBoundaries = { 7.89f, 19.99f, 31.43f, 48.24f, 70.38f };

	private static readonly Color Light = new Color32(255, 234, 128, 255);

	private static readonly Color Dark = new Color32(255, 212, 0, 255);

	// The column's edge: white strip inside, shadow outside (popup units).
	private const float EdgeWhite = 4.6f;

	private const float EdgeShadow = 3.4f;

	private static readonly Color Shadow = new Color(0f, 0f, 0f, 85f / 255f);

	public Rect Box;

	public Vector2 RayCenter;

	public Vector2 CircleCenter;

	public float CircleRadius;

	public float OverlayAlpha;

	public float ColorSwapInterval = 4f;

	public float ColorFadeTime = 1.6f;

	private float m_UiPixel = 1f;

	private Camera m_Camera;

	private Mesh m_FlatMesh;

	private Mesh m_SlotMesh;

	private Material m_FlatMaterial;

	private Material m_SlotMaterial;

	// Real renderers (Graphics.DrawMesh only draws in the one render it is queued for, and the editor's Game view also
	// repaints without a new frame: those repaints showed no backdrop).
	private MeshRenderer m_FlatRenderer;

	private MeshRenderer m_SlotRenderer;

	private readonly List<SlotQuarter> m_SlotQuarters = new List<SlotQuarter>();

	// 0 = the popup's colours, 1 = swapped.
	private float m_Swap;

	private float m_SwapTimer;

	private bool m_SwapTarget;

	private struct SlotQuarter
	{
		// Corner offsets from the slot centre in slot radii (BL, TL, TR, BR) and their UVs.
		public Vector2[] Corners;

		public Vector2[] Uvs;
	}

	// template: a camera of the game whose render settings (rendering path, HDR, MSAA) this one copies. A camera that
	// differs (HDR on, or Forward while the game renders VertexLit) draws into its own buffer, which never showed.
	public static BirdUnlockBackdrop Create(Vector3 origin, float depth, float uiPixel, Camera template = null)
	{
		var go = new GameObject("BirdUnlockBackdrop");
		go.transform.position = origin;
		if (Application.isPlaying)
		{
			DontDestroyOnLoad(go);
		}
		var backdrop = go.AddComponent<BirdUnlockBackdrop>();
		backdrop.m_UiPixel = uiPixel;
		var cameraObject = new GameObject("Camera");
		cameraObject.transform.SetParent(go.transform, false);
		cameraObject.transform.localPosition = new Vector3(0f, 0f, -100f);
		var camera = cameraObject.AddComponent<Camera>();
		if (template != null)
		{
			camera.CopyFrom(template);
			camera.targetTexture = null;
			camera.rect = new Rect(0f, 0f, 1f, 1f);
			camera.transform.localPosition = new Vector3(0f, 0f, -100f);
			camera.transform.localRotation = Quaternion.identity;
		}
		camera.orthographic = true;
		camera.clearFlags = CameraClearFlags.Depth;
		camera.depth = depth;
		camera.nearClipPlane = 1f;
		camera.farClipPlane = 200f;
		camera.allowHDR = false;
		camera.cullingMask = 1 << go.layer;
		backdrop.m_Camera = camera;
		var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent Colored");
		backdrop.m_FlatMaterial = new Material(shader);
		backdrop.m_FlatMaterial.mainTexture = Texture2D.whiteTexture;
		backdrop.m_FlatMaterial.SetColor("_RendererColor", Color.white);
		backdrop.m_FlatMaterial.SetVector("_Flip", Vector4.one);
		backdrop.m_FlatMesh = new Mesh { name = "BirdUnlockBackdrop" };
		backdrop.m_FlatMesh.MarkDynamic();
		backdrop.m_SlotMesh = new Mesh { name = "BirdUnlockSlot" };
		backdrop.m_SlotMesh.MarkDynamic();
		backdrop.m_FlatRenderer = backdrop.AddMeshObject("Flat", backdrop.m_FlatMesh, backdrop.m_FlatMaterial, 0f);
		backdrop.m_SlotRenderer = backdrop.AddMeshObject("Slot", backdrop.m_SlotMesh, null, -1f);
		return backdrop;
	}

	private MeshRenderer AddMeshObject(string name, Mesh mesh, Material material, float z)
	{
		var go = new GameObject(name);
		go.layer = gameObject.layer;
		go.transform.SetParent(transform, false);
		go.transform.localPosition = new Vector3(0f, 0f, z);
		go.AddComponent<MeshFilter>().sharedMesh = mesh;
		var renderer = go.AddComponent<MeshRenderer>();
		renderer.sharedMaterial = material;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		return renderer;
	}

	// The popup's four LargeSlot_Quarter sprites: drawn with their own atlas, UVs and flips, around CircleCenter.
	public void SetSlotSprites(UISprite[] sprites, Camera uiCamera)
	{
		m_SlotQuarters.Clear();
		if (sprites.Length == 0 || sprites[0].atlas == null)
		{
			return;
		}
		m_SlotMaterial = sprites[0].atlas.spriteMaterial;
		var texture = m_SlotMaterial != null ? m_SlotMaterial.mainTexture : null;
		if (texture == null)
		{
			m_SlotMaterial = null;
			return;
		}
		var corners = sprites.SelectMany(s => s.worldCorners).Select(c => (Vector2)uiCamera.WorldToScreenPoint(c)).ToList();
		var min = new Vector2(corners.Min(c => c.x), corners.Min(c => c.y));
		var max = new Vector2(corners.Max(c => c.x), corners.Max(c => c.y));
		var center = (min + max) * 0.5f;
		var radius = Mathf.Max(1f, (max.x - min.x) * 0.5f);
		foreach (var sprite in sprites)
		{
			var data = sprite.GetAtlasSprite();
			if (data == null)
			{
				continue;
			}
			var u0 = (float)data.x / texture.width;
			var u1 = (float)(data.x + data.width) / texture.width;
			var v0 = 1f - (float)(data.y + data.height) / texture.height;
			var v1 = 1f - (float)data.y / texture.height;
			if (sprite.flip == UIBasicSprite.Flip.Horizontally || sprite.flip == UIBasicSprite.Flip.Both)
			{
				var u = u0;
				u0 = u1;
				u1 = u;
			}
			if (sprite.flip == UIBasicSprite.Flip.Vertically || sprite.flip == UIBasicSprite.Flip.Both)
			{
				var v = v0;
				v0 = v1;
				v1 = v;
			}
			var quarter = new SlotQuarter
			{
				Corners = sprite.worldCorners.Select(c => ((Vector2)uiCamera.WorldToScreenPoint(c) - center) / radius).ToArray(),
				Uvs = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) }
			};
			m_SlotQuarters.Add(quarter);
		}
	}

	private void LateUpdate()
	{
		m_SwapTimer += Time.deltaTime;
		if (ColorSwapInterval > 0f && m_SwapTimer >= ColorSwapInterval)
		{
			m_SwapTimer -= ColorSwapInterval;
			m_SwapTarget = !m_SwapTarget;
		}
		m_Swap = Mathf.MoveTowards(m_Swap, m_SwapTarget ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, ColorFadeTime));
		m_Camera.ResetAspect();
		m_Camera.orthographicSize = Screen.height * 0.5f;
		BuildFlatMesh();
		BuildSlotMesh();
		// Mesh coordinates are screen pixels; the camera looks at the middle of the screen.
		var corner = new Vector3(-Screen.width * 0.5f, -Screen.height * 0.5f, 0f);
		m_FlatRenderer.transform.localPosition = corner;
		m_SlotRenderer.transform.localPosition = corner + new Vector3(0f, 0f, -1f);
		m_SlotRenderer.sharedMaterial = m_SlotMaterial;
		m_SlotRenderer.enabled = m_SlotMaterial != null && m_SlotQuarters.Count > 0 && CircleRadius > 0.01f;
	}

	private void BuildFlatMesh()
	{
		var vertices = new List<Vector3>();
		var colors = new List<Color>();
		var triangles = new List<int>();
		var w = Screen.width;
		var h = Screen.height;
		// Dark overlay around the box (drawn first, the box covers its middle).
		var overlay = new Color(0f, 0f, 0f, OverlayAlpha);
		AddQuad(vertices, colors, triangles, new Rect(-10f, -10f, w + 20f, h + 20f), overlay, 2f);
		if (Box.width > 0.01f)
		{
			var shadow = EdgeShadow * m_UiPixel;
			var white = EdgeWhite * m_UiPixel;
			AddQuad(vertices, colors, triangles, Rect.MinMaxRect(Box.xMin - shadow, Box.yMin, Box.xMin, Box.yMax), Shadow, 1.5f);
			AddQuad(vertices, colors, triangles, Rect.MinMaxRect(Box.xMax, Box.yMin, Box.xMax + shadow, Box.yMax), Shadow, 1.5f);
			AddSunburst(vertices, colors, triangles, 1f);
			var whiteWidth = Mathf.Min(white, Box.width * 0.5f);
			AddQuad(vertices, colors, triangles, Rect.MinMaxRect(Box.xMin, Box.yMin, Box.xMin + whiteWidth, Box.yMax), Color.white, 0.5f);
			AddQuad(vertices, colors, triangles, Rect.MinMaxRect(Box.xMax - whiteWidth, Box.yMin, Box.xMax, Box.yMax), Color.white, 0.5f);
		}
		m_FlatMesh.Clear();
		m_FlatMesh.SetVertices(vertices);
		m_FlatMesh.SetColors(colors);
		m_FlatMesh.SetTriangles(triangles, 0);
		m_FlatMesh.bounds = new Bounds(new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(w * 4f, h * 4f, 10f));
	}

	private static void AddQuad(List<Vector3> vertices, List<Color> colors, List<int> triangles, Rect rect, Color color, float z)
	{
		var i = vertices.Count;
		vertices.Add(new Vector3(rect.xMin, rect.yMin, z));
		vertices.Add(new Vector3(rect.xMin, rect.yMax, z));
		vertices.Add(new Vector3(rect.xMax, rect.yMax, z));
		vertices.Add(new Vector3(rect.xMax, rect.yMin, z));
		for (var k = 0; k < 4; k++)
		{
			colors.Add(color);
		}
		triangles.Add(i);
		triangles.Add(i + 1);
		triangles.Add(i + 2);
		triangles.Add(i);
		triangles.Add(i + 2);
		triangles.Add(i + 3);
	}

	// Wedges from RayCenter, clipped to Box: fan of the centre, the two boundary hits and the box corners between them.
	private void AddSunburst(List<Vector3> vertices, List<Color> colors, List<int> triangles, float z)
	{
		var center = new Vector2(Mathf.Clamp(RayCenter.x, Box.xMin + 0.01f, Box.xMax - 0.01f), Mathf.Clamp(RayCenter.y, Box.yMin + 0.01f, Box.yMax - 0.01f));
		var angles = new List<float>();
		foreach (var q in QuarterBoundaries)
		{
			angles.Add(90f - q);
			angles.Add(90f + q);
			angles.Add(270f - q);
			angles.Add(270f + q);
		}
		angles.Sort();
		var corners = new[] { new Vector2(Box.xMax, Box.yMax), new Vector2(Box.xMin, Box.yMax), new Vector2(Box.xMin, Box.yMin), new Vector2(Box.xMax, Box.yMin) };
		var cornerAngles = corners.Select(c => Mathf.Repeat(Mathf.Atan2(c.y - center.y, c.x - center.x) * Mathf.Rad2Deg, 360f)).ToArray();
		for (var i = 0; i < angles.Count; i++)
		{
			var a0 = angles[i];
			var a1 = i + 1 < angles.Count ? angles[i + 1] : angles[0] + 360f;
			var mid = (a0 + a1) * 0.5f * Mathf.Deg2Rad;
			var fromVertical = Mathf.Atan2(Mathf.Abs(Mathf.Cos(mid)), Mathf.Abs(Mathf.Sin(mid))) * Mathf.Rad2Deg;
			var band = 0;
			while (band < QuarterBoundaries.Length && fromVertical > QuarterBoundaries[band])
			{
				band++;
			}
			var light = band % 2 == 0;
			var fade = BirdUnlockSequence.Smooth(m_Swap);
			var color = light ? Color.Lerp(Light, Dark, fade) : Color.Lerp(Dark, Light, fade);
			var polygon = new List<Vector2> { center, BoxHit(center, a0) };
			var between = new List<KeyValuePair<float, Vector2>>();
			for (var c = 0; c < 4; c++)
			{
				var ca = cornerAngles[c];
				if (ca < a0)
				{
					ca += 360f;
				}
				if (ca > a0 && ca < a1)
				{
					between.Add(new KeyValuePair<float, Vector2>(ca, corners[c]));
				}
			}
			polygon.AddRange(between.OrderBy(p => p.Key).Select(p => p.Value));
			polygon.Add(BoxHit(center, a1));
			var start = vertices.Count;
			foreach (var p in polygon)
			{
				vertices.Add(new Vector3(p.x, p.y, z));
				colors.Add(color);
			}
			for (var k = 1; k < polygon.Count - 1; k++)
			{
				triangles.Add(start);
				triangles.Add(start + k + 1);
				triangles.Add(start + k);
			}
		}
	}

	private Vector2 BoxHit(Vector2 center, float degrees)
	{
		var d = new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
		var t = float.MaxValue;
		if (d.x > 1e-6f)
		{
			t = Mathf.Min(t, (Box.xMax - center.x) / d.x);
		}
		else if (d.x < -1e-6f)
		{
			t = Mathf.Min(t, (Box.xMin - center.x) / d.x);
		}
		if (d.y > 1e-6f)
		{
			t = Mathf.Min(t, (Box.yMax - center.y) / d.y);
		}
		else if (d.y < -1e-6f)
		{
			t = Mathf.Min(t, (Box.yMin - center.y) / d.y);
		}
		return center + d * t;
	}

	private void BuildSlotMesh()
	{
		var vertices = new List<Vector3>();
		var uvs = new List<Vector2>();
		var colors = new List<Color>();
		var triangles = new List<int>();
		foreach (var quarter in m_SlotQuarters)
		{
			var start = vertices.Count;
			for (var k = 0; k < 4; k++)
			{
				var p = CircleCenter + quarter.Corners[k] * CircleRadius;
				vertices.Add(new Vector3(p.x, p.y, 0f));
				uvs.Add(quarter.Uvs[k]);
				colors.Add(Color.white);
			}
			triangles.Add(start);
			triangles.Add(start + 1);
			triangles.Add(start + 2);
			triangles.Add(start);
			triangles.Add(start + 2);
			triangles.Add(start + 3);
		}
		m_SlotMesh.Clear();
		m_SlotMesh.SetVertices(vertices);
		m_SlotMesh.SetUVs(0, uvs);
		m_SlotMesh.SetColors(colors);
		m_SlotMesh.SetTriangles(triangles, 0);
		m_SlotMesh.bounds = new Bounds(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f), new Vector3(Screen.width * 4f, Screen.height * 4f, 10f));
	}

	private void OnDestroy()
	{
		Destroy(m_FlatMesh);
		Destroy(m_SlotMesh);
		Destroy(m_FlatMaterial);
	}
}

// One equipment tap-and-hold paper (a copy of InfoOverlayMgr's equipment overlay) pinned next to a hand.
public class EquipmentPaper
{
	private GameObject m_Root;

	private Transform m_Paper;

	private Transform m_Pivot;

	private Vector3 m_EnterCorner;

	private Vector3 m_LeaveCorner;

	private const float GapPixels = 6f;

	// aboveHand: the paper's bottom-left corner sits just above the hand (main hand); otherwise its top-right corner sits
	// just below it (off hand).
	public static EquipmentPaper Show(EquipmentGameData item, Transform hand, Camera sceneryCamera, bool aboveHand)
	{
		var overlays = DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays;
		if (overlays == null || overlays.m_EquipmentOverlay == null)
		{
			return null;
		}
		var uiCamera = DIContainerInfrastructure.GetCoreStateMgr().m_InterfaceCamera;
		var source = overlays.m_EquipmentOverlay;
		var root = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent, false);
		root.name = "BirdUnlockPaper_" + item.Name;
		root.SetActive(true);
		var overlay = root.GetComponent<EquipmentOverlay>();
		overlay.ShowEquipmentOverlay(hand, item, sceneryCamera, false);
		// Its own enter clip is replaced by the corner grow: sample the clip's end state and stop it.
		var animation = root.GetComponent<Animation>();
		if (animation != null)
		{
			var state = animation["InfoOverlay_Enter"];
			if (state != null)
			{
				state.enabled = true;
				state.weight = 1f;
				state.time = state.length;
				animation.Sample();
				state.enabled = false;
			}
			animation.Stop();
		}
		if (overlay.m_Arrow != null)
		{
			overlay.m_Arrow.gameObject.SetActive(false);
		}
		var paper = overlay.m_ContainerControl.transform;
		var bounds = NGUIMath.CalculateAbsoluteWidgetBounds(paper);
		Vector3 handScreen = sceneryCamera.WorldToScreenPoint(hand.position);
		var gap = (uiCamera.ScreenToWorldPoint(new Vector3(0f, GapPixels, 0f)) - uiCamera.ScreenToWorldPoint(Vector3.zero)).y;
		var anchor = uiCamera.ScreenToWorldPoint(new Vector3(handScreen.x, handScreen.y, 0f));
		var bottomLeft = new Vector3(bounds.min.x, bounds.min.y, paper.position.z);
		var topRight = new Vector3(bounds.max.x, bounds.max.y, paper.position.z);
		var corner = aboveHand ? bottomLeft : topRight;
		var target = aboveHand ? new Vector3(anchor.x, anchor.y + gap, corner.z) : new Vector3(anchor.x, anchor.y - gap, corner.z);
		// Keep it on screen (the corner may then sit a little off the hand).
		var screenMin = uiCamera.ScreenToWorldPoint(new Vector3(8f, 8f, 0f));
		var screenMax = uiCamera.ScreenToWorldPoint(new Vector3(Screen.width - 8f, Screen.height - 8f, 0f));
		var size = bounds.size;
		if (aboveHand)
		{
			target.x = Mathf.Clamp(target.x, screenMin.x, screenMax.x - size.x);
			target.y = Mathf.Clamp(target.y, screenMin.y, screenMax.y - size.y);
		}
		else
		{
			target.x = Mathf.Clamp(target.x, screenMin.x + size.x, screenMax.x);
			target.y = Mathf.Clamp(target.y, screenMin.y + size.y, screenMax.y);
		}
		paper.position += new Vector3(target.x - corner.x, target.y - corner.y, 0f);
		var result = new EquipmentPaper { m_Root = root, m_Paper = paper };
		result.m_EnterCorner = target;
		result.m_LeaveCorner = aboveHand ? target + new Vector3(size.x, size.y, 0f) : target - new Vector3(size.x, size.y, 0f);
		result.m_Pivot = result.MakePivot(result.m_EnterCorner);
		result.m_Pivot.localScale = new Vector3(0.001f, 0.001f, 1f);
		return result;
	}

	private Transform MakePivot(Vector3 corner)
	{
		var pivot = new GameObject("Pivot").transform;
		pivot.SetParent(m_Root.transform, false);
		pivot.position = corner;
		pivot.gameObject.layer = m_Root.layer;
		m_Paper.SetParent(pivot, true);
		return pivot;
	}

	public IEnumerator Enter()
	{
		const float duration = 0.28f;
		var t = 0f;
		while (t < duration && m_Pivot != null)
		{
			t += Time.deltaTime;
			var x = Mathf.Clamp01(t / duration);
			// Grows with a small overshoot.
			var s = x < 0.7f ? Mathf.Lerp(0.001f, 1.08f, BirdUnlockSequence.Smooth(x / 0.7f)) : Mathf.Lerp(1.08f, 1f, BirdUnlockSequence.Smooth((x - 0.7f) / 0.3f));
			m_Pivot.localScale = new Vector3(s, s, 1f);
			yield return null;
		}
		if (m_Pivot != null)
		{
			m_Pivot.localScale = Vector3.one;
		}
	}

	public IEnumerator Leave()
	{
		if (m_Root == null)
		{
			yield break;
		}
		// Re-pivot on the opposite corner (from full size, so the paper does not jump) and shrink into it.
		var old = m_Pivot;
		if (old != null)
		{
			old.localScale = Vector3.one;
		}
		m_Pivot = MakePivot(m_LeaveCorner);
		if (old != null)
		{
			UnityEngine.Object.Destroy(old.gameObject);
		}
		const float duration = 0.18f;
		var t = 0f;
		while (t < duration && m_Pivot != null)
		{
			t += Time.deltaTime;
			var s = Mathf.Lerp(1f, 0.001f, BirdUnlockSequence.Smooth(t / duration));
			m_Pivot.localScale = new Vector3(s, s, 1f);
			yield return null;
		}
		if (m_Root != null)
		{
			UnityEngine.Object.Destroy(m_Root);
		}
	}
}
