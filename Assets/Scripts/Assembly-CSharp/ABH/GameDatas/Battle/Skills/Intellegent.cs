using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// "Intellegent" passive: an AI combatant (never a player's bird) with it skips its combos, health rolls and
	// targeting behaviour. Every turn IntellegentPlanner scores every move it may make (each usable skill on each
	// legal target, rage included for a PvP bird with full rage) and takes the best one.
	// Skill parameter "victory_side": 1 (default) = play for its own faction's victory, -1 = play for the opposing
	// faction's victory (it then picks the moves that help the other side most).
	// Hooks: BattleService.GetNextSkillIgnoringAmmo / GetNextTarget, and CharacterControllerBattleGround skips the
	// PvP AI override for it.
	public class Intellegent : SkillBattleDataBase
	{
		public const string SideParam = "victory_side";

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DoActionInstant(battle, source, target);
			yield break;
		}

		public override void DoActionInstant(BattleGameData battle, ICombatant source, ICombatant target)
		{
			// Nothing to apply: the passive only changes how the AI decides (see IntellegentPlanner).
			m_Source = source;
		}

		// The Intellegent passive of an AI-controlled combatant, or null. Player birds never use it.
		public static Intellegent Of(ICombatant combatant)
		{
			if (combatant == null || combatant is BirdCombatant)
			{
				return null;
			}
			var skills = combatant.GetSkills();
			if (skills == null)
			{
				return null;
			}
			for (var i = 0; i < skills.Count; i++)
			{
				var intellegent = skills[i] as Intellegent;
				if (intellegent != null)
				{
					return intellegent;
				}
			}
			return null;
		}

		// The faction whose victory this combatant plays for.
		public Faction PlaysFor(ICombatant combatant)
		{
			float side;
			if (Model.SkillParameters.TryGetValue(SideParam, out side) && side < 0f)
			{
				return combatant.CombatantFaction == Faction.Birds ? Faction.Pigs : Faction.Birds;
			}
			return combatant.CombatantFaction;
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(Model.SkillDescription, new Dictionary<string, string>());
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(Model.SkillDescription, new Dictionary<string, string>());
		}
	}

	// Move search for Intellegent combatants.
	//
	// The battle can't be cloned (skills are coroutines with views), so the planner runs on a light copy of the
	// field: health, max health, attack, damage dealt/taken multipliers and stun odds of every combatant still in
	// the fight. A move is applied to that copy as its expected value (chances become fractions), then every other
	// combatant takes one turn in initiative order, each greedily making its own side's best attack. The result is
	// scored for the side the planner plays for:
	//   V = sum over that side of worth(c) - sum over the other side of worth(c)
	//   worth(c) = threat(c) * (1 + health(c) / maxHealth(c)) while alive, 0 when knocked out
	// threat is the expected damage (or healing) c produces per turn. Damage therefore pays threat / maxHealth per
	// point (the focus-fire rule: remove output as fast as possible), a knock-out pays one more threat, and a
	// victory or defeat outweighs everything. The move's score is V minus V of passing the turn; a move that needs
	// N charge turns has its score divided by N + 1 (it uses N + 1 turns).
	public static class IntellegentPlanner
	{
		private const float WinValue = 1000000f;

		private const float KeyUnitWeight = 5f;

		// Future damage/healing over time counts this much per turn.
		private const float FutureFactor = 0.75f;

		private class Unit
		{
			public ICombatant C;

			public Faction Side;

			public float Hp;

			public float MaxHp;

			public float Attack;

			public float Threat;

			public float DealtMul = 1f;

			public float TakenMul = 1f;

			public float StunChance;

			public bool Key;

			public bool Banner;

			public bool Alive => Hp > 0f;

			public Unit Copy()
			{
				return (Unit)MemberwiseClone();
			}
		}

		private class Plan
		{
			public BattleGameData Battle;

			public SkillGameData Skill;

			public ICombatant Target;
		}

		private static readonly Dictionary<ICombatant, Plan> s_Plans = new Dictionary<ICombatant, Plan>();

		// Called from BattleService.GetNextSkillIgnoringAmmo. False: not Intellegent, or nothing worth planning (the
		// normal AI then runs).
		public static bool TryPickSkill(BattleGameData battle, ICombatant actor, out SkillBattleDataBase skill)
		{
			skill = null;
			var intellegent = Intellegent.Of(actor);
			if (intellegent == null || battle == null)
			{
				return false;
			}
			if (s_Plans.Count > 32)
			{
				s_Plans.Clear();
			}
			s_Plans.Remove(actor);
			var side = intellegent.PlaysFor(actor);
			var field = BuildField(battle);
			var self = field.FirstOrDefault(u => u.C == actor);
			if (self == null)
			{
				return false;
			}
			var baseline = Evaluate(battle, Respond(battle, field, self), side);
			var bestScore = float.NegativeInfinity;
			ICombatant bestTarget = null;
			foreach (var candidate in GetCandidateSkills(battle, actor))
			{
				foreach (var target in GetCandidateTargets(battle, actor, candidate))
				{
					var score = ScoreMove(battle, field, self, candidate, target, side, baseline);
					if (score > bestScore + 0.0001f)
					{
						bestScore = score;
						skill = candidate;
						bestTarget = target;
					}
				}
			}
			if (skill == null)
			{
				return false;
			}
			actor.ComboInfo = null;
			actor.AttackTarget = bestTarget;
			s_Plans[actor] = new Plan { Battle = battle, Skill = skill.Model, Target = bestTarget };
			DebugLog.Log("[Intellegent] " + actor.CombatantNameId + " plays for " + side + ": " + skill.Model.Balancing.NameId + " -> " + bestTarget.CombatantNameId + " (score " + bestScore.ToString("0.00") + ")");
			return true;
		}

		// Called from BattleService.GetNextTarget: the planned target for the planned skill, otherwise the best legal
		// target for whatever skill the turn ended up with (forced order, ammo swap, a charge whose target fell...).
		public static bool TryPickTarget(BattleGameData battle, ICombatant actor, SkillGameData skill, out ICombatant target)
		{
			target = null;
			var intellegent = Intellegent.Of(actor);
			if (intellegent == null || battle == null || skill == null)
			{
				return false;
			}
			var skillData = actor.GetSkills().FirstOrDefault(s => s.Model == skill) ?? skill.GenerateSkillBattleData();
			var targets = GetCandidateTargets(battle, actor, skillData);
			Plan plan;
			if (s_Plans.TryGetValue(actor, out plan) && plan.Battle == battle && plan.Skill == skill && targets.Contains(plan.Target))
			{
				target = plan.Target;
				return true;
			}
			if (targets.Count == 0)
			{
				return false;
			}
			var side = intellegent.PlaysFor(actor);
			var field = BuildField(battle);
			var self = field.FirstOrDefault(u => u.C == actor);
			if (self == null)
			{
				target = targets[0];
				return true;
			}
			var baseline = Evaluate(battle, Respond(battle, field, self), side);
			var bestScore = float.NegativeInfinity;
			foreach (var candidate in targets)
			{
				var score = ScoreMove(battle, field, self, skillData, candidate, side, baseline);
				if (score > bestScore + 0.0001f)
				{
					bestScore = score;
					target = candidate;
				}
			}
			return target != null;
		}

		// Everything the actor may use this turn: its attack/support skills (not passives or combo sign-ups), only ones
		// it has ammo for, only the kind a forced move order allows, special skills only when they have a target.
		private static List<SkillBattleDataBase> GetCandidateSkills(BattleGameData battle, ICombatant actor)
		{
			var result = new List<SkillBattleDataBase>();
			var skills = actor.GetSkills();
			for (var i = 0; i < skills.Count; i++)
			{
				var skill = skills[i];
				if (skill == null || skill.Model == null || skill is Intellegent)
				{
					continue;
				}
				var targetType = skill.Model.Balancing.TargetType;
				if (targetType != SkillTargetTypes.Attack && targetType != SkillTargetTypes.Support)
				{
					continue;
				}
				// A PvP bird's slot 2 is its rage: only while the side's rage is full.
				if (actor.IsPvPBird && i == 2 && !battle.IsRageFull(actor.CombatantFaction))
				{
					continue;
				}
				if (!Ammo.CanAfford(actor, skill))
				{
					continue;
				}
				if (AttackAndForceMoveOrder.IsMoveBlocked(battle, actor, targetType == SkillTargetTypes.Support))
				{
					continue;
				}
				if (DuelKnockback.IsDuelSkill(skill.Model) && !DuelKnockback.CanStart(actor))
				{
					continue;
				}
				result.Add(skill);
			}
			return result;
		}

		// The targets the game lets the actor pick for skill.
		private static List<ICombatant> GetCandidateTargets(BattleGameData battle, ICombatant actor, SkillBattleDataBase skill)
		{
			var result = new List<ICombatant>();
			var model = skill.Model;
			var enemyFaction = actor.CombatantFaction == Faction.Birds ? Faction.Pigs : Faction.Birds;
			if (model.Balancing.TargetType == SkillTargetTypes.Attack)
			{
				if (DuelKnockback.IsDuelSkill(model))
				{
					AddIfValid(result, DuelKnockback.PickTarget(actor));
					return result;
				}
				if (actor.CombatantFaction == Faction.Pigs)
				{
					var forced = Stinkeye.GetForcedMarkTarget(battle, actor);
					if (forced != null)
					{
						AddIfValid(result, forced);
						return result;
					}
				}
				if (model.Balancing.TargetingBehavior == PigTargetingBehavior.Self)
				{
					AddIfValid(result, actor);
					return result;
				}
				foreach (var c in battle.m_CombatantsByInitiative)
				{
					if (c.CombatantFaction == enemyFaction && (model.Balancing.TargetSelfPossible || c != actor))
					{
						AddIfValid(result, c);
					}
				}
				return result;
			}
			if (model.Balancing.TargetType == SkillTargetTypes.Support)
			{
				if (GoadAllyIntoAttack.IsGoadSkill(model))
				{
					AddIfValid(result, GoadAllyIntoAttack.PickAlly(battle, actor, model));
					return result;
				}
				if (RemoveAllyEffectForCrit.IsRemoveAllyEffectSkill(model))
				{
					AddIfValid(result, RemoveAllyEffectForCrit.PickAlly(battle, actor));
					return result;
				}
				if (model.Balancing.TargetingBehavior == PigTargetingBehavior.Self || model.SkillParameters.ContainsKey("self"))
				{
					AddIfValid(result, actor);
					return result;
				}
				foreach (var c in battle.m_CombatantsByInitiative)
				{
					if (c.CombatantFaction != actor.CombatantFaction || (!model.Balancing.TargetSelfPossible && c == actor))
					{
						continue;
					}
					// A blessing it already holds can't be stacked (the normal AI's IsSkillSupportPossible rule).
					if (!model.Balancing.TargetAlreadyAffectedTargets && c.CurrrentEffects != null && c.CurrrentEffects.ContainsKey(model.Balancing.AssetId))
					{
						continue;
					}
					AddIfValid(result, c);
				}
			}
			return result;
		}

		private static void AddIfValid(List<ICombatant> list, ICombatant c)
		{
			if (c != null && c.IsParticipating && c.CurrentHealth > 0f && !list.Contains(c))
			{
				list.Add(c);
			}
		}

		private static float ScoreMove(BattleGameData battle, List<Unit> field, Unit self, SkillBattleDataBase skill, ICombatant target, Faction side, float baseline)
		{
			var after = Clone(field);
			var actor = after.First(u => u.C == self.C);
			ApplyMove(battle, after, actor, skill, target);
			var score = Evaluate(battle, Respond(battle, after, actor), side) - baseline;
			float charge;
			if (actor.C.ChachedSkill == null && skill.Model.SkillParameters.TryGetValue("charge", out charge) && charge > 0f)
			{
				score /= 1f + charge;
			}
			return score;
		}

		private static List<Unit> BuildField(BattleGameData battle)
		{
			var field = new List<Unit>();
			foreach (var c in battle.m_CombatantsByInitiative)
			{
				if (!c.IsParticipating || c.CurrentHealth <= 0f)
				{
					continue;
				}
				var unit = new Unit
				{
					C = c,
					Side = c.CombatantFaction,
					Hp = c.CurrentHealth,
					MaxHp = Mathf.Max(1f, c.ModifiedHealth),
					Attack = Mathf.Max(0f, c.ModifiedAttack),
					Banner = c.IsBanner,
					Key = IsKeyUnit(battle, c)
				};
				ReadEffects(unit);
				field.Add(unit);
			}
			foreach (var unit in field)
			{
				unit.Threat = EstimateThreat(battle, field, unit);
			}
			return field;
		}

		// Units whose fall decides the battle on their own (PvP banners, explicit victory targets).
		private static bool IsKeyUnit(BattleGameData battle, ICombatant c)
		{
			if (battle.IsPvP)
			{
				return c.IsBanner;
			}
			var condition = battle.m_CurrentVictoryCondition;
			if (condition == null || string.IsNullOrEmpty(condition.NameId))
			{
				return false;
			}
			if ((condition.Type == VictoryConditionTypes.DefeatExplicit && c.CombatantFaction == Faction.Pigs) || (condition.Type == VictoryConditionTypes.CharacterSurviveTurns && c.CombatantFaction == Faction.Birds))
			{
				return c.CombatantNameId.Equals(condition.NameId, System.StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}

		private static void ReadEffects(Unit unit)
		{
			if (unit.C.CurrrentEffects == null)
			{
				return;
			}
			foreach (var effectData in unit.C.CurrrentEffects.Values)
			{
				if (effectData == null || effectData.m_IsRemoved || effectData.m_Effects == null)
				{
					continue;
				}
				foreach (var effect in effectData.m_Effects)
				{
					if (effect.Values == null || effect.Values.Count == 0)
					{
						continue;
					}
					var v = effect.Values[0] / 100f;
					switch (effect.EffectType)
					{
					case BattleEffectType.IncreaseDamageReceived:
						unit.TakenMul += v;
						break;
					case BattleEffectType.ReduceDamageReceived:
						unit.TakenMul -= v;
						break;
					case BattleEffectType.IncreaseDamage:
						unit.DealtMul += v;
						break;
					case BattleEffectType.ReduceDamageDealt:
						unit.DealtMul -= v;
						break;
					}
				}
			}
			unit.TakenMul = Mathf.Max(0f, unit.TakenMul);
			unit.DealtMul = Mathf.Max(0f, unit.DealtMul);
			if (unit.C.IsStunned)
			{
				unit.StunChance = 1f;
			}
		}

		// Expected damage (or healing, whichever is larger) per turn: the most it gets out of one of its skills.
		private static float EstimateThreat(BattleGameData battle, List<Unit> field, Unit unit)
		{
			var enemies = Mathf.Max(1, field.Count(u => u.Side != unit.Side && u.Alive));
			var allies = field.Where(u => u.Side == unit.Side && u.Alive).ToList();
			var avgAllyMaxHp = allies.Count > 0 ? allies.Average(u => u.MaxHp) : unit.MaxHp;
			var best = 0f;
			var skills = unit.C.GetSkills();
			if (skills != null)
			{
				for (var i = 0; i < skills.Count; i++)
				{
					var skill = skills[i];
					if (skill == null || skill.Model == null || (unit.C is BirdCombatant && i >= 2) || (unit.C.IsPvPBird && i >= 2))
					{
						continue;
					}
					var p = skill.Model.SkillParameters;
					var turns = 1f + Mathf.Max(0f, Param(p, "charge", 0f));
					var value = 0f;
					if (skill.Model.Balancing.TargetType == SkillTargetTypes.Attack && !p.ContainsKey("nodamage"))
					{
						value = unit.Attack * Param(p, "damage_in_percent", 100f) / 100f * Mathf.Max(1f, Param(p, "attack_count", 1f)) * (p.ContainsKey("all") ? enemies : 1f);
						value += unit.Attack * Param(p, "dot_damage_in_percent", 0f) / 100f * Param(p, "dot_chance", 100f) / 100f * 2f * FutureFactor;
						value *= unit.DealtMul;
					}
					else if (skill.Model.Balancing.TargetType == SkillTargetTypes.Support)
					{
						value = avgAllyMaxHp * Param(p, "health_in_percent", 0f) / 100f * (p.ContainsKey("all") ? allies.Count : 1f) * 0.5f;
					}
					best = Mathf.Max(best, value / turns);
				}
			}
			return Mathf.Max(best, unit.Attack * 0.1f, 1f);
		}

		private static float Param(Dictionary<string, float> p, string key, float fallback)
		{
			float value;
			return p != null && p.TryGetValue(key, out value) ? value : fallback;
		}

		private static List<Unit> Clone(List<Unit> field)
		{
			return field.Select(u => u.Copy()).ToList();
		}

		// The move's expected effect on the field.
		private static void ApplyMove(BattleGameData battle, List<Unit> field, Unit actor, SkillBattleDataBase skill, ICombatant targetCombatant)
		{
			var p = skill.Model.SkillParameters;
			var target = field.FirstOrDefault(u => u.C == targetCombatant);
			if (target == null)
			{
				return;
			}
			var template = skill.Model.Balancing.SkillTemplateType ?? string.Empty;
			if (GoadAllyIntoAttack.IsGoadSkill(skill.Model))
			{
				// The goaded ally attacks the birds at ally_damage_in_percent, after taking the goad's own hit.
				target.Hp = Mathf.Max(1f, target.Hp - actor.Attack * Param(p, "damage_in_percent", 0f) / 100f);
				var allyAttack = target.C.GetSkill(0);
				var allyPercent = allyAttack != null ? Param(allyAttack.Model.SkillParameters, "damage_in_percent", 100f) : 100f;
				GreedyHit(battle, field, target, target.Attack * allyPercent / 100f * Param(p, "ally_damage_in_percent", 100f) / 100f * target.DealtMul);
				return;
			}
			if (skill.Model.Balancing.TargetType == SkillTargetTypes.Attack)
			{
				ApplyAttack(battle, field, actor, skill, target, p, template);
			}
			else
			{
				ApplySupport(field, actor, target, p, template, skill.Model.Balancing.EffectDuration);
			}
		}

		private static void ApplyAttack(BattleGameData battle, List<Unit> field, Unit actor, SkillBattleDataBase skill, Unit target, Dictionary<string, float> p, string template)
		{
			var weights = new Dictionary<Unit, float>();
			var others = field.Where(u => u.Side == target.Side && u != target && u.Alive).ToList();
			weights[target] = 1f;
			if (p.ContainsKey("all"))
			{
				foreach (var u in others)
				{
					weights[u] = 1f;
				}
			}
			else
			{
				var extra = Param(p, "additional_enemies_affected", 0f);
				if (extra > 0f && others.Count > 0)
				{
					foreach (var u in others)
					{
						weights[u] = Mathf.Min(1f, extra / others.Count);
					}
				}
				var collateral = Param(p, "collateral_damage_in_percent", 0f);
				if (collateral > 0f)
				{
					foreach (var u in others)
					{
						float w;
						weights.TryGetValue(u, out w);
						weights[u] = w + collateral / 100f * 0.5f;
					}
				}
			}
			if (Param(p, "target_self", 0f) == 1f && actor.Side != target.Side)
			{
				weights[actor] = 1f;
			}
			var attack = skill as AttackSkillTemplate;
			var melee = false;
			try
			{
				melee = attack != null && attack.IsMeeleAttack(actor.C);
			}
			catch
			{
			}
			var hits = Mathf.Max(1f, Param(p, "attack_count", 1f));
			var noDamage = p.ContainsKey("nodamage") || template == "DoNothingSkill";
			var stun = template.Contains("Stun") ? Param(p, "chance", 0f) : Param(p, "stun_chance", 0f);
			var damageDebuff = Param(p, "reduction_in_percent", 0f);
			var takenIncrease = Param(p, "damage_taken_increase", Param(p, "increase_in_percent", 0f));
			var dealtTotal = 0f;
			foreach (var pair in weights)
			{
				var u = pair.Key;
				if (!u.Alive || (melee && ImmunityToMelee.IsImmune(u.C)))
				{
					continue;
				}
				var percent = Param(p, "damage_in_percent", 100f) + (u == target ? Param(p, "extra_damage_in_percent_on_target", 0f) : 0f);
				var perHit = actor.Attack * percent / 100f;
				if (attack != null)
				{
					// The game's own estimate (applies IgnoreDamage passives)
					perHit = Mathf.Max(0f, attack.EvaluateBaseDamage(actor.C, u.C) * (percent / Mathf.Max(1f, Param(p, "damage_in_percent", 100f))));
				}
				var damage = noDamage ? 0f : perHit * hits * actor.DealtMul * u.TakenMul * pair.Value;
				damage += actor.Attack * Param(p, "dot_damage_in_percent", 0f) / 100f * Param(p, "dot_chance", 100f) / 100f * Mathf.Max(1f, Param(p, "duration", 2f)) * FutureFactor * u.TakenMul * pair.Value;
				var dealt = Mathf.Min(u.Hp, damage);
				u.Hp -= damage;
				if (u.Side != actor.Side)
				{
					dealtTotal += dealt;
				}
				if (u.Alive)
				{
					if (stun > 0f)
					{
						u.StunChance = 1f - (1f - u.StunChance) * (1f - Mathf.Clamp01(stun / 100f * pair.Value));
					}
					if (damageDebuff > 0f && template.Contains("DamageDebuff"))
					{
						u.DealtMul *= 1f - Mathf.Clamp01(damageDebuff / 100f * pair.Value);
					}
					if (takenIncrease > 0f && template.Contains("IncreaseDamageReceived"))
					{
						u.TakenMul += takenIncrease / 100f * pair.Value;
					}
				}
			}
			var leech = Param(p, "heal_percent_of_damage", template.Contains("Leech") ? Param(p, "percent", 0f) : 0f);
			if (leech > 0f)
			{
				actor.Hp = Mathf.Min(actor.MaxHp, actor.Hp + dealtTotal * leech / 100f);
			}
			if (template.Contains("SelfDestruct"))
			{
				actor.Hp = 0f;
			}
		}

		private static void ApplySupport(List<Unit> field, Unit actor, Unit target, Dictionary<string, float> p, string template, int effectDuration)
		{
			var targets = p.ContainsKey("all") ? field.Where(u => u.Side == target.Side && u.Alive).ToList() : new List<Unit> { target };
			var turns = Mathf.Max(1f, effectDuration);
			var heal = Param(p, "health_in_percent", 0f);
			var overTime = template.StartsWith("Hot") || template.Contains("Regen");
			foreach (var u in targets)
			{
				if (!u.Alive)
				{
					continue;
				}
				if (heal > 0f)
				{
					var amount = u.MaxHp * (heal + (u == target ? Param(p, "additional_percent_on_target", 0f) : 0f)) / 100f;
					if (overTime)
					{
						amount *= turns * FutureFactor * Param(p, "hot_chance", 100f) / 100f;
					}
					u.Hp = Mathf.Min(u.MaxHp, u.Hp + amount);
				}
				var block = template.Contains("BlockDamage") ? Param(p, "percent", 0f) : Param(p, "reduction_in_percent", 0f);
				if (block > 0f)
				{
					u.TakenMul *= 1f - Mathf.Clamp01(block / 100f);
				}
				var buff = Param(p, "damage_increase", Param(p, "increase_in_percent", Param(p, "damage_dealt_increase", 0f)));
				if (buff > 0f)
				{
					// lasts the effect's duration: count its turns after this one at the future rate
					u.DealtMul *= 1f + buff / 100f * Param(p, "chance", 100f) / 100f * (1f + (turns - 1f) * FutureFactor);
				}
				var tempHealth = Param(p, "health_increase", 0f);
				if (tempHealth > 0f)
				{
					u.Hp += u.MaxHp * tempHealth / 100f;
				}
			}
			if (template.StartsWith("Summon") || p.ContainsKey("spawn"))
			{
				// An unknown reinforcement: count it as half a copy of the summoner.
				field.Add(new Unit
				{
					C = actor.C,
					Side = actor.Side,
					Hp = actor.MaxHp * 0.5f,
					MaxHp = actor.MaxHp,
					Attack = actor.Attack * 0.5f,
					Threat = actor.Threat * 0.5f
				});
			}
			if (template.Contains("SelfDestruct"))
			{
				actor.Hp = 0f;
			}
		}

		// Every other combatant takes one turn in initiative order (from the one after the actor), each making its own
		// side's best single-target attack with its main skill. Expected stuns scale its damage.
		private static List<Unit> Respond(BattleGameData battle, List<Unit> field, Unit actor)
		{
			var after = Clone(field);
			var index = after.FindIndex(u => u.C == actor.C && u.MaxHp == actor.MaxHp);
			for (var step = 1; step < after.Count; step++)
			{
				var u = after[(index + step) % after.Count];
				if (!u.Alive || u.Banner || ReferenceEquals(u.C, actor.C))
				{
					continue;
				}
				var main = u.C.GetSkill(0);
				var percent = main != null && main.Model.Balancing.TargetType == SkillTargetTypes.Attack ? Param(main.Model.SkillParameters, "damage_in_percent", 100f) * Mathf.Max(1f, Param(main.Model.SkillParameters, "attack_count", 1f)) : 0f;
				if (main != null && main.Model.SkillParameters.ContainsKey("charge") && u.C.ChachedSkill == null)
				{
					percent /= 1f + Mathf.Max(0f, main.Model.SkillParameters["charge"]);
				}
				var damage = u.Attack * percent / 100f * u.DealtMul * (1f - u.StunChance);
				if (main != null && main.Model.SkillParameters.ContainsKey("all"))
				{
					foreach (var t in after.Where(t => t.Side != u.Side && t.Alive))
					{
						t.Hp -= damage * t.TakenMul;
					}
				}
				else
				{
					GreedyHit(battle, after, u, damage);
				}
				u.StunChance = 0f;
			}
			return after;
		}

		// The attacker's side hits the foe where the damage is worth the most to it.
		private static void GreedyHit(BattleGameData battle, List<Unit> field, Unit attacker, float damage)
		{
			if (damage <= 0f)
			{
				return;
			}
			Unit best = null;
			var bestGain = float.NegativeInfinity;
			foreach (var t in field)
			{
				if (t.Side == attacker.Side || !t.Alive)
				{
					continue;
				}
				var dealt = damage * t.TakenMul;
				var gain = Weight(t) * Mathf.Min(dealt, t.Hp) / t.MaxHp + (dealt >= t.Hp ? Weight(t) : 0f);
				if (gain > bestGain)
				{
					bestGain = gain;
					best = t;
				}
			}
			if (best != null)
			{
				best.Hp -= damage * best.TakenMul;
			}
		}

		private static float Weight(Unit u)
		{
			return u.Threat * u.DealtMul * (u.Key ? KeyUnitWeight : 1f) + (u.Banner ? 1f : 0f);
		}

		private static float Evaluate(BattleGameData battle, List<Unit> field, Faction side)
		{
			var value = 0f;
			foreach (var u in field)
			{
				if (!u.Alive)
				{
					continue;
				}
				var worth = Weight(u) * (1f + Mathf.Clamp01(u.Hp / u.MaxHp));
				value += u.Side == side ? worth : -worth;
			}
			var other = side == Faction.Birds ? Faction.Pigs : Faction.Birds;
			if (IsDefeated(battle, field, other))
			{
				value += WinValue;
			}
			if (IsDefeated(battle, field, side))
			{
				value -= WinValue;
			}
			return value;
		}

		private static bool IsDefeated(BattleGameData battle, List<Unit> field, Faction faction)
		{
			var units = field.Where(u => u.Side == faction).ToList();
			if (battle.IsPvP)
			{
				return units.Any(u => u.Banner) && !units.Any(u => u.Banner && u.Alive);
			}
			// Combatants outside the field (isolated by a duel) still keep their side in the fight.
			return !units.Any(u => u.Alive) && !battle.m_IsolatedAway.Any(c => c.CombatantFaction == faction && c.IsParticipating);
		}
	}
}
