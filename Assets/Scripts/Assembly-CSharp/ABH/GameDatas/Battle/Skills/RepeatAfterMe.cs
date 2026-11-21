using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Pyropig's "Repeat After Me": he plays one of his other moves (picked at random) at "effectiveness_in_percent"
	// strength, then up to "apprentice_count" Apprentice pigs on his side each play one of their own moves of the same
	// kind (attack for an attack, support for a support, combo for a combo), also at that strength.
	//
	// A move at N% effectiveness is a per-use copy of the skill whose strength parameters (damage, healing, buff and
	// curse amounts, see IsStrengthParameter) are scaled by N%. Chances, durations, counts, thresholds and flags stay as
	// they are. The copy never charges: it goes off right away. A combo move signs up with the scaled strength, which
	// ComboRecipe.ComboDamage averages over the comboers.
	public class RepeatAfterMe : SkillBattleDataBase
	{
		private const string ApprenticePrefix = "pig_minion_apprentice";

		protected float m_EffectivenessPercent;

		protected float m_ApprenticeCount;

		// Set once Pyropig is back on his slot, so the end of his turn doesn't walk him there again.
		public bool ReturnedToBasePosition { get; protected set; }

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			if (!base.Model.SkillParameters.TryGetValue("effectiveness_in_percent", out m_EffectivenessPercent))
			{
				m_EffectivenessPercent = 60f;
			}
			if (!base.Model.SkillParameters.TryGetValue("apprentice_count", out m_ApprenticeCount))
			{
				m_ApprenticeCount = 2f;
			}
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + (target != null ? target.CombatantName : "none"));
			m_Source = source;
			m_Battle = battle;
			m_InitialTarget = target;
			ReturnedToBasePosition = false;
			yield return source.CombatantView.StartCoroutine(PlayRepeatSequence(battle, source));
		}

		// Subclasses (HoHoHelp) run their own lead-in, then this: the leader's move, then the repeaters'.
		protected IEnumerator PlayRepeatSequence(BattleGameData battle, ICombatant source)
		{
			var view = source.CombatantView;
			var factor = m_EffectivenessPercent / 100f;

			// 1. Pyropig's own move.
			var moves = RepeatableMoves(source, null);
			if (moves.Count == 0)
			{
				source.CombatantView.PlayMournAnimation();
				yield break;
			}
			var move = moves[Random.Range(0, moves.Count)];
			var kind = move.Model.Balancing.TargetType;
			DebugLog.Log("[RepeatAfterMe] " + source.CombatantNameId + " plays " + move.Model.SkillNameId + " (" + kind + ") at " + m_EffectivenessPercent + "%");
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting);
			yield return view.StartCoroutine(PlayMove(battle, source, move, factor, view));
			if (source.IsParticipating && view != null && !view.m_BattleMgr.IsBattleEnded())
			{
				yield return new WaitForSeconds(DIContainerLogic.GetPacingBalancing().TimeForShowDamageAndReturnToBasePosInSec);
				yield return new WaitForSeconds(view.PlayGoToBasePosition());
			}
			ReturnedToBasePosition = true;

			// 2. The apprentices repeat after him, each with a move of the same kind.
			var apprentices = PickRepeaters(battle, source, kind);
			m_Targets = apprentices;
			foreach (var apprentice in apprentices)
			{
				if (view == null || view.m_BattleMgr.IsBattleEnded() || !IsReadyRepeater(source, apprentice))
				{
					continue;
				}
				var apprenticeMoves = RepeatableMoves(apprentice, kind);
				if (apprenticeMoves.Count == 0)
				{
					continue;
				}
				var apprenticeMove = apprenticeMoves[Random.Range(0, apprenticeMoves.Count)];
				DebugLog.Log("[RepeatAfterMe] " + apprentice.CombatantNameId + " repeats with " + apprenticeMove.Model.SkillNameId + " at " + m_EffectivenessPercent + "%");
				var apprenticeView = apprentice.CombatantView;
				// Runs on Pyropig's view: the apprentice's could be destroyed if it gets knocked out mid-move.
				yield return view.StartCoroutine(PlayMove(battle, apprentice, apprenticeMove, factor, view));
				if (apprentice.IsParticipating && apprenticeView != null && !view.m_BattleMgr.IsBattleEnded())
				{
					yield return new WaitForSeconds(DIContainerLogic.GetPacingBalancing().TimeForShowDamageAndReturnToBasePosInSec);
					yield return new WaitForSeconds(apprenticeView.PlayGoToBasePosition());
				}
			}
		}

		// The combatant's own moves this skill can play: attacks, supports and combos it can afford right now, but never
		// another Repeat After Me or a move that steers the turn itself (goad, duel). kind == null: any of those kinds.
		// Who repeats after the leader: up to apprentice_count ready Apprentices with a move of that kind, in random order.
		protected virtual List<ICombatant> PickRepeaters(BattleGameData battle, ICombatant source, SkillTargetTypes kind)
		{
			return battle.m_CombatantsByInitiative.Where(c => IsReadyRepeater(source, c) && RepeatableMoves(c, kind).Count > 0).OrderBy(c => Random.value).Take(Mathf.Max(0, (int)m_ApprenticeCount)).ToList();
		}

		protected virtual bool IsReadyRepeater(ICombatant source, ICombatant c)
		{
			return IsReadyApprentice(source, c);
		}

		protected static List<SkillBattleDataBase> RepeatableMoves(ICombatant combatant, SkillTargetTypes? kind)
		{
			var skills = combatant.GetSkills();
			if (skills == null)
			{
				return new List<SkillBattleDataBase>();
			}
			return skills.Where(s => s != null && s.Model != null && !(s is RepeatAfterMe) && !(s is GoadAllyIntoAttack) && !(s is DuelKnockback) && !s.m_IsRageSkill && IsRepeatableKind(s.Model.Balancing.TargetType) && (!kind.HasValue || s.Model.Balancing.TargetType == kind.Value) && Ammo.CanAfford(combatant, s)).ToList();
		}

		private static bool IsRepeatableKind(SkillTargetTypes type)
		{
			return type == SkillTargetTypes.Attack || type == SkillTargetTypes.Support || type == SkillTargetTypes.Combo;
		}

		private static bool IsReadyApprentice(ICombatant source, ICombatant c)
		{
			return IsReadyAlly(source, c, ApprenticePrefix);
		}

		// An ally of source whose NameId starts with prefix, on the field and free to act right now.
		protected static bool IsReadyAlly(ICombatant source, ICombatant c, string prefix)
		{
			return c != null && c != source && c.CombatantFaction == source.CombatantFaction && c.CombatantNameId != null && c.CombatantNameId.StartsWith(prefix) && c.IsParticipating && c.IsAlive && !c.IsStunned && !c.IsBanner && c.CombatantView != null && c.ChachedSkill == null;
		}

		// Plays a scaled, charge-free copy of the move for actor, with a target picked the way the AI would pick it.
		private IEnumerator PlayMove(BattleGameData battle, ICombatant actor, SkillBattleDataBase move, float factor, MonoBehaviour runner)
		{
			var copy = ScaledCopy(move, factor);
			if (copy == null)
			{
				yield break;
			}
			var comboCopy = copy as ComboMove;
			if (comboCopy != null)
			{
				comboCopy.Effectiveness = factor;
			}
			var service = DIContainerLogic.GetBattleService();
			var previousTarget = actor.AttackTarget;
			if (!service.GetNextTarget(battle, actor, copy.Model, actor.CombatantFaction) && !service.GetRandomTarget(battle, actor, copy.Model, actor.CombatantFaction))
			{
				actor.AttackTarget = previousTarget;
				yield break;
			}
			var moveTarget = actor.AttackTarget;
			var actorView = actor.CombatantView;
			var boneAnimation = actorView != null && actorView.m_AssetController != null ? actorView.m_AssetController.m_BoneAnimation : null;
			if (boneAnimation != null)
			{
				boneAnimation.UnregisterUserTriggerDelegate(copy.BoneAnimationUserTrigger);
				boneAnimation.RegisterUserTriggerDelegate(copy.BoneAnimationUserTrigger);
			}
			if (moveTarget.CombatantFaction == actor.CombatantFaction)
			{
				service.ApplyEffectsOnTriggerType(0f, EffectTriggerType.OnSupportSkillReceived, moveTarget, actor);
			}
			else
			{
				service.ApplyEffectsOnTriggerType(0f, EffectTriggerType.BeforeAttack, actor, moveTarget);
			}
			Ammo.OnSkillStarted(actor, move);
			try
			{
				yield return runner.StartCoroutine(copy.DoAction(battle, actor, moveTarget));
			}
			finally
			{
				Ammo.OnSkillFinished(actor, move);
				if (boneAnimation != null)
				{
					boneAnimation.UnregisterUserTriggerDelegate(copy.BoneAnimationUserTrigger);
				}
				if (actorView != null)
				{
					actorView.targetSheltered = null;
				}
				actor.IsAttacking = false;
				actor.AttackTarget = previousTarget;
			}
		}

		private static SkillBattleDataBase ScaledCopy(SkillBattleDataBase move, float factor)
		{
			var parameters = new Dictionary<string, float>();
			foreach (var pair in move.Model.SkillParameters)
			{
				// No charge-up: the repeated move goes off on this turn.
				if (pair.Key == "charge" || pair.Key == "Charge" || pair.Key == "charge_min")
				{
					continue;
				}
				parameters.Add(pair.Key, IsStrengthParameter(pair.Key, pair.Value) ? pair.Value * factor : pair.Value);
			}
			var balancing = CloneSkillBalancing(move.Model.Balancing);
			balancing.SkillParameters = parameters;
			var copy = new SkillGameData(move.Model.Balancing.NameId, parameters).OverrideBalancing(balancing).GenerateSkillBattleData();
			if (copy != null)
			{
				copy.m_IsRageSkill = move.m_IsRageSkill;
			}
			return copy;
		}

		// How strong a move is: damage, healing and buff/curse amounts. Chances, durations, counts, thresholds,
		// animation settings and on/off flags (0 or 1) keep their values.
		private static bool IsStrengthParameter(string key, float value)
		{
			if (value == 0f || value == 1f)
			{
				return false;
			}
			var k = key.ToLowerInvariant();
			string[] keep = { "chance", "charge", "turn", "duration", "treshold", "threshold", "below", "activate", "count", "anim_", "table", "summon", "nodamage", "by_percent", "amount", "delay", "interval", "attention", "invoker" };
			if (keep.Any(term => k.Contains(term)))
			{
				return false;
			}
			string[] strength = { "percent", "damage", "heal", "increase", "reduc", "bonus", "boost", "fixed", "percentage", "extra", "health", "hot_", "volley", "steal", "leech", "counter", "coins" };
			return strength.Any(term => k.Contains(term));
		}

		private static SkillBalancingData CloneSkillBalancing(SkillBalancingData source)
		{
			var clone = new SkillBalancingData();
			foreach (var property in typeof(SkillBalancingData).GetProperties())
			{
				if (property.CanRead && property.CanWrite)
				{
					property.SetValue(clone, property.GetValue(source, null), null);
				}
			}
			return clone;
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", m_EffectivenessPercent.ToString("0"));
			dictionary.Add("{value_2}", m_ApprenticeCount.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
