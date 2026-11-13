using System;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using UnityEngine;

namespace ABH.GameDatas.Battle
{
	// Shows a ranged attack's projectile flying from one hit enemy to the next when it ricochets
	// (AttackWithCollateralDamage, AttackWithChainSkill, AttackWithChainAndDotSkill, ChainAttack perk, ChanceToChain).
	// While AttackSkillTemplate runs the damage of a ranged attack that has a projectile asset it opens a scope; the
	// ricochet code hands each extra hit to Hit, which queues it instead of dealing it at once. After the target's hit
	// the attack plays the queue: a copy of the projectile flies from the previous enemy to the next, and the hit lands
	// when it arrives. Outside a scope (melee, no projectile, other callers) Hit lands immediately, as before.
	// The scope only lives across synchronous code (no yields while open), so attacks running side by side can't mix.
	public static class Ricochet
	{
		public class Hop
		{
			public ICombatant From;

			public ICombatant To;

			public Action Land;
		}

		// Longest a hop is waited for; the next hop leaves only once this one has arrived
		private const float MaxFlightSeconds = 3f;

		private static List<Hop> s_Pending;

		private static int s_OpenFrame;

		public static void Open()
		{
			DropStaleScope();
			s_Pending = new List<Hop>();
			s_OpenFrame = Time.frameCount;
		}

		// A scope is opened and closed in the same frame; one still open later leaked (an exception skipped the
		// Close), so its hits land right away instead of waiting forever
		private static void DropStaleScope()
		{
			if (s_Pending != null && s_OpenFrame != Time.frameCount)
			{
				DebugLog.Warn("Ricochet scope leaked; landing " + s_Pending.Count + " queued hits");
				CloseAndLandAll();
			}
		}

		// Ends the scope and returns the queued hops (null when none)
		public static List<Hop> Close()
		{
			var pending = s_Pending;
			s_Pending = null;
			return pending != null && pending.Count > 0 ? pending : null;
		}

		// Ends the scope and lands every queued hop at once (attack aborted)
		public static void CloseAndLandAll()
		{
			var hops = Close();
			if (hops == null)
			{
				return;
			}
			foreach (var hop in hops)
			{
				hop.Land();
			}
		}

		// An extra hit from 'from' to 'to'; land deals it
		public static void Hit(ICombatant from, ICombatant to, Action land)
		{
			DropStaleScope();
			if (s_Pending == null || from == null || to == null || from.CombatantView == null || to.CombatantView == null)
			{
				land();
				return;
			}
			s_Pending.Add(new Hop
			{
				From = from,
				To = to,
				Land = land
			});
		}

		// The enemy the last queued hop went to, so chains continue from there; 'fallback' when nothing is queued
		public static ICombatant LastHit(ICombatant fallback)
		{
			DropStaleScope();
			if (s_Pending == null || s_Pending.Count == 0)
			{
				return fallback;
			}
			return s_Pending[s_Pending.Count - 1].To;
		}

		// Flies a copy of the projectile for one hop; returns how long it takes to arrive (0 when nothing was spawned)
		public static float Launch(ICombatant source, Hop hop, string projectileAssetId)
		{
			try
			{
				if (source == null || source.CombatantView == null || hop.From.CombatantView == null || hop.To.CombatantView == null)
				{
					return 0f;
				}
				var start = hop.From.CombatantView.m_AssetController.BodyCenter;
				var end = hop.To.CombatantView.m_AssetController.BodyCenter;
				if (start == null || end == null)
				{
					return 0f;
				}
				if (ForgeProjectiles.Has(projectileAssetId))
				{
					var arrival = ForgeProjectiles.Spawn(projectileAssetId, source.CombatantView.m_BattleMgr.m_BattleArea, start.position, new List<Vector3> { end.position }, end.position, source.CombatantView.m_AssetController.transform.localScale, source.CombatantView.m_AssetController.gameObject.layer, true);
					return Mathf.Clamp(arrival, 0f, MaxFlightSeconds);
				}
				var gameObject = DIContainerInfrastructure.ProjectileAssetProvider.InstantiateObject(projectileAssetId, source.CombatantView.m_BattleMgr.m_BattleArea, start.position, Quaternion.identity);
				if (gameObject == null)
				{
					return 0f;
				}
				gameObject.transform.position = start.position;
				var scale = source.CombatantView.m_AssetController.transform.localScale;
				gameObject.transform.localScale = scale;
				// The projectile is drawn for flying from the shooter toward the enemy side. Turn it toward the hop
				// instead: mostly up or down along the enemy line.
				var shooter = source.CombatantView.m_AssetController.transform.position;
				var forward = new Vector2(Mathf.Sign(start.position.x - shooter.x), 0f);
				var travel = new Vector2(end.position.x - start.position.x, end.position.y - start.position.y);
				if (travel.sqrMagnitude > 0.0001f)
				{
					var angle = Mathf.Atan2(travel.y, travel.x) - Mathf.Atan2(forward.y, forward.x);
					gameObject.transform.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
				}
				UnityHelper.SetLayerRecusively(gameObject, source.CombatantView.m_AssetController.gameObject.layer);
				var tween = gameObject.GetComponentInChildren<CHMotionTween>();
				if (!tween)
				{
					UnityEngine.Object.Destroy(gameObject);
					return 0f;
				}
				tween.m_StartTransform = null;
				tween.m_StartOffset = Vector3.zero;
				tween.m_EndTransform = end;
				tween.InvertCurves(end.position.y < gameObject.transform.position.y);
				tween.Play();
				var flight = Mathf.Clamp(tween.MovementDuration, 0f, MaxFlightSeconds);
				UnityEngine.Object.Destroy(gameObject, Mathf.Max(tween.MovementDuration, tween.m_DurationInSeconds));
				return flight;
			}
			catch (Exception ex)
			{
				DebugLog.Error("Ricochet projectile failed: " + ex);
				return 0f;
			}
		}
	}
}
