using System.Collections;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

// "+X [blue arrow holding the ammo]" that appears beside a combatant gaining ammo (left of birds, right of pigs), floats
// up and fades out. The "+X" and the arrow pop in together with a squash and stretch. Birds' popups wait until the bird stands in its
// attacking position (where it steps forward to on its turn), so regen gained as the turn flips shows up next to it.
public class AmmoGainPopup : MonoBehaviour
{
	public static float Lifetime = 1.6f;

	public static float FadeIn = 0.2f;

	public static float FadeOut = 0.6f;

	public static float Rise = 90f;

	public static float SideGap = -20f;

	public static int FontSize = 60;

	public static float ArrowWidth = 104f;

	// How far the "+X" sits below the arrow's middle
	public static float LabelDrop = 16f;

	// Longest wait for a bird to reach its attacking position
	public static float MaxWait = 4f;

	// Pop scale keys (time in seconds, x, y): squash, stretch, settle
	private static readonly Vector3[] PopKeys =
	{
		new Vector3(0f, 1.35f, 0.55f),
		new Vector3(0.09f, 0.8f, 1.25f),
		new Vector3(0.18f, 1.06f, 0.95f),
		new Vector3(0.26f, 1f, 1f)
	};

	// Resources/UI/AmmoGainArrow.png (GenericElements' UpgradeArrow recoloured blue, shaft made solid and longer):
	// shaft share of the arrow's width, and the shaft's centre below the middle (share of the height)
	private const string ArrowTexture = "UI/AmmoGainArrow";

	private const float ShaftWidth = 32f / 64f;

	private const float ShaftCenter = 18f / 80f;

	private UIPanel m_Panel;

	private Transform m_PopRoot;

	private ICombatant m_Combatant;

	private int m_Amount;

	[RuntimeInitializeOnLoadMethod]
	private static void Register()
	{
		Ammo.Gained -= Spawn;
		Ammo.Gained += Spawn;
	}

	public static void Spawn(ICombatant combatant, int amount)
	{
		var view = combatant != null ? combatant.CombatantView : null;
		if (view == null || view.m_AssetController == null || view.m_BattleMgr == null || !view.gameObject.activeInHierarchy)
		{
			return;
		}
		var root = new GameObject("AmmoGainPopup");
		root.layer = view.gameObject.layer;
		root.transform.parent = view.m_BattleMgr.m_BattleArea;
		root.transform.rotation = Quaternion.identity;
		root.transform.localScale = Vector3.one;
		var popup = root.AddComponent<AmmoGainPopup>();
		popup.m_Panel = root.AddComponent<UIPanel>();
		popup.m_Panel.alpha = 0f;
		popup.m_Panel.depth = 100;
		popup.m_Combatant = combatant;
		popup.m_Amount = amount;
		popup.StartCoroutine(popup.Run());
	}

	private IEnumerator Run()
	{
		var view = m_Combatant.CombatantView;
		if (m_Combatant.CombatantFaction == Faction.Birds)
		{
			var waited = 0f;
			var lastPosition = view != null ? view.transform.position : Vector3.zero;
			var still = 0f;
			while (waited < MaxWait && view != null)
			{
				var position = view.transform.position;
				still = (position - lastPosition).sqrMagnitude < 0.01f ? still + Time.deltaTime : 0f;
				lastPosition = position;
				if (view.m_BattleMgr.m_BirdTurnStarted && view.IsAtBirdFocusPosition() && still > 0.05f)
				{
					break;
				}
				waited += Time.deltaTime;
				yield return null;
			}
		}
		if (view == null || view.m_AssetController == null)
		{
			Destroy(gameObject);
			yield break;
		}
		var assets = view.m_AssetController;
		var side = m_Combatant.CombatantFaction == Faction.Pigs ? 1f : -1f;
		var halfWidth = Mathf.Abs(assets.ColliderSize.x * assets.transform.localScale.x) * 0.5f;
		var anchor = assets.BodyCenter != null ? assets.BodyCenter.position : view.transform.position;
		transform.position = new Vector3(view.transform.position.x + side * (halfWidth + SideGap), anchor.y, anchor.z - 20f);
		transform.rotation = Quaternion.identity;
		var content = new GameObject("Content").transform;
		content.gameObject.layer = gameObject.layer;
		content.parent = transform;
		content.localPosition = Vector3.zero;
		content.localRotation = Quaternion.identity;
		content.localScale = Vector3.one;
		var combatText = view.m_ScrollingCombatTextPrefab;
		var width = BuildContent(content, combatText != null ? combatText.m_LabelHealthChange : null, m_Amount, AmmoUI.GetIconSource(m_Combatant), out m_PopRoot);
		// Grow away from the character: birds' popups end at the anchor, pigs' start there
		content.localPosition = new Vector3(side < 0f ? -width : 0f, 0f, 0f);
		yield return StartCoroutine(Animate());
	}

