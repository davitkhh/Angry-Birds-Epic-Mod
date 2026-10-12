using System;
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
//
// It also replaces some tracks with a snippet cut out of another track (see Snippets below): the
// snippet is copied into a new in-memory clip of the replaced track's exact length and name, and
// handed to the cue's asset provider in place of the original, so the game plays and loops it
// exactly like the clip it replaces.
public static class MusicLoopPointOverrides
{
	private struct Snippet
	{
		// Cue that plays the replaced clip, used to find its asset provider.
		public string CueNameId;

		// Clip being replaced; the snippet keeps its name and length.
		public string AssetNameId;

		// Clip under Assets/Resources/Sound/Music/ the snippet is cut from, and where it starts.
		public string SourceAssetId;

		public double SourceStartSeconds;

		// Undoes a fade-out in the source: from FadeStartSeconds (relative to the snippet start) on,
		// the snippet is boosted by FadeDbPerSecond more for every second.
		public double FadeStartSeconds;

		public double FadeDbPerSecond;
	}

	// Battle_05 (2025 remaster) contains remastered versions of the cutscene tracks; the start
	// offsets are where each original cutscene clip lines up with it.
	private static readonly List<Snippet> Snippets = new List<Snippet>
	{
		new Snippet { CueNameId = "music_cutscene_01_long", AssetNameId = "Cutscene_01_Long", SourceAssetId = "Battle_05", SourceStartSeconds = 2.7002 },
		new Snippet { CueNameId = "music_cutscene_01_short", AssetNameId = "Cutscene_01_Short", SourceAssetId = "Battle_05", SourceStartSeconds = 2.6998 },
		new Snippet { CueNameId = "music_cutscene_02_short", AssetNameId = "Cutscene_02_Short", SourceAssetId = "Battle_05", SourceStartSeconds = 86.0831, FadeStartSeconds = 2, FadeDbPerSecond = 0.47 },
	};

	// double (not float) so plain decimal literals below, e.g. 143.5, compile without needing an
	// 'f' suffix.
	private struct LoopPoint
	{
		public double StartSeconds;

		public double? EndSeconds;
	}

	private static readonly Dictionary<string, LoopPoint> LoopPoints = new Dictionary<string, LoopPoint>
	{
		{ "heartache", new LoopPoint { StartSeconds = 16, EndSeconds = 138.553 } },
		{ "campswag", new LoopPoint { StartSeconds = 10.168, EndSeconds = 45.077 } },

		{ "camp", new LoopPoint { StartSeconds = 0, EndSeconds = 75.707 } },
		{ "arenacamp", new LoopPoint { StartSeconds = 6.65, EndSeconds = 54.653 } },
		{ "worldmap", new LoopPoint { StartSeconds = 0, EndSeconds = null } },
		{ "chroniclecave", new LoopPoint { StartSeconds = 0, EndSeconds = 128.742 } },
		{ "wheeloffortune", new LoopPoint { StartSeconds = 15.6, EndSeconds = 29.371 } },

		{ "titletheme", new LoopPoint { StartSeconds = 0.031, EndSeconds = 89.69 } },
		{ "battle_01", new LoopPoint { StartSeconds = 6.25, EndSeconds = 78.25 } },
		{ "battle_02", new LoopPoint { StartSeconds = 8.25, EndSeconds = 49.392 } },
		{ "battle_03", new LoopPoint { StartSeconds = 4.3, EndSeconds = 45.442 } },
		{ "battle_04", new LoopPoint { StartSeconds = 8.9, EndSeconds = 66.499 } },
		{ "battle_05", new LoopPoint { StartSeconds = 1, EndSeconds = 84.359 } },
		{ "arenabattle", new LoopPoint { StartSeconds = 3.6, EndSeconds = 67.635 } },
		{ "boss", new LoopPoint { StartSeconds = 2.8, EndSeconds = 75.064 } },
		{ "apocalypse", new LoopPoint { StartSeconds = 0.124, EndSeconds = 109.851 } },
	};

	// Last observed playback position per music source, remembered together with the clip it
	// belonged to so a freshly started track (which begins below the previous track's position)
	// is not mistaken for a wrap at the clip end.
	private struct PlaybackState
	{
		public AudioClip Clip;

		public int TimeSamples;
	}

