using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Battle.Skills;
using ABH.GameDatas.Interfaces;
using ABH.Services.Logic;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

// Side-effect free mirror of the attack/heal pipeline (AttackSkillTemplate.DoAction, the OnDealDamage /
// OnReceiveDamage / OnReceiveHealing effect handlers in BattleService, weapon perks, Ammo) for the health bar
// preview. Effects with random outcomes become chance changes; effects that only matter in rare edge cases
// (retaliation, banners, sheltering) are left out.
public static class HealthPreviewCalculator
{
	// A random modifier: with Chance the result is Factor times as much.
	private struct Proc
	{
		public float Chance;

		public float Factor;

		public Color Color;
	}

	// Which effects pushed a value up or down, for the overlay tints.
	private class TintTracker
	{
		private Vector4 m_Up;

		private Vector4 m_Down;

		public void Add(Color color, float ratio)
		{
			if (ratio <= 0f || float.IsNaN(ratio) || Mathf.Abs(ratio - 1f) < 0.0001f)
			{
				return;
			}
			var weight = Mathf.Abs(Mathf.Log(ratio));
			var c = new Vector4(color.r * weight, color.g * weight, color.b * weight, weight);
			if (ratio > 1f)
			{
				m_Up += c;
			}
			else
			{
				m_Down += c;
			}
		}

		public Color Up
		{
			get { return Mix(m_Up); }
		}

		public Color Down
		{
			get { return Mix(m_Down); }
		}

		private static Color Mix(Vector4 v)
		{
			return v.w <= 0f ? HealthPreviewColors.Fallback : new Color(v.x / v.w, v.y / v.w, v.z / v.w);
		}
	}

	public static Dictionary<ICombatant, HealthForecast> Predict(BattleGameData battle, ICombatant source, SkillBattleDataBase skill, ICombatant target, bool isRage)
	{
		return PredictAll(battle, source, skill, target, isRage).Forecasts;
	}

	// Health forecasts plus the rage chili forecast (ctx.Rage).
	public static HealthPreviewContext PredictAll(BattleGameData battle, ICombatant source, SkillBattleDataBase skill, ICombatant target, bool isRage)
	{
		var ctx = new HealthPreviewContext
		{
			Battle = battle,
			Source = source,
			Target = target,
			Skill = skill,
			IsRage = isRage
		};
		if (battle == null || source == null || skill == null || target == null)
		{
			return ctx;
		}
		skill.PreviewHealthChanges(ctx);
		AddKnockoutRage(ctx);
		foreach (var key in ctx.Forecasts.Where(p => p.Value.IsEmpty || p.Key.CombatantView == null).Select(p => p.Key).ToList())
		{
			ctx.Forecasts.Remove(key);
		}
		return ctx;
	}

	// Rage income of a combatant's side goes through its OnReceiveRage effects (BattleService IncreaseRage / ReduceRage).
	private static float ModifyRageIncome(ICombatant owner, float value)
	{
		foreach (var data in OrderedEffects(owner))
		{
			foreach (var effect in data.m_Effects.OrderBy(e => e.EffectType).ToList())
			{
				if (effect.EffectTrigger != EffectTriggerType.OnReceiveRage || effect.Values == null || effect.Values.Count == 0)
				{
					continue;
				}
				if (effect.EffectType == BattleEffectType.IncreaseRage)
				{
					value += value * effect.Values[0] / 100f;
				}
				else if (effect.EffectType == BattleEffectType.ReduceRage)
				{
					value -= value * effect.Values[0] / 100f;
				}
			}
		}
		return value;
	}

	// AddRageForAttack / ReduceRageFromAttack only count for the birds (or both sides in PvP) while rage is available.
	private static bool RageCounts(HealthPreviewContext ctx, ICombatant owner)
	{
		return owner != null && ctx.Battle.GetRageAvailable(owner.CombatantFaction) && (owner.CombatantFaction == Faction.Birds || ctx.Battle.IsPvP);
	}

