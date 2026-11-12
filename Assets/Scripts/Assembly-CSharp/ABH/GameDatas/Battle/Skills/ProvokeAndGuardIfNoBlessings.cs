using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Deals no damage. One random bird (the one the AI targeted, so the target bubble stays true) immediately attacks the
	// caster with its first skill, on top of its normal turn; this uses the counter attack flow. If the caster has no visible
	// positive effects it first gets "reduction_in_percent" less damage for EffectDuration turns, so the guard also covers
	// the provoked attack. That effect is itself positive, so it can't be refreshed while it lasts.
	public class ProvokeAndGuardIfNoBlessings : SkillBattleDataBase
	{
		private const float ProvokedDamagePercent = 100f;

		private float m_ReductionPercent;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			base.Model.SkillParameters.TryGetValue("reduction_in_percent", out m_ReductionPercent);
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger skill: " + base.Model.Balancing.NameId);
			m_Source = source;
			m_Battle = battle;
			m_InitialTarget = target;
			m_Targets = new List<ICombatant> { source };
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting);
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			if (Ammo.CountBlessings(source) == 0)
			{
				AddGuard(battle, source);
			}
			// Picked after the animation, since a bird can be stunned or knocked out by then.
			var bird = CanBeProvoked(source, target) ? target : PickBird(battle, source);
			if (bird != null)
			{
				bird.CombatantView.InitCounterAttack(source, source, bird, ProvokedDamagePercent, false);
			}
		}

		private static bool CanBeProvoked(ICombatant source, ICombatant bird)
		{
			return bird != null && bird.CombatantFaction != source.CombatantFaction && bird.IsParticipating && !bird.IsBanner && !bird.IsStunned && bird.CombatantView != null && bird.GetSkill(0) != null && Ammo.CanAfford(bird, bird.GetSkill(0));
		}

		private static ICombatant PickBird(BattleGameData battle, ICombatant source)
		{
			var birds = battle.m_CombatantsByInitiative.Where(c => CanBeProvoked(source, c)).ToList();
			return birds.Count == 0 ? null : birds[Random.Range(0, birds.Count)];
		}

		private void AddGuard(BattleGameData battle, ICombatant source)
		{
			new BattleEffectGameData(source, source, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnReceiveDamage,
					EffectType = BattleEffectType.ReduceDamageReceived,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float> { m_ReductionPercent },
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId).AddEffect();
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", m_ReductionPercent.ToString("0"));
			dictionary.Add("{value_2}", base.Model.Balancing.EffectDuration.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
