using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// King DJ (Forge): turns his back so the cannon strapped to it faces the birds, plays his Action_Support clip and the
	// cannon fires a sound wave (SoundWaveVisual) at the target for damage_in_percent damage; "charge" works as usual
	// (AttackSkillTemplate). The target is then cursed
	// for EffectDuration of its own moves: it may only use them in the order move_1, move_2, ... (0 = attack,
	// 1 = support; the order repeats when the curse outlasts it; without move_N parameters: attack, support, support,
	// attack). Every attack or support skill the target uses on its own turn takes one step and counts down the curse;
	// rage, counter attacks and attacks other skills make it do don't. When the move the order asks for can't be used
	// (no ammo, no valid target), the target picks freely and that move still counts. Player birds are held to the order
	// when they drag or click (CharacterControllerBattleGround), AI combatants when their skill is picked
	// (BattleService.GetNextSkill, CharacterControllerBattleGround.DoTurnAfterInput). The curse ends early when King DJ
	// is defeated or it is cleansed.
	public class AttackAndForceMoveOrder : AttackSkillTemplate
	{
		public const string EffectIdent = "ForcedMoveOrder";

		private const float AttackMove = 0f;

		private const float SupportMove = 1f;

		// Action_Support runs at 24 fps. The OffHand bone, parent of the cannon's OffHand_Back, widens until frame 12
		// (x scale 1 -> 1.1 -> 1.2), holds to frame 15 and then pops: the blast leaves the muzzle when it stops widening.
		private const float ClipFps = 24f;

		private const float BlastFrame = 12f;

		private const string RecoilAnimation = "Action_Support";

		private float m_ChargeTurns;

		private const string CannonBone = "OffHand_Back";

		// The cannon's muzzle in OffHand_Back's local space (rig pixels, y up): King DJ's Forge project puts
		// MainHand_Foreman's centre at (124.7, 65.3) on that bone, and the muzzle sits (63.5, 25.5) from the centre of
		// MainHand_Foreman.png.
		private static readonly Vector3 MuzzleOffset = new Vector3(188f, 91f, 0f);

		private const string BlastPrefab = "SkillFX_CloudExplosion_Small";

		private const string HitPrefab = "SkillFX_Hit_Single_Ranged_Projectile";

		private static VisualEffectSetting s_HitEffect;

		private float m_DamageMod;

		private int m_Duration;

		private List<float> m_Order;

		private ChargeTurnAround m_TurnAround;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			m_ForceRanged = true;
			var value = 0f;
			if (!base.Model.SkillParameters.TryGetValue("damage_in_percent", out value))
			{
				value = 100f;
			}
			m_DamageMod = value / 100f;
			m_Duration = base.Model.Balancing.EffectDuration;
			base.Model.SkillParameters.TryGetValue("charge", out m_ChargeTurns);
			m_Order = ReadOrder(base.Model.SkillParameters);
			m_TurnAround = new ChargeTurnAround(this, RecoilAnimation);
			m_AttackAnimation = TurnAndRecoil;
			ActionsOnImpact.Add((battle, source, targets, initialTarget) => Fire(source, targets));
			ActionsOnImpact.Add((battle, source, targets, initialTarget) => Land(targets));
			ActionsAfterDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				ShareableActionPart(battle, source, target, false);
			});
			// No step if he was defeated during the attack (it would replace his death animation).
			ActionsOnEnd.Add((battle, source, target) => m_TurnAround.TurnBack(source.IsAlive && source.IsParticipating));
		}

		private static List<float> ReadOrder(Dictionary<string, float> parameters)
		{
			var order = new List<float>();
			float move;
			for (var i = 1; parameters.TryGetValue("move_" + i, out move); i++)
			{
				order.Add(move >= 0.5f ? SupportMove : AttackMove);
			}
			if (order.Count == 0)
			{
				order.AddRange(new[] { AttackMove, SupportMove, SupportMove, AttackMove });
			}
			return order;
		}

		public override float BeforeChargeStart(ICombatant source)
		{
			return m_TurnAround.BeforeChargeStart(source);
		}

		// He turns his back when the charge starts and stays turned until the attack animation is over (ChargeTurnAround).
		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			return m_TurnAround.Run(source, base.DoAction(battle, source, target, shared, illusion));
		}

		public override void ShareableActionPart(BattleGameData battle, ICombatant source, ICombatant target, bool isShared)
		{
			if (target == null || !target.IsParticipating || m_Duration <= 0)
			{
				return;
			}
			var effect = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.None,
					EffectType = BattleEffectType.None,
					AfflicionType = base.Model.Balancing.EffectType,
					Values = new List<float>(m_Order),
					Duration = m_Duration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, m_Duration, battle, EffectIdent, base.Model.Balancing.EffectType, GetLocalizedName(), base.Model.SkillNameId);
			// The curse counts the target's moves, not rounds (OnMoveFinished).
			effect.SetManualIncrement(true);
			effect.AddEffect();
		}

		// ---------------------------------------------------------------- the forced order

		private static BattleEffectGameData GetOrderEffect(ICombatant combatant)
		{
			BattleEffectGameData effect;
			if (combatant == null || combatant.CurrrentEffects == null || !combatant.CurrrentEffects.TryGetValue(EffectIdent, out effect) || effect == null || effect.m_IsRemoved)
			{
				return null;
			}
			var order = effect.m_Effects.FirstOrDefault();
			return order != null && order.Values != null && order.Values.Count > 0 ? effect : null;
		}

		// The move (true = support) the order asks of the combatant now, or null when it may choose freely: not cursed,
		// or the asked move can't be used.
		public static bool? GetRequiredMove(BattleGameData battle, ICombatant combatant)
		{
			var effect = GetOrderEffect(combatant);
			if (effect == null)
			{
				return null;
			}
			var order = effect.m_Effects[0].Values;
			var step = Mathf.Max(0, effect.TotalDuration - effect.GetTurnsLeft());
			var support = order[step % order.Count] >= 0.5f;
			return GetUsableSkill(battle, combatant, support) != null ? support : (bool?)null;
		}

		// The move (true = support) the order asks for next, whether or not it can be used; null when not cursed. For
		// visuals (CharacterControlHUD's Fog Machine tint).
		public static bool? GetOrderedMove(ICombatant combatant)
		{
			var effect = GetOrderEffect(combatant);
			if (effect == null)
			{
				return null;
			}
			var order = effect.m_Effects[0].Values;
			var step = Mathf.Max(0, effect.TotalDuration - effect.GetTurnsLeft());
			return order[step % order.Count] >= 0.5f;
		}

		// True when the order forbids the combatant its rage now: rage skills are attacks (Red, Bomb, the Blues) or supports
		// (Matilda, Chuck) by their TargetType and follow the order like any other move.
		public static bool IsRageBlocked(BattleGameData battle, ICombatant combatant)
		{
			// Only birds have a rage in slot 2 (a pig's third skill is an ordinary one).
			var rage = combatant is BirdCombatant ? combatant.GetSkill(2) : null;
			return rage != null && IsMove(rage) && IsMoveBlocked(battle, combatant, IsSupport(rage));
		}

		// True when the order forbids the combatant this kind of move right now.
		public static bool IsMoveBlocked(BattleGameData battle, ICombatant combatant, bool support)
		{
			var required = GetRequiredMove(battle, combatant);
			return required.HasValue && required.Value != support;
		}

		// For AI combatants: the skill to use instead of skill when the order asks for the other kind of move.
		public static SkillBattleDataBase ForceSkill(BattleGameData battle, ICombatant combatant, SkillBattleDataBase skill)
		{
			if (skill == null || skill.m_IsRageSkill || combatant.ChachedSkill != null || !IsMove(skill))
			{
				return skill;
			}
			var required = GetRequiredMove(battle, combatant);
			if (!required.HasValue || IsSupport(skill) == required.Value)
			{
				return skill;
			}
			return GetUsableSkill(battle, combatant, required.Value) ?? skill;
		}

		// Called after the combatant's own turn skill finished: one step of the order is done.
		// A rage skill takes the bird's turn, so it counts as a step too (attack or support by its TargetType).
		public static void OnMoveFinished(ICombatant combatant, SkillBattleDataBase skill)
		{
			var effect = GetOrderEffect(combatant);
			if (effect == null || skill == null || !IsMove(skill))
			{
				return;
			}
			effect.IncrementCurrentTurnManual(true);
		}

		private static bool IsMove(SkillBattleDataBase skill)
		{
			var type = skill.Model.Balancing.TargetType;
			return type == SkillTargetTypes.Attack || type == SkillTargetTypes.Support;
		}

		private static bool IsSupport(SkillBattleDataBase skill)
		{
			return skill.Model.Balancing.TargetType == SkillTargetTypes.Support;
		}

		// The combatant's first skill of that kind it can afford and has a target for (birds: skill 0 or 1).
		private static SkillBattleDataBase GetUsableSkill(BattleGameData battle, ICombatant combatant, bool support)
		{
			var skills = combatant.GetSkills();
			if (skills == null || battle == null)
			{
				return null;
			}
			var service = DIContainerLogic.GetBattleService();
			foreach (var skill in skills)
			{
				if (skill == null || skill.m_IsRageSkill || (combatant is BirdCombatant && skill == combatant.GetSkill(2)) || !IsMove(skill) || IsSupport(skill) != support || !Ammo.CanAfford(combatant, skill))
				{
					continue;
				}
				var hasTarget = battle.m_CombatantsByInitiative.Any(c => c.IsParticipating && (support ? c.CombatantFaction == combatant.CombatantFaction && (c != combatant || skill.Model.Balancing.TargetSelfPossible || combatant is BirdCombatant) : c.CombatantFaction != combatant.CombatantFaction) && service.isTargetAllowedForSkill(c, skill));
				if (hasTarget)
				{
					return skill;
				}
			}
			return null;
		}

		// ---------------------------------------------------------------- animation and visuals

		private float TurnAndRecoil(ICombatant source)
		{
			var time = m_TurnAround.TurnAndPlay(source, BlastFrame / ClipFps);
			return time >= 0f ? time : source.CombatantView.PlayAttackAnimation();
		}

		private float Fire(ICombatant source, List<ICombatant> targets)
		{
			var landed = GetLivingTargets(targets);
			var view = source.CombatantView;
			if (landed.Count == 0 || view == null || view.m_AssetController == null)
			{
				return 0f;
			}
			var asset = view.m_AssetController;
			var cannon = FindBone(asset, CannonBone);
			var muzzle = cannon != null ? cannon.TransformPoint(MuzzleOffset) : (asset.OffHandBone ?? asset.BodyCenter).position;
			muzzle.z = asset.BodyCenter.position.z;
			SpawnBlast(view, asset, muzzle);
			var root = FindBone(asset, "Root") ?? asset.transform;
			var renderer = asset.GetComponentInChildren<Renderer>();
			var renderQueue = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.renderQueue : 3000;
			var target = landed[0].CombatantView.m_AssetController.BodyCenter.position;
			return SoundWaveVisual.Launch(view.m_BattleMgr, muzzle, target, Mathf.Abs(root.lossyScale.y), view.m_BattleMgr.m_BattleArea, asset.gameObject.layer, renderQueue);
		}

		private static void SpawnBlast(CharacterControllerBattleGroundBase view, CharacterAssetController asset, Vector3 muzzle)
		{
			var blast = DIContainerInfrastructure.GetBattleEffectAssetProvider().InstantiateObject(BlastPrefab, view.m_BattleMgr.m_BattleArea, muzzle, Quaternion.identity);
			if (blast == null)
			{
				return;
			}
			blast.transform.position = muzzle + new Vector3(0f, 0f, -2f);
			blast.transform.rotation = Quaternion.identity;
			UnityHelper.SetLayerRecusively(blast, asset.gameObject.layer);
			UnityEngine.Object.Destroy(blast, 3f);
		}

		private float Land(List<ICombatant> targets)
		{
			var landed = GetLivingTargets(targets);
			if (landed.Count > 0)
			{
				SpawnVisualEffects(VisualEffectSpawnTiming.Impact, GetHitEffect(), landed);
			}
			return 0f;
		}

		private static VisualEffectSetting GetHitEffect()
		{
			if (s_HitEffect == null)
			{
				s_HitEffect = new VisualEffectSetting();
				s_HitEffect.BalancingId = "SoundWaveHit";
				s_HitEffect.VisualEffects = new List<VisualEffect>
				{
					new VisualEffect
					{
						SpawnTiming = VisualEffectSpawnTiming.Impact,
						Type = VisualEffectType.Instant,
						TargetCombatant = VisualEffectTargetCombatant.Target,
						TargetAnchor = VisualEffectTargetAnchor.Center,
						SoundId = string.Empty,
						PrefabName = HitPrefab,
						OnHitPrefix = string.Empty,
						EffectTextIdent = string.Empty
					}
				};
			}
			return s_HitEffect;
		}

		private static Transform FindBone(CharacterAssetController asset, string boneName)
		{
			return asset.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == boneName);
		}

		private static List<ICombatant> GetLivingTargets(List<ICombatant> targets)
		{
			if (targets == null)
			{
				return new List<ICombatant>();
			}
			return targets.Where(t => t != null && t.IsParticipating && t.CombatantView != null && t.CombatantView.m_AssetController != null && t.CombatantView.m_AssetController.BodyCenter != null).ToList();
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			var num = Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack);
			dictionary.Add("{value_1}", string.Empty + num);
			dictionary.Add("{value_2}", string.Empty + m_Duration);
			dictionary.Add("{value_3}", string.Empty + m_ChargeTurns);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
