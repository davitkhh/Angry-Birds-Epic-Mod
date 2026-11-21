using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using SmoothMoves;
using UnityEngine;

namespace ABH.GameDatas.Battle
{
	public abstract class SkillBattleDataBase
	{
		private SkillGameData m_Model;

		protected VisualEffectSetting m_VisualEffectSetting;

		protected BubbleSetting m_BubbleSetting;

		protected BattleGameData m_Battle;

		protected ICombatant m_InitialTarget;

		protected bool m_ForceSourceIcon;

		protected string m_SkillProjectileAssetId = string.Empty;

		public bool m_IsRageSkill;

		public int m_SkillStartTurn;

		public int m_CurrentTurn;

		public ICombatant m_Source;

		public List<ICombatant> m_Targets;

		public ICombatant m_InitialTargetSelection;

		public SkillGameData Model
		{
			get
			{
				return m_Model;
			}
		}

		public SkillBattleDataBase()
		{
		}

		public SkillBattleDataBase(SkillGameData model)
		{
			Init(model);
		}

		// A projectile is drawn flying level toward the enemy side: turn it toward where it really goes (as Ricochet turns
		// its hops), so a shot up or down at a target points its tip there.
		private static void AimProjectile(GameObject projectile, ICombatant source, Vector3 end)
		{
			var start = projectile.transform.position;
			var shooter = source.CombatantView.m_AssetController.transform.position;
			var travel = new Vector2(end.x - start.x, end.y - start.y);
			if (travel.sqrMagnitude <= 0.0001f)
			{
				return;
			}
			var forward = new Vector2(Mathf.Sign(end.x - shooter.x), 0f);
			var angle = Mathf.Atan2(travel.y, travel.x) - Mathf.Atan2(forward.y, forward.x);
			projectile.transform.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
		}

		public virtual void BoneAnimationUserTrigger(UserTriggerEvent triggerEvent)
		{
			if (triggerEvent.tag == "Movement")
			{
				SpawnVisualEffects(VisualEffectSpawnTiming.Movement, m_VisualEffectSetting);
			}
			if (triggerEvent.tag == "Impact")
			{
				DebugLog.Log("Impact occured");
				SpawnVisualEffects(VisualEffectSpawnTiming.Impact, m_VisualEffectSetting);
			}
			if (triggerEvent.tag == "Projectile")
			{
				DebugLog.Log("Projectile fired");
				SpawnProjectile(m_Battle, m_Source, m_Targets, m_SkillProjectileAssetId);
			}
		}

		// When the last Forge projectile this skill fired reaches its first target (Time.time); 0 for none
		public float m_ForgeArrival;

		// When the last projectile (any kind) this skill fired reaches its target (Time.time); 0 for none. Ricochet
		// hops wait for it so the bounce only leaves after the shot has landed
		public float m_ProjectileArrival;

		// Combatant Forge "projectile hit counts as the Impact event": set when the skill fires a projectile with
		// anim_impact_on_hit; its Impact effects then play when the projectile arrives (SpawnProjectile)
		public bool m_ImpactFromProjectile;

		// the Forge projectile this skill fired last (it can land later than m_ForgeArrival when slowed on its way)
		public ForgeProjectileRunner m_ForgeRunner;

		// Combatant Forge timings: a projectile was fired / hit / bounced / stopped, or a slow motion started, at pos
		// (ForgeEffectExtras.Timing*). Plays the skill's effects with that timing: "At the projectile" ones at pos.
		public void ForgeEvent(int timing, Vector3 pos)
		{
			if (m_VisualEffectSetting == null || m_VisualEffectSetting.VisualEffects == null || m_Source == null || m_Source.CombatantView == null)
			{
				return;
			}
			var mgr = m_Source.CombatantView.m_BattleMgr;
			foreach (var effect in m_VisualEffectSetting.VisualEffects)
			{
				if ((int)effect.SpawnTiming != timing || (timing == ForgeEffectExtras.TimingSlowMotion && effect.TargetCombatant == VisualEffectTargetCombatant.SlowMotion))
				{
					continue;
				}
				if ((int)effect.TargetCombatant == ForgeEffectExtras.TargetProjectile)
				{
					mgr.SpawnEffectAt(m_Source, effect, m_VisualEffectSetting, pos);
				}
				else
				{
					mgr.InstantiateEffect(m_Source, effect, m_VisualEffectSetting, m_Targets ?? new List<ICombatant>(), false);
				}
			}
		}

