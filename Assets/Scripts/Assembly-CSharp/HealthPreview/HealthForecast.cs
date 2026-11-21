using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

// One predicted health change on a combatant, as the health bar preview draws it (see HealthBarPreview).
public class HealthChange
{
	public bool IsHeal;

	// Hit points after every modifier.
	public int Amount;

	// The part of Amount that boosting effects added: drawn after the base part, tinted with BonusTint.
	public int BonusAmount;

	public Color BonusTint = HealthPreviewColors.Default;

	public float BonusTintAlpha;

	// Effects that lowered the amount: only the lowered amount shows, tinted over its whole length.
	public Color ReducedTint = HealthPreviewColors.Default;

	public float ReducedTintAlpha;

	// White for hits, the heal green for heals, the effect's colour for damage over time.
	public Color BaseColor = Color.white;

	// 0 = on impact; n = the nth turn after (damage/heal over time ticks).
	public int Tick;

	public float Chance = 1f;

	public bool Guaranteed
	{
		get { return Chance >= 0.999f; }
	}
}

// Everything one hovered move would do to one combatant's health.
public class HealthForecast
{
	public readonly ICombatant Combatant;

	public readonly List<HealthChange> Changes = new List<HealthChange>();

	// Maximum health gained (+) or lost (-).
	public float MaxHealthDelta;

	// An unused Guardian perk: the first blow that would knock it out leaves it at 1 hp.
	public bool Guardian;

	public HealthForecast(ICombatant combatant)
	{
		Combatant = combatant;
		Guardian = HealthPreviewCalculator.HasUnusedGuardian(combatant);
	}

	public bool IsEmpty
	{
		get { return Mathf.Abs(MaxHealthDelta) < 0.5f && !Changes.Any(c => c.Amount > 0); }
	}

	public float OldMaxHealth
	{
		get { return Mathf.Max(1f, Combatant.ModifiedHealth); }
	}

	public float NewMaxHealth
	{
		get { return Mathf.Max(1f, Combatant.ModifiedHealth + MaxHealthDelta); }
	}

	public int GuaranteedImpactDamage
	{
		get { return Changes.Where(c => !c.IsHeal && c.Tick == 0 && c.Guaranteed).Sum(c => c.Amount); }
	}

	// The hit on impact surely knocks the combatant out.
	public bool IsLethal
	{
		get
		{
			var health = Mathf.Min(Combatant.CurrentHealth, NewMaxHealth);
			if (health <= 0f || Guardian || GuaranteedImpactDamage < Mathf.CeilToInt(health))
			{
				return false;
			}
			// Effects that keep it alive at 1 hp (bosses that can't be killed by birds, ...)
			return Combatant.CurrrentEffects == null || !Combatant.CurrrentEffects.Values.Any(e => e != null && e.m_Effects != null && e.m_Effects.Any(x => x.EffectType == BattleEffectType.DoNotGetKilledByBird));
		}
	}

	// The first turn whose sure damage (impact = 0, damage over time ticks after) knocks the combatant out; -1 if none.
	public int LethalTick
	{
		get
		{
			var health = Mathf.Min(Combatant.CurrentHealth, NewMaxHealth);
			if (health <= 0f || (Combatant.CurrrentEffects != null && Combatant.CurrrentEffects.Values.Any(e => e != null && e.m_Effects != null && e.m_Effects.Any(x => x.EffectType == BattleEffectType.DoNotGetKilledByBird))))
			{
				return -1;
			}
			var sure = Changes.Where(c => c.Guaranteed && c.Amount > 0).ToList();
			var maxTick = sure.Select(c => c.Tick).DefaultIfEmpty(-1).Max();
			var guardian = Guardian;
			for (var tick = 0; tick <= maxTick; tick++)
			{
				// sure heals of earlier turns (heal over time) keep it up longer
				health += sure.Where(c => c.IsHeal && c.Tick == tick - 1).Sum(c => c.Amount);
				health = Mathf.Min(health, NewMaxHealth);
				health -= sure.Where(c => !c.IsHeal && c.Tick == tick).Sum(c => c.Amount);
				if (health < 0.5f && guardian)
				{
					guardian = false;
					health = 1f;
				}
				else if (health < 0.5f)
				{
					return tick;
				}
			}
			return -1;
		}
	}

