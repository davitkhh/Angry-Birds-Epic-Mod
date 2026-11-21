using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

		// Wind-up: the first 20 frames of Action_Rage (24 fps; the wind-up leap and the start of the shuffle, half of
		// what the rage used to show with its item scatter cut), then the overhand swing (Action_Attack_1, 24 fps)
		// throws the helmet on its Impact frame 12.
		private const string RageAnimation = "Action_Rage";

		private const string ThrowAnimation = "Action_Attack_1";

		private const float AnimationFps = 24f;

		private const float RageWindUpFrames = 20f;

		private const float ThrowReleaseFrame = 12f;

		private float m_DamageMod;

		private bool m_Used;

		private SkillBattleDataBase m_FallbackRage;

		// Drop after impact. The helmet is a big chunk of metal: it falls hard, barely bounces, skids to a stop and rocks
		// on its dome or rim before lying still. Distances are in helmet widths so they follow the battle's unit scale.
		private const float HelmetSpriteWidth = 148f;

		// The helmet strikes the side of the target facing the thrower, not its middle: the share of the target mesh's
		// half-width that counts as solid body (the mesh bounds include gear and transparent padding), plus the helmet's half-width.
		private const float TargetBodyWidthShare = 0.6f;

		private const float HelmetHalfWidth = 0.5f;

		// Rebound off the target: share of the incoming horizontal speed that is reflected back toward the thrower, clamped
		// so it always clears the target's body but never flies off-screen.
		private const float ImpactRestitution = 0.3f;

		private const float MinReboundSpeed = 0.8f;

		private const float MaxReboundSpeed = 2.5f;

		// Share of the incoming vertical speed it keeps, plus the small pop off the target.
		private const float ImpactVerticalCarry = 0.3f;

		private const float BounceUpSpeed = 0.4f;

		// The rebound also goes into or out of the screen (picked at random per throw). Depth is faked with perspective:
		// moving into the screen shrinks the helmet and raises its floor line, moving out grows it and lowers the floor.
		private const float DepthReboundSpeed = 1.2f;

		private const float DepthPerspective = 0.5f;

		private const float DepthFloorRise = 0.6f;

		private const float MinDepth = -0.6f;

		private const float MaxDepth = 0.8f;

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

		// Ground shadow (the rigs' own Shadow texture) under the thrown helmet: its width on the ground in helmet widths, its
		// height relative to its width, and how it shrinks with the helmet's height above the floor, like the pigs' shadows.
		private const float ShadowWidth = 0.9f;

		private const float ShadowSquash = 0.8f;

		private const float ShadowFadeHeight = 4f;

		private const float ShadowMinSize = 0.35f;

		// Extra depth (helmet widths) between the helmet and the rig it is layered behind (McCool) or in front of (the target).
		private const float LayerDepthMargin = 0.05f;

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
			// Thrown from where he stands: never walk up to the target (the swing's "Movement" tag would otherwise).
			m_ForceRanged = true;
			// Opposite of Red's rage: whichever pig the player picks, the helmet goes for the one with the least health left.
			ActionsAfterTargetSelection.Add((battle, source, target) =>
			{
				var weakest = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == target.CombatantFaction && c.IsParticipating).OrderBy(c => c.CurrentHealth).FirstOrDefault();
				if (weakest != null)
				{
					m_Targets = new List<ICombatant> { weakest };
					source.AttackTarget = weakest;
				}
				return 0f;
			});
			m_AttackAnimation = c =>
			{
				ShowHelmetInHand(c);
				return PlayWindUpAndThrow(c);
			};
			// Released on the swing's Impact frame; the hit lands when the helmet arrives.
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

		// Returns the time until the helmet leaves his hand.
		private static float PlayWindUpAndThrow(ICombatant source)
		{
			var asset = source.CombatantView.m_AssetController;
			var windUp = RageWindUpFrames / AnimationFps;
			var release = ThrowReleaseFrame / AnimationFps;
			if (asset.GetAnimationLength(RageAnimation) <= 0f)
			{
				windUp = 0f;
			}
			else
			{
				source.CombatantView.PlayRageSkillAnimation();
			}
			source.CombatantView.StartCoroutine(SwingAfter(asset, windUp));
			return windUp + release;
		}

		private static IEnumerator SwingAfter(CharacterAssetController asset, float delay)
		{
			if (delay > 0f)
			{
				yield return new WaitForSeconds(delay);
			}
			if (asset == null)
			{
				yield break;
			}
			asset.PlayAnimation(ThrowAnimation);
			asset.PlayAnimationQueued("Idle");
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
			var rig = GetRigRenderer(asset);
			var renderers = helmet.GetComponentsInChildren<Renderer>();
			while (helmet != null && rig != null && renderers.Length > 0)
			{
				var margin = Mathf.Abs(helmet.transform.lossyScale.x) * HelmetSpriteWidth * LayerDepthMargin;
				helmet.transform.position += new Vector3(0f, 0f, DepthShift(renderers, rig, margin, true));
				yield return null;
			}
		}

		private static Renderer GetRigRenderer(CharacterAssetController asset)
		{
			if (asset == null)
			{
				return null;
			}
			var rig = asset.m_BoneAnimation != null ? asset.m_BoneAnimation.GetComponent<Renderer>() : null;
			return rig != null ? rig : asset.GetComponentInChildren<Renderer>();
		}

		// Z shift that puts all of the renderers behind (or in front of) the rig's mesh; smaller z draws in front.
		private static float DepthShift(Renderer[] renderers, Renderer rig, float margin, bool behind)
		{
			var nearest = float.MaxValue;
			var farthest = float.MinValue;
			foreach (var renderer in renderers)
			{
				if (renderer != null)
				{
					nearest = Mathf.Min(nearest, renderer.bounds.min.z);
					farthest = Mathf.Max(farthest, renderer.bounds.max.z);
				}
			}
			if (rig == null || nearest == float.MaxValue)
			{
				return 0f;
			}
			var shift = behind ? rig.bounds.max.z + margin - nearest : rig.bounds.min.z - margin - farthest;
			return Mathf.Abs(shift) > 0.0001f ? shift : 0f;
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
			var targetAsset = targets[0].CombatantView.m_AssetController;
			tween.m_EndTransform = targetAsset.BodyCenter;
			if (tween.m_EndTransform.position.y < projectile.transform.position.y)
			{
				tween.InvertCurves(true);
			}
			var floorBone = targetAsset.BodyRoot;
			var floorY = floorBone != null ? floorBone.position.y : tween.m_EndTransform.position.y;
			var awayDirection = Mathf.Sign(hand.position.x - tween.m_EndTransform.position.x);
			// Land on the target's near side so the helmet touches its body instead of covering it.
			var targetRig = GetRigRenderer(targetAsset);
			var unit = Mathf.Abs(projectile.transform.lossyScale.x) * HelmetSpriteWidth;
			var bodyHalfWidth = targetRig != null ? targetRig.bounds.extents.x * TargetBodyWidthShare : unit * 0.5f;
			tween.m_EndOffset = new Vector3(awayDirection * (bodyHalfWidth + unit * HelmetHalfWidth), 0f, 0f);
			var throwerFloor = source.CombatantView.m_AssetController.BodyRoot;
			var throwFloorY = throwerFloor != null ? throwerFloor.position.y : floorY;
			var shadow = CreateShadow(source.CombatantView.m_AssetController, targetAsset, projectile.layer);
			tween.Play();
			DIContainerInfrastructure.GetCoreStateMgr().StartCoroutine(BounceOffTarget(projectile, tween, targetRig, floorY, throwFloorY, awayDirection, shadow));
			return tween.m_DurationInSeconds;
		}

		// After the hit the heavy helmet rebounds off the target's side (part of its incoming speed reflected back toward
		// the thrower), drops hard, gives a short dull bounce, skids and rocks on the floor before lying still. It always
		// moves away from the target and is drawn in front of it, so neither covers the other.
		private static IEnumerator BounceOffTarget(GameObject root, CHMotionTween tween, Renderer targetRig, float floorY, float throwFloorY, float awayDirection, GameObject shadow)
		{
			try
			{
				var flight = FlyOverGround(tween, floorY, throwFloorY, shadow);
				var incoming = Vector3.zero;
				while (flight.MoveNext())
				{
					if (flight.Current is Vector3)
					{
						incoming = (Vector3)flight.Current;
						break;
					}
					yield return flight.Current;
				}
				if (tween == null)
				{
					yield break;
				}
				var bounce = BounceAndSettle(root, tween, targetRig, floorY, awayDirection, shadow, incoming);
				while (bounce.MoveNext())
				{
					yield return bounce.Current;
				}
			}
			finally
			{
				if (shadow != null)
				{
					UnityEngine.Object.Destroy(shadow);
				}
			}
		}

		// Follows the tween's flight, keeping the shadow on the ground under the helmet (the floor line runs from the
		// thrower's feet to the target's). The last value yielded is the helmet's velocity on arrival.
		private static IEnumerator FlyOverGround(CHMotionTween tween, float floorY, float throwFloorY, GameObject shadow)
		{
			var body = tween.transform;
			var previous = body.position;
			var startX = body.position.x;
			var endX = tween.m_EndTransform != null ? tween.m_EndTransform.position.x + tween.m_EndOffset.x : startX;
			var renderers = body.GetComponentsInChildren<Renderer>();
			var unit = Mathf.Abs(body.lossyScale.x) * HelmetSpriteWidth;
			var naturalWidth = ShadowNaturalWidth(shadow);
			var incoming = Vector3.zero;
			while (tween != null && tween.IsPlaying)
			{
				var floorLine = Mathf.Lerp(throwFloorY, floorY, Mathf.InverseLerp(startX, endX, body.position.x));
				PlaceShadow(shadow, naturalWidth, renderers, body.position.x, floorLine, body.position.y - floorLine, 1f, unit);
				yield return null;
				if (tween != null && Time.deltaTime > 0f)
				{
					incoming = (body.position - previous) / Time.deltaTime;
					previous = body.position;
				}
			}
			yield return incoming;
		}

		private static IEnumerator BounceAndSettle(GameObject root, CHMotionTween tween, Renderer targetRig, float floorY, float awayDirection, GameObject shadow, Vector3 incoming)
		{
			var body = tween.transform;
			var animation = root.GetComponent<Animation>();
			if (animation != null)
			{
				animation.Stop();
			}
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
			var baseScale = body.localScale;
			var rebound = Mathf.Clamp(Mathf.Abs(incoming.x) * ImpactRestitution, MinReboundSpeed * unit, MaxReboundSpeed * unit);
			var lift = Mathf.Clamp(incoming.y * ImpactVerticalCarry, -2f * unit, unit) + BounceUpSpeed * unit;
			var renderers = root.GetComponentsInChildren<Renderer>();
			var margin = unit * LayerDepthMargin;
			var spin = awayDirection * SpinDegreesPerSecond;
			var restAngle = NearestRestAngle(angle);
			// Simulated in physical terms: x across the screen, height above the floor, and depth into the screen.
			var x = body.position.x;
			var height = Mathf.Max(0f, body.position.y - ground - unit * RestHeight(restAngle));
			var depth = 0f;
			var velocityX = awayDirection * rebound;
			var velocityHeight = lift;
			var velocityDepth = (UnityEngine.Random.value < 0.5f ? 1f : -1f) * DepthReboundSpeed;
			var naturalWidth = ShadowNaturalWidth(shadow);
			while (body != null)
			{
				var deltaTime = Time.deltaTime;
				velocityHeight -= Gravity * unit * deltaTime;
				x += velocityX * deltaTime;
				height += velocityHeight * deltaTime;
				depth = Mathf.Clamp(depth + velocityDepth * deltaTime, MinDepth, MaxDepth);
				angle += spin * deltaTime;
				restAngle = NearestRestAngle(angle);
				var landed = false;
				if (height <= 0f && velocityHeight < 0f)
				{
					height = 0f;
					velocityHeight = -velocityHeight * Restitution;
					velocityX *= LandingFriction;
					velocityDepth *= LandingFriction;
					spin *= 0.3f;
					landed = velocityHeight < MinBounceSpeed * unit;
				}
				PlaceAtDepth(body, baseScale, x, height, depth, ground, unit, restAngle);
				body.position += new Vector3(0f, 0f, DepthShift(renderers, targetRig, margin, false));
				PlaceShadow(shadow, naturalWidth, renderers, x, ground + depth * DepthFloorRise * unit, height, Perspective(depth), unit);
				if (landed)
				{
					break;
				}
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
			var elapsed = 0f;
			while (elapsed < RockSeconds && body != null)
			{
				var deltaTime = Time.deltaTime;
				elapsed += deltaTime;
				var decay = Mathf.Exp(-RockDamping * elapsed);
				x += velocityX * decay * deltaTime;
				depth = Mathf.Clamp(depth + velocityDepth * decay * deltaTime, MinDepth, MaxDepth);
				PlaceAtDepth(body, baseScale, x, 0f, depth, ground, unit, restAngle);
				body.position += new Vector3(0f, 0f, DepthShift(renderers, targetRig, margin, false));
				PlaceShadow(shadow, naturalWidth, renderers, x, ground + depth * DepthFloorRise * unit, 0f, Perspective(depth), unit);
				body.rotation = Quaternion.Euler(0f, 0f, restAngle + tilt * decay * Mathf.Cos(elapsed * RockFrequency * 2f * Mathf.PI));
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

		// Projects the physical position onto the screen: farther into the screen is smaller and sits on a higher floor line.
		private static void PlaceAtDepth(Transform body, Vector3 baseScale, float x, float height, float depth, float ground, float unit, float restAngle)
		{
			var perspective = Perspective(depth);
			body.localScale = baseScale * perspective;
			var y = ground + depth * DepthFloorRise * unit + (height + unit * RestHeight(restAngle)) * perspective;
			body.position = new Vector3(x, y, body.position.z);
		}

		private static float Perspective(float depth)
		{
			return 1f / (1f + depth * DepthPerspective);
		}

		private static GameObject CreateShadow(CharacterAssetController thrower, CharacterAssetController target, int layer)
		{
			var atlas = new[] { thrower, target }
				.Where(a => a != null && !a.HasNoBoneAnimation && a.m_BoneAnimation != null)
				.SelectMany(a => a.m_BoneAnimation.textureAtlases)
				.FirstOrDefault(a => a != null && a.textureNames.Contains("Shadow"));
			if (atlas == null)
			{
				return null;
			}
			var shadow = new GameObject("Projectile_McPig_Helmet_Shadow");
			var mesh = shadow.AddComponent<CHMeshSprite>();
			mesh.m_AtlasType = AtlasTypes.SmoothMoves;
			mesh.m_SmoothMovesAtlas = atlas;
			mesh.m_SpriteName = "Shadow";
			mesh.UpdateSprite(true, true);
			UnityHelper.SetLayerRecusively(shadow, layer);
			return shadow;
		}

		private static float ShadowNaturalWidth(GameObject shadow)
		{
			var renderer = shadow != null ? shadow.GetComponent<Renderer>() : null;
			if (renderer == null)
			{
				return 1f;
			}
			var scaleX = Mathf.Abs(shadow.transform.localScale.x);
			return Mathf.Max(renderer.bounds.size.x / (scaleX > 0.0001f ? scaleX : 1f), 0.0001f);
		}

		// Lays the shadow on the floor line under the helmet, just behind it, smaller the higher the helmet is.
		private static void PlaceShadow(GameObject shadow, float naturalWidth, Renderer[] helmetRenderers, float x, float floorLine, float height, float perspective, float unit)
		{
			if (shadow == null)
			{
				return;
			}
			var size = Mathf.Lerp(1f, ShadowMinSize, Mathf.Clamp01(height / (ShadowFadeHeight * unit)));
			var scale = unit * ShadowWidth * perspective * size / naturalWidth;
			shadow.transform.localScale = new Vector3(scale, scale * ShadowSquash, 1f);
			var back = float.MinValue;
			foreach (var renderer in helmetRenderers)
			{
				if (renderer != null)
				{
					back = Mathf.Max(back, renderer.bounds.max.z);
				}
			}
			var z = back > float.MinValue ? back + unit * LayerDepthMargin : shadow.transform.position.z;
			shadow.transform.position = new Vector3(x, floorLine, z);
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

		protected override List<ICombatant> GetPreviewTargets(HealthPreviewContext ctx)
		{
			var weakest = ctx.FactionOf(ctx.Target).Where(c => c.IsParticipating).OrderBy(c => c.CurrentHealth).FirstOrDefault();
			return new List<ICombatant> { weakest ?? ctx.Target };
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
