using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Shelters random allies, but only on every Nth attack against each of them: the shelterer takes the
	// first hit, lets the next (hit_interval - 1) hits through, then takes one again. The counting lives in
	// the Sheltered handler in BattleService (4-value Sheltered effect).
	public class ProtectAllyEveryNthHitSkill : SkillBattleDataBase
	{
		private int m_BuffDuration;

		private float m_AmountOfAllies = 1f;

		private float m_HitInterval = 2f;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			base.Model.SkillParameters.TryGetValue("amount_of_targets", out m_AmountOfAllies);
			base.Model.SkillParameters.TryGetValue("hit_interval", out m_HitInterval);
			if (m_AmountOfAllies < 1f)
			{
				m_AmountOfAllies = 1f;
			}
			if (m_HitInterval < 1f)
			{
				m_HitInterval = 1f;
			}
			m_BuffDuration = base.Model.Balancing.EffectDuration;
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting);
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			var source2 = source;
			var allAllies = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == source2.CombatantFaction && c != source2 && c.IsAlive).ToList();
			m_Targets = new List<ICombatant>();
			while ((float)m_Targets.Count < m_AmountOfAllies && allAllies.Count > 0)
			{
				var chosenOne = allAllies[Random.Range(0, allAllies.Count)];
				m_Targets.Add(chosenOne);
				allAllies.Remove(chosenOne);
			}
			foreach (var protectTarget in m_Targets)
			{
				// [unused, chance to protect, hit interval, hits left to skip]
				var valueList = new List<float> { 0f, 100f, m_HitInterval, 0f };
				var effect = new BattleEffectGameData(source, protectTarget, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.AfterTargetSelection,
						EffectType = BattleEffectType.Sheltered,
						AfflicionType = base.Model.Balancing.EffectType,
						Values = valueList,
						Duration = m_BuffDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, m_BuffDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
				effect.SetPersistanceAfterDefeat(false).AddEffect();
				protectTarget.CombatantView.PlayCheerCharacter();
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + m_HitInterval);
			dictionary.Add("{value_2}", string.Empty + m_BuffDuration);
			dictionary.Add("{value_3}", string.Empty + m_AmountOfAllies);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
