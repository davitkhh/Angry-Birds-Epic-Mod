using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

namespace ABH.GameDatas.Battle.Skills
{
	// Sugar Rush: hits for damage_in_percent and curses the target for EffectDuration turns. While cursed, the target
	// gets extra_moves bonus actions each turn, and every skill it uses (bonus ones included) deals
	// dot_damage_in_percent of the caster's attack to it. Handled by BattleEffectType.SugarRush on AfterSkillUse.
	public class SugarRushSkill : AttackSkillTemplate
	{
		private float m_DamageMod;

		private float m_ChargeTurns;

		private float m_DotChance;

		private float m_DotModifier;

		private float m_ExtraMoves;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			base.Model.SkillParameters.TryGetValue("charge", out m_ChargeTurns);
			if (!base.Model.SkillParameters.TryGetValue("dot_chance", out m_DotChance))
			{
				m_DotChance = 100f;
			}
			base.Model.SkillParameters.TryGetValue("dot_damage_in_percent", out m_DotModifier);
			if (!base.Model.SkillParameters.TryGetValue("extra_moves", out m_ExtraMoves))
			{
				m_ExtraMoves = 1f;
			}
			ActionsAfterDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				ShareableActionPart(battle, source, target, false);
			});
		}

		public override void ShareableActionPart(BattleGameData battle, ICombatant source, ICombatant target, bool isShared)
		{
			if (target == null || !target.IsAlive || UnityEngine.Random.value > m_DotChance / 100f)
			{
				return;
			}
			// Values: [0] damage % of the caster's attack per skill use, [1] bonus actions per turn, [2] last turn the bonus was granted.
			var values = new List<float> { m_DotModifier, m_ExtraMoves, -1f };
			var battleEffectGameData = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.AfterSkillUse,
					EffectType = BattleEffectType.SugarRush,
					Values = values,
					AfflicionType = base.Model.Balancing.EffectType,
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, SkillEffectTypes.Curse, GetLocalizedName(), base.Model.SkillNameId);
			battleEffectGameData.AddEffect();
		}

		// The BattleEffectType.SugarRush handler: the cursed combatant just finished using a skill.
		public static void OnSkillUsed(BattleEffectGameData effectGameData, BattleEffect effect)
		{
			var target = effectGameData.m_Target;
			if (target == null || !target.IsAlive)
			{
				return;
			}
			var source = effectGameData.m_Source;
			effectGameData.EvaluateEffect();
			if (source != null && effect.Values.Count > 0 && effect.Values[0] > 0f)
			{
				target.ReceiveDamage(source.ModifiedAttack * effect.Values[0] / 100f, source);
				DIContainerLogic.GetBattleService().DealDamageFromCurrentTurn(target, effectGameData.m_Battle, source);
			}
			if (!target.IsAlive || effect.Values.Count < 3)
			{
				return;
			}
			var battle = effectGameData.m_Battle;
			if (battle == null || (int)effect.Values[2] == battle.m_CurrentTurn)
			{
				return;
			}
			// Once per turn, so the bonus actions don't grant further bonus actions.
			effect.Values[2] = battle.m_CurrentTurn;
			var moves = Math.Max(0, (int)effect.Values[1]);
			if (moves <= 0)
			{
				return;
			}
			DebugLog.Log("[SugarRush] " + target.CombatantNameId + " gets " + moves + " bonus move(s) on turn " + battle.m_CurrentTurn);
			target.ExtraTurns += moves;
			if (target.CombatantView != null)
			{
				target.CombatantView.m_CommandGiven = false;
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack));
			dictionary.Add("{value_2}", string.Empty + base.Model.Balancing.EffectDuration);
			dictionary.Add("{value_3}", string.Empty + m_DotChance);
			dictionary.Add("{value_4}", string.Empty + m_ExtraMoves);
			dictionary.Add("{value_5}", string.Empty + m_ChargeTurns);
			dictionary.Add("{value_7}", string.Empty + Convert.ToInt32(m_DotModifier / 100f * invoker.ModifiedAttack));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_2}", string.Empty + m_ChargeTurns);
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, dictionary);
		}
	}
}
