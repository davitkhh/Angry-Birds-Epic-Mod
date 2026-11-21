using System;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

namespace ABH.GameDatas.Battle.Skills
{
	// Attack that afterwards blesses the attacker with damage reduction (Apprentice's Poke): the attacker takes
	// reduction_in_percent less damage for EffectDuration turns. Supports "charge" like any attack; the blessing
	// lands once the charged attack goes off. Uses the same ReduceDamageReceived effect as BlockDamageSkill.
	public class AttackAndApplySelfDamageReductionSkill : AttackSkillTemplate
	{
		private const string ReductionEffectIdent = "Protect";

		private float m_DamageMod;

		private float m_ChargeTurns;

		private float m_ReductionPercent;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			base.Model.SkillParameters.TryGetValue("charge", out m_ChargeTurns);
			base.Model.SkillParameters.TryGetValue("reduction_in_percent", out m_ReductionPercent);
			ActionsOnEnd.Add(delegate(BattleGameData battle, ICombatant source, ICombatant target)
			{
				if (m_ReductionPercent <= 0f || base.Model.Balancing.EffectDuration <= 0 || !source.IsParticipating)
				{
					return 0f;
				}
				var effect = new BattleEffectGameData(source, source, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnReceiveDamage,
						EffectType = BattleEffectType.ReduceDamageReceived,
						Values = new List<float> { m_ReductionPercent },
						AfflicionType = SkillEffectTypes.Blessing,
						Duration = base.Model.Balancing.EffectDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, base.Model.Balancing.EffectDuration, battle, ReductionEffectIdent, SkillEffectTypes.Blessing, GetLocalizedName(), base.Model.SkillNameId);
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
			dictionary.Add("{value_3}", m_ReductionPercent.ToString("0"));
			dictionary.Add("{value_4}", base.Model.Balancing.EffectDuration.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
