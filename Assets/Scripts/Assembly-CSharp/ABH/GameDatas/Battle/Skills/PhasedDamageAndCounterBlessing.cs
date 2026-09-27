using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	public class PhasedDamageAndCounterBlessing : SkillBattleDataBase
	{
		private float m_ReductionInPercent;

		private float m_IncreaseInPercent;

		private float m_CounterChance;

		private float m_CounterDamage;

		private float m_WeakenDuration = 1f;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			base.Model.SkillParameters.TryGetValue("reduction_in_percent", out m_ReductionInPercent);
			base.Model.SkillParameters.TryGetValue("increase_in_percent", out m_IncreaseInPercent);
			base.Model.SkillParameters.TryGetValue("counter", out m_CounterChance);
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out m_CounterDamage);
			if (base.Model.SkillParameters.ContainsKey("weaken_duration"))
			{
				base.Model.SkillParameters.TryGetValue("weaken_duration", out m_WeakenDuration);
			}
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			var sourceFaction = source.CombatantFaction;
			m_Targets = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == sourceFaction).ToList();
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting);
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			var totalDuration = base.Model.Balancing.EffectDuration;
			var weakenDuration = Mathf.Clamp((int)m_WeakenDuration, 0, totalDuration);
			var empowerDuration = totalDuration - weakenDuration;
			foreach (var skillTarget in m_Targets)
			{
				if (skillTarget != source)
				{
					skillTarget.CombatantView.PlayCheerCharacter();
				}
				var counterEffect = new BattleEffectGameData(source, skillTarget, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnReceiveDamage,
						EffectType = BattleEffectType.Counter,
						AfflicionType = base.Model.Balancing.EffectType,
						Values = new List<float> { m_CounterChance, m_CounterDamage },
						Duration = totalDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, totalDuration, battle, base.Model.Balancing.AssetId + "_counter", base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
				counterEffect.AddEffect();
				if (weakenDuration > 0)
				{
					var weakenedTarget = skillTarget;
					var weakenEffect = new BattleEffectGameData(source, skillTarget, new List<BattleEffect>
					{
						new BattleEffect
						{
							EffectTrigger = EffectTriggerType.OnDealDamage,
							EffectType = BattleEffectType.ReduceDamageDealt,
							AfflicionType = base.Model.Balancing.EffectType,
							Values = new List<float> { m_ReductionInPercent },
							Duration = weakenDuration,
							EffectAssetId = base.Model.Balancing.EffectIconAssetId,
							EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
						}
					}, weakenDuration, battle, base.Model.Balancing.AssetId + "_weaken", base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
					weakenEffect.EffectRemovedAction = delegate(BattleEffectGameData e)
					{
						if (e.GetTurnsLeft() > 0)
						{
							return;
						}
						ApplyEmpower(battle, source, weakenedTarget, empowerDuration);
					};
					weakenEffect.AddEffect();
				}
				else
				{
					ApplyEmpower(battle, source, skillTarget, empowerDuration);
				}
			}
		}

		private void ApplyEmpower(BattleGameData battle, ICombatant source, ICombatant target, int duration)
		{
			if (duration <= 0 || !target.IsAlive)
			{
				return;
			}
			var empowerEffect = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnDealDamage,
					EffectType = BattleEffectType.IncreaseDamage,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float> { m_IncreaseInPercent },
					Duration = duration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, duration, battle, base.Model.Balancing.AssetId + "_empower", base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			empowerEffect.AddEffect();
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + m_ReductionInPercent);
			dictionary.Add("{value_2}", string.Empty + m_IncreaseInPercent);
			dictionary.Add("{value_3}", string.Empty + m_CounterDamage);
			dictionary.Add("{value_4}", string.Empty + base.Model.Balancing.EffectDuration);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
