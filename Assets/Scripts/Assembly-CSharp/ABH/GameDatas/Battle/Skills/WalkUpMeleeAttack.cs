using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// A single-target melee hit for "damage_in_percent" on rigs whose attack clip has no "Movement" trigger (PigMage):
	// the attacker walks (Move_Loop) up to its target, or to whoever shelters it, then plays its attack animation there.
	// The turn walks it back home as after any melee attack.
	public class WalkUpMeleeAttack : SimpleAttackSkill
	{
		public override void Init(SkillGameData model)
		{
			base.Init(model);
			m_IsMelee = true;
			// The walk happens here; a Movement trigger added to the clip later must not start a second one.
			m_BlockMovement = true;
			ActionsOnStartSkill.Add(WalkToTarget);
		}

		private float WalkToTarget(BattleGameData battle, ICombatant source, System.Collections.Generic.List<ICombatant> targets, ICombatant initialTarget)
		{
			var view = source.CombatantView;
			var victim = view != null ? view.targetSheltered ?? (targets != null && targets.Count > 0 ? targets[0] : initialTarget) : null;
			if (view == null || victim == null || victim.CombatantView == null || source.IsBanner)
			{
				return 0f;
			}
			var victimView = victim.CombatantView;
			// Pigs stand right of the birds, birds left of the pigs.
			var side = source.CombatantFaction == Faction.Pigs ? 1f : -1f;
			var offset = new Vector3(side * (view.m_AssetController.OffsetFromEnemyX + victimView.m_AssetController.OffsetFromEnemyX) * Mathf.Abs(view.m_AssetController.transform.localScale.x), 0f, view.OffsetFromEnemyZ);
			if (victim.IsBanner)
			{
				offset += new Vector3(offset.x / 1.5f, 0f, 0f);
			}
			return view.PlayGoToPosition(offset, DIContainerLogic.GetPacingBalancing().TimeFromFocusPosToAttackPosInSec, true, victimView.transform);
		}
	}
}
