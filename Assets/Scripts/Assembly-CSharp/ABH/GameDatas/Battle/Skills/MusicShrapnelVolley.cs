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
	// King DJ (Forge): turns his back so the cannon strapped to it faces the enemies and recoils with his Hit clip. A
	// blast leaves the cannon's muzzle and it fires one projectile (MusicShrapnelVisual: pieces of a broken
	// instrument) at every enemy plus extra_projectiles more at random living enemies, so those get hit twice. Every
	// projectile deals damage_in_percent damage and stuns its target for duration turns. Needs the "all" parameter
	// (every enemy is a target); "charge" works as usual.
	public class MusicShrapnelVolley : AttackSkillTemplate
	{
		// Hit runs at 24 fps. The OffHand bone, parent of the cannon's OffHand_Back, widens until frame 6 (x scale
		// 1 -> 1.2 -> 1.3) and narrows after it: the blast leaves the muzzle there.
		private const float ClipFps = 24f;

		private const float BlastFrame = 6f;

		private const string RecoilAnimation = "Hit";

		private const string CannonBone = "OffHand_Back";

		// The cannon's muzzle in OffHand_Back's local space (rig pixels, y up): King DJ's Forge project puts
		// MainHand_Foreman's centre at (124.7, 65.3) on that bone, and the muzzle sits (63.5, 25.5) from the centre of
		// MainHand_Foreman.png.
		private static readonly Vector3 MuzzleOffset = new Vector3(188f, 91f, 0f);

		private const string BlastPrefab = "SkillFX_CloudExplosion_Small";

		private const string HitPrefab = "SkillFX_Hit_Single_Ranged_Projectile";

		// The cannon report, played with the muzzle blast (the world map cannon's shot; the blast prefab has none).
		private const string BlastSound = "World_CannonShot";

		// Gap between two shots leaving the cannon.
		private const float ShotInterval = 0.07f;

		private static VisualEffectSetting s_HitEffect;

		private float m_DamageMod;

		private float m_StunDuration;

		private float m_ChargeTurns;

		private int m_ExtraProjectiles;

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
			if (!base.Model.SkillParameters.TryGetValue("duration", out m_StunDuration))
			{
				m_StunDuration = 1f;
			}
			base.Model.SkillParameters.TryGetValue("charge", out m_ChargeTurns);
			var extra = 1f;
			if (!base.Model.SkillParameters.TryGetValue("extra_projectiles", out extra))
			{
				extra = 1f;
			}
			m_ExtraProjectiles = Mathf.Max(0, Mathf.RoundToInt(extra));
			m_TurnAround = new ChargeTurnAround(this, RecoilAnimation);
			m_AttackAnimation = TurnAndRecoil;
			// After the charge: the extra shots are rolled when the attack really fires.
			ActionsOnStartSkill.Add((battle, source, targets, initialTarget) => AddExtraTargets(source));
			ActionsOnImpact.Add((battle, source, targets, initialTarget) => Fire(source));
			ActionsAfterDamageDealt.Add(delegate(float damage, BattleGameData battle, ICombatant source, ICombatant target)
			{
				ShareableActionPart(battle, source, target, false);
			});
			// No step if he was defeated during the attack (it would replace his death animation).
			ActionsOnEnd.Add((battle, source, target) => m_TurnAround.TurnBack(source.IsAlive && source.IsParticipating));
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

		// A target listed twice is hit twice by the damage loop.
		private float AddExtraTargets(ICombatant source)
		{
			var living = m_Targets.Where(t => t != null && t.IsParticipating && t.CombatantFaction != source.CombatantFaction).Distinct().ToList();
			for (var i = 0; i < m_ExtraProjectiles && living.Count > 0; i++)
			{
				m_Targets.Add(living[UnityEngine.Random.Range(0, living.Count)]);
			}
			return 0f;
		}

		public override void ShareableActionPart(BattleGameData battle, ICombatant source, ICombatant target, bool isShared)
		{
			if (target == null || !target.IsParticipating || m_StunDuration < 1f)
			{
				return;
			}
			// A second shot at the same target replaces its stun: AddEffect removes the old one first, and the old one's
			// removal callback would put the target back to Idle right after the new stun started.
			BattleEffectGameData previous;
			if (target.CurrrentEffects != null && target.CurrrentEffects.TryGetValue("Stun", out previous) && previous != null)
			{
				previous.EffectRemovedAction = null;
			}
			var effect = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.BeforeStartOfTurn,
					EffectType = BattleEffectType.Stun,
					AfflicionType = SkillEffectTypes.Curse,
					Values = new List<float> { 1f },
					Duration = (int)m_StunDuration,
					EffectAssetId = "Stun",
					EffectAtlasId = "Skills_Generic"
				}
			}, (int)m_StunDuration, battle, "Stun", SkillEffectTypes.Curse, GetLocalizedName(), base.Model.SkillNameId);
			effect.AddEffect();
			effect.EffectRemovedAction = delegate(BattleEffectGameData e)
			{
				e.m_Target.CombatantView.PlayIdle();
			};
			target.CombatantView.PlayStunnedAnimation();
		}

		// ---------------------------------------------------------------- animation and visuals

		private float TurnAndRecoil(ICombatant source)
		{
			var time = m_TurnAround.TurnAndPlay(source, BlastFrame / ClipFps);
			return time >= 0f ? time : source.CombatantView.PlayAttackAnimation();
		}

		// One shot per entry of m_Targets (twice for the extra targets), in random order; waits until the last lands.
		private float Fire(ICombatant source)
		{
			var shots = GetLivingTargets(m_Targets).OrderBy(t => UnityEngine.Random.value).ToList();
			var view = source.CombatantView;
			if (shots.Count == 0 || view == null || view.m_AssetController == null)
			{
				return 0f;
			}
			var asset = view.m_AssetController;
			var cannon = FindBone(asset, CannonBone);
			var muzzle = cannon != null ? cannon.TransformPoint(MuzzleOffset) : (asset.OffHandBone ?? asset.BodyCenter).position;
			muzzle.z = asset.BodyCenter.position.z;
			SpawnBlast(view, asset, muzzle);
			if (DIContainerInfrastructure.AudioManager != null)
			{
				DIContainerInfrastructure.AudioManager.PlaySound(BlastSound);
			}
			var root = FindBone(asset, "Root") ?? asset.transform;
			var renderer = asset.GetComponentInChildren<Renderer>();
			var renderQueue = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.renderQueue : 3000;
			var pixelSize = Mathf.Abs(root.lossyScale.y);
			var wait = 0f;
			for (var i = 0; i < shots.Count; i++)
			{
				var target = shots[i];
				// The pieces are drawn at the size the instrument has on a bird's rig: the target's rig pixel size.
				var targetAsset = target.CombatantView.m_AssetController;
				var targetRoot = FindBone(targetAsset, "Root") ?? targetAsset.transform;
				var itemPixelSize = Mathf.Abs(targetRoot.lossyScale.y) > 0f ? Mathf.Abs(targetRoot.lossyScale.y) : pixelSize;
				var landsIn = MusicShrapnelVisual.Launch(view.m_BattleMgr, muzzle, targetAsset.BodyCenter.position, pixelSize, itemPixelSize, view.m_BattleMgr.m_BattleArea, asset.gameObject.layer, renderQueue, i * ShotInterval, () => Land(target));
				wait = Mathf.Max(wait, landsIn);
			}
			return wait;
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

		private void Land(ICombatant target)
		{
			var landed = GetLivingTargets(new List<ICombatant> { target });
			if (landed.Count > 0)
			{
				SpawnVisualEffects(VisualEffectSpawnTiming.Impact, GetHitEffect(), landed);
			}
		}

		private static VisualEffectSetting GetHitEffect()
		{
			if (s_HitEffect == null)
			{
				s_HitEffect = new VisualEffectSetting();
				s_HitEffect.BalancingId = "MusicShrapnelHit";
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
			dictionary.Add("{value_2}", string.Empty + m_StunDuration);
			dictionary.Add("{value_3}", string.Empty + m_ChargeTurns);
			dictionary.Add("{value_4}", string.Empty + m_ExtraProjectiles);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
