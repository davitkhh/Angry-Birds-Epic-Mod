using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using SmoothMoves;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Passive (golden pigs, Wealthy Rogues): a purse of total_amount snoutlings that spills evenly as the pig loses health.
	// The coins still in the purse are spread over the pig's remaining health at the moment of each hit, so heals and
	// health changes re-spread them; whatever is left drops when the pig goes down.
	public class FullPockets : SkillBattleDataBase
	{
		private float m_TotalAmount;

		public override void BoneAnimationUserTrigger(UserTriggerEvent triggerEvent)
		{
			base.BoneAnimationUserTrigger(triggerEvent);
		}

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			if (!model.SkillParameters.TryGetValue("total_amount", out m_TotalAmount))
			{
				model.SkillParameters.TryGetValue("max_amount", out m_TotalAmount);
			}
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DoActionInstant(battle, source, target);
			yield break;
		}

		public override void DoActionInstant(BattleGameData battle, ICombatant source, ICombatant target)
		{
			DebugLog.Log("Trigger passive skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			var values = new List<float> { m_TotalAmount };
			var battleEffectGameData = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnReceiveDamage,
					EffectType = BattleEffectType.DropGold,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = values,
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			battleEffectGameData.AddEffect();
		}

		// Called by BattleService.DealDamageFromCurrentTurn after the target's health dropped from healthBefore by damage.
		// A CashStrike thief turns some coins into more damage on the pig, which re-enters here and spills from what's left.
		public static void OnDamageTaken(BattleGameData battle, ICombatant target, ICombatant source, int damage, float healthBefore)
		{
			if (target.CurrrentEffects == null || damage <= 0 || healthBefore <= 0f)
			{
				return;
			}
			var effectGameData = target.CurrrentEffects.Values.FirstOrDefault(e => !e.m_IsRemoved && e.m_Effects.Any(IsPurse));
			if (effectGameData == null)
			{
				return;
			}
			var total = (int)effectGameData.m_Effects.First(IsPurse).Values[0];
			int dropped;
			battle.m_FullPocketsDropped.TryGetValue(target, out dropped);
			var remaining = total - dropped;
			if (remaining <= 0)
			{
				return;
			}
			var amount = target.CurrentHealth <= 0f ? remaining : Math.Min(remaining, Mathf.FloorToInt(damage * remaining / healthBefore + 0.0001f));
			if (amount <= 0)
			{
				return;
			}
			battle.m_FullPocketsDropped[target] = dropped + amount;
			effectGameData.EvaluateEffect();
			DIContainerLogic.GetBattleService().DropStolenGold(battle, target, source ?? target.m_LastDamageSource, target, amount, false);
		}

		private static bool IsPurse(BattleEffect effect)
		{
			return effect.EffectType == BattleEffectType.DropGold && effect.EffectTrigger == EffectTriggerType.OnReceiveDamage;
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", m_TotalAmount.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