		// whether one of the skill's effects plays at this Forge timing
		public bool HasForgeTiming(int timing)
		{
			if (m_VisualEffectSetting == null || m_VisualEffectSetting.VisualEffects == null)
			{
				return false;
			}
			foreach (var effect in m_VisualEffectSetting.VisualEffects)
			{
				if ((int)effect.SpawnTiming == timing)
				{
					return true;
				}
			}
			return false;
		}

		private IEnumerator ForgeEventLater(int timing, float delay, Transform at)
		{
			yield return new WaitForSeconds(delay);
			if (at != null)
			{
				ForgeEvent(timing, at.position);
			}
		}

		private IEnumerator ImpactWhenArrived(float delay, ForgeProjectileRunner runner = null)
		{
			if (delay > 0f)
			{
				yield return new WaitForSeconds(delay);
			}
			for (var waited = 0f; runner != null && !runner.Landed && waited < 3f; waited += Time.deltaTime)
			{
				yield return null;
			}
			SpawnVisualEffects(VisualEffectSpawnTiming.Impact, m_VisualEffectSetting);
		}

		private void ImpactOnArrival(ICombatant source, float delay, ForgeProjectileRunner runner = null)
		{
			if (m_ImpactFromProjectile && source != null && source.CombatantView != null)
			{
				source.CombatantView.StartCoroutine(ImpactWhenArrived(delay, runner));
			}
		}

		public void SpawnProjectile(BattleGameData battle, ICombatant source, List<ICombatant> targets, string skillProjectileAssetId, bool useOffhand = false)
		{
			if (string.IsNullOrEmpty(skillProjectileAssetId))
			{
				DebugLog.Log("no asset ID");
				return;
			}
			Ammo.OnProjectileFired(source, skillProjectileAssetId);
			var transform = !useOffhand || !(source.CombatantView.m_AssetController.OffHandBone != null) ? source.CombatantView.m_AssetController.MainHandBone : source.CombatantView.m_AssetController.OffHandBone;
			if (ForgeProjectiles.Has(skillProjectileAssetId))
			{
				// a Forge projectile flies itself (ForgeProjectiles), at each target's body centre
				var ends = new List<Vector3>();
				if (targets != null)
				{
					foreach (var target in targets)
					{
						if (target != null && target.CombatantView && target.CombatantView.m_AssetController.BodyCenter)
						{
							ends.Add(target.CombatantView.m_AssetController.BodyCenter.position);
						}
					}
				}
				if (ends.Count == 0)
				{
					return;
				}
				var mgr = source.CombatantView.m_BattleMgr;
				var centreTransform = source.CombatantFaction != 0 ? mgr.m_BirdCenterPosition : mgr.m_PigCenterPosition;
				var centre = ends.Count > 1 && centreTransform ? centreTransform.position : ends[0];
				var arrival = ForgeProjectiles.Spawn(skillProjectileAssetId, mgr.m_BattleArea, transform.position, ends, centre, source.CombatantView.m_AssetController.transform.localScale, source.CombatantView.m_AssetController.gameObject.layer, false, ForgeEvent);
				m_ForgeArrival = Time.time + arrival;
				m_ProjectileArrival = m_ForgeArrival;
				m_ForgeRunner = ForgeProjectiles.LastRunner;
				ImpactOnArrival(source, arrival, m_ForgeRunner);
				return;
			}
			if (targets != null && targets.Count == 1)
			{
				var combatant = targets[0];
				var gameObject = DIContainerInfrastructure.ProjectileAssetProvider.InstantiateObject(skillProjectileAssetId, source.CombatantView.m_BattleMgr.m_BattleArea, transform.position, Quaternion.identity);
				if (gameObject == null)
				{
					return;
				}
				gameObject.transform.position = transform.position;
				gameObject.transform.localScale = source.CombatantView.m_AssetController.transform.localScale;
				UnityHelper.SetLayerRecusively(gameObject, source.CombatantView.m_AssetController.gameObject.layer);
				var componentInChildren = gameObject.GetComponentInChildren<CHMotionTween>();
				if (!componentInChildren)
				{
					Object.Destroy(gameObject);
					return;
				}
				if (!combatant.CombatantView || !combatant.CombatantView.m_AssetController.BodyCenter)
				{
					DebugLog.Error("Combatant view or its Body Center is Missing!");
					Object.Destroy(gameObject);
					return;
				}
				componentInChildren.m_EndTransform = combatant.CombatantView.m_AssetController.BodyCenter;
				AimProjectile(gameObject, source, componentInChildren.m_EndTransform.position);
				if (componentInChildren.m_EndTransform.position.y < gameObject.transform.position.y)
				{
					componentInChildren.InvertCurves(true);
				}
				componentInChildren.Play();
				ForgeEvent(ForgeEffectExtras.TimingProjectileLaunch, transform.position);
				source.CombatantView.StartCoroutine(ForgeEventLater(ForgeEffectExtras.TimingProjectileHit, componentInChildren.MovementDuration, componentInChildren.m_EndTransform));
				m_ProjectileArrival = Time.time + componentInChildren.MovementDuration;
				ImpactOnArrival(source, componentInChildren.MovementDuration);
				Object.Destroy(gameObject, componentInChildren.m_DurationInSeconds);
			}
			else
			{
				if (targets == null || targets.Count <= 1)
				{
					return;
				}
				var gameObject2 = DIContainerInfrastructure.ProjectileAssetProvider.InstantiateObject(skillProjectileAssetId, source.CombatantView.m_BattleMgr.m_BattleArea, transform.position, Quaternion.identity);
				gameObject2.transform.position = transform.position;
				gameObject2.transform.localScale = source.CombatantView.m_AssetController.transform.localScale;
				UnityHelper.SetLayerRecusively(gameObject2, source.CombatantView.m_AssetController.gameObject.layer);
				var componentInChildren2 = gameObject2.GetComponentInChildren<CHMotionTween>();
				if (!componentInChildren2)
				{
					Object.Destroy(gameObject2);
					return;
				}
				componentInChildren2.m_EndTransform = source.CombatantFaction != 0 ? source.CombatantView.m_BattleMgr.m_BirdCenterPosition : source.CombatantView.m_BattleMgr.m_PigCenterPosition;
				AimProjectile(gameObject2, source, componentInChildren2.m_EndTransform.position);
				if (componentInChildren2.m_EndTransform.position.y < gameObject2.transform.position.y)
				{
					componentInChildren2.InvertCurves(true);
				}
				componentInChildren2.Play();
				ForgeEvent(ForgeEffectExtras.TimingProjectileLaunch, transform.position);
				source.CombatantView.StartCoroutine(ForgeEventLater(ForgeEffectExtras.TimingProjectileHit, componentInChildren2.MovementDuration, componentInChildren2.m_EndTransform));
				m_ProjectileArrival = Time.time + componentInChildren2.MovementDuration;
				ImpactOnArrival(source, componentInChildren2.MovementDuration);
				Object.Destroy(gameObject2, componentInChildren2.m_DurationInSeconds);
			}
		}