	// AttackSkillTemplate.DoAction: every hit fills the attacker's chili (the move's first hit more), and an enemy's
	// ReduceRage effect (OnReceiveDamage) empties it by Values[0] per hit.
	private static void AddAttackRage(HealthPreviewContext ctx, ICombatant target, int hits, float chance)
	{
		var source = ctx.Source;
		if (!RageCounts(ctx, source))
		{
			return;
		}
		var constants = DIContainerBalancing.GameConstantsBalancingDataProvider;
		for (var i = 0; i < hits; i++)
		{
			var amount = ctx.Rage.AttackHits == 0 ? constants.RageMeterIncreasePerHitInPercent : constants.RageMeterIncreasePerHiAfterFirstAOEInPercent;
			ctx.Rage.AttackHits++;
			ctx.Rage.Add(ModifyRageIncome(source, amount), chance);
		}
		foreach (var data in OrderedEffects(target))
		{
			foreach (var effect in data.m_Effects)
			{
				if (effect.EffectTrigger == EffectTriggerType.OnReceiveDamage && effect.EffectType == BattleEffectType.ReduceRage && effect.Values != null && effect.Values.Count > 0)
				{
					for (var i = 0; i < hits; i++)
					{
						ctx.Rage.Add(-effect.Values[0], chance);
					}
				}
			}
		}
	}

	// OnEnemyKnockedOut IncreaseRage (FillRageOnKnockout set bonus): each enemy the move surely knocks out.
	private static void AddKnockoutRage(HealthPreviewContext ctx)
	{
		var source = ctx.Source;
		var killed = ctx.Forecasts.Count(p => p.Key.CombatantFaction != source.CombatantFaction && p.Value.IsLethal);
		if (killed == 0)
		{
			return;
		}
		foreach (var data in OrderedEffects(source))
		{
			foreach (var effect in data.m_Effects)
			{
				if (effect.EffectTrigger == EffectTriggerType.OnEnemyKnockedOut && effect.EffectType == BattleEffectType.IncreaseRage && effect.Values != null && effect.Values.Count > 0 && data.m_Source != null && data.m_Source.CombatantFaction == source.CombatantFaction)
				{
					for (var i = 0; i < killed; i++)
					{
						ctx.Rage.Add(effect.Values[0], 1f);
					}
				}
			}
		}
	}

	private static BattleService Service
	{
		get { return DIContainerLogic.GetBattleService(); }
	}

	private static IEnumerable<BattleEffectGameData> OrderedEffects(ICombatant owner)
	{
		if (owner == null || owner.CurrrentEffects == null)
		{
			return Enumerable.Empty<BattleEffectGameData>();
		}
		return owner.CurrrentEffects.Values.Where(e => e != null && e.m_Effects != null && e.m_Effects.Count > 0 && !e.m_IsRemoved).OrderBy(e => e.m_Effects[0].EffectType).ToList();
	}

	private static float Value(BattleEffectGameData data, BattleEffect effect, int index, ICombatant target)
	{
		if (effect.Values == null || effect.Values.Count <= index)
		{
			return 0f;
		}
		var value = effect.Values[index] + (index == 0 ? BattleService.GetRallyBonus(data, effect, target) : 0f);
		return BlessingAmplifier.Scale(effect, index, value, BlessingAmplifier.GetMultiplier(data, effect));
	}