	public HealthChange Add(bool heal, int amount, int tick, float chance, Color baseColor)
	{
		var change = new HealthChange
		{
			IsHeal = heal,
			Amount = Mathf.Max(0, amount),
			Tick = Mathf.Max(0, tick),
			Chance = Mathf.Clamp01(chance),
			BaseColor = baseColor
		};
		Changes.Add(change);
		return change;
	}
}

// What a skill's PreviewHealthChanges gets: the move being hovered and where its predictions go.
// Skills must only read battle state here - nothing may change while the player is still dragging.
public class HealthPreviewContext
{
	public BattleGameData Battle;

	public ICombatant Source;

	// The combatant the move is dropped on (for a rage, the target the rage would start from).
	public ICombatant Target;

	public SkillBattleDataBase Skill;

	public bool IsRage;

	public readonly Dictionary<ICombatant, HealthForecast> Forecasts = new Dictionary<ICombatant, HealthForecast>();

	// What the move does to the source's rage chili (attack hits, chili drains, fixed rage, ...)
	public readonly RageForecast Rage = new RageForecast();

	// A fixed amount of rage for the source's side (BattleService.AddFixRage): + fills, - empties.
	public void AddRage(float amount, float chance = 1f)
	{
		if (Source != null && Battle.GetRageAvailable(Source.CombatantFaction))
		{
			Rage.Add(amount, chance);
		}
	}

	public HealthForecast For(ICombatant combatant)
	{
		HealthForecast forecast;
		if (!Forecasts.TryGetValue(combatant, out forecast))
		{
			forecast = new HealthForecast(combatant);
			Forecasts.Add(combatant, forecast);
		}
		return forecast;
	}

	public List<ICombatant> Enemies(ICombatant of)
	{
		return Battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction != of.CombatantFaction).ToList();
	}

	public List<ICombatant> FactionOf(ICombatant of)
	{
		return Battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == of.CombatantFaction).ToList();
	}

	// An attack hit (attackValue = the source's attack times the skill's percentage), through perks and effects.
	public HealthChange AddHit(ICombatant target, float attackValue, int hits = 1, bool applyPerks = true, float chance = 1f, int tick = 0)
	{
		return HealthPreviewCalculator.AddHit(this, target, attackValue, hits, applyPerks, chance, tick);
	}

	// Healing through the target's healing modifiers.
	public HealthChange AddHeal(ICombatant target, float amount, float chance = 1f, int tick = 0)
	{
		return HealthPreviewCalculator.AddHeal(this, target, amount, chance, tick, HealthPreviewColors.Heal);
	}

	// Damage over time: one change per tick (flashed in turn order), in the effect's colour.
	public void AddDamageOverTime(ICombatant target, float damagePerTick, int ticks, float chance, string effectIdent)
	{
		if (target == null || ticks <= 0 || damagePerTick <= 0f)
		{
			return;
		}
		var color = HealthPreviewColors.ForIdent(effectIdent);
		for (var tick = 1; tick <= ticks; tick++)
		{
			For(target).Add(false, Mathf.RoundToInt(damagePerTick), tick, chance, color);
		}
	}

	public void AddHealOverTime(ICombatant target, float healPerTick, int ticks, float chance)
	{
		if (target == null || ticks <= 0 || healPerTick <= 0f)
		{
			return;
		}
		for (var tick = 1; tick <= ticks; tick++)
		{
			HealthPreviewCalculator.AddHeal(this, target, healPerTick, chance, tick, HealthPreviewColors.Heal);
		}
	}

	public void AddMaxHealth(ICombatant target, float delta)
	{
		if (target != null)
		{
			For(target).MaxHealthDelta += delta;
		}
	}
}
