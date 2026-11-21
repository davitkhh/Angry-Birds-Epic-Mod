using ABH.GameDatas;
using UnityEditor;
using UnityEngine;

// Play mode, on the world map with no popup open: shows Bomb's "bird unlocked" popup again so its unlock sequence
// (BirdUnlockSequence) can be replayed. The player must already own Bomb. Also reachable without focus through
// Temp/BirdUnlockReplay.request (content: the add-bird item, default addbird_blackbird).
public static class BirdUnlockSequenceMenu
{
	private const string RequestPath = "Temp/BirdUnlockReplay.request";

	[MenuItem("Tools/Bird Unlock/Replay Bomb's Unlock Popup")]
	private static void ReplayBomb()
	{
		Replay("addbird_blackbird");
	}

	[MenuItem("Tools/Bird Unlock/Replay Bomb's Unlock Popup", true)]
	private static bool ReplayBombValid()
	{
		return EditorApplication.isPlaying;
	}

	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update += () =>
		{
			if (!System.IO.File.Exists(RequestPath) || !EditorApplication.isPlaying)
			{
				return;
			}
			var item = System.IO.File.ReadAllText(RequestPath).Trim();
			System.IO.File.Delete(RequestPath);
			Replay(string.IsNullOrEmpty(item) ? "addbird_blackbird" : item);
		};
	}

	private static void Replay(string addBirdItem)
	{
		var core = DIContainerInfrastructure.GetCoreStateMgr();
		if (core == null || core.m_FeatureUnlockedPopup == null)
		{
			Debug.LogError("[BirdUnlock] No feature popup (is the world map loaded?)");
			return;
		}
		if (core.m_FeatureUnlockedPopup.m_IsShowing || BirdUnlockSequence.Active != null)
		{
			Debug.LogError("[BirdUnlock] A popup or sequence is already showing");
			return;
		}
		var item = new BasicItemGameData(addBirdItem);
		Debug.Log("[BirdUnlock] Replaying the unlock popup of " + item.ItemAssetName);
		core.StartCoroutine(core.m_FeatureUnlockedPopup.ShowUnlockFeaturePopup(item).Run());
	}
}
