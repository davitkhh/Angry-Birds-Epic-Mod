using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle
{
	// Per-battle ammo, driven by the combatant's weapon (EquipmentBalancingData.WeaponAttributes of the main hand), so any
	// class can use it. A weapon with ammo_start uses ammo; its attributes:
	//   ammo_start                   ammo at the start of every battle
	//   ammo_cost                    ammo each attack (the class's attack skills, not rage) uses; default 1. An attack
	//                                can't be used without that much
	//   ammo_regen                   ammo gained every ammo_regen_turns turns (counted at the end of each turn after the
	//   ammo_regen_turns             battle's first)
	//   ammo_on_support              ammo gained after using a support skill
	//   ammo_on_kill                 ammo gained for every pig the combatant knocks out
	//   ammo_free_with_blessing      1: attacks use no ammo while the attacker has a positive effect
	//   damage_per_blessing_percent  attacks deal this much more damage (%) for every positive effect on the attacker
	// Positive effects are the visible Blessings on the combatant. Later an ammo item will add attributes of its own
	// (GetAttributes is where they get merged). Combatants are rebuilt for every battle, so the state lives as long as the
	// combatant.
	public static class Ammo
	{
		public const string StartKey = "ammo_start";

		public const string CostKey = "ammo_cost";

		public const string RegenKey = "ammo_regen";

		public const string RegenTurnsKey = "ammo_regen_turns";

		public const string SupportGainKey = "ammo_on_support";

		public const string KillGainKey = "ammo_on_kill";

		public const string FreeWithBlessingKey = "ammo_free_with_blessing";

		public const string DamagePerBlessingKey = "damage_per_blessing_percent";

		public class State
		{
			public int Current;

			public int Start;

			public int Regen;

			public int RegenTurns;

			// Turns counted towards the next regen
			public int RegenProgress;

			public Dictionary<string, float> Attributes;

			public string FiredProjectile;
		}

		private static readonly ConditionalWeakTable<ICombatant, State> s_States = new ConditionalWeakTable<ICombatant, State>();

		private static readonly Dictionary<string, float> NoAttributes = new Dictionary<string, float>();

		// Raised whenever a combatant's ammo or regen progress changes.
		public static event Action<ICombatant> Changed;

		// Raised when a combatant gains ammo (amount > 0), for the floating "+X" popup.
		public static event Action<ICombatant, int> Gained;

		// The combatant's weapon attributes (empty when it has none)
		public static Dictionary<string, float> GetAttributes(ICombatant combatant)
		{
			var mainHand = combatant != null ? combatant.CombatantMainHandEquipment : null;
			var attributes = mainHand != null && mainHand.BalancingData != null ? mainHand.BalancingData.WeaponAttributes : null;
			return attributes ?? NoAttributes;
		}

		public static bool UsesAmmo(ICombatant combatant)
		{
			return GetState(combatant) != null;
		}

		// Null when the combatant's weapon doesn't use ammo.
		public static State GetState(ICombatant combatant)
		{
			if (combatant == null)
			{
				return null;
			}
			State state;
			if (s_States.TryGetValue(combatant, out state))
			{
				return state;
			}
			var attributes = GetAttributes(combatant);
			if (!attributes.ContainsKey(StartKey))
			{
				return null;
			}
			state = new State
			{
				Start = GetInt(attributes, StartKey),
				Regen = GetInt(attributes, RegenKey),
				RegenTurns = GetInt(attributes, RegenTurnsKey),
				Attributes = attributes
			};
			state.Current = state.Start;
			s_States.Add(combatant, state);
			return state;
		}

		public static int GetAmmo(ICombatant combatant)
		{
			var state = GetState(combatant);
			return state != null ? state.Current : 0;
		}

		public static bool HasRegen(ICombatant combatant)
		{
			var state = GetState(combatant);
			return state != null && state.Regen > 0 && state.RegenTurns > 0;
		}

		// Skills that spend the weapon's ammo: the combatant's own attacks, never rage. m_IsRageSkill is only set once
		// rage is triggered, so a bird's rage skill (slot 2) is also recognised by name.
		public static bool IsAmmoSkill(ICombatant combatant, SkillBattleDataBase skill)
		{
			if (skill == null || skill.Model == null || skill.m_IsRageSkill || skill.Model.Balancing.TargetType != SkillTargetTypes.Attack)
			{
				return false;
			}
			var rage = combatant is BirdCombatant ? combatant.GetSkill(2) : null;
			return rage == null || rage.Model == null || rage.Model.Balancing.NameId != skill.Model.Balancing.NameId;
		}

		// Whether the skill shows and spends ammo for this combatant (ignores free shots)
		public static bool UsesAmmo(ICombatant combatant, SkillBattleDataBase skill)
		{
			return IsAmmoSkill(combatant, skill) && GetState(combatant) != null;
		}

		// The projectile the combatant's weapon last fired (null before its first shot)
		public static string GetFiredProjectile(ICombatant combatant)
		{
			var state = GetState(combatant);
			return state != null ? state.FiredProjectile : null;
		}

		// SkillBattleDataBase.SpawnProjectile reports every projectile it spawns
		public static void OnProjectileFired(ICombatant combatant, string projectileAssetId)
		{
			var state = GetState(combatant);
			if (state != null && !string.IsNullOrEmpty(projectileAssetId) && state.FiredProjectile != projectileAssetId)
			{
				state.FiredProjectile = projectileAssetId;
				RaiseChanged(combatant);
			}
		}

		// Ammo the skill would use right now (0 for skills that don't use ammo, or free shots)
		public static int GetCost(ICombatant combatant, SkillBattleDataBase skill)
		{
			var state = GetState(combatant);
			if (state == null || !IsAmmoSkill(combatant, skill))
			{
				return 0;
			}
			if (GetInt(state.Attributes, FreeWithBlessingKey) > 0 && CountBlessings(combatant) > 0)
			{
				return 0;
			}
			float cost;
			return state.Attributes.TryGetValue(CostKey, out cost) ? Mathf.RoundToInt(cost) : 1;
		}

		public static bool CanAfford(ICombatant combatant, SkillBattleDataBase skill)
		{
			var cost = GetCost(combatant, skill);
			return cost <= 0 || GetAmmo(combatant) >= cost;
		}

		// Visible positive effects on the combatant
		public static int CountBlessings(ICombatant combatant)
		{
			if (combatant == null || combatant.CurrrentEffects == null)
			{
				return 0;
			}
			return combatant.CurrrentEffects.Values.Count(e => e != null && e.m_EffectType == SkillEffectTypes.Blessing && !string.IsNullOrEmpty(e.m_IconAssetId));
		}

		// Weapon damage bonus for an attack (damage_per_blessing_percent per positive effect)
		public static void ModifyDamage(ICombatant source, ref float damage)
		{
			var percent = GetAttributes(source).TryGetValue(DamagePerBlessingKey, out var value) ? value : 0f;
			if (percent <= 0f)
			{
				return;
			}
			var blessings = CountBlessings(source);
			if (blessings > 0)
			{
				damage *= 1f + percent * blessings / 100f;
			}
		}

		// The skill the combatant would use, or the first other usable skill it can afford when it lacks the ammo
		// (attack/support only, never rage). Falls back to the skill itself when nothing is affordable.
		public static SkillBattleDataBase GetAffordableSkill(ICombatant combatant, SkillBattleDataBase skill)
		{
			if (skill == null || combatant == null || skill.m_IsRageSkill || CanAfford(combatant, skill))
			{
				return skill;
			}
			var skills = combatant.GetSkills();
			if (skills == null)
			{
				return skill;
			}
			foreach (var candidate in skills)
			{
				if (candidate == null || candidate == skill || candidate.m_IsRageSkill || candidate.Model.Balancing.TargetType == SkillTargetTypes.Passive)
				{
					continue;
				}
				if (CanAfford(combatant, candidate))
				{
					return candidate;
				}
			}
			return skill;
		}

		// Called right before the skill runs: spends its ammo.
		public static void OnSkillStarted(ICombatant combatant, SkillBattleDataBase skill)
		{
			var cost = GetCost(combatant, skill);
			var state = GetState(combatant);
			if (cost <= 0 || state == null)
			{
				return;
			}
			state.Current = Mathf.Max(0, state.Current - cost);
			RaiseChanged(combatant);
		}

		// Called after the skill ran: supports give the weapon's ammo_on_support to their user.
		public static void OnSkillFinished(ICombatant combatant, SkillBattleDataBase skill)
		{
			var state = GetState(combatant);
			if (state == null || skill == null || skill.m_IsRageSkill || skill.Model.Balancing.TargetType != SkillTargetTypes.Support || combatant == null || !combatant.IsParticipating)
			{
				return;
			}
			Gain(combatant, GetInt(state.Attributes, SupportGainKey));
		}

		// A pig was knocked out by killer
		public static void OnKill(ICombatant killer, ICombatant victim)
		{
			var state = GetState(killer);
			if (state == null || victim == null || victim.CombatantFaction != Faction.Pigs || killer.CombatantFaction == Faction.Pigs || !killer.IsParticipating)
			{
				return;
			}
			Gain(killer, GetInt(state.Attributes, KillGainKey));
		}

		public static void Gain(ICombatant combatant, int amount)
		{
			var state = GetState(combatant);
			if (state == null || amount <= 0)
			{
				return;
			}
			state.Current += amount;
			RaiseChanged(combatant);
			if (Gained != null)
			{
				Gained(combatant, amount);
			}
		}

		// End of a turn: counts towards each combatant's regen and pays it out every RegenTurns turns.
		public static void OnTurnEnded(BattleGameData battle)
		{
			// m_CurrentTurn was already advanced; the battle's first turn (and the set-up step before it) don't count
			if (battle == null || battle.m_CombatantsByInitiative == null || battle.m_CurrentTurn - 1 < 2)
			{
				return;
			}
			foreach (var combatant in battle.m_CombatantsByInitiative.ToList())
			{
				if (combatant == null || !combatant.IsParticipating || !HasRegen(combatant))
				{
					continue;
				}
				var state = GetState(combatant);
				state.RegenProgress++;
				if (state.RegenProgress >= state.RegenTurns)
				{
					state.RegenProgress = 0;
					Gain(combatant, state.Regen);
				}
				else
				{
					RaiseChanged(combatant);
				}
			}
		}

		private static void RaiseChanged(ICombatant combatant)
		{
			if (Changed != null)
			{
				Changed(combatant);
			}
		}

		private static int GetInt(Dictionary<string, float> attributes, string key)
		{
			float value;
			return attributes != null && attributes.TryGetValue(key, out value) ? Mathf.RoundToInt(value) : 0;
		}
	}
}
