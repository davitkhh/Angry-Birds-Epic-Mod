using System;
using System.Collections;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// One-shot rage: throws a projectile (Corporal McPig's helmet) at a single enemy for heavy damage.
	// After it has been used once, the combatant's rage slot resolves to the regular fallback rage instead.
	public class RageThrowHelmetOnce : AttackSkillTemplate
	{
		private const string ProjectileAssetId = "Projectile_McPig_Helmet";

		private const string FallbackRageNameId = "bird_rage_minion_merchant";

		// Action_Rage (53 frames): wind-up leap 0-14, then the item-scattering shuffle and hops 14-47 that belong to
		// the regular "Happy Hour!" rage, then the release (Impact) at 48. The scatter section is skipped.
		private const float RageFrameCount = 53f;

		private const float ScatterStartFrame = 24f;

		private const float ScatterEndFrame = 37f;

		private float m_DamageMod;

		private bool m_Used;

		private SkillBattleDataBase m_FallbackRage;

		// Drop after impact. The helmet is a big chunk of metal: it falls hard, barely bounces, skids to a stop and rocks
		// on its dome or rim before lying still. Distances are in helmet widths so they follow the battle's unit scale.
		private const float HelmetSpriteWidth = 148f;

		private const float BounceSideSpeed = 0.4f;

		private const float BounceUpSpeed = 0.4f;

		private const float Gravity = 30f;

		private const float Restitution = 0.18f;

		// Below this upward speed (helmet widths/s) a landing stops bouncing and the helmet starts rocking.
		private const float MinBounceSpeed = 0.5f;

		// Share of the sideways speed kept on each landing (metal scraping on the ground).
		private const float LandingFriction = 0.35f;

		// Pivot height above the floor when it rests on its rim (upright) or on its dome (upside down), from the sprite extents.
		private const float RestHeightUpright = 0.35f;

		private const float RestHeightUpsideDown = 0.5f;

		private const float SpinDegreesPerSecond = 180f;

		private const float RockMaxDegrees = 25f;

		private const float RockFrequency = 2.5f;

		private const float RockDamping = 4.5f;

		private const float RockSeconds = 1.2f;

		private const float LieOnFloorSeconds = 1.2f;

		// Extra depth (helmet widths) between McCool's body and the helmet held behind it.
		private const float BehindHolderDepth = 0.05f;

		private GameObject m_HandHelmet;

		private CharacterAssetController m_HiddenHandRig;

		private string m_HiddenHandBone;

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var value = 0f;
			base.Model.SkillParameters.TryGetValue("damage_in_percent", out value);
			m_DamageMod = value / 100f;
			m_FallBackTime *= 4f;
			m_ApplyPerks = false;
			m_AttackAnimation = c =>
			{
				ShowHelmetInHand(c);
				var length = c.CombatantView.PlayRageSkillAnimation();
				c.CombatantView.StartCoroutine(SkipItemScatter(c.CombatantView.m_AssetController));
				return length * (1f - (ScatterEndFrame - ScatterStartFrame) / RageFrameCount);
			};
			// Released on the rage animation's Impact trigger; the hit lands when the helmet arrives.
			ActionsOnImpact.Add((battle, source, targets, initialTarget) => ThrowHelmet(source, targets));
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			battle.SetRageAvailable(source.CombatantFaction, false);
			try
			{
				yield return DIContainerInfrastructure.GetCoreStateMgr().StartCoroutine(base.DoAction(battle, source, target, shared, false));
			}
			finally
			{
				// Also covers aborted skills that never reach the release.
				RemoveHelmetFromHand();
			}
			m_Used = true;
			battle.SetRageAvailable(source.CombatantFaction, true);
		}

		public override SkillBattleDataBase CheckForReplacement(ICombatant invoker)
		{
			if (!m_Used)
			{
				return this;
			}
			if (m_FallbackRage == null)
			{
				m_FallbackRage = new SkillGameData(FallbackRageNameId).GenerateSkillBattleData();
			}
			return m_FallbackRage ?? this;
		}

		private static IEnumerator SkipItemScatter(CharacterAssetController asset)
		{
			var state = asset.m_BoneAnimation["Action_Rage"];
			if (state == null)
			{
				yield break;
			}
			var timeout = asset.GetRageSkillAnimationLength();
			var elapsed = 0f;
			while (state.normalizedTime < ScatterStartFrame / RageFrameCount && elapsed < timeout && asset.IsPlayingAnimation("Action_Rage"))
			{
				elapsed += Time.deltaTime;
				yield return null;
			}
			if (asset.IsPlayingAnimation("Action_Rage"))
			{
				state.normalizedTime = ScatterEndFrame / RageFrameCount;
			}
		}

		// The helmet is held in the main hand from the start of the rage animation until the release.
		private void ShowHelmetInHand(ICombatant source)
		{
			RemoveHelmetFromHand();
			var asset = source.CombatantView.m_AssetController;
			var hand = asset.MainHandBone;
			if (hand == null)
			{
				return;
			}
			var helmet = DIContainerInfrastructure.ProjectileAssetProvider.InstantiateObject(ProjectileAssetId, hand, hand.position, Quaternion.identity);
			if (helmet == null)
			{
				return;
			}
			foreach (var tween in helmet.GetComponentsInChildren<CHMotionTween>())
			{
				tween.enabled = false;
			}
			var animation = helmet.GetComponent<Animation>();
			if (animation != null)
			{
				animation.Stop();
				UnityEngine.Object.Destroy(animation);
			}
			var projectile = helmet.GetComponentInChildren<CHMotionTween>();
			if (projectile != null)
			{
				projectile.transform.localPosition = Vector3.zero;
			}
			helmet.transform.SetParent(hand, false);
			helmet.transform.localPosition = Vector3.zero;
			helmet.transform.localRotation = Quaternion.identity;
			var wanted = Vector3.Scale(asset.transform.localScale, new Vector3(Mathf.Sign(source.CombatantView.transform.lossyScale.x), 1f, 1f));
			var parentScale = hand.lossyScale;
			helmet.transform.localScale = new Vector3(wanted.x / NonZero(parentScale.x), wanted.y / NonZero(parentScale.y), wanted.z / NonZero(parentScale.z));
			UnityHelper.SetLayerRecusively(helmet, asset.gameObject.layer);
			m_HandHelmet = helmet;
			DIContainerInfrastructure.GetCoreStateMgr().StartCoroutine(KeepBehindHolder(helmet, asset));
			if (source.CharacterModel.MainHandItem != null && !asset.HasNoBoneAnimation && asset.m_BoneAnimation != null)
			{
				asset.m_BoneAnimation.HideBone(hand.name, true);
				m_HiddenHandRig = asset;
				m_HiddenHandBone = hand.name;
			}
		}

		private void RemoveHelmetFromHand()
		{
			if (m_HandHelmet != null)
			{
				UnityEngine.Object.Destroy(m_HandHelmet);
			}
			m_HandHelmet = null;
			if (m_HiddenHandRig != null && m_HiddenHandRig.m_BoneAnimation != null)
			{
				m_HiddenHandRig.m_BoneAnimation.HideBone(m_HiddenHandBone, false);
			}
			m_HiddenHandRig = null;
		}

		// The hand bone's depth changes with the animation, so the helmet is pushed behind the rig's mesh every frame.
		private static IEnumerator KeepBehindHolder(GameObject helmet, CharacterAssetController asset)
		{
			var rig = asset.m_BoneAnimation != null ? asset.m_BoneAnimation.GetComponent<Renderer>() : null;
			if (rig == null)
			{
				rig = asset.GetComponentInChildren<Renderer>();
			}
			var renderers = helmet.GetComponentsInChildren<Renderer>();
			while (helmet != null && rig != null && renderers.Length > 0)
			{
				var front = float.MaxValue;
				foreach (var renderer in renderers)
				{
					if (renderer != null)
					{
						front = Mathf.Min(front, renderer.bounds.min.z);
					}
				}
				var margin = Mathf.Abs(helmet.transform.lossyScale.x) * HelmetSpriteWidth * BehindHolderDepth;
				var shift = rig.bounds.max.z + margin - front;
				if (front < float.MaxValue && Mathf.Abs(shift) > 0.0001f)
				{
					helmet.transform.position += new Vector3(0f, 0f, shift);
				}
				yield return null;
			}
		}

		private static float NonZero(float value)
		{
			return Mathf.Abs(value) < 0.0001f ? 1f : value;
		}

		private float ThrowHelmet(ICombatant source, List<ICombatant> targets)
		{
			// The projectile takes over from the helmet in the hand.
			if (m_HandHelmet != null)
			{
				UnityEngine.Object.Destroy(m_HandHelmet);
				m_HandHelmet = null;
			}
			if (targets == null || targets.Count == 0 || targets[0].CombatantView == null)
			{
				return 0f;
			}
			var hand = source.CombatantView.m_AssetController.MainHandBone ?? source.CombatantView.m_AssetController.BodyCenter;
			var projectile = DIContainerInfrastructure.ProjectileAssetProvider.InstantiateObject(ProjectileAssetId, source.CombatantView.m_BattleMgr.m_BattleArea, hand.position, Quaternion.identity);
			if (projectile == null)
			{
				return 0f;
			}
			projectile.transform.position = hand.position;
			// Match the thrower's facing so the helmet isn't mirrored relative to how he holds it.
			projectile.transform.localScale = Vector3.Scale(source.CombatantView.m_AssetController.transform.localScale, new Vector3(Mathf.Sign(source.CombatantView.transform.lossyScale.x), 1f, 1f));
			UnityHelper.SetLayerRecusively(projectile, source.CombatantView.m_AssetController.gameObject.layer);
			var tween = projectile.GetComponentInChildren<CHMotionTween>();
			if (!tween)
			{
				UnityEngine.Object.Destroy(projectile);
				return 0f;
			}
			tween.m_EndTransform = targets[0].CombatantView.m_AssetController.BodyCenter;
			if (tween.m_EndTransform.position.y < projectile.transform.position.y)
			{
				tween.InvertCurves(true);
			}
			var floorBone = targets[0].CombatantView.m_AssetController.BodyRoot;
			var floorY = floorBone != null ? floorBone.position.y : tween.m_EndTransform.position.y;
			var awayDirection = Mathf.Sign(hand.position.x - tween.m_EndTransform.position.x);
			tween.Play();
			DIContainerInfrastructure.GetCoreStateMgr().StartCoroutine(BounceOffTarget(projectile, tween, floorY, awayDirection));
			return tween.m_DurationInSeconds;
		}

		// After the hit the heavy helmet glances off with a small pop, drops hard, gives a short dull bounce, skids and
		// rocks on the floor before lying still.
		private static IEnumerator BounceOffTarget(GameObject root, CHMotionTween tween, float floorY, float awayDirection)
		{
			while (tween != null && tween.IsPlaying)
			{
				yield return null;
			}
			if (tween == null)
			{
				yield break;
			}
			var animation = root.GetComponent<Animation>();
			if (animation != null)
			{
				animation.Stop();
			}
			var body = tween.transform;
			// Fold the flight spin (animated on the Sprite child) into the body so one angle drives the tumble.
			var sprite = body.Find("Sprite");
			if (sprite != null)
			{
				body.rotation = body.rotation * Quaternion.Euler(0f, 0f, sprite.localEulerAngles.z);
				sprite.localRotation = Quaternion.identity;
			}
			var angle = body.eulerAngles.z;
			var unit = Mathf.Abs(root.transform.lossyScale.x) * HelmetSpriteWidth;
			var ground = Mathf.Min(floorY, body.position.y - unit * RestHeightUpsideDown);
			var velocity = new Vector3(awayDirection * BounceSideSpeed * unit, BounceUpSpeed * unit, 0f);
			var spin = awayDirection * SpinDegreesPerSecond;
			var restAngle = NearestRestAngle(angle);
			while (body != null)
			{
				var deltaTime = Time.deltaTime;
				velocity.y -= Gravity * unit * deltaTime;
				var position = body.position + velocity * deltaTime;
				angle += spin * deltaTime;
				restAngle = NearestRestAngle(angle);
				var floor = ground + unit * RestHeight(restAngle);
				if (position.y <= floor && velocity.y < 0f)
				{
					position.y = floor;
					velocity.y = -velocity.y * Restitution;
					velocity.x *= LandingFriction;
					spin *= 0.3f;
					if (velocity.y < MinBounceSpeed * unit)
					{
						body.position = position;
						break;
					}
				}
				body.position = position;
				body.rotation = Quaternion.Euler(0f, 0f, angle);
				yield return null;
			}
			if (body == null)
			{
				yield break;
			}
			// Rock about the resting orientation while the leftover slide scrapes to a halt.
			var tilt = Mathf.Clamp(Mathf.DeltaAngle(restAngle, angle), -RockMaxDegrees, RockMaxDegrees);
			if (Mathf.Abs(tilt) < 6f)
			{
				tilt = RockMaxDegrees * 0.5f * -awayDirection;
			}
			var slide = velocity.x;
			var elapsed = 0f;
			while (elapsed < RockSeconds && body != null)
			{
				elapsed += Time.deltaTime;
				var decay = Mathf.Exp(-RockDamping * elapsed);
				body.rotation = Quaternion.Euler(0f, 0f, restAngle + tilt * decay * Mathf.Cos(elapsed * RockFrequency * 2f * Mathf.PI));
				body.position += new Vector3(slide * decay * Time.deltaTime, 0f, 0f);
				yield return null;
			}
			if (body == null)
			{
				yield break;
			}
			body.rotation = Quaternion.Euler(0f, 0f, restAngle);
			yield return new WaitForSeconds(LieOnFloorSeconds);
			if (root != null)
			{
				UnityEngine.Object.Destroy(root);
			}
		}

		// A helmet settles either on its rim (0) or upside down on its dome (180).
		private static float NearestRestAngle(float angle)
		{
			return Mathf.Abs(Mathf.DeltaAngle(angle, 180f)) < 90f ? 180f : 0f;
		}

		private static float RestHeight(float restAngle)
		{
			return restAngle > 90f ? RestHeightUpsideDown : RestHeightUpright;
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + Convert.ToInt32(m_DamageMod * invoker.ModifiedAttack));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
