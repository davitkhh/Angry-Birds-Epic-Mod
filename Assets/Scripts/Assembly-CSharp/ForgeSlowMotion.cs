using System.Collections;
using UnityEngine;

// Slows the whole battle for a moment without anything to see (a Combatant Forge projectile's "Slow motion" component,
// ForgeProjectileRunner): eases down to the time scale, holds it for the duration (real seconds), eases back. Same
// shape as the game's SlowMotion effect (BattleMgrBase.ChangeTimeScale), on its own TimeScaleMgr tag.
public class ForgeSlowMotion : MonoBehaviour
{
	private const string Tag = "forge_slowmo";

	private static ForgeSlowMotion s_Current;

	public static void Play(float scale, float duration, float ease, float easeOut = -1f)
	{
		if (DIContainerInfrastructure.TimeScaleMgr == null)
		{
			return;
		}
		if (s_Current != null)
		{
			Destroy(s_Current.gameObject);  // a newer one takes over
		}
		var go = new GameObject("ForgeSlowMotion");
		s_Current = go.AddComponent<ForgeSlowMotion>();
		s_Current.StartCoroutine(s_Current.Run(Mathf.Clamp(scale, 0.01f, 1f), Mathf.Max(0f, duration), Mathf.Max(0f, ease), easeOut < 0f ? Mathf.Max(0f, ease) : easeOut));
	}

	private static bool Paused()
	{
		var core = DIContainerInfrastructure.GetCoreStateMgr();
		return core != null && core.m_GameIsPaused;
	}

	private IEnumerator Run(float scale, float duration, float ease, float easeOut)
	{
		var mgr = DIContainerInfrastructure.TimeScaleMgr;
		for (var t = 0f; t < ease; t += Time.unscaledDeltaTime)
		{
			while (Paused())
			{
				yield return null;
			}
			mgr.AddTimeScale(Tag, Mathf.Lerp(1f, scale, t / ease));
			yield return null;
		}
		mgr.AddTimeScale(Tag, scale);
		for (var t = 0f; t < duration; t += Time.unscaledDeltaTime)
		{
			yield return null;
		}
		for (var t = 0f; t < easeOut; t += Time.unscaledDeltaTime)
		{
			while (Paused())
			{
				yield return null;
			}
			mgr.AddTimeScale(Tag, Mathf.Lerp(scale, 1f, t / easeOut));
			yield return null;
		}
		Destroy(gameObject);
	}

	private void OnDestroy()
	{
		if (DIContainerInfrastructure.TimeScaleMgr != null)
		{
			DIContainerInfrastructure.TimeScaleMgr.RemoveTimeScale(Tag);
		}
		if (s_Current == this)
		{
			s_Current = null;
		}
	}
}
