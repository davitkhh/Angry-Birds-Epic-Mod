using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Battle.Skills;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

// Prince Porky's Duel (DuelKnockback) splits a battle into two interactions: Porky and the bird he knocked back fight
// one battleground to the left (the previous wave's scenery), the rest of the cast stays where it was. Each turn the
// duel goes first (the bird, then Porky), then the camera pans to the rest of the cast for theirs (BattleMgr.DoNextStep
// asks RunDuelRound / IsolateMainGroup / IsolateFor / AfterPigTurn). Right after the knockback the bird and then Porky get
// a turn of their own, Porky's counting as the first duel turn. After "duel_turns" of Porky's duel turns he walks back with the bird 0.3 s behind, the camera following him. Both interactions share the rage chili.
//
// While one interaction acts the battle's combatant lists only hold its members (Isolate), so targeting, area skills
// and rage only reach that side; turn bookkeeping (m_CombatantsNotActed, the turn counter) always sees everyone, and
// BattleGameData.AllDeadOfFaction also counts the hidden side. During the duel's turns the battleground anchors (bird and
// pig centers, focus spots, banners) are shifted into the duel area, so slots, focus and attack positions resolve there;
// Porky's slot and the focus spots also move onto the bird's line, so he stays lined up with it.
// Added to the battle manager on first use.
public class DuelDirector : MonoBehaviour
{
	// "Average pace" for the long walks (to the duel spot and back), in world units per second.
	private const float WalkSpeed = 450f;

	private const float BirdFollowDelay = 0.3f;

	private const float PanDuration = 1f;

	private const float KnockbackDuration = 1.2f;

	private enum Side
	{
		None,
		Duel,
		Main
	}

	private BattleMgr m_BattleMgr;

	public ICombatant Porky { get; private set; }

	public ICombatant Bird { get; private set; }

	public bool IsActive { get; private set; }

	// Extra damage the knocked back bird takes while the duel is on, in percent (added to the damage received modifier
	// in BattleService.ApplyEffectsOnTriggerType). Set by the skill that started the duel ("damage_taken_bonus_in_percent").
	public float BirdDamageTakenBonusPercent { get; private set; }

	private int m_TurnsLeft;

	// Set by the knockback; the bird's bonus turn is still to come (AfterPigTurn).
	private bool m_JustStarted;

	// Duel area minus main area (x only).
	private Vector3 m_Offset;

	private Vector3 m_MainCameraPosition;

	private bool m_AnchorsShifted;

	// Per focus anchor, the vertical move onto the bird's line (on top of m_Offset while in the duel area).
	private readonly Dictionary<Transform, Vector3> m_LineShifts = new Dictionary<Transform, Vector3>();

	// Porky's own slot, given back when the duel ends.
	private Vector3 m_PorkySlot;

	private bool m_CameraInDuelArea;

	// The pig formation was laid out again while the duel was on (a summon): lay it out once more after the walk back.
	private bool m_FormationChanged;

	private Side m_Isolated;

	private HashSet<ICombatant> m_Hidden;

	private List<ICombatant> m_SavedByInitiative;

	private List<ICombatant> m_SavedBirdsByInitiative;

	private List<ICombatant> m_SavedPigsByInitiative;

	private Dictionary<Faction, List<ICombatant>> m_SavedPerFaction;

	// Porky's turn-start effects already ran in this turn's duel round.
	private ICombatant m_TickedCombatant;

	private int m_TickedTurn = -1;

	private BattleGameData Model
	{
		get
		{
			return m_BattleMgr.Model;
		}
	}

	public static DuelDirector For(BattleMgr battleMgr)
	{
		var director = Find(battleMgr);
		if (director == null)
		{
			director = battleMgr.gameObject.AddComponent<DuelDirector>();
			director.m_BattleMgr = battleMgr;
		}
		return director;
	}

