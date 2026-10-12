using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using SmoothMoves;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Runs away like RunawaySkill, then returns "return_turns" pig turns later as a bird-side ally:
	// ambushes a remaining pig from behind, everyone reacts, and the ally walks over to join the birds.
	public class RunawayAndReturnAsAlly : RunawaySkill
	{
		private const string AllyBirdNameId = "bird_merchant_mcpig";

		// First castle: he returns without his Phat Loot off hand, and the birds cheer then get angry.
		private const string CastleAllyBirdNameId = "bird_merchant_mcpig_castle";

		// The return jump takes this many times as long as a regular attack jump (animation slowed to match).
		private const float JumpSlowdown = 1.5f;

		// Extra height (on top of the attack animation's own hop) added at the middle of the return jump.
		private const float ExtraJumpArcHeight = 20f;

		private float m_ReturnTurns = 3f;

		private float m_AmbushDamageMod = 1f;

		private bool m_CastleReunion;

		// 1-based wave after which he heals the birds, leaves the team and waves goodbye as the Corporal (0 = stays).
		private int m_FarewellAfterWave;

		// Same as CharacterControllerBattleGroundBase.m_BirdFocusPosOffset: where a bird steps forward to act.
		private const float FrontOfBirdsOffsetX = 300f;

		// His healing rage (Free Items): only its scattering-items effect is reused for the farewell heal.
		private const string ScatterEffectsIdent = "FreeItems";

		private const string ScatterPrefabName = "SkillFX_Spell_FreeItems";

		// Pause before he runs off, and before he comes back as the Corporal once he's offscreen.
		private const float FarewellPause = 0.2f;

		private const string FarewellPigNameId = "pig_merchant_corporal_castle";

		// Knocked out when his farewell wave was cleared: no heal, run-off or goodbye.
		private bool m_FellBeforeFarewell;

		// The scenery (pig center included) moves to the next floor before the birds walk off, so the farewell
		// floor's pig center and the Corporal's spot are captured during the cheer.
		private Vector3 m_FarewellPigCenter;

		private float m_FarewellStandX;

		private float m_OffscreenTime;

		private bool m_RunawayRegistered;

		private int m_LeftOnTurn;

		private BattleMgrBase m_BattleMgr;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			float value;
			if (base.Model.SkillParameters.TryGetValue("return_turns", out value))
			{
				m_ReturnTurns = value;
			}
			if (base.Model.SkillParameters.TryGetValue("ambush_damage_in_percent", out value))
			{
				m_AmbushDamageMod = value / 100f;
			}
			if (base.Model.SkillParameters.TryGetValue("castle_reunion", out value))
			{
				m_CastleReunion = value > 0f;
			}
			if (base.Model.SkillParameters.TryGetValue("farewell_after_wave", out value))
			{
				m_FarewellAfterWave = (int)value;
			}
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			m_Battle = battle;
			if (!m_RunawayRegistered)
			{
				m_RunawayRegistered = true;
				m_BattleMgr = source.CombatantView.m_BattleMgr;
				source.Runaway += OnSourceRunaway;
			}
			yield return source.CombatantView.StartCoroutine(base.DoAction(battle, source, target, shared, illusion));
		}

		private void OnSourceRunaway()
		{
			m_Source.Runaway -= OnSourceRunaway;
			m_LeftOnTurn = m_Battle.m_CurrentTurn;
			m_Battle.PigsTurnStarted += OnPigsTurnStarted;
		}

		private void OnPigsTurnStarted(int turn)
		{
			if (turn - m_LeftOnTurn < m_ReturnTurns)
			{
				return;
			}
			m_Battle.PigsTurnStarted -= OnPigsTurnStarted;
			var previousSummon = m_BattleMgr.m_CurrentSummonAction;
			m_BattleMgr.m_CurrentSummonAction = () => ReturnAfter(previousSummon);
		}

		private IEnumerator ReturnAfter(System.Func<IEnumerator> previousSummon)
		{
			if (previousSummon != null)
			{
				yield return m_BattleMgr.StartCoroutine(previousSummon());
			}
			yield return m_BattleMgr.StartCoroutine(ReturnAsAlly());
		}

		private IEnumerator ReturnAsAlly()
		{
			var mgr = m_BattleMgr;
			var battle = m_Battle;
			var pacing = DIContainerLogic.GetPacingBalancing();
			var victim = m_CastleReunion ? PickCastleVictim(battle) : battle.m_CombatantsPerFaction[Faction.Pigs].FirstOrDefault(p => p.IsAlive && p.IsParticipating && p.CombatantView != null);
			var birds = battle.m_CombatantsPerFaction[Faction.Birds].Where(b => b.IsAlive && b.CombatantView != null).ToList();
			// Every pig on the field turns around to look at him, not just the one he hits.
			var startledPigs = new List<ICombatant>();
			// The same bird the Corporal took his max HP and attack from, at his level (see PigGameData.AllyBird).
			var sourcePig = m_Source.CharacterModel as PigGameData;
			var allyBird = sourcePig != null ? sourcePig.AllyBird : null;
			var ally = new BirdCombatant(allyBird ?? new BirdGameData(m_CastleReunion ? CastleAllyBirdNameId : AllyBirdNameId, m_Source.CharacterModel.Level));

			// Spawn offscreen to the right, level with the victim, facing left.
			var view = Object.Instantiate(mgr.m_CharacterControllerBattlegroundPrefab, mgr.m_PigCenterPosition.position, Quaternion.identity) as CharacterControllerBattleGroundBase;
			view.transform.parent = mgr.m_BattleArea;
			view.SetModel(ally, mgr);
			// Birds are always scale 1; match the size he had as the pig who ran away.
			var sizeScale = m_Source.CharacterModel.Scale;
			view.transform.localScale = new Vector3(view.transform.localScale.x * sizeScale, view.transform.localScale.y * sizeScale, view.transform.localScale.z);
			UnityHelper.SetLayerRecusively(view.gameObject, LayerMask.NameToLayer("Scenery"));
			var anchor = victim != null ? victim.CombatantView.transform.position : mgr.m_PigCenterPosition.position;
			view.transform.position = anchor + new Vector3(mgr.m_StartPosOffset, 0f, 0f);
			SetFacing(view, false);
			// The rig's Animation auto-plays Idle on its first frame, which would override anything started now
			// (and the sound tags inside it).
			yield return null;

			// 1. Attack from offscreen like a regular melee attack: the attack animation's "Movement" tag jumps him
			// behind the victim, its "Impact" tag lands the hit; the animation's own tags play the jump/attack sounds.
			if (victim != null)
			{
				var victimView = victim.CombatantView;
				// Same z offset regular melee attacks use, so the two rigs never share a depth (z-fighting).
				var behind = new Vector3(view.m_AssetController.OffsetFromEnemyX + victimView.m_AssetController.OffsetFromEnemyX * Mathf.Abs(view.m_AssetController.transform.localScale.x), 0f, view.OffsetFromEnemyZ);
				// Same visual effect setting as his regular attack skill.
				var attackSkill = ally.GetSkill(0);
				VisualEffectSetting attackEffects;
				DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting(attackSkill.Model.Balancing.AssetId, out attackEffects);
				var victimList = new List<ICombatant> { victim };
				var impacted = false;
				UserTriggerDelegate onTrigger = triggerEvent =>
				{
					if (triggerEvent.tag == "Movement")
					{
						var tween = view.m_TweenMovement;
						tween.m_StartTransform = view.transform;
						tween.m_StartOffset = Vector3.zero;
						tween.m_EndTransform = victimView.transform;
						tween.m_EndOffset = behind;
						tween.m_DurationInSeconds = pacing.TimeFromFocusPosToAttackPosInSec * JumpSlowdown;
						tween.Play();
						view.StartCoroutine(ExtraJumpArc(view.m_AssetController.transform, tween.m_DurationInSeconds));
						SpawnEffects(mgr, ally, attackEffects, VisualEffectSpawnTiming.Movement, victimList);
					}
					else if (triggerEvent.tag == "Impact")
					{
						impacted = true;
					}
				};
				var boneAnimation = view.m_AssetController.m_BoneAnimation;
				boneAnimation.RegisterUserTriggerDelegate(onTrigger);
				var attackDuration = view.PlayAttackAnimation() * JumpSlowdown;
				// Slow the attack animation by the same factor as the jump so its hit and sounds still land on arrival.
				var attackState = boneAnimation[AttackAnimationName(ally)];
				var originalAttackSpeed = attackState != null ? attackState.speed : 1f;
				if (attackState != null)
				{
					attackState.speed = originalAttackSpeed / JumpSlowdown;
				}
				var elapsed = 0f;
				while (!impacted && elapsed < pacing.FallBackTimeWaitForSkillImpact * JumpSlowdown)
				{
					elapsed += Time.deltaTime;
					yield return null;
				}
				boneAnimation.UnregisterUserTriggerDelegate(onTrigger);
				if (!impacted)
				{
					view.transform.position = victimView.transform.position + behind;
				}
				var damage = Mathf.Min(ally.ModifiedAttack * m_AmbushDamageMod, victim.CurrentHealth - 1f);
				if (damage > 0f)
				{
					victim.ReceiveDamage(damage, ally);
					DIContainerLogic.GetBattleService().DealDamageFromCurrentTurn(victim, battle, ally, true);
				}
				SpawnEffects(mgr, ally, attackEffects, VisualEffectSpawnTiming.Impact, victimList);
				// His attack skill's steal (2 snoutlings, dropped from the victim).
				attackSkill.ShareableActionPart(battle, ally, victim, false);

				// 2. Victim flinches; every pig turns around and is surprised; the birds are surprised (cheer at the castle); the ally cheers.
				var hitDuration = victimView.PlayHitAnimation();
				yield return new WaitForSeconds(Mathf.Max(hitDuration, attackDuration - elapsed));
				if (attackState != null)
				{
					attackState.speed = originalAttackSpeed;
				}
				startledPigs.AddRange(battle.m_CombatantsPerFaction[Faction.Pigs].Where(p => p.IsAlive && p.IsParticipating && p.CombatantView != null));
				var surprisedDuration = 0f;
				foreach (var pig in startledPigs)
				{
					SetFacing(pig.CombatantView, true);
					surprisedDuration = Mathf.Max(surprisedDuration, pig.CombatantView.PlaySurprisedAnimation());
				}
				foreach (var bird in birds)
				{
					surprisedDuration = Mathf.Max(surprisedDuration, m_CastleReunion ? bird.CombatantView.PlayCheerCharacter() : bird.CombatantView.PlaySurprisedAnimation());
				}
				yield return new WaitForSeconds(surprisedDuration);
				var allyCheerDuration = view.PlayCheerCharacter();
				PlayRigSound(view, "PlaySound_Attack_1");
				yield return new WaitForSeconds(allyCheerDuration);
			}

			// 3. Join the birds: take a slot, walk over to it while the birds shuffle, then face the pigs.
			ally.ActedThisTurn = true;
			ally.Entered = true;
			battle.m_CombatantsPerFaction[Faction.Birds].Add(ally);
			DIContainerLogic.GetBattleService().ReCalculateInitiative(battle);
			var oldSlot = view.CachedUnfocusedPos;
			var placing = mgr.StartCoroutine(mgr.PlaceCharacter(mgr.m_BirdCenterPosition, Faction.Birds));
			var waited = 0f;
			while (view.CachedUnfocusedPos == oldSlot && waited < 1f)
			{
				waited += Time.deltaTime;
				yield return null;
			}
			foreach (var pig in startledPigs.Where(p => p.CombatantView != null))
			{
				SetFacing(pig.CombatantView, false);
			}
			// The remaining pig(s) re-space at the same time (e.g. the lone Corporal moves to the single-enemy spot).
			var pigPlacing = mgr.StartCoroutine(mgr.PlaceCharacter(mgr.m_PigCenterPosition, Faction.Pigs));
			yield return new WaitForSeconds(view.PlayGoToPosition(view.CachedUnfocusedPos, pacing.BaseBattleEnterDelay, false, mgr.m_BirdCenterPosition));
			yield return placing;
			yield return pigPlacing;
			SetFacing(view, true);
			// Birds enter from (and leave to) the left; without this he'd stay put while the birds walk off to climb a floor.
			view.SetStartPosOffset(new Vector3(0f - mgr.m_StartPosOffset, 0f, 0f));
			view.SpawnHealthBar();
			if (ally.PassiveSkill != null)
			{
				view.StartCoroutine(ally.PassiveSkill.DoAction(battle, ally, ally));
				ally.IsAttacking = false;
			}

			// 4. The birds cheer (get angry at the castle).
			var cheerDuration = 0f;
			foreach (var bird in birds.Where(b => b.IsAlive && b.CombatantView != null))
			{
				cheerDuration = Mathf.Max(cheerDuration, m_CastleReunion ? bird.CombatantView.PlayRageAnimation() : bird.CombatantView.PlayCheerCharacter());
			}
			yield return new WaitForSeconds(cheerDuration);

			if (m_FarewellAfterWave > 0)
			{
				mgr.m_WaveClearedInterludes[m_FarewellAfterWave - 1] = new BattleMgrBase.WaveClearedInterlude
				{
					BeforeCheer = () => StepForwardAndHeal(ally, view),
					DuringCheer = () => RunOffRight(ally, view),
					DuringBirdsLeave = WaveGoodbyeAsCorporal
				};
			}
		}

		// Whether this pig has this skill, i.e. runs off and comes back on the birds' side.
		public static bool DefectsToBirds(ICombatant combatant)
		{
			var pig = combatant.CharacterModel as PigGameData;
			return pig != null && GetAllyBirdNameId(pig.BalancingData) != null;
		}

		// The bird a pig with this skill comes back as, or null if it has no such skill.
		public static string GetAllyBirdNameId(ABH.Shared.BalancingData.PigBalancingData pig)
		{
			if (pig == null || pig.SkillNameIds == null)
			{
				return null;
			}
			foreach (var id in pig.SkillNameIds)
			{
				var skill = DIContainerBalancing.Service.GetBalancingData<ABH.Shared.BalancingData.SkillBalancingData>(id);
				if (skill != null && skill.SkillTemplateType == typeof(RunawayAndReturnAsAlly).Name)
				{
					float castle;
					return skill.SkillParameters != null && skill.SkillParameters.TryGetValue("castle_reunion", out castle) && castle > 0f ? CastleAllyBirdNameId : AllyBirdNameId;
				}
			}
			return null;
		}

		// Castle: only pigs in the rightmost row can be ambushed; of those, the one with the most health.
		private static ICombatant PickCastleVictim(BattleGameData battle)
		{
			var pigs = battle.m_CombatantsPerFaction[Faction.Pigs].Where(p => p.IsAlive && p.IsParticipating && p.CombatantView != null).ToList();
			if (pigs.Count == 0)
			{
				return null;
			}
			var rightmostRow = pigs.Max(p => p.CombatantView.m_PlacedRow);
			return pigs.Where(p => p.CombatantView.m_PlacedRow == rightmostRow).OrderByDescending(p => p.CurrentHealth).First();
		}

		// Farewell 1+2: leaves the team (the birds re-space for two), steps in front of them and heals them to full.
		private IEnumerator StepForwardAndHeal(BirdCombatant ally, CharacterControllerBattleGroundBase view)
		{
			var mgr = m_BattleMgr;
			var battle = m_Battle;
			var pacing = DIContainerLogic.GetPacingBalancing();
			battle.m_CombatantsPerFaction[Faction.Birds].Remove(ally);
			DIContainerLogic.GetBattleService().ReCalculateInitiative(battle);
			if (view == null)
			{
				m_FellBeforeFarewell = true;
				yield break;
			}
			view.ActivateControlHUD(false);
			foreach (var healthBar in view.GetComponentsInChildren<CharacterHealthBar>(true))
			{
				healthBar.gameObject.SetActive(false);
			}
			if (!ally.IsAlive)
			{
				m_FellBeforeFarewell = true;
				view.DestroyCharacterAfter(0f);
				yield break;
			}
			var placing = mgr.StartCoroutine(mgr.PlaceCharacter(mgr.m_BirdCenterPosition, Faction.Birds));
			SetFacing(view, true);
			yield return new WaitForSeconds(view.PlayGoToPosition(new Vector3(FrontOfBirdsOffsetX, 0f, view.OffsetFromEnemyZ), pacing.BaseBattleEnterDelay, false, mgr.m_BirdCenterPosition));
			yield return placing;
			SetFacing(view, false);

			var birds = battle.m_CombatantsPerFaction[Faction.Birds].Where(b => b.IsAlive && b.CombatantView != null).ToList();
			var supportDuration = view.PlaySupportAnimation();
			// The support clip's own sound tag has no sound on his rig; same sounds as his cheer when he joins.
			PlayRigSound(view, "PlaySound_Cheer");
			PlayRigSound(view, "PlaySound_Attack_1");
			yield return new WaitForSeconds(supportDuration * 0.5f);
			SpawnScatteredItems(mgr, ally, birds);
			foreach (var bird in birds)
			{
				var missing = Mathf.Ceil(bird.ModifiedHealth - bird.CurrentHealth);
				if (missing > 0f)
				{
					bird.HealDamage(missing, ally);
					DIContainerLogic.GetBattleService().HealCurrentTurn(bird, battle, true, true, true);
				}
			}
			yield return new WaitForSeconds(supportDuration * 0.5f);
		}

		// Farewell 3: while the birds cheer, he runs off to the right and is gone.
		private IEnumerator RunOffRight(BirdCombatant ally, CharacterControllerBattleGroundBase view)
		{
			var mgr = m_BattleMgr;
			m_FarewellPigCenter = mgr.m_PigCenterPosition.position;
			// A quarter as far from the right edge of the screen as the pig center.
			var rightEdgeX = ScreenRightEdgeX(m_FarewellPigCenter, m_FarewellPigCenter.x);
			m_FarewellStandX = rightEdgeX - (rightEdgeX - m_FarewellPigCenter.x) * 0.25f;
			if (view == null || !ally.IsAlive)
			{
				yield break;
			}
			yield return new WaitForSeconds(FarewellPause);
			SetFacing(view, true);
			var offscreenX = m_FarewellPigCenter.x + mgr.m_StartPosOffset;
			var duration = view.PlayGoToPosition(new Vector3(offscreenX - view.transform.position.x, 0f, 0f), DIContainerLogic.GetPacingBalancing().BaseBattleEndDelay, true);
			yield return new WaitForSeconds(duration);
			m_OffscreenTime = Time.time;
			view.DestroyCharacterAfter(0f);
		}

		// The items his healing rage throws, spawned right away (in the rage they wait for its longer animation).
		private static void SpawnScatteredItems(BattleMgrBase mgr, ICombatant source, List<ICombatant> targets)
		{
			VisualEffectSetting rageEffects;
			if (!DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting(ScatterEffectsIdent, out rageEffects))
			{
				return;
			}
			var scatter = rageEffects.VisualEffects.FirstOrDefault(e => e.PrefabName == ScatterPrefabName);
			if (scatter == null)
			{
				return;
			}
			// Copy so the shared balancing entry keeps its delay.
			var immediate = JsonUtility.FromJson<VisualEffect>(JsonUtility.ToJson(scatter));
			immediate.DelayInSeconds = 0f;
			mgr.InstantiateEffect(source, immediate, rageEffects, targets, false);
		}

		// World x of the right screen edge at the depth of point, seen by the camera that draws the battle scenery.
		private static float ScreenRightEdgeX(Vector3 point, float fallback)
		{
			var layer = LayerMask.NameToLayer("Scenery");
			var camera = Camera.allCameras.FirstOrDefault(c => (c.cullingMask & (1 << layer)) != 0);
			if (camera == null)
			{
				return fallback;
			}
			var depth = Vector3.Dot(point - camera.transform.position, camera.transform.forward);
			return camera.ViewportToWorldPoint(new Vector3(1f, 0.5f, depth)).x;
		}

		// Farewell 4: as the birds walk off to climb, he comes back in from the right as the Corporal and waves goodbye.
		// He stays behind on this floor (unparented from the battle area, which moves up with the birds).
		// The camera waits for his walk in, not for the wave.
		private IEnumerator WaveGoodbyeAsCorporal()
		{
			if (m_FellBeforeFarewell)
			{
				yield break;
			}
			var mgr = m_BattleMgr;
			var pacing = DIContainerLogic.GetPacingBalancing();
			var pause = m_OffscreenTime + FarewellPause - Time.time;
			if (pause > 0f)
			{
				yield return new WaitForSeconds(pause);
			}
			var corporal = new PigCombatant(new PigGameData(FarewellPigNameId, m_Source.CharacterModel.Level));
			var view = Object.Instantiate(mgr.m_CharacterControllerBattlegroundPrefab, m_FarewellPigCenter, Quaternion.identity) as CharacterControllerBattleGroundBase;
			view.transform.parent = mgr.m_BattleArea;
			view.SetModel(corporal, mgr);
			UnityHelper.SetLayerRecusively(view.gameObject, LayerMask.NameToLayer("Scenery"));
			view.transform.position = m_FarewellPigCenter + new Vector3(mgr.m_StartPosOffset, 0f, 0f);
			view.transform.SetParent(null, true);
			SetFacing(view, false);
			// Let the rig's auto-played Idle pass before starting anything (see ReturnAsAlly).
			yield return null;
			// Constant speed: the pace of a regular walk in from offscreen (start offset per enter delay).
			var walkDistance = m_FarewellStandX - view.transform.position.x;
			var walkSpeed = mgr.m_StartPosOffset / pacing.BaseBattleEnterDelay;
			yield return new WaitForSeconds(view.PlayGoToPosition(new Vector3(walkDistance, 0f, 0f), Mathf.Abs(walkDistance) / walkSpeed, false));
			// The walk's switch back to Idle fires at the same moment; let it go first so it can't cut the wave off.
			yield return null;
			var waveDuration = view.PlaySupportAnimation();
			// Gone once he's done waving and the camera has left this floor.
			view.DestroyCharacterAfter(Mathf.Max(waveDuration, pacing.ScenerySwitchTimeWalk) + 0.5f);
		}

		private static void SpawnEffects(BattleMgrBase mgr, ICombatant source, VisualEffectSetting setting, VisualEffectSpawnTiming timing, List<ICombatant> targets)
		{
			if (setting == null)
			{
				return;
			}
			foreach (var effect in setting.VisualEffects)
			{
				if (effect.SpawnTiming == timing)
				{
					mgr.InstantiateEffect(source, effect, setting, targets, false);
				}
			}
		}

		private static string AttackAnimationName(ICombatant combatant)
		{
			var mainHand = combatant.CharacterModel.MainHandItem;
			return "Action_Attack_" + (mainHand == null ? 1 : mainHand.BalancingData.AnimationIndex);
		}

		private static IEnumerator ExtraJumpArc(Transform rig, float duration)
		{
			var basePosition = rig.localPosition;
			var elapsed = 0f;
			while (elapsed < duration && rig != null)
			{
				rig.localPosition = basePosition + new Vector3(0f, Mathf.Sin(Mathf.PI * elapsed / duration) * ExtraJumpArcHeight, 0f);
				elapsed += Time.deltaTime;
				yield return null;
			}
			if (rig != null)
			{
				rig.localPosition = basePosition;
			}
		}

		// Plays a sound through the rig's own trigger-tag -> sound table, as if the animation had fired the tag.
		private static void PlayRigSound(CharacterControllerBattleGroundBase view, string triggerTag)
		{
			var sounds = view.m_AssetController.GetComponent<TriggerSoundByBoneAnimation>();
			if (sounds != null)
			{
				sounds.OnTriggerEventFired(new UserTriggerEvent { tag = triggerTag });
			}
		}

		// Pig assets face left at positive x scale, bird assets face right.
		private static void SetFacing(CharacterControllerBattleGroundBase view, bool faceRight)
		{
			var scale = view.transform.localScale;
			var rightSign = view.m_AssetController.m_AssetFaction == AssetFaction.Bird ? 1f : -1f;
			var newX = Mathf.Abs(scale.x) * (faceRight ? rightSign : -rightSign);
			if (Mathf.Sign(newX) != Mathf.Sign(scale.x))
			{
				// Counter-flip health bars so turning around doesn't mirror them.
				foreach (var healthBar in view.GetComponentsInChildren<CharacterHealthBar>(true))
				{
					var barScale = healthBar.transform.localScale;
					healthBar.transform.localScale = new Vector3(-barScale.x, barScale.y, barScale.z);
				}
			}
			view.transform.localScale = new Vector3(newX, scale.y, scale.z);
		}
	}
}
