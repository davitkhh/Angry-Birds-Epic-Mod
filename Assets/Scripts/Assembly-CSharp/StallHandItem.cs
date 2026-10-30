using System.Linq;
using SmoothMoves;
using UnityEngine;

// Shows an atlas texture in a static rig's hand, e.g. McCool's bag in the shop MerchantPig's hand. Pinned to the hand
// bone's sprite like CharacterAssetController.ShowHandItem pins an item from another rig, so it follows the idle animation
// (the bone itself stays hidden by DisableSmoothMoveBone). Shown only while the StoryToggles toggle is off.
public class StallHandItem : MonoBehaviour
{
	public BoneAnimation m_BoneAnimation;

	public string m_BoneName = "MainHand";

	public string m_TextureName = "OffHand_PhatLootMcool";

	// StoryToggles name: the item is hidden while it is on (Porky had the bag stolen).
	public string m_HiddenWhileToggleOn = StoryToggles.PorkyStealsMcCoolBagName;

	// x/y: offset from the hand in the hand's space; z: world depth (negative = in front of the body).
	public Vector3 m_Offset = new Vector3(0f, 0f, -1f);

	// Degrees.
	public float m_Turn;

	private GameObject m_Carried;

	private void OnEnable()
	{
		Refresh();
	}

	public void Refresh()
	{
		var show = string.IsNullOrEmpty(m_HiddenWhileToggleOn) || !StoryToggles.IsOn(m_HiddenWhileToggleOn);
		if (!show)
		{
			if (m_Carried != null)
			{
				Destroy(m_Carried);
			}
			return;
		}
		if (m_Carried != null || m_BoneAnimation == null)
		{
			return;
		}
		var hand = m_BoneAnimation.GetBoneTransform(m_BoneName);
		var atlas = m_BoneAnimation.textureAtlases == null ? null : m_BoneAnimation.textureAtlases.FirstOrDefault(a => a != null && a.textureNames.Contains(m_TextureName));
		if (hand == null || atlas == null)
		{
			DebugLog.Warn(GetType(), "no " + m_BoneName + " bone or no atlas with " + m_TextureName + " on " + name);
			return;
		}
		var visual = hand.Find(m_BoneName + "_Sprite") ?? hand;
		m_Carried = new GameObject("Carried_" + m_BoneName + "_" + m_TextureName);
		m_Carried.transform.SetParent(visual, false);
		m_Carried.transform.localPosition = new Vector3(m_Offset.x, m_Offset.y, 0f);
		m_Carried.transform.position += new Vector3(0f, 0f, m_Offset.z);
		m_Carried.transform.localRotation = Quaternion.Euler(0f, 0f, m_Turn);
		var sprite = m_Carried.AddComponent<CHMeshSprite>();
		sprite.m_AtlasType = AtlasTypes.SmoothMoves;
		sprite.m_SmoothMovesAtlas = atlas;
		sprite.m_SpriteName = m_TextureName;
		sprite.UpdateSprite(true, true);
		UnityHelper.SetLayerRecusively(m_Carried, gameObject.layer);
	}
}