	// ["+X"][blue arrow with the ammo in its shaft] laid out from x = 0 to the right; returns the width. popRoot holds
	// both and is their scaling root, at the bottom centre of the pair.
	public static float BuildContent(Transform content, UILabel style, int amount, AmmoUI.IconSource icon, out Transform popRoot)
	{
		var arrowTexture = Resources.Load<Texture2D>(ArrowTexture);
		var arrowHeight = arrowTexture != null ? ArrowWidth * arrowTexture.height / arrowTexture.width : ArrowWidth;
		popRoot = new GameObject("PopRoot").transform;
		popRoot.gameObject.layer = content.gameObject.layer;
		popRoot.parent = content;
		popRoot.localRotation = Quaternion.identity;
		popRoot.localScale = Vector3.one;
		var label = AmmoUI.CreateLabel(popRoot, style, "+" + amount, Color.white, UIWidget.Pivot.Left, FontSize, 3);
		label.effectStyle = UILabel.Effect.Outline;
		label.effectColor = new Color(0.07f, 0.27f, 0.63f, 1f);
		label.effectDistance = new Vector2(2f, 2f);
		var x = label.printedSize.x - 4f;
		var width = x + ArrowWidth;
		popRoot.localPosition = new Vector3(width * 0.5f, -arrowHeight * 0.5f, 0f);
		label.cachedTransform.localPosition = new Vector3(-width * 0.5f, arrowHeight * 0.5f - LabelDrop, 0f);
		var arrowRoot = new GameObject("ArrowRoot").transform;
		arrowRoot.gameObject.layer = content.gameObject.layer;
		arrowRoot.parent = popRoot;
		arrowRoot.localRotation = Quaternion.identity;
		arrowRoot.localScale = Vector3.one;
		arrowRoot.localPosition = new Vector3(x + ArrowWidth * 0.5f - width * 0.5f, 0f, 0f);
		if (arrowTexture != null)
		{
			var arrowObject = new GameObject("Arrow");
			arrowObject.layer = content.gameObject.layer;
			arrowObject.transform.parent = arrowRoot;
			arrowObject.transform.localRotation = Quaternion.identity;
			arrowObject.transform.localScale = Vector3.one;
			arrowObject.transform.localPosition = new Vector3(0f, arrowHeight * 0.5f, 0f);
			var arrow = arrowObject.AddComponent<UITexture>();
			arrow.mainTexture = arrowTexture;
			arrow.width = Mathf.RoundToInt(ArrowWidth);
			arrow.height = Mathf.RoundToInt(arrowHeight);
			arrow.depth = 1;
		}
		// The ammo, centred in the shaft
		var widget = AmmoUI.CreateIcon(arrowRoot, icon, ArrowWidth * ShaftWidth - 10f, 2);
		if (widget != null)
		{
			widget.cachedTransform.localPosition = new Vector3(0f, arrowHeight * (0.5f - ShaftCenter), 0f);
		}
		return width;
	}

	private IEnumerator Animate()
	{
		var start = transform.position;
		var time = 0f;
		while (time < Lifetime)
		{
			time += Time.deltaTime;
			var t = Mathf.Clamp01(time / Lifetime);
			var eased = 1f - (1f - t) * (1f - t);
			transform.position = start + new Vector3(0f, Rise * eased, 0f);
			if (m_PopRoot != null)
			{
				m_PopRoot.localScale = PopScale(time);
			}
			var alpha = 1f;
			if (time < FadeIn)
			{
				alpha = time / FadeIn;
			}
			else if (time > Lifetime - FadeOut)
			{
				alpha = Mathf.Clamp01((Lifetime - time) / FadeOut);
			}
			m_Panel.alpha = alpha;
			yield return null;
		}
		Destroy(gameObject);
	}

	private static Vector3 PopScale(float time)
	{
		for (var i = 1; i < PopKeys.Length; i++)
		{
			if (time < PopKeys[i].x)
			{
				var a = PopKeys[i - 1];
				var b = PopKeys[i];
				var t = Mathf.SmoothStep(0f, 1f, (time - a.x) / (b.x - a.x));
				return new Vector3(Mathf.Lerp(a.y, b.y, t), Mathf.Lerp(a.z, b.z, t), 1f);
			}
		}
		return Vector3.one;
	}
}
