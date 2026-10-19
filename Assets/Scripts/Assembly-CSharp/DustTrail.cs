using System.Collections;
using UnityEngine;

// A dust trail (the intro raid swarm's old run smoke, as FX_DustTrail) following a transform: Chuck sliding, the pigs
// carrying him, Porky dashing in to attack. Its particles are simulated in world space, so they stay behind.
public static class DustTrail
{
	private const string PrefabPath = "Prefabs/0_Generic/3_Props/FX_DustTrail";

	// The dust starts just towards the camera from its kicker, so it draws in front of it, but no further: a kicker behind
	// scenery (the intro raid's rogue passing behind the blue rock, 2 units back) keeps its dust behind it too.
	private static readonly Vector3 InFront = new Vector3(0f, 0f, -1f);

	// rate: particles a second (0: the prefab's); burst: particles let out at once.
	public static ParticleSystem Begin(Transform follow, Vector3 offset, float rate = 0f, int burst = 0)
	{
		var prefab = Resources.Load<GameObject>(PrefabPath);
		if (prefab == null || follow == null)
		{
			return null;
		}
		var dust = Object.Instantiate(prefab, follow.position + offset + InFront, Quaternion.identity);
		dust.transform.SetParent(follow, true);
		UnityHelper.SetLayerRecusively(dust, follow.gameObject.layer);
		var particles = dust.GetComponentInChildren<ParticleSystem>();
		if (particles != null)
		{
			if (rate > 0f)
			{
				var emission = particles.emission;
				emission.rateOverTimeMultiplier = rate;
			}
			particles.Play(true);
			if (burst > 0)
			{
				particles.Emit(burst);
			}
		}
		return particles;
	}

	// Stops emitting; the dust already out fades as usual before the object goes.
	public static void End(ParticleSystem particles)
	{
		if (particles == null)
		{
			return;
		}
		particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
		particles.transform.SetParent(null, true);
		Object.Destroy(particles.gameObject, 3f);
	}

	public static IEnumerator For(Transform follow, Vector3 offset, float seconds, float rate = 0f, int burst = 0)
	{
		var particles = Begin(follow, offset, rate, burst);
		yield return new WaitForSeconds(seconds);
		End(particles);
	}
}
