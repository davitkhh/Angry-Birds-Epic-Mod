using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle
{
	// Per-battle ammo for combatants whose skills carry ammo parameters (SkillParameters, any skill of the combatant):
	//   ammo_cost         ammo the skill uses each time it is used; it can't be used without that much
	//   ammo_start        ammo at the start of every battle (largest value over the combatant's skills)
	//   ammo_regen        ammo gained every ammo_regen_turns turns (counted at the end of each turn after the first)
	//   ammo_regen_turns
	//   ammo_gain         ammo the skill gives its user after it was used (e.g. a support)
	// Combatants are rebuilt for every battle, so the state lives as long as the combatant.
	public static class Ammo
	{
		public const string CostKey = "ammo_cost";

		public const string StartKey = "ammo_start";

		public const string RegenKey = "ammo_regen";

		public const string RegenTurnsKey = "ammo_regen_turns";

		public const string GainKey = "ammo_gain";

		public class State
		{
			public int Current;

			public int Start;

			public int Regen;

			public int RegenTurns;

			// Turns counted towards the next regen
			public int RegenProgress;
		}

		private static readonly ConditionalWeakTable<ICombatant, State> s_States = new ConditionalWeakTable<ICombatant, State>();

		// Raised whenever a combatant's ammo or regen progress changes.
		public static event Action<ICombatant> Changed;

		// Raised when a combatant gains ammo (amount > 0), for the floating "+X" popup.
		public static event Action<ICombatant, int> Gained;

		public static bool UsesAmmo(ICombatant combatant)
		{
			return GetState(combatant) != null;
		}

		// Null when the combatant has no ammo parameters on any skill.
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
			var skills = GetAllSkills(combatant);
			if (!skills.Any(s => HasParameter(s, CostKey) || HasParameter(s, StartKey) || HasParameter(s, RegenKey) || HasParameter(s, GainKey)))
			{
				return null;
			}
			state = new State
			{
				Start = skills.Max(s => GetInt(s, StartKey)),
				Regen = skills.Max(s => GetInt(s, RegenKey)),
				RegenTurns = skills.Max(s => GetInt(s, RegenTurnsKey))
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

		public static int GetCost(SkillBattleDataBase skill)
		{
			return GetInt(skill, CostKey);
		}

		public static int GetGain(SkillBattleDataBase skill)
		{
			return GetInt(skill, GainKey);
		}

		public static bool CanAfford(ICombatant combatant, SkillBattleDataBase skill)
		{
			var cost = GetCost(skill);
			return cost <= 0 || GetAmmo(combatant) >= cost;
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
			var cost = GetCost(skill);
			var state = GetState(combatant);
			if (cost <= 0 || state == null)
			{
				return;
			}
			state.Current = Mathf.Max(0, state.Current - cost);
			RaiseChanged(combatant);
		}

		// Called after the skill ran: gives its ammo_gain to the user.
		public static void OnSkillFinished(ICombatant combatant, SkillBattleDataBase skill)
		{
			var gain = GetGain(skill);
			if (gain > 0 && combatant != null && combatant.IsParticipating)
			{
				Gain(combatant, gain);
			}
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

		private static List<SkillBattleDataBase> GetAllSkills(ICombatant combatant)
		{
			var skills = combatant.GetSkills();
			return skills == null ? new List<SkillBattleDataBase>() : skills.Where(s => s != null && s.Model != null).ToList();
		}

		private static bool HasParameter(SkillBattleDataBase skill, string key)
		{
			return skill != null && skill.Model != null && skill.Model.SkillParameters != null && skill.Model.SkillParameters.ContainsKey(key);
		}

		private static int GetInt(SkillBattleDataBase skill, string key)
		{
			float value;
			if (skill == null || skill.Model == null || skill.Model.SkillParameters == null || !skill.Model.SkillParameters.TryGetValue(key, out value))
			{
				return 0;
			}
			return Mathf.RoundToInt(value);
		}
	}
}
