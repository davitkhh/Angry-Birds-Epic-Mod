using System;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;

namespace ABH.GameDatas.Battle.Skills
{
	// ThrowMainHandAttack where a damaging hit that crits (CriticalStrike perk or Crit effect on the thrower) also removes
	// the status effect most recently put on the target it hit.
	public class ThrowMainHandCritPurgeAttack : ThrowMainHandAttack
	{
		public override void Init(SkillGameData model)
		{
			base.Init(model);
			ActionsBeforeDamageDealt.Add(delegate
			{
				DIContainerLogic.GetBattleService().LastHitCrit = false;
			});
			ActionsOnDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				var battleService = DIContainerLogic.GetBattleService();
				if (!battleService.LastHitCrit || damage <= 0f || !target.IsAlive || !battleService.RemoveMostRecentBattleEffect(target))
				{
					return;
				}
				VisualEffectSetting setting = null;
				if (DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting("Purge", out setting))
				{
					SpawnVisualEffects(VisualEffectSpawnTiming.Affected, setting, new List<ICombatant> { target });
				}
			});
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var percent = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out percent);
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + Convert.ToInt32(percent / 100f * invoker.ModifiedAttack));
			dictionary.Add("{value_6}", string.Empty + percent);
			var description = DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
			return PickPocketPassive.AppendStealText(description, invoker, base.Model.SkillParameters.ContainsKey("all"));
		}
	}
}