		public int GetChargeDuration()
		{
			var value = 0f;
			if (Model.SkillParameters != null && Model.SkillParameters.TryGetValue("charge", out value))
			{
				return (int)value;
			}
			return 0;
		}

		public void SetChargeDuration(int turns)
		{
			if (Model.SkillParameters != null && Model.SkillParameters.ContainsKey("charge"))
			{
				m_Model = new SkillGameData(m_Model);
				Model.SkillParameters["charge"] = turns;
			}
		}

		// Length of the current charge as first queued, before ReduceCharge or mid-charge shortening/delays
		public int QueuedChargeDuration { get; private set; }

		// Turns the last finished charge really took, after ReduceCharge and mid-charge shortening/delays
		public int FiredChargeDuration { get; private set; }

		// "charge_min" makes each new charge take a random number of turns from charge_min to charge
		private int RollChargeDuration()
		{
			var charge = (int)Model.Balancing.SkillParameters["charge"];
			var chargeMin = 0f;
			if (Model.Balancing.SkillParameters.TryGetValue("charge_min", out chargeMin) && (int)chargeMin < charge)
			{
				return Random.Range((int)chargeMin, charge + 1);
			}
			return charge;
		}

		public bool TryGetChargeDurationLeft(out int duration, ICombatant source)
		{
			duration = 0;
			BattleEffectGameData value = null;
			if (Model.SkillParameters != null && source != null && source.CurrrentEffects != null && Model.SkillParameters.ContainsKey("charge") && source.CurrrentEffects.TryGetValue("Charge_" + Model.Balancing.AssetId, out value) && value.m_SkillIdent == Model.SkillNameId)
			{
				duration = value.GetTurnsLeft();
				return true;
			}
			return false;
		}

		public virtual void ShareableActionPart(BattleGameData battle, ICombatant source, ICombatant target, bool isShared)
		{
		}

		public abstract IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false);

		public virtual void Init(SkillGameData model)
		{
			m_Model = model;
			// an ability edited in the Combatant Forge has its own effects; others use the ones listed for the AssetId
			if (!ForgeSkillEffects.TryGet(m_Model.Balancing.NameId, out m_VisualEffectSetting))
			{
				DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting(m_Model.Balancing.AssetId, out m_VisualEffectSetting);
			}
			DIContainerLogic.GetVisualEffectsBalancing().TryGetBubbleSetting(m_Model.Balancing.AssetId, out m_BubbleSetting);
		}

