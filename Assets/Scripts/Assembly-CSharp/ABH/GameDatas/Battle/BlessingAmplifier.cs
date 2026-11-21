using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

namespace ABH.GameDatas.Battle
{
	// McCool's Pep Rally (RestockSkill): while the carrier effect (m_BlessingAmplifyPercent > 0) is on a combatant, every
	// helpful value of every other Blessing on that combatant is multiplied by 1 + percent / 100. Each time that combatant
	// applies a Blessing or Curse to anyone (Pep Rally included), the percent grows by m_BlessingAmplifyStep and m_Stacks by one.
	public static class BlessingAmplifier
	{
		// Value slots that aren't strengths, so boosting them would do nothing or hurt: modes, turn intervals,
		// HP/rage thresholds, summon table indices, countdowns and turn stamps. Read from the BattleService handlers.
		private static readonly Dictionary<BattleEffectType, int[]> NotHelpful = new Dictionary<BattleEffectType, int[]>
		{
			{ BattleEffectType.HealCounter, new[] { 1 } },                              // 1 = whole team, 2 = allies
			{ BattleEffectType.ModifyHealingByHealthTreshold, new[] { 1 } },            // HP threshold
			{ BattleEffectType.ApplyStunDelayed, new[] { 1 } },                         // every Nth turn
			{ BattleEffectType.DoFixedDamageDelayed, new[] { 1 } },
			{ BattleEffectType.DealDamageAndApplyStunDelayed, new[] { 1 } },
			{ BattleEffectType.DoFixedHealDelayed, new[] { 1 } },
			{ BattleEffectType.DoHealDelayed, new[] { 1 } },
			{ BattleEffectType.IncreaseDamageDelayed, new[] { 1 } },
			{ BattleEffectType.ReduceDamageReceivedDelayed, new[] { 1 } },
			{ BattleEffectType.ReflectDelayed, new[] { 1 } },
			{ BattleEffectType.SummonCombatant, new[] { 1 } },                          // interval / amount
			{ BattleEffectType.SummonCombatantHealthGated, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 } },
			{ BattleEffectType.Sheltered, new[] { 2, 3 } },                             // hit interval, hits left to skip
			{ BattleEffectType.ModifyDamageByHealth, new[] { 1 } },                     // HP threshold
			{ BattleEffectType.ModifyDamageByHealthTreshold, new[] { 1 } },
			{ BattleEffectType.ModifyDamageByRage, new[] { 1 } },                       // rage threshold
			{ BattleEffectType.IncreaseDamageToBanner, new[] { 1 } },                   // HP threshold
			{ BattleEffectType.SugarRush, new[] { 2 } }                                 // last turn granted
		};

		// Strongest active amplifier carried by owner, or null
		public static BattleEffectGameData FindCarrier(ICombatant owner)
		{
			if (owner == null || owner.CurrrentEffects == null)
			{
				return null;
			}
			BattleEffectGameData best = null;
			foreach (var effect in owner.CurrrentEffects.Values)
			{
				if (effect != null && !effect.m_IsRemoved && effect.m_BlessingAmplifyPercent > 0f && (best == null || effect.m_BlessingAmplifyPercent > best.m_BlessingAmplifyPercent))
				{
					best = effect;
				}
			}
			return best;
		}

		// Multiplier for the effect's helpful values; 1 when it isn't an amplified blessing
		public static float GetMultiplier(BattleEffectGameData effectGameData, BattleEffect effect)
		{
			if (effectGameData == null || effect == null || effectGameData.m_BlessingAmplifyPercent > 0f)
			{
				return 1f;
			}
			if (effectGameData.m_EffectType != SkillEffectTypes.Blessing || effect.AfflicionType == SkillEffectTypes.Curse)
			{
				return 1f;
			}
			if (effect.Values == null || effect.Values.Count == 0)
			{
				return 1f;
			}
			var carrier = FindCarrier(effectGameData.m_Target);
			return carrier == null ? 1f : 1f + carrier.m_BlessingAmplifyPercent / 100f;
		}

		// Whether Values[index] (currently value) of an effect of this type gets the multiplier
		public static bool IsHelpful(BattleEffectType type, int index, float value)
		{
			if (index == 0)
			{
				return true;
			}
			// 0/1 in a secondary slot is an on/off flag (or nothing to boost)
			if (value == 0f || value == 1f)
			{
				return false;
			}
			int[] excluded;
			return !NotHelpful.TryGetValue(type, out excluded) || System.Array.IndexOf(excluded, index) < 0;
		}

		public static float Scale(BattleEffect effect, int index, float value, float multiplier)
		{
			return multiplier != 1f && IsHelpful(effect.EffectType, index, value) ? value * multiplier : value;
		}

		// Called from BattleEffectGameData.AddEffect once the effect is on its target
		public static void OnStatusApplied(BattleEffectGameData applied)
		{
			if (applied == null)
			{
				return;
			}
			if (applied.m_EffectType != SkillEffectTypes.Blessing && applied.m_EffectType != SkillEffectTypes.Curse)
			{
				return;
			}
			var applier = applied.AppliedBy;
			if (applier == null || applier.CurrrentEffects == null)
			{
				return;
			}
			var carriers = new List<BattleEffectGameData>();
			foreach (var effect in applier.CurrrentEffects.Values)
			{
				// A Pep Rally recast on the applier replaces its own carrier; RestockSkill counts that one itself
				if (effect != null && effect != applied && !effect.m_IsRemoved && effect.m_BlessingAmplifyPercent > 0f && effect.m_BlessingAmplifyStep != 0f)
				{
					carriers.Add(effect);
				}
			}
			foreach (var carrier in carriers)
			{
				AddStack(carrier);
				carrier.EvaluateEffect();
				DebugLog.Log("[BlessingAmplifier] " + applier.CombatantName + " applied " + applied.m_EffectIdent + ": blessings now +" + carrier.m_BlessingAmplifyPercent + "%");
			}
		}

		// One stack per improvement; the status row shows "Nx" from 2 up (BattleEffectBlind.ShowStacks)
		public static void AddStack(BattleEffectGameData carrier)
		{
			carrier.m_BlessingAmplifyPercent += carrier.m_BlessingAmplifyStep;
			carrier.m_Stacks++;
		}
	}
}
