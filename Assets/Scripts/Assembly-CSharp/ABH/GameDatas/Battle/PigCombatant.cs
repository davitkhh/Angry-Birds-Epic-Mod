using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

namespace ABH.GameDatas.Battle
{
	public class PigCombatant : BaseCombatant<PigGameData>
	{
		private SkillBattleDataBase m_PassiveSkill;

		public override string CombatantNameId
		{
			get
			{
				return Model.BalancingData.NameId;
			}
		}

		public override string CombatantName
		{
			get
			{
				return DIContainerInfrastructure.GetLocaService().GetCharacterName(Model.BalancingData.LocaId);
			}
		}

		public override string CombatantAssetId
		{
			get
			{
				return Model.BalancingData.AssetId;
			}
		}

		public override Faction CombatantFaction
		{
			get
			{
				return Model.BalancingData.Faction;
			}
			set
			{
			}
		}

		public SkillBattleDataBase PassiveSkill
		{
			get
			{
				if (m_PassiveSkill == null)
				{
					m_PassiveSkill = GenerateSkillBattleData(Model.PassiveSkill);
				}
				return m_PassiveSkill;
			}
		}

		public override bool UsedConsumable
		{
			get
			{
				return true;
			}
			set
			{
			}
		}

		public override bool IsRageAvailiable
		{
			get
			{
				return false;
			}
		}

		public override bool CanUseConsumable
		{
			get
			{
				return false;
			}
		}

		public override InterruptAction InterruptAction
		{
			get
			{
				return Model.ClassItem != null ? Model.ClassItem.BalancingData.InterruptAction : InterruptAction.none;
			}
		}

		public override List<InterruptCondition> InterruptCondition
		{
			get
			{
				return Model.ClassItem != null ? Model.ClassItem.m_InterruptCondition : new List<InterruptCondition>();
			}
		}

		public override bool IsCharging
		{
			get
			{
				var result = false;
				foreach (var value in CurrrentEffects.Values)
				{
					for (var i = 0; i < value.m_Effects.Count; i++)
					{
						if (value.m_Effects[i].EffectType == BattleEffectType.Charge)
						{
							result = true;
						}
					}
				}
				return result;
			}
		}

		public override bool UseRage
		{
			get
			{
				return false;
			}
			set
			{
			}
		}

		public PigCombatant(PigGameData model)
			: base(model)
		{
		}

		private class SwapFormState
		{
			public PigGameData Model;

			public List<SkillBattleDataBase> Skills;

			public SkillBattleDataBase PassiveSkill;

			// Passive skills whose effects were ended when the pig left this form; they're re-applied on return.
			public HashSet<string> EndedPassives = new HashSet<string>();
		}

		// Forms taken via a swap crystal, by pig NameId. Swapping back reuses the same model and skill instances,
		// so a passive that subscribes to events isn't subscribed a second time.
		private Dictionary<string, SwapFormState> m_SwapForms;

		// Swap crystal (SwapCrystalDirector): becomes another pig type in place. The combatant object stays, so
		// initiative, buffs/curses, view and health bar are untouched; max and current health carry over and only
		// attack, skills and looks change. The old form's passive effects end and the new form's start.
		public void SwapForm(string newPigNameId, BattleGameData battle)
		{
			var maxHealth = ModifiedHealth;
			var health = CurrentHealth;
			if (m_SwapForms == null)
			{
				m_SwapForms = new Dictionary<string, SwapFormState>();
			}
			SwapFormState oldForm;
			if (!m_SwapForms.TryGetValue(Model.BalancingData.NameId, out oldForm))
			{
				oldForm = new SwapFormState { Model = Model };
				m_SwapForms[Model.BalancingData.NameId] = oldForm;
			}
			oldForm.Skills = new List<SkillBattleDataBase>(m_Skills);
			oldForm.PassiveSkill = m_PassiveSkill;
			EndPassiveEffects(oldForm, battle);

			SwapFormState newForm;
			var firstTime = !m_SwapForms.TryGetValue(newPigNameId, out newForm);
			if (firstTime)
			{
				newForm = new SwapFormState { Model = new PigGameData(newPigNameId).SetDifficulties(battle.GetPlayerLevelForHotSpot(), battle.Balancing) };
				m_SwapForms[newPigNameId] = newForm;
			}
			newForm.Model.BaseHealthOverride = Model.BaseHealth;
			Model = newForm.Model;
			m_Skills.Clear();
			if (newForm.Skills != null)
			{
				m_Skills.AddRange(newForm.Skills);
			}
			m_PassiveSkill = newForm.PassiveSkill;
			m_CombatantClass = null;
			m_CombatantMainHandEquipment = null;
			m_CombatantOffHandEquipment = null;
			m_ChachedSkill = null;
			ComboInfo = null;
			// Attack is recomputed for the new type. Health isn't flagged: recomputing it refills current health.
			m_buffsAttackChanged = true;
			m_ModifiedHealth = maxHealth;
			m_currentHealth = health;

			foreach (var skill in GetSkills())
			{
				if (skill.Model.Balancing.TargetType == SkillTargetTypes.Passive && (firstTime || newForm.EndedPassives.Remove(skill.Model.SkillNameId)))
				{
					skill.DoActionInstant(battle, this, this);
				}
			}
		}

		private void EndPassiveEffects(SwapFormState form, BattleGameData battle)
		{
			var passiveIds = new HashSet<string>();
			foreach (var skill in form.Skills)
			{
				if (skill.Model.Balancing.TargetType == SkillTargetTypes.Passive)
				{
					passiveIds.Add(skill.Model.SkillNameId);
				}
			}
			if (passiveIds.Count == 0)
			{
				return;
			}
			foreach (var combatant in battle.m_CombatantsByInitiative)
			{
				foreach (var effect in new List<BattleEffectGameData>(combatant.CurrrentEffects.Values))
				{
					if (effect.m_Source == this && effect.m_EffectType == SkillEffectTypes.Passive && passiveIds.Contains(effect.m_SkillIdent))
					{
						effect.RemoveEffect();
						form.EndedPassives.Add(effect.m_SkillIdent);
					}
				}
			}
		}

		public override void RaiseCombatantDefeated()
		{
			base.RaiseCombatantDefeated();
		}

		public override bool AutoBattleDoRage()
		{
			return false;
		}

		public override bool AutoBattleReadyForRage()
		{
			return false;
		}

		public override void RaiseCombatantKnockedOut()
		{
			base.RaiseCombatantKnockedOut();
		}

		public override SkillBattleDataBase GetSkill(int index)
		{
			if (Model.Skills == null || Model.Skills.Count <= index)
			{
				return null;
			}
			if (m_Skills.Count != Model.Skills.Count)
			{
				foreach (var skill in Model.Skills)
				{
					AddBattleSkillImpl(skill);
				}
			}
			var skillBattleDataBase = m_Skills.Count <= index ? null : m_Skills[index];
			if (skillBattleDataBase != null)
			{
				skillBattleDataBase = skillBattleDataBase.CheckForReplacement(this);
			}
			return skillBattleDataBase;
		}

		public override void RaiseSkillTriggered(ICombatant invoker, ICombatant target)
		{
		}

		public override bool AddBonusTurns(int turns)
		{
			return false;
		}

		public override void CleansedCurse(BattleEffect effect, bool ignoreEffectFromStun = false)
		{
			base.CleansedCurse(effect);
			if (effect.EffectType == BattleEffectType.Stun && !ignoreEffectFromStun)
			{
				CombatantView.RefreshFromStun();
			}
		}
	}
}
