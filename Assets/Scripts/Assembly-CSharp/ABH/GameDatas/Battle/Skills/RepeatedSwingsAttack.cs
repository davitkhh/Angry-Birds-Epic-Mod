using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ABH.GameDatas.Battle.Skills
{
	// Royal Bodyguard's Assault (AttackHailSkill) with the attacker as its only fighter: "swing_count" separate attacks,
	// each this skill's own swing for "damage_in_percent" at a random enemy, pulling back to the focus point between them
	// (unlike "attack_count", which splits one swing's hit into several). The first attack goes at the picked target.
	public class RepeatedSwingsAttack : SimpleAttackSkill
	{
		private float m_DamageMod;

		private int m_SwingCount;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			var swings = 1f;
			base.Model.SkillParameters.TryGetValue("swing_count", out swings);
			m_SwingCount = Mathf.Max(1, Mathf.RoundToInt(swings));
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			m_BlockMovement = false;
			battle.SetRageAvailable(source.CombatantFaction, false);
			try
			{
				for (var i = 0; i < m_SwingCount; i++)
				{
					if (i > 0)
					{
						if (!source.IsParticipating || source.IsStunned || source.CombatantView == null || source.CombatantView.m_BattleMgr.IsBattleEnded())
						{
							break;
						}
						var enemies = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction != source.CombatantFaction && c.IsParticipating && !c.IsBanner).ToList();
						if (enemies.Count == 0)
						{
							break;
						}
						// Back to the focus point between the two sides (where every attack dash starts), then pick the next
						// target like a fresh attack.
						yield return new WaitForSeconds(source.CombatantView.PlayGoToFocusPosition());
						target = enemies[Random.Range(0, enemies.Count)];
						source.AttackTarget = target;
						DIContainerLogic.GetBattleService().ApplyEffectsOnTriggerType(0f, EffectTriggerType.OnTarget, source, target);
						DIContainerLogic.GetBattleService().ApplyEffectsOnTriggerType(0f, EffectTriggerType.BeforeAttack, source, target);
					}
					// Not on his own view: it is destroyed if he gets knocked out mid-combo.
					yield return DIContainerInfrastructure.GetCoreStateMgr().StartCoroutine(base.DoAction(battle, source, target, shared, illusion));
					source.IsAttacking = false;
					var sheltered = source.CombatantView != null ? source.CombatantView.targetSheltered : null;
					if (sheltered != null && i < m_SwingCount - 1)
					{
						// The turn only sends the last shelterer home; send each earlier one back here.
						yield return new WaitForSeconds(DIContainerLogic.GetPacingBalancing().TimeAfterSheltering);
						sheltered.CombatantView.PlayGoToBasePosition();
						target.CombatantView.PlayGoToBasePosition();
						source.CombatantView.targetSheltered = null;
					}
				}
			}
			finally
			{
				battle.SetRageAvailable(source.CombatantFaction, true);
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack));
			dictionary.Add("{value_4}", string.Empty + m_SwingCount);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}
	}
}
