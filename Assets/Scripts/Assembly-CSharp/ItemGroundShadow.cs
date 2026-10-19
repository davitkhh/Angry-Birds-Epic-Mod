using UnityEngine;

// A shadow under a dropped hand item lying on the ground; once the item is gone (picked up), it shrinks away.
public class ItemGroundShadow : MonoBehaviour
{
	private const float ShrinkTime = 0.15f;

	private Transform m_Item;

	private Vector3 m_Scale;

	private float m_ShrinkStart = -1f;

	public static void Attach(Transform item, GameObject shadow)
	{
		var follow = shadow.AddComponent<ItemGroundShadow>();
		follow.m_Item = item;
		follow.m_Scale = shadow.transform.localScale;
	}

	private void LateUpdate()
	{
		if (m_Item != null)
		{
			return;
		}
		if (m_ShrinkStart < 0f)
		{
			m_ShrinkStart = Time.time;
		}
		var size = 1f - Mathf.Clamp01((Time.time - m_ShrinkStart) / ShrinkTime);
		transform.localScale = new Vector3(m_Scale.x * size, m_Scale.y * size, m_Scale.z);
		if (size <= 0f)
		{
			Destroy(gameObject);
		}
	}
}
