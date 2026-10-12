using System.Collections;
using SmoothMoves;
using UnityEngine;

public class DisableSmoothMoveBone : MonoBehaviour
{
	[SerializeField]
	private BoneAnimation m_BoneAnimation;

	[SerializeField]
	private string BoneNameToDisable = "MainHand";

	// Hiding only at the end of the first frame let one frame render with the bone visible (e.g. McCool's
	// stall pig showing his main hand as it is switched on). Hide as soon as the mesh exists, and again for the first few
	// frames in case the rig rebuilds itself after being switched on.
	private const int HideFrames = 3;

	private int m_FramesLeft;

	private void OnEnable()
	{
		m_FramesLeft = HideFrames;
		TryHide();
		StartCoroutine(CheckIfBoneAnimationInitializedThenHideBone());
	}

	private void LateUpdate()
	{
		if (m_FramesLeft > 0 && TryHide())
		{
			m_FramesLeft--;
		}
	}

	private bool TryHide()
	{
		if (m_BoneAnimation && m_BoneAnimation.mMesh != null)
		{
			m_BoneAnimation.HideBone(BoneNameToDisable, true);
			return true;
		}
		return false;
	}

	private IEnumerator CheckIfBoneAnimationInitializedThenHideBone()
	{
		yield return new WaitForEndOfFrame();
		TryHide();
	}
}
