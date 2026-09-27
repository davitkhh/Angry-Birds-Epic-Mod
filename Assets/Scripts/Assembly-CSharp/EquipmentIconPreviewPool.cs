using System.Collections.Generic;
using UnityEngine;

// Holds a fixed pool of off-screen "stages" (a Transform + a dedicated Camera rendering
// into a RenderTexture) that ShopOfferBlindWorldmap uses to preview equipment models.
// Rendering equipment icons to a texture lets them be shown via a UITexture widget, which
// NGUI's UIPanel clipping can actually affect - unlike the raw instantiated equipment prefab,
// which uses plain Unity Renderers that NGUI clipping cannot touch.
public class EquipmentIconPreviewPool : MonoBehaviour
{
	[System.Serializable]
	public class PreviewStage
	{
		public Transform StageRoot;

		public Camera PreviewCamera;

		public RenderTexture RenderTexture;

		[System.NonSerialized]
		public bool InUse;
	}

	[SerializeField]
	private List<PreviewStage> m_Stages = new List<PreviewStage>();

	public static EquipmentIconPreviewPool Instance { get; private set; }

	private void Awake()
	{
		Instance = this;
		for (var i = 0; i < m_Stages.Count; i++)
		{
			var stage = m_Stages[i];
			if (stage.PreviewCamera != null)
			{
				stage.PreviewCamera.enabled = false;
			}
		}
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public PreviewStage AcquireStage()
	{
		for (var i = 0; i < m_Stages.Count; i++)
		{
			var stage = m_Stages[i];
			if (!stage.InUse)
			{
				stage.InUse = true;
				if (stage.PreviewCamera != null)
				{
					stage.PreviewCamera.enabled = true;
				}
				return stage;
			}
		}
		return null;
	}

	public void ReleaseStage(PreviewStage stage)
	{
		if (stage == null)
		{
			return;
		}
		stage.InUse = false;
		if (stage.PreviewCamera != null)
		{
			stage.PreviewCamera.enabled = false;
		}
	}
}
