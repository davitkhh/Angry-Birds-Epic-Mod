using System.Collections;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// A combo move (TargetType Combo, the orange name strip). It is chosen and played like an attack, but on the
	// combatant's turn it only signs up with the side's ComboMoveDirector. When that side's turn ends, every combo move
	// it played that turn adds up to one ComboRecipe (a SkillBalancingData row "combo_<ids>", see ComboRecipe.KeyFor)
	// in which every comboer takes part.
	//
	// The combo id is the skill's AssetId (lower case), so variants of one move (_bird3 copies etc.) combine as the
	// same move. Give the row the "no_focus" parameter so the combatant stays on its slot while signing up.
	public class ComboMove : SkillBattleDataBase
	{
		// Shortest time the sign-up shows before the turn moves on.
		private const float MinSignUpTime = 0.4f;

		private const float MaxSignUpTime = 1.2f;

		// Strength of this sign-up (1 = full). A move repeated at reduced effectiveness (RepeatAfterMe) lowers it;
		// the recipe's damage is scaled by the comboers' average.
		public float Effectiveness = 1f;

		public string ComboId
		{
			get
			{
				return (base.Model.Balancing.AssetId ?? string.Empty).ToLowerInvariant();
			}
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			m_Source = source;
			var view = source.CombatantView;
			if (view == null || view.m_BattleMgr == null)
			{
				yield break;
			}
			ComboMoveDirector.For(view.m_BattleMgr).Join(source, this);
			DebugLog.Log("[ComboMove] " + source.CombatantNameId + " joins the combo with " + base.Model.SkillNameId + " (" + ComboId + ")");
			view.ShowCombatLabel(DIContainerInfrastructure.GetLocaService().Tr("combo_signup", "Combo!"), SkillBlind.ComboColor);
			var time = view.PlayCheerCharacter();
			yield return new WaitForSeconds(Mathf.Clamp(time, MinSignUpTime, MaxSignUpTime));
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			// {value_N} come from the recipes this move appears in: <recipe NameId>.<parameter>, damage already computed.
			var dictionary = new Dictionary<string, string>();
			var attack = invoker != null ? invoker.ModifiedAttack : 0f;
			ComboRecipe.AddDescriptionValues(dictionary, ComboId, attack);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
