using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Custom patch (not part of the original game) that gives specific music tracks a custom loop
// range: instead of always restarting from 0:00 when the clip ends, playback jumps to StartSeconds
// once it reaches EndSeconds (or the clip's natural end, if EndSeconds is left null). Unity's
// AudioSource.loop always wraps back to sample 0 at the clip's own end, so both re-seeks are
// enforced manually here by watching both music sources each frame.
//
// To give another track a loop range, add its clip name (matching the .ogg file name under
// Assets/Resources/Sound/Music/, case-insensitive) to LoopPoints below. Leave EndSeconds null to
// loop the whole clip (just moving where it restarts from).
public static class MusicLoopPointOverrides
{
	// double (not float) so plain decimal literals below, e.g. 143.5, compile without needing an
	// 'f' suffix.
	private struct LoopPoint
	{
		public double StartSeconds;

		public double? EndSeconds;
	}

	private static readonly Dictionary<string, LoopPoint> LoopPoints = new Dictionary<string, LoopPoint>
	{
		{ "heartache", new LoopPoint { StartSeconds = 20.5, EndSeconds = 143 } },
		{ "campswag", new LoopPoint { StartSeconds = 16.5, EndSeconds = 51.5 } },
	};

	private static readonly Dictionary<AudioSource, int> LastTimeSamples = new Dictionary<AudioSource, int>();

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Init()
	{
		MonoBehaviour host = DIContainerInfrastructure.GetCoreStateMgr();
		if (host == null) host = ContentLoader.Instance;
		if (host == null)
		{
			DebugLog.Error(typeof(MusicLoopPointOverrides), "No MonoBehaviour available to host the watch coroutine, loop points were not overridden.");
			return;
		}
		host.StartCoroutine(Watch());
	}

	private static IEnumerator Watch()
	{
		while (DIContainerInfrastructure.AudioManager == null)
		{
			yield return null;
		}

		while (true)
		{
			CheckSource(DIContainerInfrastructure.PrimaryMusicSource);
			CheckSource(DIContainerInfrastructure.SecondaryMusicSource);
			yield return null;
		}
	}

	private static void CheckSource(AudioSource source)
	{
		if (source == null || source.clip == null || !source.loop || !source.isPlaying)
		{
			return;
		}

		if (!LoopPoints.TryGetValue(source.clip.name.ToLower(), out var loopPoint))
		{
			LastTimeSamples.Remove(source);
			return;
		}

		var startSamples = Mathf.Clamp((int)(loopPoint.StartSeconds * source.clip.frequency), 0, source.clip.samples - 1);
		var endSamples = loopPoint.EndSeconds.HasValue
			? Mathf.Clamp((int)(loopPoint.EndSeconds.Value * source.clip.frequency), startSamples + 1, source.clip.samples)
			: source.clip.samples;

		var currentSamples = source.timeSamples;
		var reachedConfiguredEnd = currentSamples >= endSamples;
		var wrappedAtClipEnd = LastTimeSamples.TryGetValue(source, out var lastSamples) && currentSamples < lastSamples;
		if (reachedConfiguredEnd || wrappedAtClipEnd)
		{
			source.timeSamples = startSamples;
			currentSamples = startSamples;
		}

		LastTimeSamples[source] = currentSamples;
	}
}
