using System;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Attack that curses the target with a damage-over-time effect. When the curse
	// is removed (duration runs out, cleansed, or the invoker is defeated) the target
	// is healed for heal_in_percent of the invoker's attack (captured when applied).
	// No heal if the target is down, or if the curse is just refreshed by a new cast.
	public class AttackWithDotAndHealOnRemove : AttackSkillTemplate
	{
		private float m_DamageMod;

		private float m_ChargeTurns;

		private float m_AttackCount;

		private float m_DotChance = 100f;

		private float m_DotModifier;

		private float m_HealModifier;

		private readonly HashSet<BattleEffectGameData> m_Refreshed = new HashSet<BattleEffectGameData>();

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
			base.Model.SkillParameters.TryGetValue("heal_in_percent", out m_HealModifier);
			ActionsAfterDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				ShareableActionPart(battle, source, target, false);
			});
		}

		public override void ShareableActionPart(BattleGameData battle, ICombatant source, ICombatant target, bool isShared)
		{
			if (UnityEngine.Random.value > m_DotChance / 100f || target.CurrentHealth <= 0f)
			{
				return;
			}
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
			var healAmount = m_HealModifier / 100f * source.ModifiedAttack;
			battleEffectGameData.EffectRemovedAction = delegate(BattleEffectGameData e)
			{
				OnEffectRemoved(e, target, source, battle, healAmount);
			};
			// Re-casting replaces the curse (AddEffectValue removes the old one first);
			// the old instance must not pay out its heal in that case.
			BattleEffectGameData previous;
			if (target.CurrrentEffects != null && target.CurrrentEffects.TryGetValue(base.Model.Balancing.AssetId, out previous) && previous != null && previous.m_SkillIdent == base.Model.SkillNameId)
			{
				m_Refreshed.Add(previous);
			}
			battleEffectGameData.AddEffect();
		}

		private void OnEffectRemoved(BattleEffectGameData effect, ICombatant target, ICombatant source, BattleGameData battle, float healAmount)
		{
			if (m_Refreshed.Remove(effect))
			{
				return;
			}
			if (target == null || target.CurrentHealth <= 0f || healAmount <= 0f || battle == null)
			{
				return;
			}
			VisualEffectSetting setting = null;
			if (target.CombatantView != null && DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting("Heal_Strong", out setting))
			{
				SpawnVisualEffects(VisualEffectSpawnTiming.Affected, setting, new List<ICombatant> { target });
			}
			target.HealDamage(healAmount, source);
			DIContainerLogic.GetBattleService().HealCurrentTurn(target, battle);
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack));
			dictionary.Add("{value_2}", string.Empty + base.Model.Balancing.EffectDuration);
			dictionary.Add("{value_3}", string.Empty + m_DotChance);
			dictionary.Add("{value_4}", string.Empty + m_AttackCount);
			dictionary.Add("{value_5}", string.Empty + m_ChargeTurns);
			dictionary.Add("{value_7}", string.Empty + Convert.ToInt32(m_DotModifier / 100f * invoker.ModifiedAttack));
			dictionary.Add("{value_8}", string.Empty + Convert.ToInt32(m_HealModifier / 100f * invoker.ModifiedAttack));
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
