using System.Collections;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// The pig stumbles (tumble animation, no damage) and curses its target:
	// the target deals reduction_in_percent less damage for EffectDuration turns.
	public class TumbleAndApplyDamageDebuff : SkillBattleDataBase
	{
		private float m_ReductionInPercent;

		private float m_ChargeTurns;

		private int m_BuffDuration;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			base.Model.SkillParameters.TryGetValue("reduction_in_percent", out m_ReductionInPercent);
			base.Model.SkillParameters.TryGetValue("charge", out m_ChargeTurns);
			m_BuffDuration = base.Model.Balancing.EffectDuration;
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger attack skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			if (EvaluateCharge(battle, source, m_Targets, target))
			{
				yield break;
			}
			if (source.CombatantView != null)
			{
				yield return new WaitForSeconds(source.CombatantView.PlayTumbledAnimation());
			}
			if (target == null || !target.IsAlive)
			{
				yield break;
			}
			var effect = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnDealDamage,
					EffectType = BattleEffectType.ReduceDamageDealt,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float> { m_ReductionInPercent },
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			effect.AddEffect();
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + m_ReductionInPercent);
			dictionary.Add("{value_2}", string.Empty + m_BuffDuration);
			dictionary.Add("{value_3}", string.Empty + m_ChargeTurns);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
