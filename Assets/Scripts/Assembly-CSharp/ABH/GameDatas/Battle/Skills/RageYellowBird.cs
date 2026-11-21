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
	public class RageYellowBird : SkillBattleDataBase
	{
		private int m_AttackCount;

		protected float m_Percent;

		protected float m_Fixed;

		protected bool m_All;

		protected float m_ChargeRounds;

		protected bool m_SpawnImage;

		private bool m_ImpactDone;

		private CharacterAssetController m_copy;

		private ICombatant m_currentOriginal;

		private List<ICombatant> m_possibleAttackingCharacters;

		public bool SkipCastAnimation;

		public override void BoneAnimationUserTrigger(UserTriggerEvent triggerEvent)
		{
			if (triggerEvent.tag == "Impact")
			{
				DebugLog.Log("Impact occured");
				SpawnVisualEffects(VisualEffectSpawnTiming.Impact, m_VisualEffectSetting);
			}
		}

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			model.SkillParameters.TryGetValue("attack_count", out value);
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out m_Percent);
			m_AttackCount = (int)value;
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			var aliveEnemiesBefore = battle.m_CombatantsPerFaction[Faction.Pigs].Where(c => c.IsAlive).ToList();
			DebugLog.Log("Trigger attack skill: " + base.Model.Balancing.NameId + "; Attack: " + target.CombatantName);
			m_Source = source;
			m_possibleAttackingCharacters = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == target.CombatantFaction && !c.IsStunned && c.IsParticipating).ToList();
			m_possibleAttackingCharacters.Shuffle();
			if (!SkipCastAnimation)
			{
				SpawnVisualEffects(VisualEffectSpawnTiming.Start, m_VisualEffectSetting);
				yield return new WaitForSeconds(source.CombatantView.PlayRageSkillAnimation());
			}
			battle.SetRageAvailable(source.CombatantFaction, false);
			if (source.CombatantFaction == Faction.Pigs)
			{
				battle.SetFactionRage(Faction.Pigs, 0f);
				battle.RegisterRageUsed(100f, source);
			}
			for (var i = 0; i < m_AttackCount; i++)
			{
				if (m_possibleAttackingCharacters.Count <= 0)
				{
					break;
				}
				// Birds out of ammo for their attack sit this out
				m_possibleAttackingCharacters = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == target.CombatantFaction && !c.IsStunned && c.IsParticipating && !c.IsBanner && Ammo.CanAfford(c, c.GetSkills().FirstOrDefault(s => s.Model.Balancing.TargetType == SkillTargetTypes.Attack))).ToList();
				if (m_possibleAttackingCharacters.Count <= 0)
				{
					break;
				}
				var skillTarget = m_possibleAttackingCharacters[i % m_possibleAttackingCharacters.Count];
				var possibleEnemies = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction != skillTarget.CombatantFaction).ToList();
				if (possibleEnemies.Count <= 0)
				{
					continue;
				}
				var orderTarget = possibleEnemies[Random.Range(0, possibleEnemies.Count)];
				var skill = skillTarget.GetSkills().FirstOrDefault(s => s.Model.Balancing.TargetType == SkillTargetTypes.Attack);
				if (skill == null)
				{
					continue;
				}
				m_copy = null;
				if (m_SpawnImage)
				{
					m_copy = DIContainerInfrastructure.GetCharacterAssetProvider(false).InstantiateObject(skillTarget.CombatantAssetId, null, Vector3.zero, Quaternion.identity).GetComponent<CharacterAssetController>();
					m_copy.transform.position = new Vector3(skillTarget.CombatantView.transform.position.x + Mathf.Sign(skillTarget.CombatantView.transform.position.x) * 100f, skillTarget.CombatantView.transform.position.y, skillTarget.CombatantView.transform.position.z);
					m_copy.transform.parent = skillTarget.CombatantView.m_BattleMgr.m_BattleArea;
					m_copy.transform.rotation = skillTarget.CombatantView.transform.rotation;
					m_currentOriginal = skillTarget;
					m_copy.SetModel(skillTarget.CharacterModel, false);
					yield return new WaitForSeconds(0.5f);
				}
				var valueList = new List<float> { 100f - m_Percent };
				var effect = new BattleEffectGameData(source, skillTarget, new List<BattleEffect>
				{
					new BattleEffect
					{
						EffectTrigger = EffectTriggerType.OnDealDamage,
						EffectType = BattleEffectType.ReduceDamageDealt,
						Values = valueList,
						AfflicionType = base.Model.Balancing.EffectType,
						Duration = base.Model.Balancing.EffectDuration,
						EffectAssetId = base.Model.Balancing.EffectIconAssetId,
						EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
					}
				}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, SkillEffectTypes.Blessing, GetLocalizedName(), base.Model.SkillNameId);
				effect.AddEffect();
				if (skillTarget.CombatantView.m_AssetController)
				{
					skillTarget.CombatantView.m_AssetController.m_BoneAnimation.UnregisterUserTriggerDelegate(skill.BoneAnimationUserTrigger);
					skillTarget.CombatantView.m_AssetController.m_BoneAnimation.RegisterUserTriggerDelegate(skill.BoneAnimationUserTrigger);
				}
				var cachedTarget = skillTarget.AttackTarget;
				skillTarget.AttackTarget = orderTarget;
				DIContainerLogic.GetBattleService().ApplyEffectsOnTriggerType(0f, EffectTriggerType.OnTarget, skillTarget, skillTarget.AttackTarget);
				skillTarget.Defeated -= skillTarget_Defeated;
				skillTarget.Defeated += skillTarget_Defeated;
				var pig = skillTarget as PigCombatant;
				if (pig != null && pig.ChachedSkill != null && skill.EvaluateCharge(battle, pig, new List<ICombatant> { pig.AttackTarget }, null))
				{
					yield return new WaitForSeconds(DIContainerLogic.GetPacingBalancing().TimeForPigToChooseTargetAndDoSkillInSec);
				}
				DebugLog.Log("Character: " + skillTarget.CombatantNameId + " starts his attack!");
				Ammo.OnSkillStarted(skillTarget, skill);
				yield return skillTarget.CombatantView.StartCoroutine(skill.DoAction(battle, skillTarget, orderTarget));
				skillTarget.IsAttacking = false;
				skillTarget.AttackTarget = cachedTarget;
				DebugLog.Log("Character: " + skillTarget.CombatantNameId + " finished his attack!");
				if (skillTarget.CombatantView.m_AssetController)
				{
					skillTarget.CombatantView.m_AssetController.m_BoneAnimation.UnregisterUserTriggerDelegate(skill.BoneAnimationUserTrigger);
				}
				effect.RemoveEffect();
				skillTarget.Defeated -= skillTarget_Defeated;
				if (skillTarget.IsParticipating)
				{
					if (skillTarget is BirdCombatant)
					{
						yield return new WaitForSeconds(skillTarget.CombatantView.PlayGoToFocusPosition());
					}
					else
					{
						yield return new WaitForSeconds(skillTarget.CombatantView.PlayGoToBasePosition());
					}
				}
				else if (skillTarget == source)
				{
					battle.SetRageAvailable(source.CombatantFaction, true);
					yield break;
				}
			}
			battle.SetRageAvailable(source.CombatantFaction, true);
			if (battle.IsPvP && source.CombatantFaction == Faction.Birds)
			{
				CalculateRageKills(battle, aliveEnemiesBefore);
			}
		}

		private void CalculateRageKills(BattleGameData battle, List<ICombatant> aliveEnemiesBefore)
		{
			var list = battle.m_CombatantsPerFaction[Faction.Pigs].Where(c => c.IsAlive).ToList();
			foreach (var item in list)
			{
				aliveEnemiesBefore.Remove(item);
			}
			if (aliveEnemiesBefore.Count <= 0)
			{
				return;
			}
			foreach (var item2 in aliveEnemiesBefore)
			{
				DIContainerLogic.GetPvpObjectivesService().RageUsedToKill(item2);
			}
		}

		private void skillTarget_Defeated()
		{
			if (m_currentOriginal != null)
			{
				m_currentOriginal.Defeated -= skillTarget_Defeated;
			}
			if (m_SpawnImage && m_copy && m_currentOriginal != null)
			{
				Object.Destroy(m_copy.gameObject);
			}
			for (var num = m_possibleAttackingCharacters.Count - 1; num >= 0; num--)
			{
				if (!m_possibleAttackingCharacters[num].IsParticipating)
				{
					m_possibleAttackingCharacters.RemoveAt(num);
				}
			}
		}

		private static SkillBattleDataBase PreviewAttackSkillOf(ICombatant combatant)
		{
			return combatant.GetSkills().FirstOrDefault(s => s.Model.Balancing.TargetType == SkillTargetTypes.Attack);
		}

		// Health bar preview: DoAction's m_AttackCount attacks, by the target's side in initiative order, each one the
		// attacker's own attack on a random enemy. Every possible enemy is predicted with weight 1/enemies and equal
		// changes are merged, so an attack that hits all enemies stays sure and a single-target one is 1/enemies each.
		public override void PreviewHealthChanges(HealthPreviewContext ctx)
		{
			var faction = ctx.Target.CombatantFaction;
			var scale = m_Percent > 0f ? m_Percent / 100f : 1f;
			// per combatant and change: the change and its chance in each attack that can cause it
			var outcomes = new Dictionary<ICombatant, Dictionary<string, KeyValuePair<HealthChange, List<float>>>>();
			for (var i = 0; i < m_AttackCount; i++)
			{
				var attackers = ctx.Battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == faction && !c.IsStunned && c.IsParticipating && !c.IsBanner && Ammo.CanAfford(c, PreviewAttackSkillOf(c))).ToList();
				if (attackers.Count == 0)
				{
					break;
				}
				var attacker = attackers[i % attackers.Count];
				var skill = PreviewAttackSkillOf(attacker);
				var enemies = ctx.Battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction != attacker.CombatantFaction).ToList();
				if (skill == null || enemies.Count == 0)
				{
					continue;
				}
				var weight = 1f / enemies.Count;
				var merged = new Dictionary<ICombatant, Dictionary<string, HealthChange>>();
				foreach (var enemy in enemies)
				{
					var sub = new HealthPreviewContext
					{
						Battle = ctx.Battle,
						Source = attacker,
						Target = enemy,
						Skill = skill,
						IsRage = false
					};
					skill.PreviewHealthChanges(sub);
					foreach (var pair in sub.Forecasts)
					{
						ctx.For(pair.Key).MaxHealthDelta += pair.Value.MaxHealthDelta * weight;
						Dictionary<string, HealthChange> changes;
						if (!merged.TryGetValue(pair.Key, out changes))
						{
							changes = new Dictionary<string, HealthChange>();
							merged.Add(pair.Key, changes);
						}
						var seen = new Dictionary<string, int>();
						foreach (var change in pair.Value.Changes)
						{
							var key = string.Format("{0}|{1}|{2}|{3}|{4:F4}|{5}", change.IsHeal, change.Amount, change.BonusAmount, change.Tick, change.Chance, change.BaseColor);
							int count;
							seen.TryGetValue(key, out count);
							seen[key] = count + 1;
							key += "#" + count;
							HealthChange existing;
							if (changes.TryGetValue(key, out existing))
							{
								existing.Chance += change.Chance * weight;
								continue;
							}
							changes.Add(key, new HealthChange
							{
								IsHeal = change.IsHeal,
								Amount = Mathf.RoundToInt(change.Amount * scale),
								BonusAmount = Mathf.RoundToInt(change.BonusAmount * scale),
								BonusTint = change.BonusTint,
								BonusTintAlpha = change.BonusTintAlpha,
								ReducedTint = change.ReducedTint,
								ReducedTintAlpha = change.ReducedTintAlpha,
								BaseColor = change.BaseColor,
								Tick = change.Tick,
								Chance = change.Chance * weight
							});
						}
					}
				}
				foreach (var pair in merged)
				{
					Dictionary<string, KeyValuePair<HealthChange, List<float>>> byKey;
					if (!outcomes.TryGetValue(pair.Key, out byKey))
					{
						byKey = new Dictionary<string, KeyValuePair<HealthChange, List<float>>>();
						outcomes.Add(pair.Key, byKey);
					}
					foreach (var change in pair.Value)
					{
						KeyValuePair<HealthChange, List<float>> entry;
						if (!byKey.TryGetValue(change.Key, out entry))
						{
							entry = new KeyValuePair<HealthChange, List<float>>(change.Value, new List<float>());
							byKey.Add(change.Key, entry);
						}
						entry.Value.Add(Mathf.Clamp01(change.Value.Chance));
					}
				}
			}
			// The same change from several attacks (Red attacking twice, each time a 1/3 pick): the nth copy shows
			// with the chance that at least n of those attacks cause it (5/9 for one hit, 1/9 for both).
			foreach (var pair in outcomes)
			{
				foreach (var entry in pair.Value.Values)
				{
					var atLeast = AtLeastChances(entry.Value);
					for (var n = 0; n < atLeast.Count; n++)
					{
						if (atLeast[n] < 0.005f)
						{
							break;
						}
						var template = entry.Key;
						ctx.For(pair.Key).Changes.Add(new HealthChange
						{
							IsHeal = template.IsHeal,
							Amount = template.Amount,
							BonusAmount = template.BonusAmount,
							BonusTint = template.BonusTint,
							BonusTintAlpha = template.BonusTintAlpha,
							ReducedTint = template.ReducedTint,
							ReducedTintAlpha = template.ReducedTintAlpha,
							BaseColor = template.BaseColor,
							Tick = template.Tick,
							Chance = atLeast[n] >= 0.999f ? 1f : atLeast[n]
						});
					}
				}
			}
		}

		// [n-1] = the chance that at least n of the independent events (with these chances) happen.
		private static List<float> AtLeastChances(List<float> chances)
		{
			var exactly = new List<float> { 1f };
			foreach (var p in chances)
			{
				var next = new List<float>();
				for (var k = 0; k <= exactly.Count; k++)
				{
					var stay = k < exactly.Count ? exactly[k] * (1f - p) : 0f;
					var hit = k > 0 ? exactly[k - 1] * p : 0f;
					next.Add(stay + hit);
				}
				exactly = next;
			}
			var result = new List<float>();
			var tail = 0f;
			for (var k = exactly.Count - 1; k >= 1; k--)
			{
				tail += exactly[k];
				result.Insert(0, Mathf.Clamp01(tail));
			}
			return result;
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + m_AttackCount);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
