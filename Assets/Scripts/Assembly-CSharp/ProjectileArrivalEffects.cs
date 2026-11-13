using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using UnityEngine;

// Plays a visual effect setting on the combatant a projectile flies to, when it gets there. Skill effects with Impact
// timing play on the attack clip's Impact event, which for a ranged attack is about when it fires. This plays them
// when the projectile arrives instead: on the first target, and on every enemy a ricochet copy hops to
// (Ricochet.Launch aims the same prefab at the next enemy's BodyCenter).
// m_EffectSettingId names a VisualEffectSettings entry in Root.unity (VisualEffectsBalancing). Its Impact effects play
// with that combatant as the target. A projectile sent at the middle of a side, not at one combatant, plays nothing.
// The battle destroys the projectile when its tween's time is up, often in the same frame the tween would finish, so
// arriving is also caught in OnDestroy.
public class ProjectileArrivalEffects : MonoBehaviour
{
	public string m_EffectSettingId;

	private CHMotionTween m_Tween;

	private Transform m_Target;

	private bool m_Arrived;

	private static bool s_Quitting;

	private void Awake()
	{
		m_Tween = GetComponentInChildren<CHMotionTween>();
		if (m_Tween != null)
		{
			m_Tween.Finished += Arrive;
		}
	}

	private void Update()
	{
		if (m_Tween != null && m_Tween.IsPlaying)
		{
			m_Target = m_Tween.m_EndTransform;
		}
	}

	private void OnApplicationQuit()
	{
		s_Quitting = true;
	}

	private void OnDestroy()
	{
		if (m_Tween != null)
		{
			m_Tween.Finished -= Arrive;
		}
		if (!s_Quitting && m_Target != null)
		{
			Arrive();
		}
	}

	private void Arrive()
	{
		if (m_Arrived)
		{
			return;
		}
		m_Arrived = true;
		var end = m_Target != null ? m_Target : m_Tween != null ? m_Tween.m_EndTransform : null;
		if (end == null || string.IsNullOrEmpty(m_EffectSettingId))
		{
			return;
		}
		var character = end.GetComponentInParent<CharacterControllerBattleGroundBase>();
		if (character == null || character.m_BattleMgr == null || !character.m_BattleMgr.isActiveAndEnabled)
		{
			return;
		}
		var model = character.GetModel();
		VisualEffectSetting setting;
		if (model == null || !DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting(m_EffectSettingId, out setting) || setting.VisualEffects == null)
		{
			return;
		}
		foreach (var effect in setting.VisualEffects)
		{
			if (effect.SpawnTiming == VisualEffectSpawnTiming.Impact)
			{
				character.m_BattleMgr.InstantiateEffect(model, effect, setting, new List<ICombatant> { model }, false);
			}
		}
	}
}