	public static DuelDirector Find(BattleMgrBase battleMgr)
	{
		return battleMgr != null ? battleMgr.GetComponent<DuelDirector>() : null;
	}

	public static bool IsDuelist(BattleMgrBase battleMgr, ICombatant combatant)
	{
		var director = Find(battleMgr);
		return director != null && director.IsDuelist(combatant);
	}

	public bool IsDuelist(ICombatant combatant)
	{
		return IsActive && combatant != null && (combatant == Porky || combatant == Bird);
	}

	// The damage received modifier bonus (fraction) for this combatant: only the isolated bird, only while the duel is on.
	public static float DamageTakenBonus(ICombatant combatant)
	{
		if (combatant == null || combatant.CombatantView == null)
		{
			return 0f;
		}
		var director = Find(combatant.CombatantView.m_BattleMgr);
		if (director == null || !director.IsActive || director.Bird != combatant)
		{
			return 0f;
		}
		return director.BirdDamageTakenBonusPercent / 100f;
	}

	// The scenery one battleground to the left of the current one, as an offset from it. Only horizontal neighbors
	// count (vertical scrolling battlegrounds have no room for the duel).
	public bool TryGetDuelAreaOffset(out Vector3 offset)
	{
		offset = Vector3.zero;
		var settings = m_BattleMgr.m_ScenePerWaveSettings;
		var index = m_BattleMgr.SceneryIndex;
		if (settings == null || index < 1 || index >= settings.Count || settings[index - 1].SceneryRoot == null || settings[index].SceneryRoot == null)
		{
			return false;
		}
		var delta = settings[index - 1].SceneryRoot.position - settings[index].SceneryRoot.position;
		if (Mathf.Abs(delta.y) > 1f || delta.x > -1f)
		{
			return false;
		}
		offset = new Vector3(delta.x, 0f, 0f);
		return true;
	}

	// Porky can knock this bird back (target null: any bird). Never while a duel is on, and never the last bird standing.
	public bool CanStartDuel(ICombatant source, ICombatant target)
	{
		Vector3 offset;
		if (IsActive || Model.IsPvP || source == null || source.CombatantView == null || !TryGetDuelAreaOffset(out offset))
		{
			return false;
		}
		var birds = Model.m_CombatantsByInitiative.Where(c => c.CombatantFaction == Faction.Birds && c.IsParticipating && !c.IsBanner && c.CombatantView != null).ToList();
		if (birds.Count < 2)
		{
			return false;
		}
		return target == null || birds.Contains(target);
	}

	public ICombatant PickTarget(ICombatant source)
	{
		if (!CanStartDuel(source, null))
		{
			return null;
		}
		var birds = Model.m_CombatantsByInitiative.Where(c => CanStartDuel(source, c)).ToList();
		return birds.Count == 0 ? null : birds[Random.Range(0, birds.Count)];
	}

	// X position for the source to stand at so its body ends "margin" before every other pig's body (weapons and shadows
	// left out: they may overlap).
	public float FrontOfPigs(ICombatant source, float margin)
	{
		var front = float.MaxValue;
		foreach (var pig in Model.m_CombatantsPerFaction[Faction.Pigs].Where(c => c != source && c.IsParticipating && c.CombatantView != null))
		{
			float min, max;
			front = Mathf.Min(front, TryGetBodyExtent(pig.CombatantView, out min, out max) ? min : pig.CombatantView.transform.position.x);
		}
		var view = source.CombatantView;
		if (front == float.MaxValue)
		{
			return view.transform.position.x;
		}
		float ownMin, ownMax;
		var reach = TryGetBodyExtent(view, out ownMin, out ownMax) ? Mathf.Max(0f, ownMax - view.transform.position.x) : 0f;
		return front - reach - margin;
	}

	private static Mesh s_BakedMesh;

