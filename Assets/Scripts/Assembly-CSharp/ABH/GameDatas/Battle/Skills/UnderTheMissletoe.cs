using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Santa's Little Helper's "Under the Missletoe": picks "target_count" random allies (never the caster unless it
	// is the only partner left), heals each of them for "heal_in_percent" of the pair's combined max health, then
	// gives each one a Gang Up (AttackSameEnemy, like Sea Dog) whose source is the other: when one of them attacks,
	// the other attacks the same target at "damage_in_percent" ("chance" % of the time) for EffectDuration turns.
	// A gang-up attack is a counter attack, and counter attackers can't trigger another counter, so the pair never
	// ping-pongs.
	public class UnderTheMissletoe : SkillBattleDataBase
	{
		private float m_TargetCount = 2f;

		private float m_HealPercent = 20f;

		private float m_Damage = 100f;

		private float m_Chance = 100f;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			if (!base.Model.SkillParameters.TryGetValue("target_count", out m_TargetCount))
			{
				m_TargetCount = 2f;
			}
			if (!base.Model.SkillParameters.TryGetValue("heal_in_percent", out m_HealPercent))
			{
				m_HealPercent = 20f;
			}
			if (!base.Model.SkillParameters.TryGetValue("damage_in_percent", out m_Damage))
			{
				m_Damage = 100f;
			}
			if (!base.Model.SkillParameters.TryGetValue("chance", out m_Chance))
			{
				m_Chance = 100f;
			}
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + (target != null ? target.CombatantName : "none"));
			m_Source = source;
			m_InitialTarget = target;
			m_Targets = PickAllies(battle, source);
			if (m_Targets.Count == 0)
			{
				yield break;
			}
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting, m_Targets);
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			var combinedHealth = m_Targets.Sum(c => c.ModifiedHealth);
			var heal = m_HealPercent / 100f * combinedHealth;
			VisualEffectSetting healSetting = null;
			DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting("Heal_Strong", out healSetting);
			foreach (var ally in m_Targets)
			{
				if (ally != source)
				{
					ally.CombatantView.PlayCheerCharacter();
				}
				if (healSetting != null)
				{
					SpawnVisualEffects(VisualEffectSpawnTiming.Affected, healSetting, new List<ICombatant> { ally });
				}
				ally.HealDamage(heal, source);
				DIContainerLogic.GetBattleService().HealCurrentTurn(ally, battle, true, false, false, false, true, source);
			}
			if (m_Targets.Count < 2)
			{
				yield break;
			}
			var valueList = new List<float> { m_Chance, m_Damage, 0f };
			for (var i = 0; i < m_Targets.Count; i++)
			{
				var ally = m_Targets[i];
				var partner = m_Targets[(i + 1) % m_Targets.Count];
				var effect = new BattleEffectGameData(partner, ally, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.AfterAttack,
						EffectType = BattleEffectType.AttackSameEnemy,
						AfflicionType = base.Model.Balancing.EffectType,
						Values = valueList,
						Duration = base.Model.Balancing.EffectDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
				effect.SetPersistanceAfterDefeat(false);
				effect.AddEffect();
			}
			DebugLog.Log("[UnderTheMissletoe] healed " + m_Targets.Count + " allies for " + heal + " each and linked them with Gang Up");
		}

		// Random allies other than the caster; the caster fills in only when fewer than target_count others are left.
		private List<ICombatant> PickAllies(BattleGameData battle, ICombatant source)
		{
			var count = Mathf.Max(1, (int)m_TargetCount);
			var picked = battle.m_CombatantsByInitiative
				.Where(c => c != source && c.CombatantFaction == source.CombatantFaction && c.IsParticipating && c.CurrentHealth > 0f)
				.OrderBy(c => Random.value)
				.Take(count)
				.ToList();
			if (picked.Count < count && source.IsParticipating)
			{
				picked.Add(source);
			}
			return picked;
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", m_TargetCount.ToString("0"));
			dictionary.Add("{value_2}", m_HealPercent.ToString("0"));
			dictionary.Add("{value_3}", base.Model.Balancing.EffectDuration.ToString("0"));
			dictionary.Add("{value_4}", m_Damage.ToString("0"));
			dictionary.Add("{value_5}", m_Chance.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
