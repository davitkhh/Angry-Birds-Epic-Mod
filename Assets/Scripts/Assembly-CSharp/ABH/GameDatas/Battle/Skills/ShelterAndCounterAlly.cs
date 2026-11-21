using System;
using System.Collections;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// The caster takes attacks aimed at the target ally (Sheltered, as in ProtectAndBlockDamage) and counters
	// every enemy that attacks that ally (CounterWithSource, as in ProtectAndCounterSkill).
	// Unlike ProtectAndCounterSkill, the redirect effect sits on the protected ally, so the caster really takes the hit.
	// Parameters: counter_damage (% of attack for the counter), chance (counter chance in %, default 100).
	public class ShelterAndCounterAlly : SkillBattleDataBase
	{
		private int m_BuffDuration;

		private float m_Chance = 100f;

		private float m_CounterAttackDamage;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			m_BuffDuration = base.Model.Balancing.EffectDuration;
			if (!base.Model.SkillParameters.TryGetValue("chance", out m_Chance))
			{
				m_Chance = 100f;
			}
			base.Model.SkillParameters.TryGetValue("counter_damage", out m_CounterAttackDamage);
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			m_Targets = new List<ICombatant> { target };
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting);
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			if (source == target)
			{
				yield break;
			}
			target.CombatantView.PlayCheerCharacter();
			var shelter = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.AfterTargetSelection,
					EffectType = BattleEffectType.Sheltered,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float>(),
					Duration = m_BuffDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, m_BuffDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			shelter.SetPersistanceAfterDefeat(false).AddEffect();
			var counter = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.AfterTargetSelection,
					EffectType = BattleEffectType.CounterWithSource,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float> { m_Chance, m_CounterAttackDamage },
					Duration = m_BuffDuration,
					EffectAssetId = string.Empty,
					EffectAtlasId = string.Empty
				}
			}, m_BuffDuration, battle, base.Model.Balancing.AssetId + "_counter", base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			counter.SetPersistanceAfterDefeat(false).LinkEffect(shelter).AddEffect(false);
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			// The counter replays the caster's first skill (InitCounterAttack uses GetSkill(0)) at counter_damage %,
			// so the per-hit number folds in that skill's damage_in_percent and {value_4} is its attack_count.
			var attackPercent = 100f;
			var hits = 1f;
			var counterSkill = invoker.GetSkill(0);
			if (counterSkill != null && counterSkill.Model.SkillParameters != null)
			{
				if (!counterSkill.Model.SkillParameters.TryGetValue("damage_in_percent", out attackPercent))
				{
					attackPercent = 100f;
				}
				if (!counterSkill.Model.SkillParameters.TryGetValue("attack_count", out hits) || hits < 1f)
				{
					hits = 1f;
				}
			}
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + Convert.ToInt32(m_CounterAttackDamage / 100f * attackPercent / 100f * invoker.ModifiedAttack));
			dictionary.Add("{value_2}", string.Empty + m_BuffDuration);
			dictionary.Add("{value_3}", string.Empty + m_Chance);
			dictionary.Add("{value_4}", string.Empty + Convert.ToInt32(hits));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
