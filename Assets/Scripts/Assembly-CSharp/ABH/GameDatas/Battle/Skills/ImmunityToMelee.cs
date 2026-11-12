using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;

namespace ABH.GameDatas.Battle.Skills
{
	// Passive: melee attacks can't hit this combatant. AttackSkillTemplate drops it from a melee attack's targets, so the
	// attacker still swings, but there is no damage, rage, perk or effect. Ranged and multi-target attacks hit as normal.
	public class ImmunityToMelee : SkillBattleDataBase
	{
		public static bool IsImmune(ICombatant target)
		{
			return target.GetSkills().Any(s => s is ImmunityToMelee);
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			yield break;
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, new Dictionary<string, string>());
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
