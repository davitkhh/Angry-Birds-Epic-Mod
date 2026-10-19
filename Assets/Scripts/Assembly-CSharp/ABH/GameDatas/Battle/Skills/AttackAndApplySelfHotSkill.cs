using System;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

namespace ABH.GameDatas.Battle.Skills
{
	// Attack that afterwards blesses the attacker with a heal over time (Lassie's attack): health_in_percent of its
	// max health per turn for EffectDuration turns. Supports "charge" like any attack; the HoT lands once the charged
	// attack goes off. The blessing uses the SoothingSong effect ident so it shows the heal-over-time visual.
	public class AttackAndApplySelfHotSkill : AttackSkillTemplate
	{
		private const string HotEffectIdent = "SoothingSong";

		private float m_DamageMod;

		private float m_ChargeTurns;

		private float m_Percent;

		private float m_TargetHealthBonus;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			base.Model.SkillParameters.TryGetValue("charge", out m_ChargeTurns);
			base.Model.SkillParameters.TryGetValue("health_in_percent", out m_Percent);
			// Optional: up to bonus_vs_target_missing_health_in_percent more damage, scaled by the target's missing health (near death = full bonus)
			base.Model.SkillParameters.TryGetValue("bonus_vs_target_missing_health_in_percent", out m_TargetHealthBonus);
			ModificationsOnDamageCalculation.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				if (m_TargetHealthBonus <= 0f || target.ModifiedHealth <= 0f)
				{
					return damage;
				}
				var missingFraction = 1f - UnityEngine.Mathf.Clamp01(target.CurrentHealth / target.ModifiedHealth);
				return damage * (1f + m_TargetHealthBonus / 100f * missingFraction);
			});
			ActionsOnEnd.Add(delegate(BattleGameData battle, ICombatant source, ICombatant target)
			{
				if (m_Percent <= 0f || base.Model.Balancing.EffectDuration <= 0 || !source.IsParticipating)
				{
					return 0f;
				}
				var effect = new BattleEffectGameData(source, source, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnDealDamagePerTurn,
						EffectType = BattleEffectType.DoHeal,
						Values = new List<float> { m_Percent, 0f },
						AfflicionType = SkillEffectTypes.Blessing,
						Duration = base.Model.Balancing.EffectDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, base.Model.Balancing.EffectDuration, battle, HotEffectIdent, SkillEffectTypes.Blessing, GetLocalizedName(), base.Model.SkillNameId);
				effect.AddEffect();
				return 0f;
			});
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var num = Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack);
			dictionary.Add("{value_1}", string.Empty + num);
			dictionary.Add("{value_2}", string.Empty + m_ChargeTurns);
			dictionary.Add("{value_3}", m_Percent.ToString("0"));
			dictionary.Add("{value_4}", base.Model.Balancing.EffectDuration.ToString("0"));
			dictionary.Add("{value_5}", m_TargetHealthBonus.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
