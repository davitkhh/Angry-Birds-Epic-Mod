using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;

namespace ABH.GameDatas.Battle.Skills
{
	// AttackAndApplyHealOnAttackSkill delivered as the Stinky Zombie's barf, whatever the pig holds (Zombie Farmer's Sow):
	// plays the ZombiePig's Action_Attack_2 from range with the barf projectile and StinkyBreath's effects. The curse
	// keeps the skill's AssetId as its ident, so its lasting visual comes from that setting (AngelicTouch = the
	// magenta healing shield). Attackers are healed by health_in_percent of the damage they deal to the target.
	public class BarfAndApplyHealOnAttackSkill : AttackAndApplyHealOnAttackSkill
	{
		private const string BarfAnimation = "Action_Attack_2";

		private const string BarfProjectile = "Projectile_ZombiePig_Barf";

		private const string BarfEffects = "StinkyBreath";

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			m_ForceRanged = true;
			m_HealTrigger = EffectTriggerType.AfterReceiveDamage;
			DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting(BarfEffects, out m_VisualEffectSetting);
			m_AttackAnimation = PlayBarf;
			// DoAction takes the projectile from the main hand item first; replace it before the attack starts.
			ActionsBeforeTargetSelection.Add((battle, source, target) =>
			{
				m_SkillProjectileAssetId = BarfProjectile;
				return 0f;
			});
		}

		private float PlayBarf(ICombatant source)
		{
			var asset = source.CombatantView.m_AssetController;
			var length = asset.GetAnimationLength(BarfAnimation);
			if (length <= 0f)
			{
				return source.CombatantView.PlayAttackAnimation();
			}
			asset.PlayAnimation(BarfAnimation);
			asset.PlayAnimationQueued("Idle");
			return length;
		}
	}
}
