using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	public class SimpleAttackWithMutualTauntSkill : AttackSkillTemplate
	{
		private float m_TimeStarted;

		private int m_BuffDuration;

		private float m_DamageMod;

		private float m_AttackCount;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			m_BuffDuration = base.Model.Balancing.EffectDuration;
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			base.Model.SkillParameters.TryGetValue("attack_count", out m_AttackCount);
			ActionsOnStartSkill.Add(delegate
			{
				m_TimeStarted = Time.time;
				return 0f;
			});
			ActionsAfterDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				ShareableActionPart(battle, source, target, false);
			});
			ActionsOnEnd.Add(delegate(BattleGameData battle, ICombatant source, ICombatant initialTarget)
			{
				var time = Time.time;
				return source.CombatantView.m_AssetController.GetAttackAnimationLength() - (time - m_TimeStarted);
			});
		}

		public override void ShareableActionPart(BattleGameData battle, ICombatant source, ICombatant target, bool isShared)
		{
			if (target.IsBanner)
			{
				return;
			}
			var possibleAllies = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == source.CombatantFaction && c != source && c.IsAlive && !c.IsBanner && c.CombatantView != null).ToList();
			if (possibleAllies.Count == 0)
			{
				return;
			}
			var randomAlly = possibleAllies[UnityEngine.Random.Range(0, possibleAllies.Count)];
			var values = new List<float> { 1f };
			var birdForcedToTargetAlly = new BattleEffectGameData(randomAlly, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnTarget,
					EffectType = BattleEffectType.Taunt,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = values,
					Duration = m_BuffDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, m_BuffDuration, battle, "Force_Target", base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			birdForcedToTargetAlly.SetPersistanceAfterDefeat(false).AddEffect();
			var allyForcedToTargetBird = new BattleEffectGameData(target, randomAlly, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnTarget,
					EffectType = BattleEffectType.Taunt,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = values,
					Duration = m_BuffDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, m_BuffDuration, battle, "Force_Target", base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			allyForcedToTargetBird.SetPersistanceAfterDefeat(false).LinkEffect(birdForcedToTargetAlly).AddEffect();
			if (target.CombatantView != null)
			{
				var targetBubble = target.CombatantView.m_SpeechBubbles.Values.FirstOrDefault();
				if (targetBubble != null && targetBubble.m_IsTargetedBubble)
				{
					targetBubble.SetTargetIcon("Target_" + randomAlly.CombatantAssetId);
					targetBubble.UpdateSkill();
				}
			}
			var allyBubble = randomAlly.CombatantView.m_SpeechBubbles.Values.FirstOrDefault();
			if (allyBubble != null && allyBubble.m_IsTargetedBubble)
			{
				allyBubble.SetTargetIcon("Target_" + target.CombatantAssetId);
				allyBubble.UpdateSkill();
			}
			foreach (var bird in battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == Faction.Birds))
			{
				bird.CombatantView.CheckForTauntTarget();
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var num = Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack);
			dictionary.Add("{value_1}", string.Empty + num);
			dictionary.Add("{value_2}", string.Empty + m_BuffDuration);
			dictionary.Add("{value_4}", string.Empty + m_AttackCount);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
