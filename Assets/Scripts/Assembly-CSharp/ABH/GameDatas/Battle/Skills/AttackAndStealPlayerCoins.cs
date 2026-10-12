using System;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;

namespace ABH.GameDatas.Battle.Skills
{
	public class AttackAndStealPlayerCoins : AttackSkillTemplate
	{
		private float m_DamageMod;

		private float m_Coins;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			base.Model.SkillParameters.TryGetValue("coins", out m_Coins);
			ActionsOnDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				ShareableActionPart(battle, source, target, false);
			});
		}

		public override void ShareableActionPart(BattleGameData battle, ICombatant source, ICombatant target, bool isShared)
		{
			if (target.CombatantFaction != Faction.Birds || battle.m_ControllerInventory == null)
			{
				return;
			}
			var stolen = Math.Min((int)m_Coins, DIContainerLogic.InventoryService.GetItemValue(battle.m_ControllerInventory, "gold"));
			if (stolen > 0)
			{
				DIContainerLogic.InventoryService.RemoveItem(battle.m_ControllerInventory, "gold", stolen, "pig_steal");
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var num = Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack);
			dictionary.Add("{value_1}", string.Empty + num);
			dictionary.Add("{value_3}", string.Empty + m_Coins);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