	public static HealthChange AddHit(HealthPreviewContext ctx, ICombatant target, float attackValue, int hits, bool applyPerks, float chance, int tick)
	{
		var source = ctx.Source;
		if (target == null || !target.IsAlive || hits <= 0 || attackValue <= 0f || chance <= 0f)
		{
			return null;
		}
		if (tick == 0)
		{
			AddAttackRage(ctx, target, hits, chance);
		}
		var tint = new TintTracker();
		var procs = new List<Proc>();
		var perk = 1f;
		if (applyPerks)
		{
			var perks = new List<EquipmentPerk>();
			if (source.CombatantMainHandEquipment != null && source.CombatantMainHandEquipment.Perk != null)
			{
				perks.Add(source.CombatantMainHandEquipment.Perk);
			}
			perks.AddRange(UpgradeBlueprints.GetAttackBonusPerks(source));
			foreach (var p in perks)
			{
				float factor;
				var probability = p.ProbablityInPercent;
				switch (p.Type)
				{
				case PerkType.CriticalStrike:
				{
					// BattleService CriticalStrike perk: chance and bonus raised by ModifyCriticalStrike effects,
					// Brilliance off hands add PerkValue2% crit damage; the bonus is added to the perk multiplier so far
					probability += PerkEffectSum(source, EffectTriggerType.OnCalculatePerkChance, BattleEffectType.ModifyCriticalStrike);
					var bonus = p.PerkValue + PerkEffectSum(source, EffectTriggerType.OnApplyPerk, BattleEffectType.ModifyCriticalStrike);
					var brilliance = UpgradeBlueprints.FindEquippedPerk(source, PerkType.Brilliance);
					if (brilliance != null)
					{
						bonus += brilliance.PerkValue2;
					}
					factor = (perk + bonus / 100f * Service.CritBonusMultiplier) / Mathf.Max(0.0001f, perk);
					break;
				}
				case PerkType.Might:
				case PerkType.Brilliance:
					factor = (100f + p.PerkValue) / 100f;
					break;
				default:
					continue;
				}
				var procChance = Mathf.Clamp01(probability / 100f);
				if (procChance >= 0.999f)
				{
					perk *= factor;
					tint.Add(HealthPreviewColors.Fallback, factor);
				}
				else if (procChance > 0f)
				{
					procs.Add(new Proc { Chance = procChance, Factor = factor, Color = HealthPreviewColors.Fallback });
				}
			}
		}
		if (!ctx.IsRage)
		{
			var before = perk;
			Ammo.ModifyDamage(source, ref perk);
			tint.Add(HealthPreviewColors.Fallback, perk / before);
			var bird = source as BirdCombatant;
			var groundZero = UpgradeBlueprints.FindEquippedPerk(source, PerkType.GroundZero);
			if (bird != null && groundZero != null && target.CombatantFaction != source.CombatantFaction && !bird.FirstStrikeTargets.Contains(target))
			{
				perk *= groundZero.PerkValue / 100f;
				tint.Add(HealthPreviewColors.Fallback, groundZero.PerkValue / 100f);
			}
		}
		var dealt = EvaluateDealt(ctx, source, target, tint, procs);
		var chargeMultiplier = 1f;
		if (!ctx.IsRage && Ammo.IsAmmoSkill(source, ctx.Skill))
		{
			var state = Ammo.GetState(source);
			if (state != null && state.Charges > 0)
			{
				chargeMultiplier = Ammo.GetChargeDamageMultiplier(source, state.Charges);
				tint.Add(HealthPreviewColors.Fallback, chargeMultiplier);
			}
		}
		var attackSoFar = attackValue * perk * dealt * chargeMultiplier;
		var redirects = new List<KeyValuePair<ICombatant, float>>();
		var received = EvaluateReceived(ctx, target, source, attackSoFar, tint, procs, redirects);
		var final = attackSoFar * received * source.DamageModifier;
		// Vigor (the target's stat perk) lowers the damage it takes, in DealDamageFromCurrentTurn
		foreach (var vigor in UpgradeBlueprints.GetStatPerks(target).Where(p => p.Type == PerkType.Vigor))
		{
			var factor = Mathf.Max(0f, (100f - vigor.PerkValue - PerkEffectSum(target, EffectTriggerType.OnApplyPerk, BattleEffectType.ModifyVigor)) / 100f);
			final *= factor;
			tint.Add(HealthPreviewColors.Fallback, factor);
		}
		var amount = Mathf.RoundToInt(final) * hits;
		var baseAmount = Mathf.RoundToInt(attackValue * source.DamageModifier) * hits;
		var forecast = ctx.For(target);
		var change = forecast.Add(false, amount, tick, chance, Color.white);
		ApplyTints(change, amount, baseAmount, tint);
		foreach (var proc in procs)
		{
			var extra = Mathf.RoundToInt(final * (proc.Factor - 1f)) * hits;
			if (extra > 0)
			{
				var procChange = forecast.Add(false, extra, tick, chance * proc.Chance, Color.white);
				procChange.BonusAmount = extra;
				procChange.BonusTint = proc.Color;
				procChange.BonusTintAlpha = 0.35f;
			}
		}
		foreach (var redirect in redirects)
		{
			if (redirect.Key != null && redirect.Key.IsAlive)
			{
				ctx.For(redirect.Key).Add(false, Mathf.RoundToInt(redirect.Value) * hits, tick, chance, Color.white);
			}
		}
		if (applyPerks && tick == 0)
		{
			AddDrainHeals(ctx, Mathf.RoundToInt(final), hits, chance, procs.Select(p => new KeyValuePair<float, int>(p.Chance, Mathf.RoundToInt(final * (p.Factor - 1f)))).ToList());
		}
		return change;
	}

