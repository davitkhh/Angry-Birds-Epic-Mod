using UnityEngine;

// Renders one layer through the interface camera's view, cut down to a UIPanel's clip region.
// Characters are meshes that NGUI's panel clipping can't cut, so birds inside a scroll view are put on their own layer
// and drawn by this camera instead; anything scrolled outside the panel simply isn't drawn.
[RequireComponent(typeof(Camera))]
public class EquipmentPresetClipCamera : MonoBehaviour
{
	public UIPanel m_Panel;

	public int m_Layer;

	// Drawing stops this far (panel units) below the panel's top edge, e.g. where a header covers the panel.
	public float m_TopInset;

	private Camera m_Camera;

	private Camera m_InterfaceCamera;

	private void Awake()
	{
		m_Camera = GetComponent<Camera>();
		m_Camera.clearFlags = CameraClearFlags.Depth;
		m_Camera.allowHDR = false;
		m_Camera.allowMSAA = false;
	}

	private void LateUpdate()
	{
		if (m_Panel == null)
		{
			return;
		}
		if (m_InterfaceCamera == null)
		{
			m_InterfaceCamera = NGUITools.FindCameraForLayer(m_Panel.gameObject.layer);
			if (m_InterfaceCamera == null)
			{
				return;
			}
		}
		m_Camera.cullingMask = 1 << m_Layer;
		m_Camera.depth = m_InterfaceCamera.depth + 0.1f;
		m_Camera.orthographic = m_InterfaceCamera.orthographic;
		m_Camera.orthographicSize = m_InterfaceCamera.orthographicSize;
		m_Camera.nearClipPlane = m_InterfaceCamera.nearClipPlane;
		m_Camera.farClipPlane = m_InterfaceCamera.farClipPlane;
		transform.SetPositionAndRotation(m_InterfaceCamera.transform.position, m_InterfaceCamera.transform.rotation);

		var corners = m_Panel.worldCorners;
		var min = m_InterfaceCamera.WorldToViewportPoint(corners[0]);
		var max = m_InterfaceCamera.WorldToViewportPoint(corners[2] - m_Panel.transform.TransformVector(new Vector3(0f, m_TopInset, 0f)));
		var rect = Rect.MinMaxRect(Mathf.Clamp01(min.x), Mathf.Clamp01(min.y), Mathf.Clamp01(max.x), Mathf.Clamp01(max.y));
		if (rect.width <= 0f || rect.height <= 0f)
		{
			m_Camera.rect = new Rect(0f, 0f, 0f, 0f);
			return;
		}
		SetScissorRect(rect);
	}

	// Keeps the full-screen projection but only draws inside rect (viewport space).
	private void SetScissorRect(Rect rect)
	{
		m_Camera.rect = new Rect(0f, 0f, 1f, 1f);
		var projection = m_InterfaceCamera.projectionMatrix;
		m_Camera.rect = rect;
		var stretch = Matrix4x4.TRS(new Vector3(1f / rect.width - 1f, 1f / rect.height - 1f, 0f), Quaternion.identity, new Vector3(1f / rect.width, 1f / rect.height, 1f));
		var shift = Matrix4x4.TRS(new Vector3(-rect.x * 2f / rect.width, -rect.y * 2f / rect.height, 0f), Quaternion.identity, Vector3.one);
		m_Camera.projectionMatrix = shift * stretch * projection;
		// Cull against the whole screen, not the cut-down view: characters' bounds don't always cover what they draw,
		// so scissor-tight culling made birds near the clip edge vanish while still visible.
		m_Camera.cullingMatrix = projection * m_Camera.worldToCameraMatrix;
	}
}