		public virtual void DoActionInstant(BattleGameData battle, ICombatant source, ICombatant target)
		{
		}

		public void SpawnBubble(int duration)
		{
			if (m_BubbleSetting != null && m_Source != null)
			{
				SpawnBubble(m_BubbleSetting, duration);
			}
		}

		public void SpawnBubble(BubbleSetting setting, int duration)
		{
			if (setting != null && m_Source != null)
			{
				m_Source.CombatantView.InstantiateBubble(setting, m_Targets, m_Source, duration);
			}
		}

		public void SpawnChargeBubble(int duration, List<ICombatant> targets, ICombatant source, BattleGameData battle)
		{
			if (source != null)
			{
				var bubbleSetting = new BubbleSetting();
				bubbleSetting.TargetCombatant = VisualEffectTargetCombatant.Origin;
				if (battle.IsPvP && source == targets[0] && !source.IsAlive)
				{
					bubbleSetting.Type = BubbleType.KnockedOut;
				}
				else
				{
					bubbleSetting.Type = BubbleType.Large;
				}
				bubbleSetting.CharacterIcon = true;
				bubbleSetting.BalancingId = "Charge_" + Model.Balancing.AssetId;
				bubbleSetting.ShowDuration = true;
				bubbleSetting.AtAll = targets != null && targets.Count > 0 && Model.SkillParameters.ContainsKey("all");
				bubbleSetting.AtPigs = targets == null || targets.FirstOrDefault().CombatantFaction == Faction.Pigs;
				source.CombatantView.InstantiateBubble(bubbleSetting, targets, source, duration);
			}
		}

		public void SpawnVisualEffects(VisualEffectSpawnTiming timing, VisualEffectSetting visualEffectSetting, List<ICombatant> targets, bool isCurse = false)
		{
			if (visualEffectSetting == null)
			{
				return;
			}
			foreach (var visualEffect in visualEffectSetting.VisualEffects)
			{
				if (visualEffect.SpawnTiming != timing)
				{
					continue;
				}
				if (m_Source.CombatantView != null)
				{
					m_Source.CombatantView.m_BattleMgr.InstantiateEffect(m_Source, visualEffect, visualEffectSetting, targets, isCurse);
				}
				else if (targets.Count > 0)
				{
					var combatant = targets.FirstOrDefault();
					if (combatant != null && combatant.CombatantView != null)
					{
						combatant.CombatantView.m_BattleMgr.InstantiateEffect(m_Source, visualEffect, visualEffectSetting, targets, isCurse);
					}
				}
			}
		}

		public void SpawnVisualEffects(VisualEffectSpawnTiming timing, VisualEffectSetting visualEffectSetting, bool isCurse = false)
		{
			if (visualEffectSetting == null)
			{
				return;
			}
			foreach (var visualEffect in visualEffectSetting.VisualEffects)
			{
				if (visualEffect.SpawnTiming != timing)
				{
					continue;
				}
				if (m_Source != null && m_Source.CombatantView != null)
				{
					m_Source.CombatantView.m_BattleMgr.InstantiateEffect(m_Source, visualEffect, visualEffectSetting, m_Targets, isCurse);
				}
				else if (m_Targets != null && m_Targets.Count > 0)
				{
					var combatant = m_Targets.FirstOrDefault();
					if (combatant != null && combatant.CombatantView != null)
					{
						combatant.CombatantView.m_BattleMgr.InstantiateEffect(m_Source, visualEffect, visualEffectSetting, m_Targets, isCurse);
					}
				}
			}
		}

		// Runs at the top of an AI turn right before this skill's charge starts from zero (before the charge bubble and
		// animation); returns how long to wait. King DJ turns his back here (ChargeTurnAround).
		public virtual float BeforeChargeStart(ICombatant source)
		{
			return 0f;
		}

		public bool IsChargeStarting(ICombatant source)
		{
			return Model.SkillParameters != null && Model.SkillParameters.ContainsKey("charge") && !source.ChargeDone && (source.CurrrentEffects == null || !source.CurrrentEffects.ContainsKey("Charge_" + Model.Balancing.AssetId));
		}