	// HocusPokus / HocusPokusChiliDrain (Foolish Wand, Calming Waves): after each hit that does damage, a chance
	// to heal the attacker PerkValue% (+ ModifyHocusPokus effects) of the damage dealt.
	private static void AddDrainHeals(HealthPreviewContext ctx, int perHit, int hits, float chance, List<KeyValuePair<float, int>> procExtras)
	{
		var source = ctx.Source;
		if (perHit <= 0 || source == null || !source.IsAlive)
		{
			return;
		}
		var perks = new List<EquipmentPerk>();
		if (source.CombatantMainHandEquipment != null && source.CombatantMainHandEquipment.Perk != null)
		{
			perks.Add(source.CombatantMainHandEquipment.Perk);
		}
		perks.AddRange(UpgradeBlueprints.GetAttackBonusPerks(source));
		foreach (var p in perks)
		{
			if (p.Type != PerkType.HocusPokus && p.Type != PerkType.HocusPokusChiliDrain)
			{
				continue;
			}
			var percent = p.PerkValue + PerkEffectSum(source, EffectTriggerType.OnApplyPerk, BattleEffectType.ModifyHocusPokus);
			var healChance = Mathf.Clamp01(p.ProbablityInPercent / 100f) * chance;
			// Foolish Wand: the same proc also empties the attacker's chili by PerkValue2 per hit
			if (p.Type == PerkType.HocusPokusChiliDrain && healChance > 0f && p.PerkValue2 > 0f && RageCounts(ctx, source))
			{
				for (var i = 0; i < hits; i++)
				{
					ctx.Rage.Add(-p.PerkValue2, healChance);
				}
			}
			if (percent <= 0f || healChance <= 0f)
			{
				continue;
			}
			AddHeal(ctx, source, perHit * hits * percent / 100f, healChance, 0, HealthPreviewColors.Heal);
			// the extra damage of a crit (or other proc) also heals, when both happen
			foreach (var extra in procExtras)
			{
				if (extra.Value > 0)
				{
					AddHeal(ctx, source, extra.Value * hits * percent / 100f, healChance * extra.Key, 0, HealthPreviewColors.Heal);
				}
			}
		}
	}

	// Sum of Values[0] of the owner's effects of one type on one trigger (the ModifyX perk handlers all add it).
	private static float PerkEffectSum(ICombatant owner, EffectTriggerType trigger, BattleEffectType type)
	{
		var sum = 0f;
		foreach (var data in OrderedEffects(owner))
		{
			foreach (var effect in data.m_Effects)
			{
				if (effect.EffectTrigger == trigger && effect.EffectType == type && effect.Values != null && effect.Values.Count > 0)
				{
					sum += effect.Values[0];
				}
			}
		}
		return sum;
	}

	// Guardian (stat perk): the first hit that would knock the wearer out leaves it at 1 hp instead.
	public static bool HasUnusedGuardian(ICombatant combatant)
	{
		return combatant != null && !combatant.HasUsedGuardianPerk && UpgradeBlueprints.GetStatPerks(combatant).Any(p => p.Type == PerkType.Guardian);
	}

	private static void ApplyTints(HealthChange change, int amount, int baseAmount, TintTracker tint)
	{
		if (baseAmount <= 0)
		{
			return;
		}
		var ratio = (float)amount / baseAmount;
		if (amount > baseAmount)
		{
			// 80 boosted to 110 (+37.5%) tints the extra 30 at 15%
			change.BonusAmount = amount - baseAmount;
			change.BonusTint = tint.Up;
			change.BonusTintAlpha = Mathf.Clamp(0.4f * (ratio - 1f), 0.08f, 0.6f);
		}
		else if (amount < baseAmount)
		{
			// halved by a weakening effect: the half that's left is tinted at 50%
			change.ReducedTint = tint.Down;
			change.ReducedTintAlpha = Mathf.Clamp(1f - ratio, 0.08f, 0.9f);
		}
	}

