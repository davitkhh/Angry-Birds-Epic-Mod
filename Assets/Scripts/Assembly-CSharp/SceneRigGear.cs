using System;
using System.Collections.Generic;
using System.Linq;
using SmoothMoves;
using UnityEngine;

// Gear for a SmoothMoves rig placed in a scene (no model, so CharacterAssetController.SetModel never dresses it): swaps
// bones to textures from the rig's own atlases on start, like battle equipment does. Replaces gear that was faked with
// separate quads pinned to the bones (e.g. the intro raid's corporal helmet), which drew in an unstable order against
// the rig's mesh. Each quad is hidden only once its bone swap worked.
[DefaultExecutionOrder(100)]
public class SceneRigGear : MonoBehaviour
{
	[Serializable]
	public class Swap
	{
		public string BoneName;

		public string TextureName;

		// The quad this swap replaces.
		public GameObject Replaces;
	}

	public List<Swap> m_Swaps = new List<Swap>();

	// Like battle equipment (SetModel right after Instantiate): after the rig's Awake, before its Start. Swapping in Start
	// left bones that change atlas blank.
	private void Awake()
	{
		var bones = GetComponent<BoneAnimation>();
		if (bones == null)
		{
			return;
		}
		foreach (var swap in m_Swaps)
		{
			if (SwapBone(bones, swap.BoneName, swap.TextureName))
			{
				if (swap.Replaces != null)
				{
					swap.Replaces.SetActive(false);
				}
			}
			else
			{
				DebugLog.Warn("[SceneRigGear] " + name + ": could not swap " + swap.BoneName + " to " + swap.TextureName);
			}
		}
	}

	// An empty texture name hides the bone.
	private static bool SwapBone(BoneAnimation bones, string boneName, string textureName)
	{
		var bone = bones.mBoneSource.FirstOrDefault(b => b.boneName == boneName);
		if (bone == null)
		{
			return false;
		}
		if (string.IsNullOrEmpty(textureName))
		{
			bones.HideBone(boneName, true);
			return true;
		}
		var frame = bones.triggerFrames.FirstOrDefault(f => f.GetTriggerFrameBone(bone.boneNodeIndex) != null && !string.IsNullOrEmpty(f.GetTriggerFrameBone(bone.boneNodeIndex).originalTextureGUID));
		if (frame == null)
		{
			return false;
		}
		var originalGuid = frame.GetTriggerFrameBone(bone.boneNodeIndex).originalTextureGUID;
		var target = bones.textureAtlases.FirstOrDefault(a => a != null && a.textureNames.Contains(textureName));
		var original = bones.textureAtlases.FirstOrDefault(a => a != null && a.GetTextureIndex(originalGuid) != -1);
		if (target == null || original == null)
		{
			return false;
		}
		bones.HideBone(boneName, false);
		bones.SwapBoneTexture(boneName, original.name, original.textureNames[original.GetTextureIndex(originalGuid)], target.name, textureName);
		return true;
	}
}
