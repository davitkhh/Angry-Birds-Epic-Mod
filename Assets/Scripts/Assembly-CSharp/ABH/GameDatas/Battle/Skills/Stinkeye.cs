using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

namespace ABH.GameDatas.Battle.Skills
{
	// Pirate captain attack: marks the target so blessed pirates deal bonus_in_percent more damage to it. Once
	// pirate_deaths pirates have died while the captain was alive, his marks (existing ones too) are empowered: every
	// pirate deals empowered_bonus_in_percent more to them, blessed or not, and pirate attack skills target marked birds.
	public class Stinkeye : AttackSkillTemplate
	{
		public const string EffectIdent = "Stinkeye";

		private float m_DamageMod;

		private float m_AttackCount;

		private float m_BonusPercent;

		private float m_EmpoweredBonusPercent;

		private float m_PirateDeaths;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			base.Model.SkillParameters.TryGetValue("attack_count", out m_AttackCount);
			base.Model.SkillParameters.TryGetValue("bonus_in_percent", out m_BonusPercent);
			base.Model.SkillParameters.TryGetValue("empowered_bonus_in_percent", out m_EmpoweredBonusPercent);
			base.Model.SkillParameters.TryGetValue("pirate_deaths", out m_PirateDeaths);
			ActionsAfterDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				ShareableActionPart(battle, source, target, false);
			});
		}

		public override void ShareableActionPart(BattleGameData battle, ICombatant source, ICombatant target, bool isShared)
		{
			var battleEffectGameData = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnReceiveDamage,
					EffectType = BattleEffectType.Stinkeye,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float> { m_BonusPercent, m_EmpoweredBonusPercent, m_PirateDeaths },
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, base.Model.Balancing.EffectDuration, battle, EffectIdent, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			battleEffectGameData.AddEffect();
		}

		// Values: [bonus %, empowered bonus %, pirate deaths needed]. Checked live, so marks already applied empower too.
		public static bool IsEmpowered(BattleEffectGameData effectGameData, BattleEffect effect)
		{
			int deaths;
			return effect.Values.Count > 2 && effectGameData.m_Battle.m_PirateDeathsWitnessed.TryGetValue(effectGameData.m_Source, out deaths) && deaths >= effect.Values[2];
		}

		public static float GetBonusPercent(BattleEffectGameData effectGameData, BattleEffect effect)
		{
			return IsEmpowered(effectGameData, effect) ? effect.Values[1] : effect.Values[0];
		}

		public static bool IsPirate(ICombatant combatant)
		{
			return combatant is PigCombatant && combatant.CombatantNameId.Contains("pirate");
		}

		// Called once per lethal hit: every pig still alive has now seen one more pirate die
		public static void RegisterPirateDeath(BattleGameData battle, ICombatant dead)
		{
			if (!IsPirate(dead))
			{
				return;
			}
			foreach (var witness in battle.m_CombatantsPerFaction[Faction.Pigs].Where(c => c.IsAlive && c != dead))
			{
				int deaths;
				battle.m_PirateDeathsWitnessed.TryGetValue(witness, out deaths);
				battle.m_PirateDeathsWitnessed[witness] = deaths + 1;
			}
		}

		// A bird carrying an empowered Stinkeye that this pirate must attack, or null
		public static ICombatant GetEmpoweredMarkTarget(BattleGameData battle, ICombatant combatant)
		{
			if (!IsPirate(combatant) || !battle.m_CombatantsPerFaction.ContainsKey(Faction.Birds))
			{
				return null;
			}
			var marked = battle.m_CombatantsPerFaction[Faction.Birds].Where(c => c.IsParticipating && c.CurrrentEffects != null && c.CurrrentEffects.Values.Any(e => !e.m_IsRemoved && e.m_Effects.Any(f => f.EffectType == BattleEffectType.Stinkeye && IsEmpowered(e, f)))).ToList();
			return marked.Count == 0 ? null : marked[UnityEngine.Random.Range(0, marked.Count)];
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var num = Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack);
			dictionary.Add("{value_1}", string.Empty + num);
			dictionary.Add("{value_2}", string.Empty + base.Model.Balancing.EffectDuration);
			dictionary.Add("{value_3}", string.Empty + (100f + m_BonusPercent));
			dictionary.Add("{value_4}", string.Empty + m_AttackCount);
			dictionary.Add("{value_5}", string.Empty + m_PirateDeaths);
			dictionary.Add("{value_6}", string.Empty + (100f + m_EmpoweredBonusPercent));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