	// BattleService's OnDealDamage handlers, without their side effects. "target" is who gets hit.
	private static float EvaluateDealt(HealthPreviewContext ctx, ICombatant source, ICombatant target, TintTracker tint, List<Proc> procs)
	{
		var param = 1f;
		foreach (var data in OrderedEffects(source))
		{
			var color = HealthPreviewColors.ForEffect(data);
			foreach (var effect in data.m_Effects.OrderBy(e => e.EffectType).ToList())
			{
				if (effect.EffectTrigger != EffectTriggerType.OnDealDamage || effect.Values == null || effect.Values.Count == 0)
				{
					continue;
				}
				var before = param;
				var v0 = Value(data, effect, 0, target);
				switch (effect.EffectType)
				{
				case BattleEffectType.IncreaseDamageVsRed:
					param *= target.CombatantNameId.Contains("bird_red") ? v0 / 100f : 1f;
					break;
				case BattleEffectType.IncreaseDamageVsYellow:
					param *= target.CombatantNameId.Contains("bird_yellow") ? v0 / 100f : 1f;
					break;
				case BattleEffectType.IncreaseDamageVsWhite:
					param *= target.CombatantNameId.Contains("bird_white") ? v0 / 100f : 1f;
					break;
				case BattleEffectType.IncreaseDamageVsBlack:
					param *= target.CombatantNameId.Contains("bird_black") ? v0 / 100f : 1f;
					break;
				case BattleEffectType.IncreaseDamageVsBlues:
					param *= target.CombatantNameId.Contains("bird_blue") ? v0 / 100f : 1f;
					break;
				case BattleEffectType.ReduceDamageDealt:
					param = Math.Max(param - v0 / 100f, 0f);
					break;
				case BattleEffectType.ReduceDamageDealtAgainstSource:
					if (target == data.m_Source)
					{
						param = Math.Max(param - v0 / 100f, 0f);
					}
					break;
				case BattleEffectType.IncreaseDamage:
				case BattleEffectType.IncreaseDamageSet:
					if (param > 0f)
					{
						param += v0 / 100f;
					}
					break;
				case BattleEffectType.Crit:
					if (param > 0f && effect.Values.Count > 1)
					{
						var critFactor = (param + Value(data, effect, 1, target) / 100f * Service.CritBonusMultiplier) / param;
						AddProc(procs, v0 / 100f, critFactor, color, ref param);
					}
					break;
				case BattleEffectType.ModifyDamageByHealth:
					if (effect.Values.Count >= 3)
					{
						var owner = data.m_Target;
						var percent = owner.CurrentHealth / owner.ModifiedHealth * 100f;
						if (v0 > 0f && percent > Value(data, effect, 1, target) && param > 0f)
						{
							param += (percent - Value(data, effect, 1, target)) * Value(data, effect, 2, target) / 100f;
						}
						else if (v0 < 0f && percent < Value(data, effect, 1, target) && param > 0f)
						{
							param += (Value(data, effect, 1, target) - percent) * Value(data, effect, 2, target) / 100f;
						}
						param = Math.Max(param, 0f);
					}
					break;
				case BattleEffectType.ModifyDamageByHealthTreshold:
					if (effect.Values.Count >= 2 && param > 0f)
					{
						var owner = data.m_Target;
						var percent = owner.CurrentHealth / owner.ModifiedHealth * 100f;
						var once = effect.Values.Count < 3 || Value(data, effect, 2, target) != 1f;
						float count = once ? (percent < Value(data, effect, 1, target) ? 1f : 0f) : Mathf.FloorToInt((100f - percent) / Value(data, effect, 1, target));
						param += v0 * count / 100f * param;
					}
					break;
				case BattleEffectType.ModifyDamageByDefeatedPig:
					param = Math.Max(param + v0 / 100f * ctx.Battle.m_CombatantsPerFaction[Faction.Pigs].Count(c => !c.IsParticipating && c.summoningType != SummoningType.Summoned), 0f);
					break;
				case BattleEffectType.ModifyDamageByRage:
					if (effect.Values.Count >= 3 && param > 0f)
					{
						var rage = ctx.Battle.GetFactionRage(target.CombatantFaction);
						if (v0 > 0f && rage > Value(data, effect, 1, target))
						{
							param += (rage - Value(data, effect, 1, target)) * Value(data, effect, 2, target) / 100f;
						}
						else if (v0 < 0f && rage < Value(data, effect, 1, target))
						{
							param += (Value(data, effect, 1, target) - rage) * Value(data, effect, 2, target) / 100f;
						}
						param = Math.Max(param, 0f);
					}
					break;
				case BattleEffectType.ModifyDamageVsDebuff:
					if (effect.Values.Count >= 8)
					{
						var effects = target.CurrrentEffects ?? new Dictionary<string, BattleEffectGameData>();
						if (Value(data, effect, 2, target) > 0f && target.IsStunned) param *= Value(data, effect, 2, target) / 100f;
						if (v0 > 0f && effects.ContainsKey("WaterBomb")) param *= v0 / 100f;
						if (Value(data, effect, 4, target) > 0f && effects.ContainsKey("ChocolateRain")) param *= Value(data, effect, 4, target) / 100f;
						if (Value(data, effect, 5, target) > 0f && effects.ContainsKey("StickyPumpkin")) param *= Value(data, effect, 5, target) / 100f;
						if (Value(data, effect, 1, target) > 0f && effects.ContainsKey("GooBomb")) param *= Value(data, effect, 1, target) / 100f;
						if (Value(data, effect, 3, target) > 0f && effects.ContainsKey("ColorfulAttack")) param *= Value(data, effect, 3, target) / 100f;
						if (Value(data, effect, 6, target) > 0f && effects.ContainsKey("InTheSpotlight")) param *= Value(data, effect, 6, target) / 100f;
						if (Value(data, effect, 7, target) > 0f && effects.ContainsKey("BreathOfTheSea")) param *= Value(data, effect, 7, target) / 100f;
						param = Math.Max(param, 0f);
					}
					break;
				case BattleEffectType.Execute:
					if (effect.Values.Count >= 2 && target.CurrentHealth / target.ModifiedHealth <= v0 / 100f)
					{
						var executeChance = effect.Values.Count > 2 ? Value(data, effect, 2, target) / 100f : 1f;
						AddProc(procs, executeChance, Value(data, effect, 1, target) / 100f, color, ref param);
					}
					break;
				case BattleEffectType.ModifyDamageByBuffs:
					if (effect.Values.Count >= 3 && param > 0f)
					{
						var blessings = source.CurrrentEffects.Values.Count(e => e.m_EffectType == SkillEffectTypes.Blessing);
						AddProc(procs, Value(data, effect, 2, target) / 100f, (param + blessings * v0 / 100f) / param, color, ref param);
					}
					break;
				}
				tint.Add(color, before > 0f ? param / before : 1f);
			}
		}
		return param;
	}

