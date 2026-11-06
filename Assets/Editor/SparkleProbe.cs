using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Play mode, read-only: once the world map is up and Temp/SparkleProbe.request exists, samples every particle system in the
// camera's view for 4 s and logs where its live particles really are (world x/y/z ranges), its emitter rotation, space,
// material and parent chain, to Temp/SparkleProbe.log. Then lists renderers over that area nearer than those particles.
public static class SparkleProbe
{
	private const string RequestFile = "Temp/SparkleProbe.request";

	private const string LogFile = "Temp/SparkleProbe.log";

	private const double SampleSeconds = 4;

	private static double s_ReadyAt;

	private static double s_EndAt;

	private class Sample
	{
		public ParticleSystem System;

		public int Count;

		public Vector3 Min = Vector3.one * float.MaxValue;

		public Vector3 Max = Vector3.one * float.MinValue;
	}

	private static Dictionary<ParticleSystem, Sample> s_Samples;

	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!EditorApplication.isPlaying || !(DIContainerInfrastructure.LocationStateMgr is WorldMapStateMgr))
		{
			s_ReadyAt = 0;
			s_Samples = null;
			return;
		}
		if (s_Samples != null)
		{
			Collect();
			if (EditorApplication.timeSinceStartup >= s_EndAt)
			{
				Report();
				s_Samples = null;
			}
			return;
		}
		if (!File.Exists(RequestFile))
		{
			s_ReadyAt = 0;
			return;
		}
		if (s_ReadyAt == 0)
		{
			s_ReadyAt = EditorApplication.timeSinceStartup + 6;
			return;
		}
		if (EditorApplication.timeSinceStartup < s_ReadyAt)
		{
			return;
		}
		File.Delete(RequestFile);
		s_Samples = new Dictionary<ParticleSystem, Sample>();
		s_EndAt = EditorApplication.timeSinceStartup + SampleSeconds;
	}

	private static Rect ViewRect(Camera camera)
	{
		var h = camera.orthographicSize;
		var w = h * camera.aspect;
		return new Rect(camera.transform.position.x - w, camera.transform.position.y - h, w * 2f, h * 2f);
	}

	private static void Collect()
	{
		var camera = DIContainerInfrastructure.LocationStateMgr.SceneryCamera;
		if (camera == null)
		{
			return;
		}
		var view = ViewRect(camera);
		foreach (var ps in Object.FindObjectsOfType<ParticleSystem>())
		{
			var buffer = new ParticleSystem.Particle[Mathf.Max(1, ps.particleCount)];
			var count = ps.GetParticles(buffer);
			for (var i = 0; i < count; i++)
			{
				var world = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local ? ps.transform.TransformPoint(buffer[i].position) : buffer[i].position;
				if (!view.Contains(world))
				{
					continue;
				}
				Sample sample;
				if (!s_Samples.TryGetValue(ps, out sample))
				{
					sample = new Sample { System = ps };
					s_Samples[ps] = sample;
				}
				sample.Count++;
				sample.Min = Vector3.Min(sample.Min, world);
				sample.Max = Vector3.Max(sample.Max, world);
			}
		}
	}

	private static void Report()
	{
		var log = new StringBuilder();
		var camera = DIContainerInfrastructure.LocationStateMgr.SceneryCamera;
		log.AppendLine("camera " + (camera != null ? camera.transform.position + " view=" + ViewRect(camera) : "none"));
		var renderers = Object.FindObjectsOfType<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)).ToArray();
		foreach (var sample in s_Samples.Values.OrderBy(s => s.Min.z))
		{
			var ps = sample.System;
			if (ps == null)
			{
				continue;
			}
			var pr = ps.GetComponent<ParticleSystemRenderer>();
			log.AppendLine("ps " + Path(ps.transform) + " samples=" + sample.Count + " x=" + sample.Min.x.ToString("0") + ".." + sample.Max.x.ToString("0") + " y=" + sample.Min.y.ToString("0") + ".." + sample.Max.y.ToString("0") + " z=" + sample.Min.z.ToString("0.#") + ".." + sample.Max.z.ToString("0.#") + " rot=" + ps.transform.rotation.eulerAngles + " space=" + ps.main.simulationSpace + " mat=" + (pr != null && pr.sharedMaterial != null ? pr.sharedMaterial.name : "-") + " bounds=" + (pr != null ? pr.bounds.center.ToString() : "-"));
			var area = new Rect(sample.Min.x, sample.Min.y, Mathf.Max(1f, sample.Max.x - sample.Min.x), Mathf.Max(1f, sample.Max.y - sample.Min.y));
			foreach (var r in renderers.Where(r => r.bounds.center.z < sample.Max.z && area.Overlaps(new Rect(r.bounds.min.x, r.bounds.min.y, r.bounds.size.x, r.bounds.size.y))).OrderBy(r => r.bounds.center.z).Take(12))
			{
				log.AppendLine("    nearer: z=" + r.bounds.center.z.ToString("0.#") + " " + Path(r.transform));
			}
		}
		File.WriteAllText(LogFile, log.ToString());
		Debug.Log("[SparkleProbe] wrote " + LogFile);
	}

	private static string Path(Transform t)
	{
		var s = t.name;
		while (t.parent != null)
		{
			t = t.parent;
			s = t.name + "/" + s;
		}
		return s;
	}
}
