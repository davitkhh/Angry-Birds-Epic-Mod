using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// SimpleAttackSkill that throws the item held in the main hand instead of walking up and jabbing (Dynamite Rogue).
	// Winds up with the overhand swing, lobs a spinning copy of the main hand gear at the targets, and plays the skill's
	// visual effects on the targets where it lands. Ends with a laugh while a fresh item reappears in the hand, unless
	// the thrower got hurt or stunned along the way. The thrower is immune to retaliation (Shock Shield, thorns...) for
	// the whole skill via BattleService.RetaliationImmuneAttackers; counter attacks still happen.
	public class ThrowMainHandAttack : SimpleAttackSkill
	{
		private const string ThrowAnimation = "Action_Attack_2";

		// Action_Attack_2 (18 frames): wind-up 0-11, the arm comes over and releases at the Impact trigger on 12.
		private const float ThrowFrameCount = 18f;

		private const float ReleaseFrame = 12f;

		private const float FlightDuration = 0.3f;

		private const float SpinsPerSecond = 3f;

		// Share of the laugh that delays returning to base; the rest plays out on the walk back.
		private const float GloatShare = 0.6f;

		private VisualEffectSetting m_LandingEffects;

		private float m_HealthBeforeThrow;

		private readonly List<SpriteRenderer> m_HiddenHandSprites = new List<SpriteRenderer>();

		private CharacterAssetController m_HiddenHandRig;

		private string m_HiddenHandBone;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			m_ForceRanged = true;
			// Effects like BombExplosion spawn on the invoker at skill start (they belong to self-destructing bombs).
			// Keep them out of the base flow and play them on the targets when the throw lands instead.
			m_LandingEffects = RetargetOnLanding(m_VisualEffectSetting);
			m_VisualEffectSetting = null;
			m_AttackAnimation = PlayWindUp;
			ActionsOnImpact.Add((battle, source, targets, initialTarget) => Throw(source, targets));
			ActionsOnImpact.Add((battle, source, targets, initialTarget) => Land(targets));
			ActionsOnEnd.Add((battle, source, target) => Gloat(source));
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			m_HealthBeforeThrow = source.CurrentHealth;
			// Thrown from a distance: the targets' Shock Shield, thorns etc. never reach the thrower (counters still do).
			var immune = DIContainerLogic.GetBattleService().RetaliationImmuneAttackers;
			immune.Add(source);
			try
			{
				yield return DIContainerInfrastructure.GetCoreStateMgr().StartCoroutine(base.DoAction(battle, source, target, shared, illusion));
			}
			finally
			{
				immune.Remove(source);
				// Covers aborted skills that never reach ActionsOnEnd.
				RestoreHand();
			}
		}

		private float PlayWindUp(ICombatant source)
		{
			var asset = source.CombatantView.m_AssetController;
			var length = asset.GetAnimationLength(ThrowAnimation);
			if (length <= 0f)
			{
				return source.CombatantView.PlayAttackAnimation();
			}
			asset.PlayAnimation(ThrowAnimation);
			asset.PlayAnimationQueued("Idle");
			return length * (ReleaseFrame / ThrowFrameCount);
		}

		private float Throw(ICombatant source, List<ICombatant> targets)
		{
			var view = source.CombatantView;
			var hand = view.m_AssetController.MainHandBone;
			var landed = GetLivingTargets(targets);
			if (hand == null || landed.Count == 0)
			{
				return 0f;
			}
			var projectile = TakeBoneItem(source, hand) ?? TakeSpriteItems(hand);
			if (projectile == null)
			{
				return 0f;
			}
			var landing = Vector3.zero;
			foreach (var target in landed)
			{
				landing += target.CombatantView.m_AssetController.BodyCenter.position;
			}
			landing /= landed.Count;
			projectile.SetParent(view.m_BattleMgr.m_BattleArea, true);
			UnityHelper.SetLayerRecusively(projectile.gameObject, view.m_AssetController.gameObject.layer);
			view.StartCoroutine(Fly(projectile, landing));
			return FlightDuration;
		}

		// Inventory gear: the item is a texture swapped onto the hand bone. Rebuild it as a quad from the rig's atlas.
		private Transform TakeBoneItem(ICombatant source, Transform hand)
		{
			var asset = source.CombatantView.m_AssetController;
			var item = source.CharacterModel.MainHandItem;
			if (item == null || string.IsNullOrEmpty(item.ItemAssetName) || asset.HasNoBoneAnimation || asset.m_BoneAnimation == null)
			{
				return null;
			}
			var textureName = item.ItemAssetName.Replace("_Ancient", string.Empty);
			var atlas = asset.m_BoneAnimation.textureAtlases.FirstOrDefault(a => a != null && a.textureNames.Contains(textureName));
			if (atlas == null)
			{
				return null;
			}
			var visual = hand.Find(hand.name + "_Sprite") ?? hand;
			var projectile = new GameObject("Projectile_" + textureName).transform;
			projectile.position = visual.position;
			projectile.rotation = visual.rotation;
			projectile.localScale = visual.lossyScale;
			var sprite = projectile.gameObject.AddComponent<CHMeshSprite>();
			sprite.m_AtlasType = AtlasTypes.SmoothMoves;
			sprite.m_SmoothMovesAtlas = atlas;
			sprite.m_SpriteName = textureName;
			sprite.UpdateSprite(true, true);
			asset.m_BoneAnimation.HideBone(hand.name, true);
			m_HiddenHandRig = asset;
			m_HiddenHandBone = hand.name;
			return projectile;
		}

		// Hand-placed SpriteRenderer gear (MyPrefabs pigs without an inventory): clone the renderers.
		private Transform TakeSpriteItems(Transform hand)
		{
			var sprites = hand.GetComponentsInChildren<SpriteRenderer>().Where(r => r.enabled && r.sprite != null).ToList();
			if (sprites.Count == 0)
			{
				return null;
			}
			var projectile = new GameObject("Projectile_" + hand.name).transform;
			projectile.position = hand.position;
			foreach (var sprite in sprites)
			{
				var copy = Object.Instantiate(sprite.gameObject, sprite.transform.position, sprite.transform.rotation, projectile).transform;
				copy.localScale = sprite.transform.lossyScale;
				sprite.enabled = false;
				m_HiddenHandSprites.Add(sprite);
			}
			return projectile;
		}

		private static IEnumerator Fly(Transform projectile, Vector3 landing)
		{
			var start = projectile.position;
			var arcHeight = Vector3.Distance(start, landing) * 0.25f;
			var spin = -Mathf.Sign(landing.x - start.x) * 360f * SpinsPerSecond;
			var elapsedTime = 0f;
			while (elapsedTime < FlightDuration && projectile != null)
			{
				elapsedTime += Time.deltaTime;
				var progress = Mathf.Clamp01(elapsedTime / FlightDuration);
				var position = Vector3.Lerp(start, landing, progress);
				position.y += Mathf.Sin(progress * Mathf.PI) * arcHeight;
				projectile.position = position;
				projectile.Rotate(0f, 0f, spin * Time.deltaTime);
				yield return null;
			}
			if (projectile != null)
			{
				Object.Destroy(projectile.gameObject);
			}
		}

		private float Land(List<ICombatant> targets)
		{
			var landed = GetLivingTargets(targets);
			if (landed.Count > 0)
			{
				SpawnVisualEffects(VisualEffectSpawnTiming.Impact, m_LandingEffects, landed);
			}
			return 0f;
		}

		private float Gloat(ICombatant source)
		{
			RestoreHand();
			var retaliated = source.CurrentHealth < m_HealthBeforeThrow;
			if (retaliated || !source.IsParticipating || source.IsStunned)
			{
				return 0f;
			}
			return source.CombatantView.PlayLaughAnimation() * GloatShare;
		}

		private void RestoreHand()
		{
			foreach (var sprite in m_HiddenHandSprites)
			{
				if (sprite != null)
				{
					sprite.enabled = true;
				}
			}
			m_HiddenHandSprites.Clear();
			if (m_HiddenHandRig != null && m_HiddenHandRig.m_BoneAnimation != null)
			{
				m_HiddenHandRig.m_BoneAnimation.HideBone(m_HiddenHandBone, false);
			}
			m_HiddenHandRig = null;
		}

		private static List<ICombatant> GetLivingTargets(List<ICombatant> targets)
		{
			if (targets == null)
			{
				return new List<ICombatant>();
			}
			return targets.Where(t => t != null && t.IsParticipating && t.CombatantView != null && t.CombatantView.m_AssetController != null && t.CombatantView.m_AssetController.BodyCenter != null).ToList();
		}

		private static VisualEffectSetting RetargetOnLanding(VisualEffectSetting setting)
		{
			if (setting == null || setting.VisualEffects == null)
			{
				return null;
			}
			var landing = new VisualEffectSetting();
			landing.BalancingId = setting.BalancingId;
			landing.VisualEffects = new List<VisualEffect>();
			foreach (var effect in setting.VisualEffects)
			{
				var copy = new VisualEffect();
				copy.SpawnTiming = effect.SpawnTiming == VisualEffectSpawnTiming.Start ? VisualEffectSpawnTiming.Impact : effect.SpawnTiming;
				copy.DelayInSeconds = effect.DelayInSeconds;
				copy.Type = effect.Type;
				copy.TargetCombatant = effect.TargetCombatant == VisualEffectTargetCombatant.Origin ? VisualEffectTargetCombatant.Target : effect.TargetCombatant == VisualEffectTargetCombatant.OriginArea ? VisualEffectTargetCombatant.TargetArea : effect.TargetCombatant;
				copy.TargetAnchor = effect.TargetAnchor;
				copy.SoundId = effect.SoundId;
				copy.DoNotParentOnBone = effect.DoNotParentOnBone;
				copy.PrefabName = effect.PrefabName;
				copy.OnHitEffect = effect.OnHitEffect;
				copy.OnHitPrefix = effect.OnHitPrefix;
				copy.ShowEffectInvokerIcon = effect.ShowEffectInvokerIcon;
				copy.InterpolationTime = effect.InterpolationTime;
				copy.SlowMotionDuration = effect.SlowMotionDuration;
				copy.SlowMotionTimeScale = effect.SlowMotionTimeScale;
				copy.UseTargetBoneScale = effect.UseTargetBoneScale;
				copy.UseTargetBoneRotation = effect.UseTargetBoneRotation;
				copy.IgnoreScaling = effect.IgnoreScaling;
				copy.EffectTextIdent = effect.EffectTextIdent;
				copy.HasText = effect.HasText;
				copy.DoNotMirror = effect.DoNotMirror;
				landing.VisualEffects.Add(copy);
			}
			return landing;
		}
	}
}
