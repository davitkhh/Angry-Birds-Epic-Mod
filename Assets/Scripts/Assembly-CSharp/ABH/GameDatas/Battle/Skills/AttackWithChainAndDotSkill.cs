using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Attack that curses the main target with a damage-over-time effect and then
	// ricochets to other enemies (AttackWithChainSkill + AttackAndApplyDotSkill).
	// The ricochet hits only deal impact damage; the DoT stays on the main target.
	public class AttackWithChainAndDotSkill : AttackSkillTemplate
	{
		private float m_DamageMod;

		private float m_ChargeTurns;

		private float m_AttackCount;

		private float m_DotChance = 100f;

		private float m_DotModifier;

		private float m_ChainCount;

		private float m_ReductionPerChain;

		private float m_ChainChance = 100f;

		private string m_AddtionalEffectInfo = string.Empty;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			base.Model.SkillParameters.TryGetValue("charge", out m_ChargeTurns);
			base.Model.SkillParameters.TryGetValue("attack_count", out m_AttackCount);
			if (base.Model.SkillParameters.ContainsKey("dot_chance"))
			{
				base.Model.SkillParameters.TryGetValue("dot_chance", out m_DotChance);
			}
			base.Model.SkillParameters.TryGetValue("dot_damage_in_percent", out m_DotModifier);
			base.Model.SkillParameters.TryGetValue("chain", out m_ChainCount);
			base.Model.SkillParameters.TryGetValue("chain_reduction", out m_ReductionPerChain);
			if (base.Model.SkillParameters.ContainsKey("chain_chance"))
			{
				base.Model.SkillParameters.TryGetValue("chain_chance", out m_ChainChance);
			}
			foreach (var key in base.Model.SkillParameters.Keys)
			{
				if (base.Model.SkillParameters[key] == -1f)
				{
					m_AddtionalEffectInfo = key;
				}
			}
			ActionsOnDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				var list = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction != source.CombatantFaction && target != c && c.CombatantView != null).ToList();
				var num = m_DamageMod * source.ModifiedAttack;
				for (var i = 0; (float)i < m_ChainCount; i++)
				{
					if (list.Count <= 0)
					{
						break;
					}
					if (UnityEngine.Random.value > m_ChainChance / 100f)
					{
						break;
					}
					var combatant = list[UnityEngine.Random.Range(0, list.Count)];
					list.Remove(combatant);
					var hopDamage = num * Mathf.Pow((100f - m_ReductionPerChain) / 100f, i + 1);
					// With a projectile the hit lands when the ricochet arrives (Ricochet)
					Ricochet.Hit(Ricochet.LastHit(target), combatant, delegate
					{
						var effectedParam = 1f;
						var num2 = hopDamage;
						var currentSkillAttackValue = m_Source.CurrentSkillAttackValue;
						m_Source.CurrentSkillAttackValue = num2;
						effectedParam = DIContainerLogic.GetBattleService().ApplyEffectsOnTriggerType(effectedParam, EffectTriggerType.OnReceiveDamage, combatant, m_Source);
						num2 *= effectedParam;
						DIContainerLogic.GetBattleService().ApplyEffectsOnTriggerType(1f, EffectTriggerType.BeforeReceiveDamage, combatant, source);
						VisualEffectSetting setting = null;
						if (DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting(m_AddtionalEffectInfo, out setting))
						{
							SpawnVisualEffects(VisualEffectSpawnTiming.Start, setting, new List<ICombatant> { combatant });
						}
						var effectedParam2 = 1f;
						if (num2 > 0f && source.CombatantMainHandEquipment != null && m_ApplyPerks && ApplyDelayedPerkSharedRoll(source, combatant, battle, ref effectedParam2) && combatant.CombatantView != null)
						{
							combatant.CombatantView.m_CurrentPerkType = source.CombatantMainHandEquipment.Perk.Type;
						}
						combatant.ReceiveDamage(num2, source);
						battle.m_DamageLastTurn += DIContainerLogic.GetBattleService().DealDamageFromCurrentTurn(combatant, battle, source);
						m_Source.CurrentSkillAttackValue = currentSkillAttackValue;
					});
					if (target.ChainedTargets == null)
					{
						target.ChainedTargets = new List<ICombatant>();
					}
					target.ChainedTargets.Add(combatant);
				}
			});
			ActionsAfterDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				ShareableActionPart(battle, source, target, false);
			});
		}

		public override void ShareableActionPart(BattleGameData battle, ICombatant source, ICombatant target, bool isShared)
		{
			if (UnityEngine.Random.value <= m_DotChance / 100f)
			{
				var values = new List<float> { m_DotModifier };
				var battleEffectGameData = new BattleEffectGameData(source, target, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnDealDamagePerTurn,
						EffectType = BattleEffectType.DoDamage,
						Values = values,
						AfflicionType = base.Model.Balancing.EffectType,
						Duration = base.Model.Balancing.EffectDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, SkillEffectTypes.Curse, GetLocalizedName(), base.Model.SkillNameId);
				battleEffectGameData.AddEffect();
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var num = Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack);
			dictionary.Add("{value_1}", string.Empty + num);
			dictionary.Add("{value_2}", string.Empty + base.Model.Balancing.EffectDuration);
			dictionary.Add("{value_3}", string.Empty + m_DotChance);
			dictionary.Add("{value_4}", string.Empty + m_AttackCount);
			dictionary.Add("{value_5}", string.Empty + m_ChargeTurns);
			dictionary.Add("{value_7}", string.Empty + Convert.ToInt32(m_DotModifier / 100f * invoker.ModifiedAttack));
			dictionary.Add("{value_8}", string.Empty + m_ChainCount);
			dictionary.Add("{value_9}", string.Empty + m_ReductionPerChain);
			dictionary.Add("{value_10}", string.Empty + Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack * (100f - m_ReductionPerChain) / 100f));
			dictionary.Add("{value_11}", string.Empty + m_ChainChance);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_2}", string.Empty + m_ChargeTurns);
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, dictionary);
		}
	}
}