	// Horizontal world extent of a rig's current pose, from the vertices that aren't skinned to the hand bones (weapons,
	// shields) or the shadow. SmoothMoves draws the whole rig as one skinned mesh, so renderer bounds can't tell them apart.
	private static bool TryGetBodyExtent(CharacterControllerBattleGroundBase view, out float min, out float max)
	{
		min = float.MaxValue;
		max = float.MinValue;
		var asset = view.m_AssetController;
		if (asset == null)
		{
			return false;
		}
		foreach (var renderer in asset.GetComponentsInChildren<SkinnedMeshRenderer>())
		{
			var shared = renderer.sharedMesh;
			if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || shared == null)
			{
				continue;
			}
			var bones = renderer.bones;
			var skip = new bool[bones.Length];
			for (var i = 0; i < bones.Length; i++)
			{
				var bone = bones[i];
				skip[i] = bone == null || bone.name.StartsWith("Shadow") || (asset.MainHandBone != null && bone.IsChildOf(asset.MainHandBone)) || (asset.OffHandBone != null && bone.IsChildOf(asset.OffHandBone));
			}
			if (s_BakedMesh == null)
			{
				s_BakedMesh = new Mesh();
			}
			renderer.BakeMesh(s_BakedMesh, true);
			var vertices = s_BakedMesh.vertices;
			var weights = shared.boneWeights;
			var origin = renderer.transform.position;
			var rotation = renderer.transform.rotation;
			for (var v = 0; v < vertices.Length; v++)
			{
				if (v < weights.Length && weights[v].boneIndex0 < skip.Length && skip[weights[v].boneIndex0])
				{
					continue;
				}
				var x = (origin + rotation * vertices[v]).x;
				min = Mathf.Min(min, x);
				max = Mathf.Max(max, x);
			}
		}
		return min <= max;
	}

	// How far the camera has moved off the main battleground (the duel area or on the way); zero without a duel.
	public Vector3 CameraShift
	{
		get
		{
			var camera = Camera;
			return IsActive && camera != null ? camera.position - m_MainCameraPosition : Vector3.zero;
		}
	}

	// A combatant on the side that isn't acting right now (the player can't target it).
	public static bool IsHiddenFromTargeting(BattleMgrBase battleMgr, ICombatant combatant)
	{
		return battleMgr != null && battleMgr.Model != null && battleMgr.Model.m_IsolatedAway.Contains(combatant);
	}

	private Transform Camera
	{
		get
		{
			return m_BattleMgr.m_BattleCameraTween != null ? m_BattleMgr.m_BattleCameraTween.transform : null;
		}
	}

	private IEnumerable<Transform> Anchors
	{
		get
		{
			return new[] { m_BattleMgr.m_BattlegroundCenterPosition, m_BattleMgr.m_BirdCenterPosition, m_BattleMgr.m_PigCenterPosition, m_BattleMgr.m_BirdBannerPosition, m_BattleMgr.m_PigBannerPosition, m_BattleMgr.m_BirdFocusPosition, m_BattleMgr.m_PigFocusPosition }.Where(t => t != null).Distinct();
		}
	}

	private void ShiftAnchors(bool toDuelArea)
	{
		if (m_AnchorsShifted == toDuelArea)
		{
			return;
		}
		m_AnchorsShifted = toDuelArea;
		foreach (var anchor in Anchors)
		{
			Vector3 line;
			var shift = m_LineShifts.TryGetValue(anchor, out line) ? m_Offset + line : m_Offset;
			anchor.position += toDuelArea ? shift : -shift;
		}
	}

	// Lines Porky's slot and the focus spots up with the bird's slot (anchors not shifted yet).
	private void LineUp(ICombatant porky, ICombatant bird)
	{
		var birdLine = (m_BattleMgr.m_BirdCenterPosition.position + bird.CombatantView.CachedUnfocusedPos).y;
		m_LineShifts.Clear();
		foreach (var anchor in new[] { m_BattleMgr.m_BirdFocusPosition, m_BattleMgr.m_PigFocusPosition }.Where(t => t != null).Distinct())
		{
			m_LineShifts[anchor] = new Vector3(0f, birdLine - anchor.position.y, 0f);
		}
		var porkyView = porky.CombatantView;
		m_PorkySlot = porkyView.CachedUnfocusedPos;
		porkyView.CachedUnfocusedPos = new Vector3(m_PorkySlot.x, birdLine - m_BattleMgr.m_PigCenterPosition.position.y, m_PorkySlot.z);
	}

	private void UndoLineUp()
	{
		if (Porky != null && Porky.CombatantView != null)
		{
			Porky.CombatantView.CachedUnfocusedPos = m_PorkySlot;
		}
		m_LineShifts.Clear();
	}

	// Camera and anchors over to one of the two interactions.
	private IEnumerator PanTo(bool duelArea)
	{
		ShiftAnchors(duelArea);
		if (m_CameraInDuelArea == duelArea)
		{
			yield break;
		}
		m_CameraInDuelArea = duelArea;
		var camera = Camera;
		if (camera == null)
		{
			yield break;
		}
		var from = camera.position;
		var to = duelArea ? m_MainCameraPosition + m_Offset : m_MainCameraPosition;
		for (var t = 0f; t < PanDuration; t += Time.deltaTime)
		{
			camera.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / PanDuration));
			yield return null;
		}
		camera.position = to;
	}

	// Called by the skill at the moment of impact: the bird flies back one battleground (the camera with it) and the duel
	// is on. Porky's own walk there is the skill's (WalkToDuelSpot).
	public IEnumerator Knockback(ICombatant porky, ICombatant bird, int turns, Vector3 offset, float birdDamageTakenBonusPercent = 0f)
	{
		Porky = porky;
		Bird = bird;
		BirdDamageTakenBonusPercent = birdDamageTakenBonusPercent;
		m_Offset = offset;
		m_TurnsLeft = Mathf.Max(1, turns);
		m_JustStarted = true;
		m_FormationChanged = false;
		IsActive = true;
		var camera = Camera;
		m_MainCameraPosition = camera != null ? camera.position : Vector3.zero;
		LineUp(porky, bird);
		ShiftAnchors(true);
		m_CameraInDuelArea = true;
		var view = bird.CombatantView;
		var asset = view.m_AssetController;
		var start = view.transform.position;
		// Its own slot, in the duel area. It only moves sideways.
		var end = new Vector3((m_BattleMgr.m_BirdCenterPosition.position + view.CachedUnfocusedPos).x, start.y, start.z);
		var cameraStart = m_MainCameraPosition;
		// Hurt animation stretched over the flight (for now; a knockback animation per bird will replace it).
		var hit = asset != null && asset.m_BoneAnimation != null && !asset.HasNoBoneAnimation ? asset.m_BoneAnimation["Hit"] : null;
		var hitSpeed = hit != null ? hit.speed : 0f;
		if (asset != null)
		{
			asset.PlayHitAnim();
			if (hit != null)
			{
				hit.speed = hitSpeed * Mathf.Min(1f, asset.GetHitAnimationLength() / KnockbackDuration);
			}
		}
		for (var t = 0f; t < KnockbackDuration && view != null; t += Time.deltaTime)
		{
			// Fast off the hit, slowing down to a stop.
			var k = 1f - (1f - t / KnockbackDuration) * (1f - t / KnockbackDuration);
			view.transform.position = Vector3.Lerp(start, end, k);
			if (camera != null)
			{
				camera.position = cameraStart + m_Offset * k;
			}
			yield return null;
		}
		if (camera != null)
		{
			camera.position = cameraStart + m_Offset;
		}
		if (hit != null)
		{
			hit.speed = hitSpeed;
		}
		if (view != null)
		{
			view.transform.position = end;
			if (asset != null)
			{
				asset.PlayIdleAnimation();
			}
		}
	}

	// Porky walks to his slot in the duel area, on the bird's line.
	public IEnumerator WalkToDuelSpot(ICombatant porky)
	{
		var view = porky.CombatantView;
		yield return StartCoroutine(Walk(view, m_BattleMgr.m_PigCenterPosition.position + view.CachedUnfocusedPos, 0f, false));
	}

	private static IEnumerator Walk(CharacterControllerBattleGroundBase view, Vector3 target, float delay, bool faceBack)
	{
		if (delay > 0f)
		{
			yield return new WaitForSeconds(delay);
		}
		if (view == null)
		{
			yield break;
		}
		var duration = Vector3.Distance(view.transform.position, target) / WalkSpeed;
		if (duration <= 0f)
		{
			yield break;
		}
		var asset = view.m_AssetController;
		// Walking away from the enemy side: turn around for the walk instead of moonwalking it.
		var flip = faceBack && asset != null;
		if (flip)
		{
			asset.transform.localScale = Vector3.Scale(asset.transform.localScale, new Vector3(-1f, 1f, 1f));
		}
		var tween = view.m_TweenMovement;
		tween.m_StartTransform = view.transform;
		tween.m_StartOffset = Vector3.zero;
		tween.m_EndTransform = null;
		tween.m_EndOffset = target - view.transform.position;
		tween.m_DurationInSeconds = duration;
		tween.Play();
		if (asset != null)
		{
			asset.PlayAnimationForTime("Move_Loop", "Idle", duration);
		}
		yield return new WaitForSeconds(duration);
		if (flip && asset != null)
		{
			asset.transform.localScale = Vector3.Scale(asset.transform.localScale, new Vector3(-1f, 1f, 1f));
		}
	}

	private void Isolate(Side side)
	{
		if (side == m_Isolated)
		{
			return;
		}
		if (m_Isolated != Side.None)
		{
			RestoreLists();
		}
		if (side == Side.None || !IsActive)
		{
			return;
		}
		var model = Model;
		var duel = side == Side.Duel;
		m_Hidden = new HashSet<ICombatant>(model.m_CombatantsPerFaction.Values.SelectMany(l => l).Concat(model.m_CombatantsByInitiative).Where(c => IsDuelist(c) != duel));
		m_SavedByInitiative = model.m_CombatantsByInitiative;
		m_SavedBirdsByInitiative = model.m_BirdsByInitiative;
		m_SavedPigsByInitiative = model.m_PigsByInitiative;
		model.m_CombatantsByInitiative = m_SavedByInitiative.Where(c => !m_Hidden.Contains(c)).ToList();
		model.m_BirdsByInitiative = m_SavedBirdsByInitiative.Where(c => !m_Hidden.Contains(c)).ToList();
		model.m_PigsByInitiative = m_SavedPigsByInitiative.Where(c => !m_Hidden.Contains(c)).ToList();
		m_SavedPerFaction = new Dictionary<Faction, List<ICombatant>>(model.m_CombatantsPerFaction);
		foreach (var pair in m_SavedPerFaction)
		{
			model.m_CombatantsPerFaction[pair.Key] = pair.Value.Where(c => !m_Hidden.Contains(c)).ToList();
		}
		model.m_IsolatedAway = m_Hidden.ToList();
		m_Isolated = side;
	}

	private void RestoreLists()
	{
		var model = Model;
		model.m_CombatantsByInitiative = Merge(m_SavedByInitiative, model.m_CombatantsByInitiative);
		model.m_BirdsByInitiative = Merge(m_SavedBirdsByInitiative, model.m_BirdsByInitiative);
		model.m_PigsByInitiative = Merge(m_SavedPigsByInitiative, model.m_PigsByInitiative);
		foreach (var pair in m_SavedPerFaction)
		{
			List<ICombatant> current;
			model.m_CombatantsPerFaction[pair.Key] = Merge(pair.Value, model.m_CombatantsPerFaction.TryGetValue(pair.Key, out current) ? current : new List<ICombatant>());
		}
		model.m_IsolatedAway = new List<ICombatant>();
		m_Isolated = Side.None;
		model.RaiseInitiativeChanged();
	}

	// The full list again in its old order: hidden members are kept, visible ones only if still there (someone may have
	// been removed), and anything added meanwhile (summons) goes at the end.
	private List<ICombatant> Merge(List<ICombatant> original, List<ICombatant> current)
	{
		var merged = original.Where(c => m_Hidden.Contains(c) || current.Contains(c)).ToList();
		merged.AddRange(current.Where(c => !original.Contains(c)));
		return merged;
	}

	// Every member of a faction, hidden or not, minus the duelists (PlaceCharacter lays out the main formation only).
	public List<ICombatant> FormationMembers(Faction faction)
	{
		List<ICombatant> list;
		Model.m_CombatantsPerFaction.TryGetValue(faction, out list);
		var all = new List<ICombatant>(list ?? new List<ICombatant>());
		if (m_Isolated != Side.None)
		{
			all.AddRange(m_Hidden.Where(c => c.CombatantFaction == faction && !all.Contains(c)));
		}
		if (IsActive)
		{
			m_FormationChanged |= faction == Faction.Pigs;
		}
		return all.Where(c => !IsDuelist(c)).ToList();
	}

	public void EndIsolation()
	{
		Isolate(Side.None);
	}

	// Before the rest of the cast's birds act.
	public void IsolateMainGroup()
	{
		if (IsActive)
		{
			Isolate(Side.Main);
		}
	}

	// Before a pig's turn in the pigs' turn.
	public void IsolateFor(ICombatant combatant)
	{
		if (IsActive)
		{
			Isolate(IsDuelist(combatant) ? Side.Duel : Side.Main);
		}
	}

	// The side this combatant fights on has nobody to fight: it sits the turn out.
	public bool SitsOutTurn(ICombatant combatant)
	{
		if (!IsActive || IsDuelist(combatant))
		{
			return false;
		}
		var all = FullList();
		return !all.Any(c => c.CombatantFaction != combatant.CombatantFaction && c.CombatantFaction != Faction.NonAttackablePig && c.IsParticipating && !c.IsBanner && !IsDuelist(c));
	}

	private List<ICombatant> FullList()
	{
		var all = new List<ICombatant>(Model.m_CombatantsByInitiative);
		if (m_Isolated != Side.None)
		{
			all.AddRange(m_Hidden.Where(c => !all.Contains(c)));
		}
		return all;
	}

	private bool MainGroupCanFight()
	{
		var all = FullList().Where(c => !IsDuelist(c) && c.IsParticipating && !c.IsBanner).ToList();
		return all.Any(c => c.CombatantFaction == Faction.Birds) && all.Any(c => c.CombatantFaction == Faction.Pigs);
	}

	public bool AlreadyTickedThisTurn(ICombatant combatant)
	{
		return combatant == m_TickedCombatant && m_TickedTurn == Model.m_CurrentTurn;
	}

	private bool DuelBroken()
	{
		return Porky == null || Bird == null || !Porky.IsParticipating || !Bird.IsParticipating || Porky.CombatantView == null || Bird.CombatantView == null;
	}

	private bool BattleOver()
	{
		return m_BattleMgr.WaveEnded || m_BattleMgr.IsBattleEnded();
	}

	// The duel's half of a birds' turn: the bird acts, then Porky. Then the camera goes to the rest of the cast (or the two
	// walk back once the duel is over).
	public IEnumerator RunDuelRound()
	{
		if (!IsActive || BattleOver())
		{
			yield break;
		}
		if (DuelBroken())
		{
			yield return StartCoroutine(Recombine());
			yield break;
		}
		yield return StartCoroutine(PanTo(true));
		Isolate(Side.Duel);
		yield return StartCoroutine(BirdTurn());
		if (!IsActive || BattleOver())
		{
			EndIsolation();
			yield break;
		}
		if (DuelBroken())
		{
			EndIsolation();
			yield return StartCoroutine(Recombine());
			yield break;
		}
		m_BattleMgr.m_BattleUI.ShowTurnIndicator(Faction.Birds, Faction.Pigs);
		yield return StartCoroutine(PorkyTurn());
		EndIsolation();
		if (!IsActive || BattleOver())
		{
			yield break;
		}
		m_TurnsLeft--;
		if (m_TurnsLeft <= 0 || DuelBroken())
		{
			yield return StartCoroutine(Recombine());
			yield break;
		}
		if (MainGroupCanFight())
		{
			yield return StartCoroutine(PanTo(false));
			m_BattleMgr.m_BattleUI.ShowTurnIndicator(Faction.Pigs, Faction.Birds);
		}
	}

	private IEnumerator BirdTurn()
	{
		var bird = Bird;
		if (!bird.IsParticipating)
		{
			yield break;
		}
		bird.ActedThisTurn = false;
		bird.StartedHisTurn = false;
		bird.CombatantView.m_CommandGiven = false;
		m_BattleMgr.IsConsumableUsePossible = !m_BattleMgr.AutoBattle;
		yield return m_BattleMgr.StartCoroutine(m_BattleMgr.RunCombatantTurn(bird));
		m_BattleMgr.IsConsumableUsePossible = false;
		bird.ActedThisTurn = true;
	}

	private IEnumerator PorkyTurn()
	{
		var porky = Porky;
		var model = Model;
		var service = DIContainerLogic.GetBattleService();
		// His share of the pigs' turn start (BattleMgr skips him there this turn).
		service.ApplyEffectsOnTriggerType(model.m_CurrentTurn, EffectTriggerType.OnDealDamagePerTurn, porky, null);
		service.HealCurrentTurn(porky, model);
		service.DealDamageFromCurrentTurn(porky, model, null);
		porky.RaiseTurnStarted(model.m_CurrentTurn);
		m_TickedCombatant = porky;
		m_TickedTurn = model.m_CurrentTurn;
		model.m_CombatantsNotActed.Remove(porky);
		model.m_CombatantsOutOfInitiativeOrder.Remove(porky);
		if (porky.IsParticipating && Bird.IsParticipating)
		{
			yield return m_BattleMgr.StartCoroutine(m_BattleMgr.RunCombatantTurn(porky));
		}
	}

	// After a pig's turn in the pigs' turn: right after the knockback the bird has its own turn, then Porky (the first duel
	// turn). Then the camera goes back to the rest of the cast if any of their pigs still have to act.
	public IEnumerator AfterPigTurn()
	{
		if (!m_JustStarted)
		{
			yield break;
		}
		m_JustStarted = false;
		if (!IsActive || BattleOver())
		{
			yield break;
		}
		var porkyActed = false;
		if (!DuelBroken())
		{
			Isolate(Side.Duel);
			m_BattleMgr.m_BattleUI.ShowTurnIndicator(Faction.Pigs, Faction.Birds);
			yield return StartCoroutine(BirdTurn());
			if (IsActive && !BattleOver() && !DuelBroken())
			{
				m_BattleMgr.m_BattleUI.ShowTurnIndicator(Faction.Birds, Faction.Pigs);
				yield return StartCoroutine(PorkyFirstDuelTurn());
				porkyActed = true;
			}
			EndIsolation();
		}
		if (!IsActive || BattleOver())
		{
			yield break;
		}
		m_TurnsLeft--;
		if (m_TurnsLeft <= 0 || DuelBroken())
		{
			yield return StartCoroutine(Recombine());
			yield break;
		}
		var mainPigsLeft = Model.m_CombatantsNotActed.Any(c => c.CombatantFaction == Faction.Pigs && c.IsParticipating && !IsDuelist(c));
		if (mainPigsLeft && MainGroupCanFight())
		{
			yield return StartCoroutine(PanTo(false));
		}
		if (!porkyActed)
		{
			m_BattleMgr.m_BattleUI.ShowTurnIndicator(Faction.Birds, Faction.Pigs);
		}
	}

	// A second go in the pigs' turn he started the duel in: his turn-start effects already ran with the first one.
	private IEnumerator PorkyFirstDuelTurn()
	{
		var porky = Porky;
		porky.StartedHisTurn = false;
		porky.ActedThisTurn = false;
		yield return m_BattleMgr.StartCoroutine(m_BattleMgr.RunCombatantTurn(porky));
		porky.ActedThisTurn = true;
	}

	// The battle or wave is decided: put everyone back together before the end sequence.
	public IEnumerator Dissolve()
	{
		EndIsolation();
		if (IsActive)
		{
			yield return StartCoroutine(Recombine());
		}
	}

	// Porky walks back to his slot with the bird 0.3 s behind him; the camera follows Porky (or the bird if Porky is down).
	// A knocked out bird is put back on its slot unseen.
	private IEnumerator Recombine()
	{
		EndIsolation();
		m_JustStarted = false;
		var porkyView = Porky != null && Porky.IsParticipating ? Porky.CombatantView : null;
		var birdView = Bird != null && Bird.IsParticipating ? Bird.CombatantView : null;
		ShiftAnchors(false);
		UndoLineUp();
		var walks = new List<Coroutine>();
		CharacterControllerBattleGroundBase leader = null;
		var leaderTarget = Vector3.zero;
		if (porkyView != null)
		{
			var target = m_BattleMgr.m_PigCenterPosition.position + porkyView.CachedUnfocusedPos;
			walks.Add(StartCoroutine(Walk(porkyView, target, 0f, true)));
			leader = porkyView;
			leaderTarget = target;
		}
		if (Bird != null && Bird.CombatantView != null)
		{
			var target = m_BattleMgr.m_BirdCenterPosition.position + Bird.CombatantView.CachedUnfocusedPos;
			if (birdView != null)
			{
				walks.Add(StartCoroutine(Walk(birdView, target, porkyView != null ? BirdFollowDelay : 0f, false)));
				if (leader == null)
				{
					leader = birdView;
					leaderTarget = target;
				}
			}
			else
			{
				Bird.CombatantView.transform.position = target;
			}
		}
		var camera = Camera;
		if (leader != null && camera != null && m_CameraInDuelArea)
		{
			var done = false;
			var routine = StartCoroutine(WaitAll(walks, () => done = true));
			var min = Mathf.Min(0f, m_Offset.x);
			var max = Mathf.Max(0f, m_Offset.x);
			while (!done)
			{
				if (leader != null)
				{
					camera.position = m_MainCameraPosition + new Vector3(Mathf.Clamp(leader.transform.position.x - leaderTarget.x, min, max), 0f, 0f);
				}
				yield return null;
			}
			yield return routine;
			camera.position = m_MainCameraPosition;
			m_CameraInDuelArea = false;
		}
		else
		{
			var pan = StartCoroutine(PanTo(false));
			foreach (var walk in walks)
			{
				yield return walk;
			}
			yield return pan;
		}
		IsActive = false;
		Porky = null;
		Bird = null;
		BirdDamageTakenBonusPercent = 0f;
		if (m_FormationChanged)
		{
			m_FormationChanged = false;
			yield return m_BattleMgr.StartCoroutine(m_BattleMgr.PlaceCharacter(m_BattleMgr.m_PigCenterPosition, Faction.Pigs));
		}
	}

	private static IEnumerator WaitAll(List<Coroutine> routines, System.Action done)
	{
		foreach (var routine in routines)
		{
			yield return routine;
		}
		done();
	}

	private void OnDestroy()
	{
		if (m_BattleMgr != null && m_BattleMgr.Model != null)
		{
			EndIsolation();
		}
	}
}
