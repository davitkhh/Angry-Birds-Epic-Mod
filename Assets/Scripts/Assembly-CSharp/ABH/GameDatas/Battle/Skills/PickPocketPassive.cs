using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Class passive carried by a class skin (Piggy McCool's blue bird costume, skin_merchant): every hit of the class
	// attack steals "coins" snoutlings from the target. It lives on the costume instead of the attack skill, so whatever
	// replaces the attack (e.g. the Dynamite main hand's throw) still steals. Phat Loot multiplies the haul and Cash
	// Strike spends part of it as damage (BattleService.DropStolenGold).
	public class PickPocketPassive : SkillBattleDataBase
	{
		private float m_Coins;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			model.SkillParameters.TryGetValue("coins", out m_Coins);
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			yield break;
		}

		public override void DoActionInstant(BattleGameData battle, ICombatant source, ICombatant target)
		{
		}

		// The thief's costume passive. A bird without a skin (e.g. the class overlay preview) wears its class's
		// default costume, which every owner of the class has.
		private static PickPocketPassive Find(ICombatant thief)
		{
			var bird = thief == null ? null : thief.CharacterModel as BirdGameData;
			if (bird == null || bird.ClassItem == null)
			{
				return null;
			}
			var skin = bird.ClassSkin == null ? null : bird.ClassSkin.BalancingData;
			if (skin == null)
			{
				skin = DIContainerBalancing.Service.GetBalancingDataList<ClassSkinBalancingData>().FirstOrDefault(s => s.SortPriority == 0 && s.OriginalClass == bird.ClassItem.BalancingData.NameId);
			}
			if (skin == null || string.IsNullOrEmpty(skin.PassiveSkillNameId))
			{
				return null;
			}
			return new SkillGameData(skin.PassiveSkillNameId).GenerateSkillBattleData() as PickPocketPassive;
		}

		// Snoutlings taken per hit, before Cash Strike spends any. extraCoins: the attack's own "coins" parameter.
		public static int GetStolenCoins(ICombatant thief, float extraCoins = 0f)
		{
			var passive = Find(thief);
			var coins = extraCoins + (passive == null ? 0f : passive.m_Coins);
			if (coins <= 0f)
			{
				return 0;
			}
			var phatLoot = UpgradeBlueprints.FindEquippedPerk(thief, PerkType.PhatLoot);
			return (int)(coins * (phatLoot == null ? 1f : phatLoot.PerkValue2));
		}

		// Snoutlings that actually drop per hit, after every perk.
		public static int GetPocketedCoins(ICombatant thief, float extraCoins = 0f)
		{
			var coins = GetStolenCoins(thief, extraCoins);
			var cashStrike = UpgradeBlueprints.FindEquippedPerk(thief, PerkType.CashStrike);
			if (coins > 0 && cashStrike != null)
			{
				coins -= Mathf.Min(coins, Mathf.CeilToInt(coins * cashStrike.PerkValue / 100f));
			}
			return coins;
		}

		// Called for every hit of an attack skill; only the class attack (skill slot 0) steals.
		public static void StealOnHit(BattleGameData battle, SkillBattleDataBase attack, ICombatant thief, ICombatant victim)
		{
			if (attack.m_IsRageSkill || !(thief is BirdCombatant) || thief.GetSkill(0) != attack)
			{
				return;
			}
			var coins = GetStolenCoins(thief);
			if (coins > 0)
			{
				DIContainerLogic.GetBattleService().DropStolenGold(battle, victim, thief, victim, coins, false);
			}
		}

		// Adds "Steals N snoutlings ..." to an attack description, or nothing if the attack pockets none.
		public static string AppendStealText(string description, ICombatant invoker, bool everyTarget, float extraCoins = 0f)
		{
			var coins = invoker == null ? 0 : GetPocketedCoins(invoker, extraCoins);
			if (coins <= 0)
			{
				return description;
			}
			var text = everyTarget
				? DIContainerInfrastructure.GetLocaService().Tr("skill_steal_snoutlings_all", "Steals [00a800]{value_1}[-] snoutlings from every target hit.")
				: DIContainerInfrastructure.GetLocaService().Tr("skill_steal_snoutlings", "Steals [00a800]{value_1}[-] snoutlings from target.");
			return description + " " + text.Replace("{value_1}", coins.ToString());
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", m_Coins.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