	// Sure modifiers go into param; random ones become a chance change (or, when they lower the value, the
	// lowered value is shown and the rest becomes the chance change).
	private static void AddProc(List<Proc> procs, float chance, float factor, Color color, ref float param)
	{
		if (factor <= 0f || chance <= 0f || Mathf.Abs(factor - 1f) < 0.0001f)
		{
			return;
		}
		if (chance >= 0.999f)
		{
			param *= factor;
		}
		else if (factor > 1f)
		{
			procs.Add(new Proc { Chance = chance, Factor = factor, Color = color });
		}
		else
		{
			param *= factor;
			procs.Add(new Proc { Chance = 1f - chance, Factor = 1f / factor, Color = color });
		}
	}

	// BattleService's OnReceiveDamage handlers (and the duel bonus), without their side effects.
	private static float EvaluateReceived(HealthPreviewContext ctx, ICombatant target, ICombatant source, float attackSoFar, TintTracker tint, List<Proc> procs, List<KeyValuePair<ICombatant, float>> redirects)
	{
		var param = 1f + DuelDirector.DamageTakenBonus(target);
		tint.Add(HealthPreviewColors.Fallback, param);
		foreach (var data in OrderedEffects(target))
		{
			var color = HealthPreviewColors.ForEffect(data);
			foreach (var effect in data.m_Effects.OrderBy(e => e.EffectType).ToList())
			{
				if (effect.EffectTrigger != EffectTriggerType.OnReceiveDamage || effect.Values == null || effect.Values.Count == 0)
				{
					continue;
				}
				var before = param;
				var v0 = Value(data, effect, 0, source);
				switch (effect.EffectType)
				{
				case BattleEffectType.Protect:
					if (data.m_Source != null && data.m_Source != target)
					{
						var redirected = (param - v0 / 100f) * attackSoFar;
						if (redirected > 0f)
						{
							redirects.Add(new KeyValuePair<ICombatant, float>(data.m_Source, redirected * source.DamageModifier));
						}
						param = 0f;
					}
					break;
				case BattleEffectType.IncreaseDamageReceived:
					param += v0 / 100f;
					break;
				case BattleEffectType.Stinkeye:
					if (Stinkeye.IsPirate(source) && (Stinkeye.IsEmpowered(effect) || Stinkeye.IsBlessed(source)))
					{
						param += Stinkeye.GetBonusPercent(effect) / 100f;
					}
					break;
				case BattleEffectType.ReduceDamageReceived:
					param = Math.Max(param - v0 / 100f, 0f);
					break;
				case BattleEffectType.ChanceToReduceDamage:
					if (effect.Values.Count > 1 && param > 0f)
					{
						var reduced = Math.Max(param - Value(data, effect, 1, source) / 100f, 0f);
						if (reduced > 0f)
						{
							AddProc(procs, v0 / 100f, reduced / param, color, ref param);
						}
						else if (v0 >= 50f)
						{
							param = 0f;
						}
					}
					break;
				case BattleEffectType.ModifyDamageVsDebuff:
					if (effect.Values.Count > 1 && param > 0f && source.CurrrentEffects != null && source.CurrrentEffects.Values.Any(e => e.m_EffectType == SkillEffectTypes.Curse))
					{
						var reduced = Math.Max(param - Value(data, effect, 1, source) / 100f, 0f);
						if (reduced > 0f)
						{
							AddProc(procs, v0 / 100f, reduced / param, color, ref param);
						}
						else if (v0 >= 50f)
						{
							param = 0f;
						}
					}
					break;
				case BattleEffectType.ReductionPerBirdAlive:
					if (data.m_Source != null)
					{
						var count = ctx.Battle.m_CombatantsByInitiative.Count(c => c.CombatantFaction == data.m_Source.CombatantFaction && c != data.m_Source);
						if (count > 0)
						{
							param = Math.Max(param - count * v0 / 100f, 0f);
						}
					}
					break;
				case BattleEffectType.LimitDamageToMaxhp:
					if (data.m_Source != null)
					{
						var maxDamage = Mathf.RoundToInt(data.m_Source.ModifiedHealth * (v0 / 100f));
						var damage = Mathf.RoundToInt(attackSoFar * param);
						if (damage > maxDamage && damage > 0)
						{
							param = (float)maxDamage / damage;
						}
					}
					break;
				case BattleEffectType.TemporaryHealthBuff:
					if (target.TemporaryHealth > 0f)
					{
						var damage = attackSoFar * param;
						param = damage <= target.TemporaryHealth ? 0f : Mathf.Max(0f, 1f - target.TemporaryHealth / damage);
					}
					break;
				case BattleEffectType.ReduceRageDamage:
					if (!ctx.Battle.GetRageAvailable(source.CombatantFaction))
					{
						param = Math.Max(v0 / 100f, 0f);
					}
					break;
				}
				tint.Add(color, before > 0f ? param / before : 1f);
			}
		}
		return param;
	}

