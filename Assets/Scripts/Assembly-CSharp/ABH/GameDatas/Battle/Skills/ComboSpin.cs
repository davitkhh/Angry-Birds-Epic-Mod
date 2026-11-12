using System.Collections;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// TEMPORARY VISUALS for the Spin combos (real animations to come): "balancing on the main hand weapon" is the pig
	// lifted off the ground, and the spin squeezes the rig's x scale through cos() via ComboSpinner / RigFacing.
	internal static class SpinMotion
	{
		// Lift while balancing on the weapon, in pig heights.
		public const float LiftFactor = 0.35f;

		// Where a pig standing on another one stands, in the lower pig's heights above its feet.
		public const float StackFactor = 0.75f;

		public const float MountTime = 0.2f;

		public const float SpinTurnsPerSecond = 2.5f;

		public const float DropTime = 0.15f;

		public static float Height(ICombatant combatant)
		{
			return combatant.CombatantView.Size.y;
		}

		// Straight (or arced) move of a view's root; smooth, or accelerating for a charge.
		public static IEnumerator MoveTo(Transform transform, Vector3 to, float duration, float arcHeight = 0f, bool accelerate = false)
		{
			if (transform == null)
			{
				yield break;
			}
			var from = transform.position;
			var time = 0f;
			while (time < duration)
			{
				yield return null;
				if (transform == null)
				{
					yield break;
				}
				time += Time.deltaTime;
				var k = Mathf.Clamp01(time / duration);
				var eased = accelerate ? k * k : Mathf.SmoothStep(0f, 1f, k);
				var position = Vector3.Lerp(from, to, eased);
				position.y += arcHeight * 4f * k * (1f - k);
				transform.position = position;
			}
			transform.position = to;
		}

		public static ComboSpinner StartSpin(ICombatant combatant, float phase = 0f)
		{
			var view = combatant.CombatantView;
			if (view == null || view.m_AssetController == null)
			{
				return null;
			}
			view.PlayIdle();
			return ComboSpinner.Begin(view.m_AssetController.transform, SpinTurnsPerSecond, phase);
		}

		public static void StopSpin(ComboSpinner spinner)
		{
			if (spinner != null)
			{
				spinner.Stop();
			}
		}

		public static void Land(ICombatant combatant)
		{
			if (combatant != null && combatant.CombatantView != null && combatant.IsAlive && !combatant.IsStunned)
			{
				combatant.CombatantView.PlayIdle();
			}
		}
	}

	// Combo: Spin (Spin alone). The pig balances on its main hand weapon and spins on it, gets dizzy and is stunned.
	// Parameters: stun_duration (1).
	public class ComboSpin : ComboRecipe
	{
		private const float SpinTime = 1.4f;

		private const float SlowDownTime = 0.6f;

		public override IEnumerator Perform(BattleGameData battle, List<ICombatant> comboers, MonoBehaviour runner)
		{
			var pig = comboers[0];
			var view = pig.CombatantView;
			var root = view.transform;
			var home = root.position;
			yield return runner.StartCoroutine(SpinMotion.MoveTo(root, home + Vector3.up * SpinMotion.Height(pig) * SpinMotion.LiftFactor, SpinMotion.MountTime));
			var spinner = SpinMotion.StartSpin(pig);
			yield return new WaitForSeconds(SpinTime);
			// Winding down, wobbly.
			var time = 0f;
			while (time < SlowDownTime && spinner != null)
			{
				time += Time.deltaTime;
				spinner.TurnsPerSecond = Mathf.Lerp(SpinMotion.SpinTurnsPerSecond, 0.5f, time / SlowDownTime);
				yield return null;
			}
			SpinMotion.StopSpin(spinner);
			yield return runner.StartCoroutine(SpinMotion.MoveTo(root, home, SpinMotion.DropTime));
			if (CanStillAct(pig))
			{
				pig.CombatantView.ShowCombatLabel(DIContainerInfrastructure.GetLocaService().Tr("combo_dizzy", "Dizzy!"), SkillBlind.ComboColor);
				Stun(battle, pig, pig, (int)Param("stun_duration", 1f));
			}
			yield return new WaitForSeconds(0.6f);
		}
	}

	// Combo: Spin + Spin. The first pig balances on its main hand weapon and spins, the second climbs onto it and does the
	// same. Spinning, the stack charges a random bird: damage_in_percent (200) of the pigs' average attack and a stun.
	// Parameters: damage_in_percent (200), stun_duration (1).
	public class ComboSpinSpin : ComboRecipe
	{
		private const float ClimbTime = 0.45f;

		private const float StackedSpinTime = 0.6f;

		private const float ChargeTime = 0.55f;

		private const float ReturnTime = 0.55f;

		public override IEnumerator Perform(BattleGameData battle, List<ICombatant> comboers, MonoBehaviour runner)
		{
			var bottom = comboers[0];
			var top = comboers[1];
			var bottomRoot = bottom.CombatantView.transform;
			var topRoot = top.CombatantView.transform;
			var bottomHome = bottomRoot.position;
			var topHome = topRoot.position;
			var bottomHeight = SpinMotion.Height(bottom);

			// The bottom pig mounts its weapon and spins.
			yield return runner.StartCoroutine(SpinMotion.MoveTo(bottomRoot, bottomHome + Vector3.up * bottomHeight * SpinMotion.LiftFactor, SpinMotion.MountTime));
			var bottomSpinner = SpinMotion.StartSpin(bottom);
			yield return new WaitForSeconds(0.3f);

			// The top pig jumps onto it (drawn in front of it) and spins too.
			var stackOffset = Vector3.up * bottomHeight * SpinMotion.StackFactor + Vector3.back * 1f;
			if (top.CombatantView.m_AssetController.HasClipSilently("Jump"))
			{
				top.CombatantView.m_AssetController.PlayAnimation("Jump");
			}
			yield return runner.StartCoroutine(SpinMotion.MoveTo(topRoot, bottomRoot.position + stackOffset, ClimbTime, bottomHeight * 0.6f));
			ComboSpinner topSpinner = null;
			if (CanStillAct(top))
			{
				topSpinner = SpinMotion.StartSpin(top, 0.25f);
			}
			yield return new WaitForSeconds(StackedSpinTime);

			// Spinning, the stack charges a random bird.
			var target = RandomFoe(battle, bottom);
			if (target != null && CanStillAct(bottom))
			{
				var targetRoot = target.CombatantView.transform;
				var side = Mathf.Sign(bottomRoot.position.x - targetRoot.position.x);
				var reach = (bottom.CombatantView.Extents.x + target.CombatantView.Extents.x) * 0.7f;
				var impact = new Vector3(targetRoot.position.x + side * reach, targetRoot.position.y + bottomHeight * SpinMotion.LiftFactor, bottomRoot.position.z);
				var charge = runner.StartCoroutine(SpinMotion.MoveTo(bottomRoot, impact, ChargeTime, 0f, true));
				var time = 0f;
				while (time < ChargeTime)
				{
					time += Time.deltaTime;
					if (bottomRoot != null && topRoot != null)
					{
						topRoot.position = bottomRoot.position + stackOffset;
					}
					yield return null;
				}
				yield return charge;
				if (bottomRoot != null && topRoot != null)
				{
					topRoot.position = bottomRoot.position + stackOffset;
				}

				// Collision.
				var damage = ComboDamage(comboers, 200f);
				DealDamage(battle, bottom, target, damage);
				if (target.IsParticipating && target.CombatantView != null)
				{
					target.CombatantView.PlayHitAnimation();
					Stun(battle, bottom, target, (int)Param("stun_duration", 1f));
				}
			}

			// The stack falls apart and both bounce home.
			SpinMotion.StopSpin(bottomSpinner);
			SpinMotion.StopSpin(topSpinner);
			var bottomBack = runner.StartCoroutine(SpinMotion.MoveTo(bottomRoot, bottomHome, ReturnTime, bottomHeight * 0.5f));
			var topBack = runner.StartCoroutine(SpinMotion.MoveTo(topRoot, topHome, ReturnTime + 0.1f, bottomHeight * 0.9f));
			yield return bottomBack;
			yield return topBack;
			SpinMotion.Land(bottom);
			SpinMotion.Land(top);
			yield return new WaitForSeconds(DIContainerLogic.GetPacingBalancing().TimeForShowDamageAndReturnToBasePosInSec);
		}
	}
}
