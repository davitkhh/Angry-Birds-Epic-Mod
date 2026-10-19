using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;

namespace ABH.GameDatas.Battle.Skills
{
	// Attack that hits harder the more curses the target has (Zombie Farmer's Reap): damage_in_percent plus
	// bonus_per_curse_in_percent for every curse on the target when it's hit. With the "target_most_curses" parameter
	// pigs aim it at the enemy with the most curses (BattleService.GetNextTargetForAttackSkill).
	public class AttackWithBonusPerCurseSkill : AttackSkillTemplate
	{
		private float m_DamagePercent;

		private float m_BonusPerCurse;

		private float m_ChargeTurns;

		public static int CountCurses(ICombatant combatant)
		{
			if (combatant.CurrrentEffects == null)
			{
				return 0;
			}
			return combatant.CurrrentEffects.Values.Count(e => e.m_EffectType == SkillEffectTypes.Curse);
		}

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			if (!base.Model.SkillParameters.TryGetValue("damage_in_percent", out m_DamagePercent))
			{
				m_DamagePercent = 100f;
			}
			base.Model.SkillParameters.TryGetValue("bonus_per_curse_in_percent", out m_BonusPerCurse);
			base.Model.SkillParameters.TryGetValue("charge", out m_ChargeTurns);
			ModificationsOnDamageCalculation.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				var curses = CountCurses(target);
				if (curses == 0 || m_DamagePercent <= 0f)
				{
					return damage;
				}
				return damage * (m_DamagePercent + m_BonusPerCurse * curses) / m_DamagePercent;
			});
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var num = Convert.ToInt32(m_DamagePercent / 100f * invoker.ModifiedAttack);
			dictionary.Add("{value_1}", string.Empty + num);
			dictionary.Add("{value_2}", string.Empty + m_ChargeTurns);
			dictionary.Add("{value_3}", string.Empty + m_BonusPerCurse);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