	public static HealthChange AddHeal(HealthPreviewContext ctx, ICombatant target, float rawAmount, float chance, int tick, Color baseColor)
	{
		if (target == null || !target.IsAlive || rawAmount <= 0f || chance <= 0f)
		{
			return null;
		}
		var tint = new TintTracker();
		var amount = (float)Mathf.RoundToInt(rawAmount);
		var baseAmount = (int)amount;
		// HealCurrentTurn: OnReceiveHealingConsumablesAlso, then OnReceiveHealing
		foreach (var trigger in new[] { EffectTriggerType.OnReceiveHealingConsumablesAlso, EffectTriggerType.OnReceiveHealing })
		{
			foreach (var data in OrderedEffects(target))
			{
				var color = HealthPreviewColors.ForEffect(data);
				foreach (var effect in data.m_Effects.OrderBy(e => e.EffectType).ToList())
				{
					if (effect.EffectTrigger != trigger || effect.Values == null || effect.Values.Count == 0)
					{
						continue;
					}
					var before = amount;
					var v0 = Value(data, effect, 0, ctx.Source);
					if (effect.EffectType == BattleEffectType.IncreaseHealingReceived)
					{
						amount = Mathf.RoundToInt(amount * (100f + v0) / 100f);
					}
					else if (effect.EffectType == BattleEffectType.ReduceHealingReceived)
					{
						amount = Mathf.RoundToInt(amount * (100f - v0) / 100f);
					}
					tint.Add(color, before > 0f ? amount / before : 1f);
				}
			}
		}
		var change = ctx.For(target).Add(true, (int)amount, tick, chance, baseColor);
		ApplyTints(change, (int)amount, baseAmount, tint);
		return change;
	}
}
