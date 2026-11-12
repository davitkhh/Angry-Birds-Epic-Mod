using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Removes the status effect most recently put on a random ally. If it really came off (a purge can be resisted), the
	// caster gets a Crit effect: "critchance_in_percent" chance for "extra_damage"% more damage. With no ally that has a
	// removable effect the AI does not pick this skill at all (hooks in BattleService.GetCurrentSkillFromComboAndUpdateCombo
	// and GetNextTarget). A pig effect ticks at the start of its caster's turn, so EffectDuration 2 covers one attack.
	public class RemoveAllyEffectForCrit : SkillBattleDataBase
	{
		private int m_BuffDuration;

		private float m_CritChance;

		private float m_ExtraDamage;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			base.Model.SkillParameters.TryGetValue("critchance_in_percent", out m_CritChance);
			base.Model.SkillParameters.TryGetValue("extra_damage", out m_ExtraDamage);
			m_BuffDuration = base.Model.Balancing.EffectDuration;
		}

		public static bool IsRemoveAllyEffectSkill(SkillGameData skill)
		{
			return skill != null && skill.Balancing.SkillTemplateType == typeof(RemoveAllyEffectForCrit).Name;
		}

		// A random other ally with a status effect that can be removed, or null.
		public static ICombatant PickAlly(BattleGameData battle, ICombatant source)
		{
			var allies = battle.m_CombatantsByInitiative.Where(c => IsValidAlly(source, c)).ToList();
			return allies.Count == 0 ? null : allies[Random.Range(0, allies.Count)];
		}

		private static bool IsValidAlly(ICombatant source, ICombatant ally)
		{
			return ally != null && ally != source && ally.CombatantFaction == source.CombatantFaction && ally.IsParticipating && ally.IsAlive && ally.CombatantView != null && DIContainerLogic.GetBattleService().HasRemovableStatusEffect(ally);
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			m_Source = source;
			m_Battle = battle;
			m_InitialTarget = target;
			// The target was picked at the start of the turn; pick again if that ally no longer qualifies.
			var ally = IsValidAlly(source, target) ? target : PickAlly(battle, source);
			if (ally == null)
			{
				yield break;
			}
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + ally.CombatantName);
			// The skill's own visuals belong to the crit buff, which goes on the caster.
			m_Targets = new List<ICombatant> { source };
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting);
			// The setting's Impact visuals include the lasting crit effect, which the animation's Impact trigger would spawn
			// before we know the buff lands. They are spawned below instead.
			m_Targets = new List<ICombatant>();
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			if (!DIContainerLogic.GetBattleService().RemoveMostRecentBattleEffect(ally))
			{
				yield break;
			}
			ally.CombatantView.PlayCheerCharacter();
			VisualEffectSetting setting = null;
			if (DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting("Cleanse", out setting))
			{
				SpawnVisualEffects(VisualEffectSpawnTiming.Affected, setting, new List<ICombatant> { ally });
			}
			var effect = new BattleEffectGameData(source, source, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnDealDamage,
					EffectType = BattleEffectType.Crit,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float> { m_CritChance, m_ExtraDamage },
					Duration = m_BuffDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, m_BuffDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			effect.AddEffect();
			m_Targets = new List<ICombatant> { source };
			SpawnVisualEffects(VisualEffectSpawnTiming.Impact, m_VisualEffectSetting);
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + m_ExtraDamage);
			dictionary.Add("{value_2}", string.Empty + m_BuffDuration);
			dictionary.Add("{value_3}", string.Empty + m_CritChance);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
