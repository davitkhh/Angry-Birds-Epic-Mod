using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Links up to "link_count" random allies (the AI's target first, so its bubble stays true, and the caster can be one of
	// them) for EffectDuration turns. Whatever a linked combatant loses to an enemy is split evenly across the living group
	// (hook in BattleService.DealDamageFromCurrentTurn), and when an enemy attacks one of them the others counter with their
	// first skill for "counter_damage_in_percent" of its damage (a first skill that isn't an attack can't counter). A group
	// is the marks that share a caster, so a new cast replaces that caster's old group. The group is shown as a ring of link
	// lines: each mark's lasting visual (setting "SplitDamageAndCounter") draws a line to the next member.
	public class SplitDamageAndCounter : SkillBattleDataBase
	{
		private const string EffectIdent = "SplitDamageAndCounter";

		// Set while the shares are dealt, so that they don't get split again
		private static bool s_IsSplitting;

		private float m_LinkCount;

		private float m_CounterDamage;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			base.Model.SkillParameters.TryGetValue("link_count", out m_LinkCount);
			base.Model.SkillParameters.TryGetValue("counter_damage_in_percent", out m_CounterDamage);
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId);
			m_Source = source;
			m_Battle = battle;
			m_InitialTarget = target;
			m_Targets = PickGroup(battle, source, target);
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting);
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			foreach (var ally in battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == source.CombatantFaction && c.CombatantView != null).ToList())
			{
				var oldMark = GetMark(ally);
				if (oldMark != null && oldMark.m_Source == source)
				{
					oldMark.RemoveEffect();
				}
			}
			foreach (var pig in m_Targets.Where(IsValidMember))
			{
				if (pig != source)
				{
					pig.CombatantView.PlayCheerCharacter();
				}
				var mark = new BattleEffectGameData(source, pig, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnReceiveDamage,
						EffectType = BattleEffectType.SplitDamageAndCounter,
						AfflicionType = base.Model.Balancing.EffectType,
						Values = new List<float> { m_CounterDamage },
						Duration = base.Model.Balancing.EffectDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, base.Model.Balancing.EffectDuration, battle, EffectIdent, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
				mark.EffectRemovedAction = RefreshLinks;
				// Without visuals: AddEffect would draw the line from the caster, who may not be in the group
				mark.AddEffect(false);
			}
			SpawnLinks(battle, source);
		}

		private static List<ICombatant> GetGroup(BattleGameData battle, ICombatant caster)
		{
			return battle.m_CombatantsByInitiative.Where(delegate(ICombatant c)
			{
				if (!IsValidMember(c))
				{
					return false;
				}
				var mark = GetMark(c);
				return mark != null && mark.m_Source == caster;
			}).ToList();
		}

		private static void SpawnLinks(BattleGameData battle, ICombatant caster)
		{
			VisualEffectSetting setting = null;
			if (!DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting(EffectIdent, out setting))
			{
				return;
			}
			var group = GetGroup(battle, caster);
			for (var i = 0; i < group.Count; i++)
			{
				foreach (var effect in setting.VisualEffects)
				{
					// The invoker is where the line starts
					caster.CombatantView.m_BattleMgr.InstantiateEffect(group[(i + 1) % group.Count], effect, setting, new List<ICombatant> { group[i] }, false);
				}
			}
		}

		// A member's mark is gone (purged, replaced, expired): close the ring over the members that are left.
		private static void RefreshLinks(BattleEffectGameData removed)
		{
			var group = GetGroup(removed.m_Battle, removed.m_Source);
			for (var i = 0; i < group.Count; i++)
			{
				var view = group[i].CombatantView;
				if (!view.CheckAndContainsVisualEffect(EffectIdent))
				{
					continue;
				}
				var controller = view.LastingVisualEffects[EffectIdent].GetComponentInChildren<BattleFXController>();
				if (controller != null)
				{
					controller.SetLineRenderer(group[(i + 1) % group.Count].CombatantView.m_AssetController.BodyRoot, view.m_AssetController.BodyRoot);
				}
			}
		}

		private List<ICombatant> PickGroup(BattleGameData battle, ICombatant source, ICombatant target)
		{
			var candidates = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == source.CombatantFaction && IsValidMember(c)).OrderBy(c => Random.value).ToList();
			if (candidates.Remove(target))
			{
				candidates.Insert(0, target);
			}
			return candidates.Take((int)m_LinkCount).ToList();
		}

		private static bool IsValidMember(ICombatant combatant)
		{
			return combatant.IsParticipating && combatant.IsAlive && !combatant.IsBanner && combatant.CombatantView != null;
		}

		private static BattleEffectGameData GetMark(ICombatant combatant)
		{
			BattleEffectGameData mark;
			if (combatant.CurrrentEffects == null || !combatant.CurrrentEffects.TryGetValue(EffectIdent, out mark) || mark.m_IsRemoved)
			{
				return null;
			}
			return mark;
		}

		private static List<ICombatant> GetLinkedAllies(BattleEffectGameData mark)
		{
			return mark.m_Battle.m_CombatantsByInitiative.Where(delegate(ICombatant c)
			{
				if (c == mark.m_Target || !IsValidMember(c))
				{
					return false;
				}
				var other = GetMark(c);
				return other != null && other.m_Source == mark.m_Source;
			}).ToList();
		}

		// Called by BattleService.DealDamageFromCurrentTurn before the target loses health: deals the allies' shares and returns
		// how much of the damage went to them. The target keeps what doesn't divide evenly. Damage from its own side (a pig
		// blowing itself up, a sacrifice) stays with the target, otherwise the group could keep such a pig alive.
		public static int ShareWithLinkedAllies(BattleGameData battle, ICombatant target, ICombatant source, int damage)
		{
			if (s_IsSplitting || damage <= 0 || target.CurrentHealth <= 0f)
			{
				return 0;
			}
			var mark = GetMark(target);
			var origin = target.m_LastDamageSource ?? source;
			if (mark == null || origin == null || origin.CombatantFaction == target.CombatantFaction)
			{
				return 0;
			}
			var allies = GetLinkedAllies(mark);
			var share = damage / (allies.Count + 1);
			if (allies.Count == 0 || share == 0)
			{
				return 0;
			}
			mark.EvaluateEffect();
			// Effects that heal from the attacker's last dealt damage must keep seeing the whole hit
			var lastDealtDamage = origin.LastDealtDamage;
			s_IsSplitting = true;
			try
			{
				foreach (var ally in allies)
				{
					ally.ReceiveDamage(share, origin);
					DIContainerLogic.GetBattleService().DealDamageFromCurrentTurn(ally, battle, origin, true, true, false, true);
				}
			}
			finally
			{
				s_IsSplitting = false;
			}
			origin.LastDealtDamage = lastDealtDamage;
			return share * allies.Count;
		}

		// The BattleEffectType.SplitDamageAndCounter handler, run when an attack skill hits the mark's owner.
		public static void CounterWithLinkedAllies(BattleEffectGameData mark, float counterDamage, ICombatant attacker)
		{
			if (attacker == null || attacker.CombatantFaction == mark.m_Target.CombatantFaction)
			{
				return;
			}
			foreach (var ally in GetLinkedAllies(mark))
			{
				var attack = ally.GetSkill(0);
				if (attack != null && attack.Model.Balancing.TargetType == SkillTargetTypes.Attack)
				{
					ally.CombatantView.InitCounterAttack(attacker, attacker, ally, counterDamage, false);
				}
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var others = m_LinkCount - 1f;
			dictionary.Add("{value_1}", string.Empty + m_LinkCount);
			dictionary.Add("{value_2}", string.Empty + others);
			dictionary.Add("{value_3}", string.Empty + m_CounterDamage);
			dictionary.Add("{value_4}", string.Empty + base.Model.Balancing.EffectDuration);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
