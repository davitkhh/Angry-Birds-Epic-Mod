using System.Collections;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

namespace ABH.GameDatas.Battle.Skills
{
	public class SummonCombatantHealthGated : SkillBattleDataBase
	{
		private float m_Amount;

		private float m_Delay;

		private float m_Random;

		private float m_Index;

		private float m_RoyalIndex;

		private float m_RoyalAmount;

		private float m_MixedIndex;

		private float m_MixedAmount;

		private float m_RoyalHealthThreshold;

		private float m_DesperateHealthThreshold;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			model.SkillParameters.TryGetValue("amount", out m_Amount);
			model.SkillParameters.TryGetValue("delay_in_turns", out m_Delay);
			model.SkillParameters.TryGetValue("choose_random_combatant", out m_Random);
			model.SkillParameters.TryGetValue("table_index", out m_Index);
			model.SkillParameters.TryGetValue("royal_table_index", out m_RoyalIndex);
			model.SkillParameters.TryGetValue("royal_amount", out m_RoyalAmount);
			model.SkillParameters.TryGetValue("mixed_table_index", out m_MixedIndex);
			model.SkillParameters.TryGetValue("mixed_amount", out m_MixedAmount);
			model.SkillParameters.TryGetValue("royal_health_threshold", out m_RoyalHealthThreshold);
			model.SkillParameters.TryGetValue("desperate_health_threshold", out m_DesperateHealthThreshold);
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DoActionInstant(battle, source, target);
			yield break;
		}

		public override void DoActionInstant(BattleGameData battle, ICombatant source, ICombatant target)
		{
			DebugLog.Log("Trigger environmental skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			var values = new List<float>
			{
				m_Amount,
				m_Delay,
				m_Random,
				m_Index,
				m_RoyalIndex,
				m_RoyalAmount,
				m_MixedIndex,
				m_MixedAmount,
				m_RoyalHealthThreshold,
				m_DesperateHealthThreshold
			};
			var battleEffectGameData = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnDealDamagePerTurn,
					EffectType = BattleEffectType.SummonCombatantHealthGated,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = values,
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			battleEffectGameData.AddEffect();
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", m_Amount.ToString("0"));
			dictionary.Add("{value_2}", m_Delay.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
