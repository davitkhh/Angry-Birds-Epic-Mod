using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Custom patch (not part of the original game) that lets a location's background music cue be
// redirected to a different track (e.g. Sound/Music/CampSwag.ogg) without hand-editing the
// SoundManager/GenericAssetProvider data baked into Root.unity.
//
// To retheme another location later, add an entry to DefaultOverrides below - CueNameId is the
// key that location's state manager passes to DIContainerInfrastructure.AudioManager.PlayMusic(...)
// (e.g. CampStateMgr calls PlayMusic("music_camp")), and ThemeAssetId is the NameId of an audio
// clip that exists under Assets/Resources/Sound/Music/<ThemeAssetId>.ogg.
public static class LocationThemeOverrides
{
	private struct Override
	{
		public string CueNameId;

		public string ThemeAssetId;
	}

	private static readonly List<Override> DefaultOverrides = new List<Override>
	{
		new Override { CueNameId = "music_camp", ThemeAssetId = "CampSwag" },
	};

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Init()
	{
		MonoBehaviour host = DIContainerInfrastructure.GetCoreStateMgr();
		if (host == null) host = ContentLoader.Instance;
		if (host == null)
		{
			DebugLog.Error(typeof(LocationThemeOverrides), "No MonoBehaviour available to host the apply coroutine, themes were not overridden.");
			return;
		}
		host.StartCoroutine(ApplyWhenReady());
	}

	private static IEnumerator ApplyWhenReady()
	{
		while (DIContainerInfrastructure.AudioManager == null)
		{
			yield return null;
		}

		foreach (var entry in DefaultOverrides)
		{
			yield return Retheme(entry.CueNameId, entry.ThemeAssetId);
		}
	}

	// Public so future patches can retheme a location on demand instead of only at boot.
	public static IEnumerator Retheme(string cueNameId, string themeAssetId)
	{
		var cue = DIContainerInfrastructure.AudioManager.GetSound(cueNameId) as Sound;
		if (cue == null)
		{
			DebugLog.Error(typeof(LocationThemeOverrides), "Cannot retheme '" + cueNameId + "': no such sound cue.");
			yield break;
		}

		var provider = DIContainerInfrastructure.GetAudioAssetProvider(cue.AssetPrioritiy);
		if (provider == null)
		{
			DebugLog.Error(typeof(LocationThemeOverrides), "Cannot retheme '" + cueNameId + "': audio asset provider for priority " + cue.AssetPrioritiy + " was not found.");
			yield break;
		}

		while (!provider.m_Initialized)
		{
			yield return null;
		}

		var key = themeAssetId.ToLower();
		if (!provider.UsedAssets.ContainsKey(key))
		{
			provider.UsedAssets[key] = new AssetLoadingInfo
			{
				AssetLoadingType = LoadingType.FromResources,
				LastRequestTime = DateTime.MinValue,
				loaded = false,
				LoadPath = "Sound/Music/" + themeAssetId
			};
		}

		cue.AssetNames = new List<string> { themeAssetId };
	}
}
