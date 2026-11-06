using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Pirate captain attack: marks the target. Pirates with a blessing can only attack marked birds and deal
	// bonus_in_percent more to them (yellow crosshair). Once every bird is marked, all marks refresh to
	// empowered_duration turns and are empowered: every pirate deals empowered_bonus_in_percent more to them (red skull
	// crosshair), and no bird can be marked until the empowered marks are gone.
	public class Stinkeye : AttackSkillTemplate
	{
		public const string EffectIdent = "Stinkeye";

		public const string AimIdent = "Stinkeye_Aim";

		public const string EmpoweredAimIdent = "Stinkeye_AimEmpowered";

		private static bool s_RefreshingIndicators;

		private float m_DamageMod;

		private float m_AttackCount;

		private float m_BonusPercent;

		private float m_EmpoweredBonusPercent;

		private float m_EmpoweredDuration;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			base.Model.SkillParameters.TryGetValue("attack_count", out m_AttackCount);
			base.Model.SkillParameters.TryGetValue("bonus_in_percent", out m_BonusPercent);
			base.Model.SkillParameters.TryGetValue("empowered_bonus_in_percent", out m_EmpoweredBonusPercent);
			base.Model.SkillParameters.TryGetValue("empowered_duration", out m_EmpoweredDuration);
			ActionsAfterDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				ShareableActionPart(battle, source, target, false);
			});
		}

		public override void ShareableActionPart(BattleGameData battle, ICombatant source, ICombatant target, bool isShared)
		{
			if (target.CombatantFaction != Faction.Birds || target.IsBanner || IsEmpowerActive(battle))
			{
				return;
			}
			AddMark(battle, source, target, base.Model.Balancing.EffectDuration, false);
			var birds = GetMarkableBirds(battle);
			if (birds.Count > 0 && birds.All(b => GetMark(b) != null))
			{
				foreach (var bird in birds)
				{
					AddMark(battle, source, bird, (int)m_EmpoweredDuration, true);
				}
			}
			RefreshIndicators(battle);
		}

		private void AddMark(BattleGameData battle, ICombatant source, ICombatant target, int duration, bool empowered)
		{
			new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnReceiveDamage,
					EffectType = BattleEffectType.Stinkeye,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float> { m_BonusPercent, m_EmpoweredBonusPercent, empowered ? 1f : 0f },
					Duration = duration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, duration, battle, EffectIdent, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId).AddEffect();
		}

		// Values: [bonus %, empowered bonus %, empowered (1/0)]
		public static bool IsEmpowered(BattleEffect effect)
		{
			return effect.Values.Count > 2 && effect.Values[2] > 0f;
		}

		public static float GetBonusPercent(BattleEffect effect)
		{
			return IsEmpowered(effect) ? effect.Values[1] : effect.Values[0];
		}

		public static bool IsPirate(ICombatant combatant)
		{
			return combatant is PigCombatant && combatant.CombatantNameId.Contains("pirate");
		}

		public static bool IsBlessed(ICombatant combatant)
		{
			return combatant.CurrrentEffects != null && combatant.CurrrentEffects.Values.Any(e => !e.m_IsRemoved && e.m_EffectType == SkillEffectTypes.Blessing);
		}

		private static BattleEffect GetMark(ICombatant combatant)
		{
			BattleEffectGameData mark;
			if (combatant.CurrrentEffects == null || !combatant.CurrrentEffects.TryGetValue(EffectIdent, out mark) || mark.m_IsRemoved)
			{
				return null;
			}
			return mark.m_Effects.FirstOrDefault(e => e.EffectType == BattleEffectType.Stinkeye);
		}

		private static List<ICombatant> GetMarkableBirds(BattleGameData battle)
		{
			List<ICombatant> birds;
			if (!battle.m_CombatantsPerFaction.TryGetValue(Faction.Birds, out birds))
			{
				return new List<ICombatant>();
			}
			return birds.Where(b => b.IsParticipating && !b.IsBanner).ToList();
		}

		private static List<ICombatant> GetMarkedBirds(BattleGameData battle, bool empowered)
		{
			return GetMarkableBirds(battle).Where(b => GetMark(b) != null && IsEmpowered(GetMark(b)) == empowered).ToList();
		}

		public static bool IsEmpowerActive(BattleGameData battle)
		{
			return GetMarkedBirds(battle, true).Count > 0;
		}

		// A regularly marked bird this pirate has to attack (pirates with a blessing only attack marked birds), or null
		public static ICombatant GetForcedMarkTarget(BattleGameData battle, ICombatant combatant)
		{
			if (!IsPirate(combatant) || !IsBlessed(combatant))
			{
				return null;
			}
			var marked = GetMarkedBirds(battle, false);
			return marked.Count == 0 ? null : marked[UnityEngine.Random.Range(0, marked.Count)];
		}

		// Shows the yellow crosshair on blessed pirates while regular marks exist, the red skull crosshair on every
		// pirate while marks are empowered. Called whenever effects change.
		public static void RefreshIndicators(BattleGameData battle)
		{
			List<ICombatant> pigs;
			if (battle == null || s_RefreshingIndicators || !battle.m_CombatantsPerFaction.TryGetValue(Faction.Pigs, out pigs))
			{
				return;
			}
			s_RefreshingIndicators = true;
			try
			{
				var marked = GetMarkedBirds(battle, false);
				var empowered = IsEmpowerActive(battle);
				var aimIcon = marked.Count == 1 ? "Target_" + marked[0].CombatantAssetId : "Target_Birds";
				foreach (var pig in pigs.ToList())
				{
					if (pig.CombatantView == null || pig.CombatantView.m_AssetController == null || pig.CombatantView.m_BattleMgr == null)
					{
						continue;
					}
					var pirate = pig.IsParticipating && IsPirate(pig);
					SetIndicator(pig, EmpoweredAimIdent, pirate && empowered, null);
					SetIndicator(pig, AimIdent, pirate && !empowered && marked.Count > 0 && IsBlessed(pig), aimIcon);
				}
			}
			finally
			{
				s_RefreshingIndicators = false;
			}
		}

		private static void SetIndicator(ICombatant pig, string ident, bool show, string icon)
		{
			var view = pig.CombatantView;
			var shown = view.CheckAndContainsVisualEffect(ident);
			if (!show)
			{
				if (shown)
				{
					var indicator = view.LastingVisualEffects[ident];
					UnityEngine.Object.Destroy(indicator, indicator.PlayAnimationOrAnimatorState("End"));
					view.LastingVisualEffects.Remove(ident);
				}
				return;
			}
			if (!shown)
			{
				VisualEffectSetting setting;
				if (!DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting(ident, out setting))
				{
					return;
				}
				foreach (var visualEffect in setting.VisualEffects.Where(v => v.SpawnTiming == VisualEffectSpawnTiming.Affected))
				{
					view.m_BattleMgr.InstantiateEffect(pig, visualEffect, setting, new List<ICombatant> { pig }, false);
				}
			}
			if (icon != null && view.CheckAndContainsVisualEffect(ident))
			{
				var fx = view.LastingVisualEffects[ident].GetComponentInChildren<BattleFXController>();
				if (fx)
				{
					fx.SendParameter(icon);
				}
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var num = Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack);
			dictionary.Add("{value_1}", string.Empty + num);
			dictionary.Add("{value_2}", string.Empty + base.Model.Balancing.EffectDuration);
			dictionary.Add("{value_3}", string.Empty + (100f + m_BonusPercent));
			dictionary.Add("{value_4}", string.Empty + m_AttackCount);
			dictionary.Add("{value_5}", string.Empty + m_EmpoweredDuration);
			dictionary.Add("{value_6}", string.Empty + (100f + m_EmpoweredBonusPercent));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
