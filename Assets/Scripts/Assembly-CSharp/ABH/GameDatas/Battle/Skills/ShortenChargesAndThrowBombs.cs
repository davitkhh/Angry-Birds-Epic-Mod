using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	public class ShortenChargesAndThrowBombs : SkillBattleDataBase
	{
		// Action_Rage (Holy Hand Grenade) is 38 frames at 24 fps. The bomb stays in Porky's hand until it ends.
		private const string ThrowAnimation = "Action_Rage";

		private const float ThrowFps = 24f;

		// Throws after the first one skip the windup and start at this frame.
		private const float LaterThrowStartFrame = 26f;

		private const float PickUpSpeedUp = 0.6f;

		private const float FlightTime = 0.4f;

		// Porky heads for the next bomb once the previous one is this far through its flight.
		private const float NextRunAtFlightFraction = 0.5f;

		private float m_TurnsToShorten;

		private float m_ChargeRounds;

		private bool m_All;

		private int m_PendingImpacts;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			base.Model.SkillParameters.TryGetValue("reduce_charge_turns", out m_TurnsToShorten);
			base.Model.SkillParameters.TryGetValue("charge", out m_ChargeRounds);
			m_All = base.Model.SkillParameters.ContainsKey("all");
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			m_Battle = battle;
			m_InitialTarget = target;
			var source2 = source;
			m_Targets = !m_All
				? new List<ICombatant> { target }
				: battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == source2.CombatantFaction && c != source2 && c.IsParticipating).ToList();
			if (m_Targets.Count == 0)
			{
				m_Targets = new List<ICombatant> { source };
			}
			if (EvaluateCharge(battle, source, m_Targets, target))
			{
				m_Source.CombatantView.targetSheltered = null;
				yield break;
			}
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting, m_Targets);
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			m_PendingImpacts = 0;
			var throwables = new List<ICombatant>();
			foreach (var ally in m_Targets)
			{
				var chargeEffect = GetChargeEffect(ally);
				if (chargeEffect == null)
				{
					// A bomb that has not started charging yet goes from 0 to 1.
					chargeEffect = StartFuse(battle, ally);
					if (chargeEffect == null)
					{
						continue;
					}
				}
				// IncreaseChargeByTurns delays a charge, so a negative amount shortens it.
				chargeEffect.IncreaseChargeByTurns(-(int)m_TurnsToShorten);
				SpawnVisualEffects(VisualEffectSpawnTiming.Affected, m_VisualEffectSetting, new List<ICombatant> { ally });
				if (chargeEffect.GetTurnsLeft() <= 0 && IsThrowable(ally.ChachedSkill))
				{
					throwables.Add(ally);
				}
			}
			if (throwables.Count == 0)
			{
				yield break;
			}
			yield return new WaitForSeconds(DIContainerLogic.GetPacingBalancing().TimeForFocusInituativeAndDOTS * 0.5f);
			var thrower = source.CombatantView;
			for (var i = 0; i < throwables.Count; i++)
			{
				yield return thrower.StartCoroutine(ThrowBomb(battle, source, throwables[i], i > 0));
			}
			while (m_PendingImpacts > 0)
			{
				yield return null;
			}
		}

		private IEnumerator ThrowBomb(BattleGameData battle, ICombatant source, ICombatant bomb, bool skipWindup)
		{
			var thrower = source.CombatantView;
			var bombView = bomb.CombatantView;
			var bombSkill = bomb.ChachedSkill;
			if (thrower == null || bombView == null || bombSkill == null || !bomb.IsParticipating)
			{
				yield break;
			}
			var healingBomb = bombSkill is SelfDestructAndHeal;
			var thrownPig = bombSkill is ThrowMainHandAttack;
			var birds = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction != bomb.CombatantFaction && c.IsParticipating).ToList();
			if (!healingBomb && birds.Count == 0)
			{
				yield break;
			}
			var side = Mathf.Sign(thrower.transform.position.x - bombView.transform.position.x);
			if (side == 0f)
			{
				side = 1f;
			}
			var pickUpOffset = new Vector3(side * (thrower.m_AssetController.OffsetFromEnemyX + bombView.m_AssetController.OffsetFromEnemyX) * Mathf.Abs(thrower.m_AssetController.transform.localScale.x), 0f, thrower.OffsetFromEnemyZ);
			var runTime = DIContainerLogic.GetPacingBalancing().TimeFromFocusPosToAttackPosInSec * (!skipWindup ? 1f : PickUpSpeedUp);
			yield return new WaitForSeconds(thrower.PlayGoToPosition(pickUpOffset, runTime, true, bombView.transform));
			var hand = thrower.m_AssetController.MainHandBone != null ? thrower.m_AssetController.MainHandBone : thrower.m_AssetController.BodyCenter;
			if (bombView.m_AssetController.HasAnimation("Surprised"))
			{
				bombView.PlaySurprisedAnimation();
			}
			var throwTimeLeft = PlayThrowAnimation(thrower, skipWindup);
			while (throwTimeLeft > 0f)
			{
				bombView.transform.position = hand.position;
				throwTimeLeft -= Time.deltaTime;
				yield return null;
			}
			var landingPosition = healingBomb
				? thrower.m_AssetController.BodyCenter.position
				: thrower.m_BattleMgr.m_BirdCenterPosition.position;
			m_PendingImpacts++;
			thrower.StartCoroutine(FlyAndImpact(thrower, battle, source, bomb, bombSkill, hand.position, landingPosition, healingBomb, thrownPig, birds));
			yield return new WaitForSeconds(FlightTime * NextRunAtFlightFraction);
		}

		// Runs on its own so Porky can already be on his way to the next bomb.
		private IEnumerator FlyAndImpact(CharacterControllerBattleGroundBase thrower, BattleGameData battle, ICombatant source, ICombatant bomb, SkillBattleDataBase bombSkill, Vector3 startPosition, Vector3 landingPosition, bool healingBomb, bool thrownPig, List<ICombatant> birds)
		{
			yield return thrower.StartCoroutine(FlyToTarget(bomb.CombatantView, startPosition, landingPosition));
			if (thrownPig)
			{
				yield return thrower.StartCoroutine(ThrownPigImpact(thrower, battle, bomb, bombSkill, birds));
			}
			else
			{
				bomb.ChargeDone = true;
				yield return thrower.StartCoroutine(bombSkill.DoAction(battle, bomb, healingBomb ? source : birds[0]));
			}
			m_PendingImpacts--;
		}

		// The first throw plays the whole windup. Later throws skip ahead into the animation.
		private static float PlayThrowAnimation(CharacterControllerBattleGroundBase thrower, bool skipWindup)
		{
			var length = thrower.PlayRageSkillAnimation();
			if (!skipWindup || length <= 0f)
			{
				return length;
			}
			var state = thrower.m_AssetController.m_BoneAnimation != null ? thrower.m_AssetController.m_BoneAnimation[ThrowAnimation] : null;
			if (state == null)
			{
				return length;
			}
			var startTime = LaterThrowStartFrame / ThrowFps;
			state.time = startTime;
			return Mathf.Max(length - startTime, 0f);
		}

		// A pig that gets thrown (the Dynamite Rogue) goes off on the birds and does not survive the trip.
		private IEnumerator ThrownPigImpact(CharacterControllerBattleGroundBase thrower, BattleGameData battle, ICombatant pig, SkillBattleDataBase pigSkill, List<ICombatant> birds)
		{
			float damagePercent;
			if (!pigSkill.Model.SkillParameters.TryGetValue("damage_in_percent", out damagePercent))
			{
				damagePercent = 100f;
			}
			var damage = pig.ModifiedAttack * damagePercent / 100f;
			var pigView = pig.CombatantView;
			SpawnBlast(thrower, pig, pigSkill.Model.Balancing.AssetId, birds);
			var service = DIContainerLogic.GetBattleService();
			foreach (var bird in birds)
			{
				service.ApplyEffectsOnTriggerType(1f, EffectTriggerType.BeforeDealDamage, pig, pig);
				service.ApplyEffectsOnTriggerType(1f, EffectTriggerType.BeforeReceiveDamage, bird, pig);
				var dealt = service.ApplyEffectsOnTriggerType(1f, EffectTriggerType.OnDealDamage, pig, bird);
				var received = service.ApplyEffectsOnTriggerType(1f, EffectTriggerType.OnReceiveDamage, bird, pig);
				var finalDamage = damage * dealt * received;
				bird.ReceiveDamage(finalDamage, pig);
				service.DealDamageFromCurrentTurn(bird, battle, pig);
				service.ApplyEffectsOnTriggerType(finalDamage, EffectTriggerType.AfterReceiveDamage, bird, pig);
			}
			pig.ChargeDone = false;
			pig.ChachedSkill = null;
			pig.ReceiveDamage(pig.CurrentHealth, pig);
			service.DealDamageFromCurrentTurn(pig, battle, pig);
			if (pigView != null && pigView.m_AssetController != null)
			{
				yield return new WaitForSeconds(pigView.m_AssetController.GetDefeatAnimationLength());
			}
		}

		private static void SpawnBlast(CharacterControllerBattleGroundBase thrower, ICombatant pig, string effectAssetId, List<ICombatant> targets)
		{
			VisualEffectSetting setting;
			if (!DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting(effectAssetId, out setting))
			{
				return;
			}
			foreach (var visualEffect in setting.VisualEffects.Where(e => e.SpawnTiming == VisualEffectSpawnTiming.Start))
			{
				thrower.m_BattleMgr.InstantiateEffect(pig, visualEffect, setting, targets, false);
			}
		}

		private static IEnumerator FlyToTarget(CharacterControllerBattleGroundBase bombView, Vector3 startPosition, Vector3 endPosition)
		{
			var arcHeight = Mathf.Max(Vector3.Distance(startPosition, endPosition) * 0.35f, bombView.m_AssetController.OffsetFromEnemyX * 1.5f);
			var elapsedTime = 0f;
			while (elapsedTime < FlightTime)
			{
				elapsedTime += Time.deltaTime;
				var progress = Mathf.Clamp01(elapsedTime / FlightTime);
				var position = Vector3.Lerp(startPosition, endPosition, progress);
				position.y += Mathf.Sin(progress * Mathf.PI) * arcHeight;
				bombView.transform.position = position;
				yield return null;
			}
			bombView.transform.position = endPosition;
		}

		// Starts the charge on a bomb that has not had a turn yet, so there is a counter to shorten.
		private static BattleEffectGameData StartFuse(BattleGameData battle, ICombatant ally)
		{
			if (ally.ChargeDone)
			{
				return null;
			}
			var fuseSkill = ally.GetSkills().FirstOrDefault(s => s.Model.SkillParameters != null && s.Model.SkillParameters.ContainsKey("charge") && IsThrowable(s));
			if (fuseSkill == null)
			{
				return null;
			}
			var affectedFaction = fuseSkill.Model.Balancing.TargetType == SkillTargetTypes.Support
				? ally.CombatantFaction
				: (ally.CombatantFaction == Faction.Pigs ? Faction.Birds : Faction.Pigs);
			var bubbleTargets = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == affectedFaction).ToList();
			if (bubbleTargets.Count == 0)
			{
				return null;
			}
			fuseSkill.EvaluateCharge(battle, ally, bubbleTargets, null);
			return GetChargeEffect(ally);
		}

		private static BattleEffectGameData GetChargeEffect(ICombatant combatant)
		{
			if (combatant.CurrrentEffects == null)
			{
				return null;
			}
			foreach (var effectGameData in combatant.CurrrentEffects.Values)
			{
				for (var i = 0; i < effectGameData.m_Effects.Count; i++)
				{
					if (effectGameData.m_Effects[i].EffectType == BattleEffectType.Charge)
					{
						return effectGameData;
					}
				}
			}
			return null;
		}

		private static bool IsThrowable(SkillBattleDataBase skill)
		{
			return skill is AttackAndSelfDestruct || skill is SelfDestructAndHeal || skill is ThrowMainHandAttack || (skill is RunawaySkill && skill.Model.Balancing.AssetId == "DudExplosion");
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", m_TurnsToShorten.ToString("0"));
			dictionary.Add("{value_2}", m_ChargeRounds.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