	private static readonly Dictionary<AudioSource, PlaybackState> LastStates = new Dictionary<AudioSource, PlaybackState>();

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
		host.StartCoroutine(ApplySnippets());
		host.StartCoroutine(Watch());
	}

	private static IEnumerator ApplySnippets()
	{
		while (DIContainerInfrastructure.AudioManager == null)
		{
			yield return null;
		}

		foreach (var snippet in Snippets)
		{
			yield return ApplySnippet(snippet);
		}
	}

	private static IEnumerator ApplySnippet(Snippet snippet)
	{
		var cue = DIContainerInfrastructure.AudioManager.GetSound(snippet.CueNameId) as Sound;
		if (cue == null)
		{
			DebugLog.Error(typeof(MusicLoopPointOverrides), "Cannot replace '" + snippet.AssetNameId + "': no such sound cue '" + snippet.CueNameId + "'.");
			yield break;
		}

		var provider = DIContainerInfrastructure.GetAudioAssetProvider(cue.AssetPrioritiy);
		if (provider == null)
		{
			DebugLog.Error(typeof(MusicLoopPointOverrides), "Cannot replace '" + snippet.AssetNameId + "': audio asset provider for priority " + cue.AssetPrioritiy + " was not found.");
			yield break;
		}

		while (!provider.m_Initialized)
		{
			yield return null;
		}

		var original = provider.GetAudioClip(snippet.AssetNameId);
		var source = Resources.Load<AudioClip>("Sound/Music/" + snippet.SourceAssetId);
		if (original == null || source == null)
		{
			DebugLog.Error(typeof(MusicLoopPointOverrides), "Cannot replace '" + snippet.AssetNameId + "' with a snippet of '" + snippet.SourceAssetId + "': clip not found.");
			yield break;
		}

		if (source.loadState != AudioDataLoadState.Loaded)
		{
			source.LoadAudioData();
			while (source.loadState == AudioDataLoadState.Loading || source.loadState == AudioDataLoadState.Unloaded)
			{
				yield return null;
			}
		}

		var lengthSamples = (int)Math.Round(original.length * source.frequency);
		var startSamples = (int)Math.Round(snippet.SourceStartSeconds * source.frequency);
		var data = new float[lengthSamples * source.channels];
		if (source.loadState != AudioDataLoadState.Loaded || startSamples + lengthSamples > source.samples || !source.GetData(data, startSamples))
		{
			DebugLog.Error(typeof(MusicLoopPointOverrides), "Cannot replace '" + snippet.AssetNameId + "': could not read the snippet from '" + snippet.SourceAssetId + "' (it must be imported as Decompress On Load and long enough).");
			yield break;
		}

		if (snippet.FadeDbPerSecond != 0)
		{
			for (var i = 0; i < lengthSamples; i++)
			{
				var fadeSeconds = (double)i / source.frequency - snippet.FadeStartSeconds;
				if (fadeSeconds <= 0)
				{
					continue;
				}
				var gain = (float)Math.Pow(10, fadeSeconds * snippet.FadeDbPerSecond / 20);
				for (var c = 0; c < source.channels; c++)
				{
					data[i * source.channels + c] *= gain;
				}
			}
		}

		var clip = AudioClip.Create(original.name, lengthSamples, source.channels, source.frequency, false);
		clip.SetData(data, 0);

		// FromMemory entries are never unloaded by the provider, so the replacement stays the one
		// clip instance the game sees for this asset.
		provider.UsedAssets[snippet.AssetNameId.ToLower()] = new AssetLoadingInfo
		{
			AssetLoadingType = LoadingType.FromMemory,
			LastRequestTime = DateTime.MinValue,
			loaded = true,
			Asset = clip
		};
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
		if (source == null)
		{
			return;
		}

		if (source.clip == null || !source.loop || !source.isPlaying || !LoopPoints.TryGetValue(source.clip.name.ToLower(), out var loopPoint))
		{
			LastStates.Remove(source);
			return;
		}

		var startSamples = Mathf.Clamp((int)(loopPoint.StartSeconds * source.clip.frequency), 0, source.clip.samples - 1);
		var endSamples = loopPoint.EndSeconds.HasValue
			? Mathf.Clamp((int)(loopPoint.EndSeconds.Value * source.clip.frequency), startSamples + 1, source.clip.samples)
			: source.clip.samples;

		var currentSamples = source.timeSamples;
		var reachedConfiguredEnd = currentSamples >= endSamples;
		// Only a backwards jump from the clip's last second counts as Unity's own wrap; a jump from
		// anywhere else (or the first frame of a newly started clip) is a fresh start from 0:00,
		// which must play the intro before StartSeconds.
		var wrappedAtClipEnd = LastStates.TryGetValue(source, out var last)
			&& last.Clip == source.clip
			&& currentSamples < last.TimeSamples
			&& last.TimeSamples >= source.clip.samples - source.clip.frequency;
		if (reachedConfiguredEnd || wrappedAtClipEnd)
		{
			source.timeSamples = startSamples;
			currentSamples = startSamples;
		}

		LastStates[source] = new PlaybackState { Clip = source.clip, TimeSamples = currentSamples };
	}
}
