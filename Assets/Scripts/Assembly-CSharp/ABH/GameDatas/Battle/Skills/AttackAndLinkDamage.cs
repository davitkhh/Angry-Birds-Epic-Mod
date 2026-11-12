using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

namespace ABH.GameDatas.Battle.Skills
{
	// Hits and marks the target for EffectDuration turns. Whenever a marked combatant loses health, the ally with the
	// least or highest health (a coin flip per hit) takes shared_damage_in_percent of that damage. Shared damage doesn't
	// share again, even when that ally is marked too. The hit that applies the mark is shared as well.
	public class AttackAndLinkDamage : AttackSkillTemplate
	{
		public const string EffectIdent = "LinkDamage";

		private float m_DamageMod;

		private float m_SharedPercent;

		private static bool s_IsSharing;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			base.Model.SkillParameters.TryGetValue("shared_damage_in_percent", out m_SharedPercent);
			ActionsOnDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				if (damage <= 0f || target.IsBanner)
				{
					return;
				}
				// An existing mark already shared this hit in DealDamageFromCurrentTurn
				if (GetMark(target) == null)
				{
					ShareDamage(battle, target, source, damage * m_SharedPercent / 100f);
				}
				if (target.CurrentHealth > 0f)
				{
					AddMark(battle, source, target);
				}
			});
		}

		private void AddMark(BattleGameData battle, ICombatant source, ICombatant target)
		{
			// No handler exists for ShareDamage on this trigger, so the effect only marks; OnDamageTaken does the sharing
			new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.AfterReceiveDamage,
					EffectType = BattleEffectType.ShareDamage,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float> { m_SharedPercent },
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, base.Model.Balancing.EffectDuration, battle, EffectIdent, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId).AddEffect();
		}

		public static BattleEffectGameData GetMark(ICombatant combatant)
		{
			BattleEffectGameData mark;
			if (combatant.CurrrentEffects == null || !combatant.CurrrentEffects.TryGetValue(EffectIdent, out mark) || mark.m_IsRemoved)
			{
				return null;
			}
			return mark;
		}

		// Called by BattleService.DealDamageFromCurrentTurn with the mark the target had before its health dropped, since a
		// knockout can clear it and the killing blow is shared too.
		public static void OnDamageTaken(BattleGameData battle, ICombatant target, ICombatant source, int damage, float healthBefore, BattleEffectGameData mark)
		{
			if (s_IsSharing || mark == null || damage <= 0 || healthBefore <= 0f)
			{
				return;
			}
			mark.EvaluateEffect();
			ShareDamage(battle, target, source ?? target.m_LastDamageSource ?? mark.m_Source, damage * mark.m_Effects[0].Values[0] / 100f);
		}

		private static void ShareDamage(BattleGameData battle, ICombatant target, ICombatant attacker, float amount)
		{
			var partner = GetPartner(battle, target);
			if (partner == null || amount <= 0f)
			{
				return;
			}
			// Effects that heal from the attacker's last dealt damage must keep seeing the original hit
			var lastDealtDamage = attacker.LastDealtDamage;
			partner.ReceiveDamage(amount, attacker);
			s_IsSharing = true;
			try
			{
				DIContainerLogic.GetBattleService().DealDamageFromCurrentTurn(partner, battle, attacker);
			}
			finally
			{
				s_IsSharing = false;
			}
			attacker.LastDealtDamage = lastDealtDamage;
		}

		private static ICombatant GetPartner(BattleGameData battle, ICombatant target)
		{
			List<ICombatant> allies;
			if (!battle.m_CombatantsPerFaction.TryGetValue(target.CombatantFaction, out allies))
			{
				return null;
			}
			var candidates = allies.Where(c => c != target && c.IsParticipating && c.IsAlive && !c.IsBanner && c.CombatantView != null).ToList();
			if (candidates.Count == 0)
			{
				return null;
			}
			return UnityEngine.Random.value < 0.5f ? candidates.OrderBy(c => c.CurrentHealth).First() : candidates.OrderByDescending(c => c.CurrentHealth).First();
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var num = Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack);
			dictionary.Add("{value_1}", string.Empty + num);
			dictionary.Add("{value_2}", string.Empty + m_SharedPercent);
			dictionary.Add("{value_3}", string.Empty + base.Model.Balancing.EffectDuration);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
