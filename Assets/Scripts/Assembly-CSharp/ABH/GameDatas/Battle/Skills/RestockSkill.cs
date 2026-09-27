using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using SmoothMoves;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	public class RestockSkill : SkillBattleDataBase
	{
		private int m_BuffDuration;

		private float m_DamageMod;

		private float m_Percent;

		private bool m_Self;

		private float m_DamageAndHealingBoost;

		private float m_DamageAndHealingBoostPerAttack;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			m_BuffDuration = base.Model.Balancing.EffectDuration;
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("chance", out value);
			m_Percent = value;
			m_Self = base.Model.SkillParameters.ContainsKey("self");
			base.Model.SkillParameters.TryGetValue("damage_and_healing_boost", out m_DamageAndHealingBoost);
			base.Model.SkillParameters.TryGetValue("damage_and_healing_boost_per_attack", out m_DamageAndHealingBoostPerAttack);
		}

		public override void BoneAnimationUserTrigger(UserTriggerEvent triggerEvent)
		{
			base.BoneAnimationUserTrigger(triggerEvent);
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			if (!base.Model.SkillParameters.ContainsKey("all"))
			{
				if (m_Self)
				{
					m_Targets = new List<ICombatant> { source };
				}
				else
				{
					m_Targets = new List<ICombatant> { target };
				}
			}
			else
			{
				m_Targets = new List<ICombatant>();
				var target2 = target;
				m_Targets.AddRange(battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == target2.CombatantFaction).ToList());
			}
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting);
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			foreach (var skillTarget in m_Targets)
			{
				if (skillTarget != source)
				{
					skillTarget.CombatantView.PlayCheerCharacter();
				}
				var valueList = new List<float> { m_Percent / 100f };
				var effect = new BattleEffectGameData(source, skillTarget, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnConsumableUsed,
						EffectType = BattleEffectType.RefundConsumable,
						AfflicionType = base.Model.Balancing.EffectType,
						Values = valueList,
						Duration = m_BuffDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, m_BuffDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
				effect.AddEffect();
			}
			var boostedAllies = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == source.CombatantFaction && c != source).ToList();
			foreach (var ally in boostedAllies)
			{
				var boostEffect = new BattleEffectGameData(source, ally, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnDealDamage,
						EffectType = BattleEffectType.IncreaseDamage,
						AfflicionType = base.Model.Balancing.EffectType,
						Values = new List<float> { m_DamageAndHealingBoost },
						Duration = m_BuffDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					},
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnReceiveHealingConsumablesAlso,
						EffectType = BattleEffectType.IncreaseHealingReceived,
						AfflicionType = base.Model.Balancing.EffectType,
						Values = new List<float> { m_DamageAndHealingBoost },
						Duration = m_BuffDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					},
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.AfterAttack,
						EffectType = BattleEffectType.IncreaseDamageAndHealingOnAttack,
						AfflicionType = base.Model.Balancing.EffectType,
						Values = new List<float> { m_DamageAndHealingBoostPerAttack },
						Duration = m_BuffDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, m_BuffDuration, battle, base.Model.Balancing.AssetId + "_boost", base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
				boostEffect.AddEffect();
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var num = Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack);
			dictionary.Add("{value_1}", string.Empty + num);
			dictionary.Add("{value_2}", string.Empty + m_BuffDuration);
			dictionary.Add("{value_3}", string.Empty + m_Percent);
			dictionary.Add("{value_4}", string.Empty + m_DamageAndHealingBoost);
			dictionary.Add("{value_5}", string.Empty + m_DamageAndHealingBoostPerAttack);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
