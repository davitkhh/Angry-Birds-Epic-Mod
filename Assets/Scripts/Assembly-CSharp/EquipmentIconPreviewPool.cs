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

	private readonly Dictionary<Camera, float> m_DefaultOrthographicSizes = new Dictionary<Camera, float>();

	private void Awake()
	{
		Instance = this;
		for (var i = 0; i < m_Stages.Count; i++)
		{
			var stage = m_Stages[i];
			if (stage.PreviewCamera != null)
			{
				stage.PreviewCamera.enabled = false;
				// The stage cameras were created with an AudioListener; the scene must only have the main one.
				var listener = stage.PreviewCamera.GetComponent<AudioListener>();
				if (listener != null)
				{
					Destroy(listener);
				}
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
				RememberDefaultSize(stage);
				stage.InUse = true;
				if (stage.PreviewCamera != null)
				{
					stage.PreviewCamera.enabled = true;
				}
				return stage;
			}
		}
		// More icon tiles than stages (a shop can list five): add one so every icon is still clipped by the list
		var added = CreateStage();
		if (added == null)
		{
			return null;
		}
		RememberDefaultSize(added);
		added.InUse = true;
		added.PreviewCamera.enabled = true;
		return added;
	}

	private void RememberDefaultSize(PreviewStage stage)
	{
		if (stage.PreviewCamera != null && !m_DefaultOrthographicSizes.ContainsKey(stage.PreviewCamera))
		{
			m_DefaultOrthographicSizes[stage.PreviewCamera] = stage.PreviewCamera.orthographicSize;
		}
	}

	// Copies the first stage (root + camera) further along x with a render texture of its own.
	private PreviewStage CreateStage()
	{
		var template = m_Stages.Count > 0 ? m_Stages[0] : null;
		if (template == null || template.StageRoot == null || template.PreviewCamera == null || template.RenderTexture == null)
		{
			return null;
		}
		var offset = new Vector3(400f * m_Stages.Count, 0f, 0f);
		var root = Instantiate(template.StageRoot.gameObject, template.StageRoot.parent);
		root.name = template.StageRoot.name + "_" + m_Stages.Count;
		root.transform.position = template.StageRoot.position + offset;
		root.transform.rotation = template.StageRoot.rotation;
		root.transform.localScale = template.StageRoot.localScale;
		for (var i = root.transform.childCount - 1; i >= 0; i--)
		{
			Destroy(root.transform.GetChild(i).gameObject);
		}
		var cameraObject = new GameObject(template.PreviewCamera.name + "_" + m_Stages.Count);
		cameraObject.layer = template.PreviewCamera.gameObject.layer;
		cameraObject.transform.parent = template.PreviewCamera.transform.parent;
		cameraObject.transform.position = template.PreviewCamera.transform.position + offset;
		cameraObject.transform.rotation = template.PreviewCamera.transform.rotation;
		var camera = cameraObject.AddComponent<Camera>();
		camera.CopyFrom(template.PreviewCamera);
		var texture = new RenderTexture(template.RenderTexture.descriptor);
		texture.name = template.RenderTexture.name + "_" + m_Stages.Count;
		camera.targetTexture = texture;
		camera.enabled = false;
		var stage = new PreviewStage { StageRoot = root.transform, PreviewCamera = camera, RenderTexture = texture };
		m_Stages.Add(stage);
		return stage;
	}

	public void ReleaseStage(PreviewStage stage)
	{
		if (stage == null)
		{
			return;
		}
		stage.InUse = false;
		// A blueprint's class picture may have widened the view; the next icon gets the authored one
		float size;
		if (stage.PreviewCamera != null && m_DefaultOrthographicSizes.TryGetValue(stage.PreviewCamera, out size))
		{
			stage.PreviewCamera.orthographicSize = size;
		}
		if (stage.PreviewCamera != null)
		{
			stage.PreviewCamera.enabled = false;
		}
	}
}
