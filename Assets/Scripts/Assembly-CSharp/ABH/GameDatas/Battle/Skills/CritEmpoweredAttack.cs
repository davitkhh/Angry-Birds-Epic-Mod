using System.Collections.Generic;
using ABH.GameDatas.Interfaces;

namespace ABH.GameDatas.Battle.Skills
{
	// Melee SimpleAttackSkill that always plays Action_Attack_1. When the hit crits (CriticalStrike perk or Crit effect on
	// the attacker), the crit's bonus damage is raised by crit_effectiveness_in_percent: at 100, +50% becomes +100%.
	public class CritEmpoweredAttack : SimpleAttackSkill
	{
		private const string AttackAnimation = "Action_Attack_1";

		private float m_DamagePercent;

		private float m_CritEffectiveness;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out m_DamagePercent);
			base.Model.SkillParameters.TryGetValue("crit_effectiveness_in_percent", out m_CritEffectiveness);
			m_IsMelee = true;
			m_AttackAnimation = PlayAttackClip;
			ActionsBeforeDamageDealt.Add(delegate
			{
				DIContainerLogic.GetBattleService().CritBonusMultiplier = 1f + m_CritEffectiveness / 100f;
			});
			// Crits roll before this list runs. Reset before the target takes damage, so its counters aren't boosted.
			ModificationsOnDamageDealtCalculation.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				DIContainerLogic.GetBattleService().CritBonusMultiplier = 1f;
				return damage;
			});
			// An evade skips the list above.
			ActionsOnEnd.Add(delegate
			{
				DIContainerLogic.GetBattleService().CritBonusMultiplier = 1f;
				return 0f;
			});
		}

		private float PlayAttackClip(ICombatant source)
		{
			var asset = source.CombatantView.m_AssetController;
			if (!asset.HasClipSilently(AttackAnimation))
			{
				return source.CombatantView.PlayAttackAnimation();
			}
			asset.PlayAnimation(AttackAnimation);
			asset.PlayAnimationQueued("Idle");
			return asset.GetAnimationLength(AttackAnimation);
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + System.Convert.ToInt32(m_DamagePercent / 100f * invoker.ModifiedAttack));
			dictionary.Add("{value_6}", string.Empty + m_DamagePercent);
			dictionary.Add("{value_7}", string.Empty + m_CritEffectiveness);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}
	}
}
