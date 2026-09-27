using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	public class IncreaseDamageRandomAlly : SkillBattleDataBase
	{
		private float m_DamageTakenIncreaseInPercent;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			model.SkillParameters.TryGetValue("damage_taken_increase", out m_DamageTakenIncreaseInPercent);
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			var candidates = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == source.CombatantFaction && c.IsAlive && c != source).ToList();
			if (candidates.Count == 0)
			{
				candidates.Add(source);
			}
			var chosenTarget = candidates[UnityEngine.Random.Range(0, candidates.Count)];
			m_InitialTarget = chosenTarget;
			m_Targets = new List<ICombatant> { chosenTarget };
			SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting);
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			var values = new List<float> { m_DamageTakenIncreaseInPercent };
			var battleEffectGameData = new BattleEffectGameData(source, chosenTarget, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnReceiveDamage,
					EffectType = BattleEffectType.IncreaseDamageReceived,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = values,
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				},
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.AfterAttack,
					EffectType = BattleEffectType.SpreadDamageReceivedDebuff,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = values,
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			battleEffectGameData.AddEffect();
			chosenTarget.CombatantView.PlayCheerCharacter();
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_3}", m_DamageTakenIncreaseInPercent.ToString("0"));
			dictionary.Add("{value_2}", base.Model.Balancing.EffectDuration.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