		public bool EvaluateCharge(BattleGameData battle, ICombatant source, List<ICombatant> targets, ICombatant initialSelectionTarget)
		{
			if (Model.SkillParameters == null || !Model.SkillParameters.ContainsKey("charge"))
			{
				return false;
			}
			BattleEffectGameData value = null;
			if (source.CurrrentEffects != null && !source.CurrrentEffects.TryGetValue("Charge_" + Model.Balancing.AssetId, out value))
			{
				var flag = false;
				var num = 1f;
				foreach (var value2 in source.CurrrentEffects.Values)
				{
					foreach (var effect in value2.m_Effects)
					{
						if (effect.EffectType == BattleEffectType.ReduceCharge && value2.m_Source.IsAlive && effect.Values[1] >= (float)Random.Range(0, 100))
						{
							flag = true;
							num = effect.Values[0];
							break;
						}
					}
					if (flag)
					{
						break;
					}
				}
				if (flag)
				{
					QueuedChargeDuration = Model.Balancing.SkillParameters.ContainsKey("charge_min") ? RollChargeDuration() : (int)Model.SkillParameters["charge"];
					SetChargeDuration(QueuedChargeDuration - (int)num);
				}
				else if (!battle.IsPvP)
				{
					QueuedChargeDuration = RollChargeDuration();
					SetChargeDuration(QueuedChargeDuration);
				}
				else
				{
					QueuedChargeDuration = GetChargeDuration();
				}
			}
			if (source.ChargeDone)
			{
				source.ChargeDone = false;
				source.ChachedSkill = null;
				m_InitialTargetSelection = null;
				if (source.CurrrentEffects != null && source.CurrrentEffects.TryGetValue("Charge_" + Model.Balancing.AssetId, out value))
				{
					FiredChargeDuration = Mathf.Max(1, GetChargeDuration() + value.m_ChargeTurnsAdded);
					value.IncrementCurrentTurnManual(true);
				}
				else
				{
					FiredChargeDuration = GetChargeDuration();
				}
				return false;
			}
			if (source.CurrrentEffects != null && source.CurrrentEffects.TryGetValue("Charge_" + Model.Balancing.AssetId, out value))
			{
				if (value.GetTurnsLeft() <= 1)
				{
					source.ChargeDone = true;
					return EvaluateCharge(battle, source, !m_ForceSourceIcon ? targets : new List<ICombatant> { source }, initialSelectionTarget);
				}
				value.IncrementCurrentTurnManual(true);
				return true;
			}
			source.ChachedSkill = this;
			m_InitialTargetSelection = initialSelectionTarget;
			var list = new List<float>();
			list.Add(0f);
			var values = list;
			var battleEffectGameData = new BattleEffectGameData(source, source, new List<BattleEffect>
			{
				new BattleEffect
				{
					Duration = (int)Model.SkillParameters["charge"],
					EffectTrigger = EffectTriggerType.OnCharge,
					EffectType = BattleEffectType.Charge,
					AfflicionType = SkillEffectTypes.None,
					Values = values
				}
			}, (int)Model.SkillParameters["charge"], battle, "Charge_" + Model.Balancing.AssetId, SkillEffectTypes.None, GetLocalizedName(), Model.SkillNameId);
			// A 1-turn charge goes off as soon as it is evaluated again this same turn: no turn counter for it.
			// (Knocked-out PvP birds still show theirs; that bubble counts down to the revive.)
			if (battleEffectGameData.GetTurnsLeft() > 1 || !source.IsAlive)
			{
				SpawnChargeBubble(battleEffectGameData.GetTurnsLeft(), !m_ForceSourceIcon ? targets : new List<ICombatant> { source }, source, battle);
			}
			battleEffectGameData.SetManualIncrement(true).AddEffect();
			return true;
		}

		public abstract string GetLocalizedDescription(ICombatant invoker);

		// Health bar preview while the player hovers this skill over ctx.Target (HealthPreviewDirector): add the
		// health changes the skill would cause to ctx. Must only read battle state.
		public virtual void PreviewHealthChanges(HealthPreviewContext ctx)
		{
		}

		// The skill would only start charging now (no effect this turn).
		protected bool PreviewStartsCharge(ICombatant source)
		{
			var charge = 0f;
			if (!Model.SkillParameters.TryGetValue("charge", out charge) || charge <= 0f || source.ChargeDone)
			{
				return false;
			}
			return source.CurrrentEffects == null || !source.CurrrentEffects.Keys.Any(k => k.StartsWith("Charge_"));
		}

		public virtual bool IsPseudoPerk()
		{
			return false;
		}

		public abstract string GetLocalizedName();

		public virtual string GetLocalizedName(ICombatant invoker)
		{
			return GetLocalizedName();
		}

		public virtual SkillBattleDataBase CheckForReplacement(ICombatant invoker)
		{
			return this;
		}
	}
}
