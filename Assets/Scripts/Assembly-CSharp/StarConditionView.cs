using ABH.Shared.Generic;
using UnityEngine;

// Put on a star widget: draws the star's condition icon on top of it and shows the star's tap-and-hold overlay
// (InfoOverlayMgr.ShowStarConditionOverlay). Regular stars get only the hold (their text says it's the score star).
public class StarConditionView : MonoBehaviour
{
	// Fallback icon size relative to the star widget, when there is no icon baked for that star sprite
	public const float IconScale = 0.62f;

	private StarCondition m_Star;

	private UITexture m_Icon;

	private UITapHoldTrigger m_Trigger;

	private bool m_IsTapping;

	// Score shown for a regular star ("Reach the score N."), asked for when the hold begins; null = no number
	public System.Func<string> ScoreText;

	public UITexture Icon
	{
		get
		{
			return m_Icon;
		}
	}

	// star null = regular star. withHold false = icon only. holdTrigger: an existing tap-and-hold of the element the
	// star belongs to (touches go to the object with the Rigidbody, e.g. a rules tile root); null = the star gets its
	// own collider and trigger.
	public static StarConditionView Attach(UIWidget star, StarCondition condition, bool withHold, UITapHoldTrigger holdTrigger = null)
	{
		var view = star.GetComponent<StarConditionView>();
		if (view == null)
		{
			view = star.gameObject.AddComponent<StarConditionView>();
		}
		view.Set(star, condition, withHold, holdTrigger);
		return view;
	}

	public void Set(UIWidget star, StarCondition condition, bool withHold, UITapHoldTrigger holdTrigger = null)
	{
		m_Star = condition;
		var sprite = star as UISprite;
		var spriteName = sprite != null ? sprite.spriteName : null;
		// Baked onto this star sprite's face, in its perspective (ArtSource/StarConditions/star_faces.py): drawn over
		// the whole widget. Otherwise the plain rimmed icon, centred.
		var baked = BattleStars.IconOnSprite(condition, spriteName);
		var texture = baked != null ? baked : BattleStars.Icon(condition, true);
		if (texture != null)
		{
			if (m_Icon == null)
			{
				var go = new GameObject("StarConditionIcon");
				go.layer = star.gameObject.layer;
				go.transform.parent = star.cachedTransform;
				go.transform.localRotation = Quaternion.identity;
				go.transform.localScale = Vector3.one;
				m_Icon = go.AddComponent<UITexture>();
				m_Icon.shader = Shader.Find("Unlit/Transparent Colored");
			}
			m_Icon.mainTexture = texture;
			// Right above the star: anything drawn over the star (the loot wheel) stays over the icon too
			m_Icon.depth = star.depth + 1;
			if (baked != null)
			{
				m_Icon.width = star.width;
				m_Icon.height = star.height;
			}
			else
			{
				var size = Mathf.Min(star.width, star.height) * IconScale;
				var aspect = (float)texture.width / Mathf.Max(1, texture.height);
				m_Icon.width = Mathf.RoundToInt(aspect >= 1f ? size : size * aspect);
				m_Icon.height = Mathf.RoundToInt(aspect >= 1f ? size / aspect : size);
			}
			var pivot = star.pivotOffset;
			m_Icon.cachedTransform.localPosition = new Vector3((0.5f - pivot.x) * star.width, (0.5f - pivot.y) * star.height, 0f);
			m_Icon.gameObject.SetActive(true);
		}
		else if (m_Icon != null)
		{
			m_Icon.gameObject.SetActive(false);
		}
		if (withHold && m_Trigger == null)
		{
			m_Trigger = holdTrigger;
			if (m_Trigger == null)
			{
				var box = GetComponent<BoxCollider>();
				if (box == null)
				{
					box = gameObject.AddComponent<BoxCollider>();
				}
				var pivot = star.pivotOffset;
				box.size = new Vector3(star.width, star.height, 1f);
				box.center = new Vector3((0.5f - pivot.x) * star.width, (0.5f - pivot.y) * star.height, 0f);
				m_Trigger = GetComponent<UITapHoldTrigger>();
				if (m_Trigger == null)
				{
					m_Trigger = gameObject.AddComponent<UITapHoldTrigger>();
				}
			}
			m_Trigger.OnTapBegin += OnTapBegin;
			m_Trigger.OnTapReleased += OnTapEnd;
		}
	}

	// Desaturated look for a star that wasn't earned (the icon dims with it)
	public void SetEarned(bool earned)
	{
		if (m_Icon != null)
		{
			m_Icon.color = earned ? Color.white : new Color(0.75f, 0.75f, 0.75f, 1f);
		}
	}

	private void OnTapBegin()
	{
		var mgr = DIContainerInfrastructure.GetCoreStateMgr();
		if (mgr != null && mgr.m_InfoOverlays != null)
		{
			mgr.m_InfoOverlays.ShowStarConditionOverlay(transform, m_Star, true, m_Star == null && ScoreText != null ? ScoreText() : null);
			m_IsTapping = true;
		}
	}

	private void OnTapEnd()
	{
		var mgr = DIContainerInfrastructure.GetCoreStateMgr();
		if (mgr != null && mgr.m_InfoOverlays != null)
		{
			mgr.m_InfoOverlays.HideAllTooltips();
		}
		m_IsTapping = false;
	}

	private void OnDestroy()
	{
		if (m_IsTapping)
		{
			OnTapEnd();
		}
		if (m_Trigger != null)
		{
			m_Trigger.OnTapBegin -= OnTapBegin;
			m_Trigger.OnTapReleased -= OnTapEnd;
		}
	}
}
