using SmoothMoves;
using UnityEngine;

// A ground shadow moved by an ActionTree MovePath node under a flying character (Wiz Pig taking the eggs in the intro
// raid). It builds its quad from a rig atlas's Shadow texture and sizes itself like the characters do with depth: the
// ScaleMgr factor for the battle plane depth of the ground point it stands on (the plane's world z there is its world
// y + PlaneDepthOffset), so it can be drawn at any z (e.g. in front of the scenery) and still shrink in the distance.
public class PathShadow : MonoBehaviour
{
	private const float PlaneDepthOffset = 384f;

	public TextureAtlas m_Atlas;

	public string m_SpriteName = "Shadow";

	// The shadow's size at depth factor 1 (the rig's base scale times its shadow sprite's scale).
	public Vector3 m_BaseScale = Vector3.one;

	private void Awake()
	{
		if (m_Atlas == null)
		{
			return;
		}
		var quad = new GameObject("Quad");
		quad.transform.SetParent(transform, false);
		var mesh = quad.AddComponent<CHMeshSprite>();
		mesh.m_AtlasType = AtlasTypes.SmoothMoves;
		mesh.m_SmoothMovesAtlas = m_Atlas;
		mesh.m_SpriteName = m_SpriteName;
		mesh.UpdateSprite(true, true);
		UnityHelper.SetLayerRecusively(quad, gameObject.layer);
		LateUpdate();
	}

	private void LateUpdate()
	{
		var scaleMgr = ScaleMgr.Instance;
		var factor = 1f;
		if (scaleMgr != null)
		{
			// The scale curve repeats past its borders, so stay within them.
			var near = Mathf.Min(scaleMgr.m_Near_z_Border, scaleMgr.m_Far_z_Border);
			var far = Mathf.Max(scaleMgr.m_Near_z_Border, scaleMgr.m_Far_z_Border);
			factor = scaleMgr.GetScaleFactor(Mathf.Clamp(transform.position.y + PlaneDepthOffset, near, far));
		}
		transform.localScale = new Vector3(m_BaseScale.x * factor, m_BaseScale.y * factor, 1f);
	}
}
