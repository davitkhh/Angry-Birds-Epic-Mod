using System.Collections;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using SmoothMoves;

namespace ABH.GameDatas.Battle.Skills
{
	public class IgnoreDamageSummonCombatant : SkillBattleDataBase
	{
		private float m_ModifiedReferencedDamageInPercent;

		private float m_LowerOrHigher;

		private float m_Delay;

		private float m_Random;

		private float m_Amount;

		private float m_Index;

		public override void BoneAnimationUserTrigger(UserTriggerEvent triggerEvent)
		{
			base.BoneAnimationUserTrigger(triggerEvent);
		}

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			model.SkillParameters.TryGetValue("refdamage_percent", out m_ModifiedReferencedDamageInPercent);
			model.SkillParameters.TryGetValue("lower_or_higher", out m_LowerOrHigher);
			
			model.SkillParameters.TryGetValue("amount", out m_Amount);
			model.SkillParameters.TryGetValue("delay_in_turns", out m_Delay);
			model.SkillParameters.TryGetValue("choose_random_combatant", out m_Random);
			model.SkillParameters.TryGetValue("table_index", out m_Index);
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
			if (m_LowerOrHigher < 0f)
			{
				var battleEffectGameData = new BattleEffectGameData(source, target, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnReceiveDamage,
						EffectType = BattleEffectType.IgnoreDamageIfLower,
						AfflicionType = base.Model.Balancing.EffectType,
						Values = new List<float> { m_ModifiedReferencedDamageInPercent },
						Duration = base.Model.Balancing.EffectDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
				battleEffectGameData.AddEffect();
			}
			else if (m_LowerOrHigher > 0f)
			{
				var battleEffectGameData = new BattleEffectGameData(source, target, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnReceiveDamage,
						EffectType = BattleEffectType.IgnoreDamageIfHigher,
						AfflicionType = base.Model.Balancing.EffectType,
						Values = new List<float> { m_ModifiedReferencedDamageInPercent },
						Duration = base.Model.Balancing.EffectDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
				battleEffectGameData.AddEffect();
			}
			DebugLog.Log("Trigger environmental skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			// Distinct effect ident so this doesn't collide with (and overwrite) the ignore-damage
			// effect above in CurrrentEffects, and so it doesn't inherit the Ironclad visual/animation
			// via the generic EvaluateEffect() visual-effect lookup - summoning plays no visual of its own.
			var summonEffectIdent = base.Model.SkillNameId + "_Summon";
			var battleEffectGameData2 = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnDealDamagePerTurn,
					EffectType = BattleEffectType.SummonCombatant,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float> { m_Amount, m_Delay, m_Random, m_Index },
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, base.Model.Balancing.EffectDuration, battle, summonEffectIdent, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			// Passives are re-applied every turn (AddPassiveEffects), replacing this effect - carry the
			// per-summoner turn counter over so the timer keeps running instead of resetting to 0.
			BattleEffectGameData previousSummonEffect;
			if (target.CurrrentEffects != null && target.CurrrentEffects.TryGetValue(summonEffectIdent, out previousSummonEffect) && previousSummonEffect != null)
			{
				battleEffectGameData2.m_SummonTurnCounter = previousSummonEffect.m_SummonTurnCounter;
			}
			battleEffectGameData2.AddEffect();
		}

		public float EvaluateIgnoreDamage(ICombatant invoker)
		{
			var num = 0;
			var gameConstantsBalancingDataProvider= DIContainerBalancing.GameConstantsBalancingDataProvider;
			var currentBattleGameData = ClientInfo.CurrentBattleGameData;
			if (m_LowerOrHigher < 0f)
			{
				if (currentBattleGameData != null && currentBattleGameData.m_ironCladValue != 0f)
				{
					return currentBattleGameData.m_ironCladValue * m_ModifiedReferencedDamageInPercent / 100f;
				}
				num = (int)(DIContainerLogic.GetBattleService().GetReferenceDamageIronclad(invoker.CharacterModel.Level, DIContainerInfrastructure.GetCurrentPlayer().Data.Level, gameConstantsBalancingDataProvider.ReferenceAttackValueBase, gameConstantsBalancingDataProvider.ReferenceAttackValuePerLevelInPercent) * m_ModifiedReferencedDamageInPercent / 100f);
			}
			else
			{
				if (currentBattleGameData != null && currentBattleGameData.m_dodgeValue != 0f)
				{
					return currentBattleGameData.m_dodgeValue * m_ModifiedReferencedDamageInPercent / 100f;
				}
				num = (int)(DIContainerLogic.GetBattleService().GetReferenceDamageDodge(invoker.CharacterModel.Level, DIContainerInfrastructure.GetCurrentPlayer().Data.Level, gameConstantsBalancingDataProvider.ReferenceAttackValueBase, gameConstantsBalancingDataProvider.ReferenceAttackValuePerLevelInPercent) * m_ModifiedReferencedDamageInPercent / 100f);
			}
			return num;
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var gameConstantsBalancingDataProvider = DIContainerBalancing.GameConstantsBalancingDataProvider;
			var num = 0;
			var currentBattleGameData = ClientInfo.CurrentBattleGameData;
			dictionary.Add("{value_1}", (m_LowerOrHigher < 0f ? currentBattleGameData == null || currentBattleGameData.m_ironCladValue == 0f ? (int)(DIContainerLogic.GetBattleService().GetReferenceDamageIronclad(invoker.CharacterModel.Level, DIContainerInfrastructure.GetCurrentPlayer().Data.Level, gameConstantsBalancingDataProvider.ReferenceAttackValueBase, gameConstantsBalancingDataProvider.ReferenceAttackValuePerLevelInPercent) * m_ModifiedReferencedDamageInPercent / 100f) : (int)(currentBattleGameData.m_ironCladValue * m_ModifiedReferencedDamageInPercent / 100f) : currentBattleGameData == null || currentBattleGameData.m_dodgeValue == 0f ? (int)(DIContainerLogic.GetBattleService().GetReferenceDamageDodge(invoker.CharacterModel.Level, DIContainerInfrastructure.GetCurrentPlayer().Data.Level, gameConstantsBalancingDataProvider.ReferenceAttackValueBase, gameConstantsBalancingDataProvider.ReferenceAttackValuePerLevelInPercent) * m_ModifiedReferencedDamageInPercent / 100f) : (int)(currentBattleGameData.m_dodgeValue * m_ModifiedReferencedDamageInPercent / 100f)).ToString("0"));
			dictionary.Add("{value_2}", m_Amount.ToString("0"));
			dictionary.Add("{value_3}", m_Delay.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
