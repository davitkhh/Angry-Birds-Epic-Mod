using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle
{
	// Hype perk (the Blues' starter horn, bird_offhand_blue_warhorn_start): when the wearer's support skill goes off, every
	// ally (the wearer too) gets a blessing that makes its next attack deal +PerkValue% damage. PerkValue2 = how many times
	// it stacks (upgrade_offhand_blue_warhorn_start: 2; below 1 counts as 1). With X stacks the bonus is X * PerkValue%,
	// and an attack uses up one stack. It lasts until used.
	// The blessing is an ordinary OnDealDamage IncreaseDamage effect (Values[0] = per-stack value * m_Stacks).
	// BattleService's IncreaseDamage delegate marks it used on the first hit, and a stack is taken off once that
	// combatant's skill (or counter attack) finishes, so every hit of a multi-hit attack still gets the full bonus.
	public static class HypeBonus
	{
		public const string EffectIdent = "HypeNextAttack";

		private static readonly HashSet<BattleEffectGameData> s_Used = new HashSet<BattleEffectGameData>();

		// Called by the IncreaseDamage delegate whenever an IncreaseDamage effect adds damage
		public static void MarkUsed(BattleEffectGameData effect)
		{
			if (effect != null && effect.m_EffectIdent == EffectIdent)
			{
				s_Used.Add(effect);
			}
		}

		// After combatant's skill: use up a stack of its bonus if the skill used it, then (support skill of a Hype wearer)
		// hand out a stack to every ally
		public static void OnSkillFinished(BattleGameData battle, ICombatant combatant, SkillBattleDataBase skill)
		{
			Consume(combatant);
			if (battle == null || combatant == null || skill == null || skill.m_IsRageSkill || skill.Model.Balancing.TargetType != SkillTargetTypes.Support)
			{
				return;
			}
			var perk = UpgradeBlueprints.FindEquippedPerk(combatant, PerkType.Hype);
			if (perk == null || perk.PerkValue <= 0f)
			{
				return;
			}
			// A charging support skill hasn't gone off yet
			if (combatant.CurrrentEffects != null && combatant.CurrrentEffects.ContainsKey("Charge_" + skill.Model.Balancing.AssetId))
			{
				return;
			}
			List<ICombatant> allies;
			if (!battle.m_CombatantsPerFaction.TryGetValue(combatant.CombatantFaction, out allies))
			{
				return;
			}
			var maxStacks = Mathf.Max(1, Mathf.FloorToInt(perk.PerkValue2 + 0.001f));
			var name = DIContainerInfrastructure.GetLocaService().Tr("perk_hype_name", "Hype");
			foreach (var ally in allies.Where(c => c != null && c.IsAlive && c.IsParticipating && !c.IsBanner).ToList())
			{
				var stacks = 1;
				BattleEffectGameData existing;
				if (ally.CurrrentEffects != null && ally.CurrrentEffects.TryGetValue(EffectIdent, out existing) && !existing.m_IsRemoved)
				{
					stacks = Mathf.Max(existing.m_Stacks, Mathf.Min(maxStacks, existing.m_Stacks + 1));
				}
				var effects = new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnDealDamage,
						EffectType = BattleEffectType.IncreaseDamage,
						AfflicionType = SkillEffectTypes.Blessing,
						Values = new List<float> { perk.PerkValue * stacks },
						Duration = -1,
						EffectAssetId = "Cheer",
						EffectAtlasId = "Skills_Birds_01"
					}
				};
				var effect = new BattleEffectGameData(combatant, ally, effects, -1, battle, EffectIdent, SkillEffectTypes.Blessing, name, EffectIdent);
				effect.m_Stacks = stacks;
				effect.AddEffect();
			}
		}

		// Take one stack off combatant's bonus if an attack has used it; the last stack removes it
		public static void Consume(ICombatant combatant)
		{
			s_Used.RemoveWhere(e => e.m_IsRemoved);
			BattleEffectGameData effect;
			if (combatant == null || combatant.CurrrentEffects == null || !combatant.CurrrentEffects.TryGetValue(EffectIdent, out effect) || !s_Used.Remove(effect))
			{
				return;
			}
			if (effect.m_Stacks <= 1)
			{
				effect.RemoveEffect();
				return;
			}
			var perStack = effect.m_Effects[0].Values[0] / effect.m_Stacks;
			effect.m_Stacks--;
			// New list: copies of an effect share their Values
			effect.m_Effects[0].Values = new List<float> { perStack * effect.m_Stacks };
		}
	}
}
