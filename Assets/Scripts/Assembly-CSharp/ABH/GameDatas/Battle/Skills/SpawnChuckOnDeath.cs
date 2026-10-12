using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Plays the Pig Prison cage-break cutscene (the vanilla wave-end ActionTree) as soon as the
	// cage dies, but at its KillBattlePigs step the real Chuck joins the birds instead.
	public class SpawnChuckOnDeath : SkillBattleDataBase
	{
		private const string ChuckBirdNameId = "bird_yellow";

		private const string ChuckRageSkillNameId = "bird_rage_yellowbird";

		private const string CapturedChuckObjectName = "YellowBird";

		// The cutscene was built for a wave with two pig_minion_guard_bird1 pigs; it finds them by name
		// and crashes (aborting the cutscene) if one is missing, so these are bound to real pigs up front.
		private static readonly string[] FleeingMinionObjectNames = { "MinionPig_1", "MinionPig_2" };

		private const int ChuckBirdSlotIndex = 1;

		private const float MaxWaitForAttackersSeconds = 5f;

		private const float CutsceneTimeoutSeconds = 30f;

		private const float CutsceneLeftoverCleanupDelaySeconds = 2f;

		// Share of the vanilla cutscene zoom to use; the full zoom showed the background's edges.
		private const float CameraZoomFactor = 0.6f;

		// Tree-spawned effects parented under the captured Chuck; they must outlive him.
		private static readonly string[] CutsceneEffectObjectNames = { "LightningField", "RageAura", "RageFX", "CageDestructionFX" };

		// Rage effects that sit on the captured Chuck; after the flash they move onto the real Chuck.
		private static readonly string[] ChuckFollowingEffectNames = { "LightningField", "RageAura", "RageFX" };

		private const string CapturedChuckCenterObjectName = "YellowBird_Center";

		private const string WhiteFlashObjectName = "WhiteFlash";

		private static readonly string[] CutsceneLeftoverObjectNames = { "RageAura", "RageFX", "CageDestructionFX", "LightningStrike_1", "LightningStrike_2", "LightningStrike_3", "LightningStrike_4", "LightningField", "WhiteFlash" };

		private class TransformSnapshot
		{
			public Transform Transform;

			public Transform Parent;

			public Vector3 LocalPosition;

			public Vector3 LocalScale;

			public Quaternion LocalRotation;
		}

		private bool m_Triggered;

		private bool m_FlashReached;

		private BattleMgrBase m_BattleMgr;

		private ActionTree m_Cutscene;

		private BirdCombatant m_Chuck;

		private readonly List<KeyValuePair<ICombatant, TransformSnapshot>> m_PigSnapshots = new List<KeyValuePair<ICombatant, TransformSnapshot>>();

		private readonly List<KeyValuePair<Transform, Vector3>> m_CameraPositions = new List<KeyValuePair<Transform, Vector3>>();

		private readonly List<KeyValuePair<Camera, float>> m_CameraSizes = new List<KeyValuePair<Camera, float>>();

		private List<ICombatant> m_AttackersOfCage = new List<ICombatant>();

		// Undo steps for cutscene node data patched for this run (scene objects, so undone afterwards).
		private readonly List<System.Action> m_NodePatchUndos = new List<System.Action>();

		private readonly List<KeyValuePair<GameObject, Vector3>> m_ChuckFollowingEffects = new List<KeyValuePair<GameObject, Vector3>>();

		private readonly List<GameObject> m_HiddenHealthBars = new List<GameObject>();

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DoActionInstant(battle, source, target);
			yield break;
		}

		public override void DoActionInstant(BattleGameData battle, ICombatant source, ICombatant target)
		{
			m_Battle = battle;
			m_Source = source;
			source.Defeated -= OnSourceDefeated;
			source.Defeated += OnSourceDefeated;
		}

		private void OnSourceDefeated()
		{
			m_Source.Defeated -= OnSourceDefeated;
			if (m_Triggered)
			{
				return;
			}
			// Cage was the last pig: the wave is won, so the vanilla wave-end cutscene plays untouched.
			if (!m_Battle.m_CombatantsPerFaction[Faction.Pigs].Any(p => p != m_Source && p.IsAlive))
			{
				return;
			}
			m_Triggered = true;
			m_AttackersOfCage = m_Battle.m_CombatantsPerFaction[Faction.Birds].Where(b => b.IsAttacking).ToList();
			m_BattleMgr = m_Source.CombatantView.m_BattleMgr;
			m_BattleMgr.m_ScriptedSequenceLocks++;
			m_BattleMgr.StartCoroutine(PlayCageBreakAndSpawnChuck());
		}

		private IEnumerator PlayCageBreakAndSpawnChuck()
		{
			var waited = 0f;
			while (waited < MaxWaitForAttackersSeconds && m_Battle.m_CombatantsPerFaction[Faction.Birds].Any(b => b.IsAttacking))
			{
				waited += Time.deltaTime;
				yield return null;
			}

			var cutscene = ClaimCageBreakCutscene();
			m_Cutscene = cutscene != null ? cutscene.Tree : null;
			if (m_Cutscene != null)
			{
				SnapshotSceneState();
				BindFleeingMinions();
				SuppressMinionFlee();
				AimCameraMoveAtCage();
				SetHealthBarsHidden(true);
				m_Cutscene.KillBattlePigsOverride = OnCutsceneFlash;
				cutscene.StartActionTree();
				var elapsed = 0f;
				while (!cutscene.IsDone() && elapsed < CutsceneTimeoutSeconds)
				{
					elapsed += Time.unscaledDeltaTime;
					yield return null;
				}
				if (!cutscene.IsDone())
				{
					DebugLog.Error("[SpawnChuckOnDeath] Cage-break cutscene timed out; forcing it to end.");
					m_Cutscene.StopExecution();
					m_Cutscene.node = null;
					ScreenElements.Instance.EnableStorySequence(false);
					DIContainerInfrastructure.TimeScaleMgr.AddTimeScale("FadeTimeScale", 1f);
				}
				m_Cutscene.KillBattlePigsOverride = null;
				UndoNodePatches();
			}
			if (!m_FlashReached)
			{
				OnCutsceneFlash();
			}
			while (m_Chuck == null || !m_Chuck.Entered)
			{
				yield return null;
			}
			m_BattleMgr.StartCoroutine(CleanUpCutsceneLeftoversLater());

			var rageSkill = m_Chuck.GetSkill(ChuckRageSkillNameId) as RageYellowBird;
			var rageTarget = m_Battle.m_CombatantsPerFaction[Faction.Birds].FirstOrDefault(b => b.IsAlive);
			if (rageSkill != null && rageTarget != null)
			{
				// The captured Chuck already played the rage animation in the cutscene.
				rageSkill.SkipCastAnimation = true;
				yield return m_Chuck.CombatantView.StartCoroutine(rageSkill.DoAction(m_Battle, m_Chuck, rageTarget));
				rageSkill.SkipCastAnimation = false;
			}
			// The rage leaves every bird it used at the focus position; birds that are done for this
			// turn (Chuck, and whoever broke the cage) go back like they would after a normal attack.
			var returnTime = SendFinishedBirdsToBase();
			if (returnTime > 0f)
			{
				yield return new WaitForSeconds(returnTime);
			}
			m_BattleMgr.m_ScriptedSequenceLocks--;
		}

		private float SendFinishedBirdsToBase()
		{
			var returnTime = 0f;
			foreach (var bird in m_Battle.m_CombatantsPerFaction[Faction.Birds].Where(b => b.IsParticipating && b.CombatantView != null))
			{
				if (bird == m_Chuck || bird.ActedThisTurn || m_AttackersOfCage.Contains(bird))
				{
					returnTime = Mathf.Max(returnTime, bird.CombatantView.PlayGoToBasePosition());
				}
			}
			return returnTime;
		}

		private void BindFleeingMinions()
		{
			var alivePigs = m_Battle.m_CombatantsByInitiative
				.Where(p => p.CombatantFaction == Faction.Pigs && p != m_Source && p.IsAlive && p.CombatantView != null)
				.ToList();
			var minions = alivePigs.Where(p => p.CombatantNameId.Contains("minion")).ToList();
			if (minions.Count == 0)
			{
				minions = alivePigs;
			}
			if (minions.Count == 0)
			{
				return;
			}
			for (var i = 0; i < FleeingMinionObjectNames.Length; i++)
			{
				m_Cutscene.SetRuntimeObject(FleeingMinionObjectNames[i], minions[i % minions.Count].CombatantView.gameObject);
			}
		}

		// The cutscene pans to where the cage stood in the original wave layout; aim it at the captured Chuck
		// instead, and keep the screen-sized white flash on the screen rather than on Chuck.
		private void AimCameraMoveAtCage()
		{
			var zoomNode = m_Cutscene.nodes.FirstOrDefault(n => n.type == NodeType.ZoomCamera && n.customInt3 == 0 && n.refObject is Camera);
			var moveNode = m_Cutscene.nodes.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectType == 0 && n.refObject is GameObject);
			var cageView = m_Source.CombatantView;
			if (zoomNode == null || moveNode == null || !cageView)
			{
				return;
			}
			var camera = (Camera)zoomNode.refObject;
			var originalZoom = zoomNode.customFloat;
			m_NodePatchUndos.Add(() => zoomNode.customFloat = originalZoom);
			zoomNode.customFloat = originalZoom * CameraZoomFactor;

			var focus = cageView.transform.position;
			var container = cageView.m_AssetController as CharacterContainerAssetController;
			if (container != null && container.m_ContainedCharacterRoot != null)
			{
				focus = container.m_ContainedCharacterRoot.position;
			}
			else if (cageView.m_AssetController && cageView.m_AssetController.BodyCenter != null)
			{
				focus = cageView.m_AssetController.BodyCenter.position;
			}

			// Pan toward Chuck only as far as the zoomed view stays inside the normal battle view, the one
			// area known to be fully covered by background (beyond it the camera's white clear color shows).
			var shrink = Mathf.Max(0f, -zoomNode.customFloat);
			var cameraPosition = camera.transform.position;
			var pan = new Vector3(
				Mathf.Clamp(focus.x - cameraPosition.x, -shrink * camera.aspect, shrink * camera.aspect),
				Mathf.Clamp(focus.y - cameraPosition.y, -shrink, shrink),
				0f);

			var cameraRoot = ((GameObject)moveNode.refObject).transform;
			var startBase = moveNode.refObject1 is Transform ? ((Transform)moveNode.refObject1).position : cameraRoot.position;
			var endBase = moveNode.refObject2 is Transform ? ((Transform)moveNode.refObject2).position : cameraRoot.position;
			var originalStart = moveNode.customVec1;
			var originalEnd = moveNode.customVec2;
			m_NodePatchUndos.Add(() =>
			{
				moveNode.customVec1 = originalStart;
				moveNode.customVec2 = originalEnd;
			});
			moveNode.customVec1 = cameraRoot.position - startBase;
			moveNode.customVec2 = cameraRoot.position + pan - endBase;

			CenterWhiteFlashOnCamera(camera, focus);
		}

		// The flash is a screen-sized sprite the cutscene places on Chuck; vanilla's pan put Chuck at the
		// screen center. Parent it to the camera instead (at Chuck's depth) so it always covers the screen.
		private void CenterWhiteFlashOnCamera(Camera camera, Vector3 focus)
		{
			var flashNode = m_Cutscene.nodes.FirstOrDefault(n => n.type == NodeType.Instantiate && n.objectName == WhiteFlashObjectName);
			if (flashNode == null)
			{
				return;
			}
			var originalObjectType = flashNode.objectType;
			var originalParent = flashNode.refObject2;
			var originalOffset = flashNode.customVec1;
			m_NodePatchUndos.Add(() =>
			{
				flashNode.objectType = originalObjectType;
				flashNode.refObject2 = originalParent;
				flashNode.customVec1 = originalOffset;
			});
			flashNode.objectType = 0;
			flashNode.refObject2 = camera.gameObject;
			flashNode.customVec1 = new Vector3(0f, 0f, focus.z - camera.transform.position.z);
		}

		// Turns the minions' run-away steps (reparent, flip, walk, move off) into no-op delays; they still
		// play the surprised reaction.
		private void SuppressMinionFlee()
		{
			foreach (var node in m_Cutscene.nodes)
			{
				if (node.objectType != 1 || !FleeingMinionObjectNames.Contains(node.objectName))
				{
					continue;
				}
				var isFleeStep = node.type == NodeType.SetParent || node.type == NodeType.SetScale || node.type == NodeType.MoveTo || (node.type == NodeType.PlayBoneAnimation && node.text == "Move_Loop");
				if (!isFleeStep)
				{
					continue;
				}
				var patched = node;
				var originalType = patched.type;
				var originalDelay = patched.customFloat;
				m_NodePatchUndos.Add(() =>
				{
					patched.type = originalType;
					patched.customFloat = originalDelay;
				});
				patched.type = NodeType.Delay;
				patched.customFloat = 0f;
			}
		}

		private void UndoNodePatches()
		{
			for (var i = m_NodePatchUndos.Count - 1; i >= 0; i--)
			{
				m_NodePatchUndos[i]();
			}
			m_NodePatchUndos.Clear();
		}

		// Health bars are drawn by the interface camera, which the cutscene doesn't zoom, so they'd drift off
		// their characters; hide them while the camera is zoomed.
		private void SetHealthBarsHidden(bool hidden)
		{
			if (!hidden)
			{
				foreach (var bar in m_HiddenHealthBars)
				{
					if (bar)
					{
						bar.SetActive(true);
					}
				}
				m_HiddenHealthBars.Clear();
				return;
			}
			foreach (var faction in m_Battle.m_CombatantsPerFaction.Values)
			{
				foreach (var combatant in faction.Where(c => c.IsAlive && c.CombatantView != null))
				{
					foreach (var bar in combatant.CombatantView.GetComponentsInChildren<CharacterHealthBar>())
					{
						bar.gameObject.SetActive(false);
						m_HiddenHealthBars.Add(bar.gameObject);
					}
				}
			}
		}

		private ExecuteBattleActionTree ClaimCageBreakCutscene()
		{
			var matching = Object.FindObjectsOfType<ExecuteBattleActionTree>()
				.Where(t => t.ShouldExecute(m_Battle.Balancing.NameId, m_Battle.CurrentWaveIndex, true, false))
				.ToList();
			foreach (var tree in matching)
			{
				tree.m_Consumed = true;
			}
			return matching.FirstOrDefault();
		}

		private void SnapshotSceneState()
		{
			foreach (var pig in m_Battle.m_CombatantsPerFaction[Faction.Pigs].Where(p => p != m_Source && p.IsAlive && p.CombatantView != null))
			{
				var t = pig.CombatantView.transform;
				m_PigSnapshots.Add(new KeyValuePair<ICombatant, TransformSnapshot>(pig, new TransformSnapshot
				{
					Transform = t,
					Parent = t.parent,
					LocalPosition = t.localPosition,
					LocalScale = t.localScale,
					LocalRotation = t.localRotation
				}));
			}
			foreach (var node in m_Cutscene.nodes)
			{
				if (node.type == NodeType.MoveTo && node.objectType == 0 && node.refObject is GameObject)
				{
					var t = ((GameObject)node.refObject).transform;
					m_CameraPositions.Add(new KeyValuePair<Transform, Vector3>(t, t.position));
				}
				else if (node.type == NodeType.ZoomCamera && node.customInt3 == 0 && node.refObject is Camera)
				{
					var cam = (Camera)node.refObject;
					m_CameraSizes.Add(new KeyValuePair<Camera, float>(cam, cam.orthographicSize));
				}
			}
		}

		// Runs at the cutscene's KillBattlePigs step, right under the white flash.
		private void OnCutsceneFlash()
		{
			if (m_FlashReached)
			{
				return;
			}
			m_FlashReached = true;
			HideCapturedChuck();
			RemoveCageView();
			RestoreCameras();
			RestorePigs();
			SetHealthBarsHidden(false);
			m_BattleMgr.StartCoroutine(TeleportChuckIn());
			EndCutsceneAtFlash();
		}

		// The vanilla tree goes on for a second or so after the flash (slow-motion fade, Chuck cheering);
		// cut it here instead, letting the already-spawned flash/lightning effects fade out on their own.
		private void EndCutsceneAtFlash()
		{
			if (m_Cutscene != null)
			{
				m_Cutscene.EndNow();
			}
			ScreenElements.Instance.EnableStorySequence(false);
			DIContainerInfrastructure.TimeScaleMgr.AddTimeScale("FadeTimeScale", 1f);
		}

		private void HideCapturedChuck()
		{
			GameObject capturedChuck;
			if (m_Cutscene == null || !m_Cutscene.TryGetRuntimeObject(CapturedChuckObjectName, out capturedChuck) || !capturedChuck)
			{
				return;
			}
			foreach (var effectName in CutsceneEffectObjectNames)
			{
				GameObject effect;
				if (m_Cutscene.TryGetRuntimeObject(effectName, out effect) && effect && effect.transform.IsChildOf(capturedChuck.transform))
				{
					effect.transform.SetParent(capturedChuck.transform.parent, true);
				}
			}
			GameObject capturedCenter;
			var anchor = m_Cutscene.TryGetRuntimeObject(CapturedChuckCenterObjectName, out capturedCenter) && capturedCenter ? capturedCenter.transform : capturedChuck.transform;
			foreach (var effectName in ChuckFollowingEffectNames)
			{
				GameObject effect;
				if (m_Cutscene.TryGetRuntimeObject(effectName, out effect) && effect)
				{
					m_ChuckFollowingEffects.Add(new KeyValuePair<GameObject, Vector3>(effect, effect.transform.position - anchor.position));
				}
			}
			capturedChuck.SetActive(false);
		}

		// Moves the rage effects that were on the captured Chuck onto the real one, at the same offset.
		private void AttachEffectsToChuck()
		{
			var view = m_Chuck.CombatantView;
			var anchor = view.m_AssetController && view.m_AssetController.BodyCenter != null ? view.m_AssetController.BodyCenter : view.transform;
			foreach (var entry in m_ChuckFollowingEffects)
			{
				if (!entry.Key)
				{
					continue;
				}
				entry.Key.transform.SetParent(anchor, true);
				entry.Key.transform.position = anchor.position + entry.Value;
				UnityHelper.SetLayerRecusively(entry.Key, anchor.gameObject.layer);
			}
			m_ChuckFollowingEffects.Clear();
		}

		private void RemoveCageView()
		{
			var cageView = m_Source.CombatantView;
			if (!cageView)
			{
				return;
			}
			if (cageView.m_AssetController)
			{
				DIContainerInfrastructure.GetCharacterAssetProvider(false).DestroyObject(m_Source.CombatantAssetId, cageView.m_AssetController.gameObject);
			}
			Object.Destroy(cageView.gameObject);
		}

		private void RestoreCameras()
		{
			foreach (var entry in m_CameraPositions)
			{
				if (!entry.Key)
				{
					continue;
				}
				var tween = entry.Key.GetComponentInChildren<CHMotionTween>();
				if (tween != null && tween.IsPlaying)
				{
					tween.Stop();
				}
				entry.Key.position = entry.Value;
			}
			foreach (var entry in m_CameraSizes)
			{
				if (entry.Key)
				{
					entry.Key.orthographicSize = entry.Value;
				}
			}
		}

		private void RestorePigs()
		{
			foreach (var entry in m_PigSnapshots)
			{
				var snapshot = entry.Value;
				if (!snapshot.Transform)
				{
					continue;
				}
				var tween = snapshot.Transform.GetComponentInChildren<CHMotionTween>();
				if (tween != null && tween.IsPlaying)
				{
					tween.Stop();
				}
				snapshot.Transform.SetParent(snapshot.Parent, false);
				snapshot.Transform.localPosition = snapshot.LocalPosition;
				snapshot.Transform.localScale = snapshot.LocalScale;
				snapshot.Transform.localRotation = snapshot.LocalRotation;
				var view = entry.Key.CombatantView;
				if (view != null && view.m_AssetController)
				{
					view.m_AssetController.PlayIdleAnimation();
				}
			}
		}

		private IEnumerator TeleportChuckIn()
		{
			var birds = m_Battle.m_CombatantsPerFaction[Faction.Birds];
			m_Chuck = new BirdCombatant(new BirdGameData(ChuckBirdNameId, birds[0].CharacterModel.Level));
			birds.Insert(Mathf.Min(ChuckBirdSlotIndex, birds.Count), m_Chuck);
			// BattleMgr's birds-turn loop (BattleMgr.cs ~1013) waits for every bird in initiative to have
			// ActedThisTurn; Chuck skips StartCombatantTurn, so nothing else would ever set it.
			m_Chuck.ActedThisTurn = true;
			DIContainerLogic.GetBattleService().ReCalculateInitiative(m_Battle);

			// PlaceCharacter creates Chuck's view and computes every bird's slot, then tweens the existing
			// birds there; snap everyone into place instead so the reshuffle is hidden by the flash.
			var placing = m_BattleMgr.StartCoroutine(m_BattleMgr.PlaceCharacter(m_BattleMgr.m_BirdCenterPosition, Faction.Birds));
			while (m_Chuck.CombatantView == null || !m_Chuck.CombatantView.gameObject.activeSelf)
			{
				yield return null;
			}
			yield return null;
			SnapBirdsToBasePositions();
			AttachEffectsToChuck();
			// Walking off at the end of the level goes to m_StartPosition, which only the walk-in sets;
			// Chuck teleported in, so give him the same off-screen spot PlaceCharacter picked for him.
			var startOffset = m_Chuck.CombatantView.m_InitialStartPosOffset;
			if (startOffset == Vector3.zero)
			{
				startOffset = new Vector3(0f - m_BattleMgr.m_StartPosOffset, 0f, 0f);
			}
			m_Chuck.CombatantView.SetStartPosOffset(startOffset);
			m_Chuck.Entered = true;
			m_Chuck.CombatantView.SpawnHealthBar();
			if (m_Chuck.PassiveSkill != null)
			{
				m_Chuck.CombatantView.StartCoroutine(m_Chuck.PassiveSkill.DoAction(m_Battle, m_Chuck, m_Chuck));
				m_Chuck.IsAttacking = false;
			}
			yield return placing;
		}

		private void SnapBirdsToBasePositions()
		{
			foreach (var bird in m_Battle.m_CombatantsPerFaction[Faction.Birds].Where(b => b.IsParticipating && b.CombatantView != null))
			{
				var view = bird.CombatantView;
				if (view.m_TweenMovement != null && view.m_TweenMovement.IsPlaying)
				{
					view.m_TweenMovement.Stop();
				}
				view.transform.position = m_BattleMgr.m_BirdCenterPosition.position + view.CachedUnfocusedPos;
				if (view.m_AssetController)
				{
					view.m_AssetController.PlayIdleAnimation();
				}
			}
		}

		// Most cutscene effects destroy themselves within ~1.5 s (BattleFXController.m_DestroyTime), but the
		// strong lightning strikes never do; wait so the flash isn't cut off, then remove what's left.
		private IEnumerator CleanUpCutsceneLeftoversLater()
		{
			yield return new WaitForSeconds(CutsceneLeftoverCleanupDelaySeconds);
			if (m_Cutscene == null)
			{
				yield break;
			}
			GameObject leftover;
			if (m_Cutscene.TryGetRuntimeObject(CapturedChuckObjectName, out leftover) && leftover)
			{
				Object.Destroy(leftover);
			}
			foreach (var objectName in CutsceneLeftoverObjectNames)
			{
				if (m_Cutscene.TryGetRuntimeObject(objectName, out leftover) && leftover)
				{
					Object.Destroy(leftover);
				}
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			return string.Empty;
		}

		public override string GetLocalizedName()
		{
			return string.Empty;
		}
	}
}
