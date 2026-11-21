using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Models.Generic;
using UnityEngine;

// Pair layout: two half-width offers in one tile (McCool's shield upgrades, MerchantShieldUpgrades). The tile's own
// content is hidden and a "PairRoot" built at runtime from clones of the tile's widgets shows, per half: the shield,
// its in-game name, the perk it adds (icon + blue text, hold for the perk overlay), the stock line and a smaller buy button (or a check once sold out).
public partial class ShopOfferBlindWorldmap
{
	private class PairHalfView
	{
		public GameObject Root;

		public UITexture IconTexture;

		public UITexture Badge;

		public EquipmentIconPreviewPool.PreviewStage Stage;

		public GameObject Icon;

		public string IconAssetName;

		public UILabel Name;

		public UILabel Perk;

		public UISprite PerkIcon;

		public EquipmentPerk PerkData;

		public UILabel Stock;

		public GameObject Button;

		public UIInputTrigger Trigger;

		public ResourceCostBlind Cost;

		public GameObject Check;

		public Action ClickHandler;

		public BasicShopOfferBalancingData Offer;
	}

	// Layout, in the tile's local units (the full tile is about 660 wide)
	private const float PairHalfOffsetX = 165f;

	private const float PairIconX = -122f;

	private const float PairIconScale = 0.72f;

	// Hammer + green arrow (Resources/UI/ShieldUpgradeBadge, ArtSource/ShieldUpgradeBadge) on the shield's lower right
	private const string PairBadgeTexture = "UI/ShieldUpgradeBadge";

	private static readonly Vector3 PairBadgeOffset = new Vector3(22f, -20f, -1f);

	private const int PairBadgeSize = 46;

	// Left edge of the text column (labels are left-pivoted like a regular tile's)
	private const float PairTextX = -78f;

	private const int PairTextWidth = 134;

	// Perk row: icon, then the perk's text
	private const float PairPerkIconScale = 0.7f;

	private const float PairPerkTextGap = 38f;

	private const float PairButtonX = 112f;

	private const float PairButtonScale = 0.74f;

	private static readonly float[] PairLineY = new float[] { 36f, 0f, -36f };

	private static readonly Color PairPerkColor = new Color(0.36f, 0.5f, 1f);

	private GameObject m_PairRoot;

	private PairHalfView[] m_PairHalves;

	private readonly List<GameObject> m_PairHiddenRoots = new List<GameObject>();

	private bool m_IsPair;

	public bool IsPair
	{
		get
		{
			return m_IsPair;
		}
	}

	// True when a purchase of this offer happened on this tile (either half of a pair).
	public bool OwnsOffer(string offerNameId)
	{
		if (m_Model != null && m_Model.NameId == offerNameId)
		{
			return true;
		}
		return m_IsPair && m_PairHalves != null && m_PairHalves.Any(h => h.Offer != null && h.Offer.NameId == offerNameId);
	}

	private Transform PairAnimationRoot
	{
		get
		{
			var animation = base.transform.Find("Animation");
			return animation != null ? animation : base.transform;
		}
	}

	private void SetPairModel(BasicShopOfferBalancingData model)
	{
		m_IsPair = true;
		EnsurePairRoot();
		m_PairHiddenRoots.Clear();
		foreach (Transform child in PairAnimationRoot)
		{
			if (child.gameObject != m_PairRoot && child.gameObject.activeSelf)
			{
				m_PairHiddenRoots.Add(child.gameObject);
				child.gameObject.SetActive(false);
			}
		}
		m_PairRoot.SetActive(true);
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		for (var i = 0; i < m_PairHalves.Length && i < MerchantShieldUpgrades.Halves.Length; i++)
		{
			ShowPairHalf(m_PairHalves[i], MerchantShieldUpgrades.Halves[i], player);
		}
	}

	private void ExitPairMode()
	{
		if (!m_IsPair)
		{
			return;
		}
		m_IsPair = false;
		if (m_PairHalves != null)
		{
			foreach (var half in m_PairHalves)
			{
				ReleasePairIcon(half);
				if (half.Offer != null)
				{
					half.Offer = null;
				}
			}
		}
		if (m_PairRoot != null)
		{
			m_PairRoot.SetActive(false);
		}
		foreach (var root in m_PairHiddenRoots)
		{
			if (root != null)
			{
				root.SetActive(true);
			}
		}
		m_PairHiddenRoots.Clear();
	}

	private void EnsurePairRoot()
	{
		if (m_PairRoot != null)
		{
			return;
		}
		m_PairRoot = new GameObject("PairRoot");
		m_PairRoot.layer = base.gameObject.layer;
		m_PairRoot.transform.parent = PairAnimationRoot;
		m_PairRoot.transform.localPosition = new Vector3(0f, 0f, -1f);
		m_PairRoot.transform.localRotation = Quaternion.identity;
		m_PairRoot.transform.localScale = Vector3.one;
		if (m_IconPreviewTexture == null && m_IconRoot != null)
		{
			m_IconPreviewTexture = m_IconRoot.GetComponentInChildren<UITexture>(true);
		}
		var perkDisplay = m_PerkSprite != null ? m_PerkSprite.transform.parent : null;
		var check = base.transform.GetComponentsInChildren<UISprite>(true).FirstOrDefault(s => s.name == "Check" && s.transform.parent != null && s.transform.parent.name == "6_SoldOut");
		m_PairHalves = new PairHalfView[2];
		for (var i = 0; i < 2; i++)
		{
			var half = new PairHalfView();
			half.Root = new GameObject(i == 0 ? "Left" : "Right");
			half.Root.layer = m_PairRoot.layer;
			half.Root.transform.parent = m_PairRoot.transform;
			half.Root.transform.localPosition = new Vector3(i == 0 ? -PairHalfOffsetX : PairHalfOffsetX, 0f, 0f);
			half.Root.transform.localRotation = Quaternion.identity;
			half.Root.transform.localScale = Vector3.one;
			if (m_IconPreviewTexture != null)
			{
				var iconObject = ClonePairWidget(m_IconPreviewTexture.gameObject, half.Root.transform, "Icon", new Vector3(PairIconX, 0f, -2f));
				iconObject.transform.localScale = Vector3.one * PairIconScale;
				half.IconTexture = iconObject.GetComponent<UITexture>();
				iconObject.SetActive(false);
				var badgeObject = ClonePairWidget(m_IconPreviewTexture.gameObject, half.Root.transform, "UpgradeBadge", new Vector3(PairIconX, 0f, -2f) + PairBadgeOffset);
				half.Badge = badgeObject.GetComponent<UITexture>();
				half.Badge.mainTexture = Resources.Load<Texture2D>(PairBadgeTexture);
				half.Badge.width = PairBadgeSize;
				half.Badge.height = PairBadgeSize;
				half.Badge.depth = half.IconTexture.depth + 2;
			}
			// Name and stock use the regular resource tile's name and description labels as they are (font, size, height)
			half.Name = ClonePairLabel(m_ConsumableLabel, half.Root.transform, "Name", PairTextX, PairLineY[0]);
			half.Stock = ClonePairLabel(m_ConsumableEffectInfo, half.Root.transform, "Stock", PairTextX, PairLineY[2]);
			half.Perk = ClonePairLabel(m_ConsumableEffectInfo, half.Root.transform, "Perk", PairTextX + PairPerkTextGap, PairLineY[1]);
			half.Perk.width = PairTextWidth - Mathf.RoundToInt(PairPerkTextGap);
			half.Perk.color = PairPerkColor;
			// One line, shrunk to fit beside the icon (the description label template wraps)
			half.Perk.maxLineCount = 1;
			if (perkDisplay != null)
			{
				// The regular tile's perk icon, with its tap-and-hold (ActionOverlayInvoker -> ShowPairPerkTooltip<i> on this tile)
				var perkObject = ClonePairWidget(perkDisplay.gameObject, half.Root.transform, "Perk", new Vector3(PairTextX + PairPerkTextGap * 0.5f - 2f, PairLineY[1], -1f));
				perkObject.transform.localScale = Vector3.one * PairPerkIconScale;
				half.PerkIcon = perkObject.GetComponentsInChildren<UISprite>(true).FirstOrDefault();
				var invoker = perkObject.GetComponent<ActionOverlayInvoker>();
				if (invoker != null)
				{
					invoker.m_ReceiverObject = base.gameObject;
					invoker.m_OpenActionName = "ShowPairPerkTooltip" + i;
				}
			}
			half.Button = ClonePairWidget(m_BuyButtonTrigger.gameObject, half.Root.transform, "BuyButton", new Vector3(PairButtonX, 0f, -1f));
			half.Button.transform.localScale = Vector3.one * PairButtonScale;
			half.Trigger = half.Button.GetComponent<UIInputTrigger>();
			half.Cost = half.Button.GetComponentsInChildren<ResourceCostBlind>(true).FirstOrDefault(c => c.name == "DefaultPrice");
			foreach (var cost in half.Button.GetComponentsInChildren<ResourceCostBlind>(true).Where(c => c != half.Cost))
			{
				cost.gameObject.SetActive(false);
			}
			if (check != null)
			{
				half.Check = ClonePairWidget(check.gameObject, half.Root.transform, "Check", new Vector3(PairButtonX, 0f, -1f));
				half.Check.transform.localScale = Vector3.one * 0.8f;
			}
			var index = i;
			half.ClickHandler = () => BuyPairHalfClicked(index);
			m_PairHalves[i] = half;
		}
	}

	private GameObject ClonePairWidget(GameObject template, Transform parent, string name, Vector3 localPosition)
	{
		var clone = UnityEngine.Object.Instantiate(template);
		clone.name = name;
		clone.transform.parent = parent;
		clone.transform.localPosition = localPosition;
		clone.transform.localRotation = Quaternion.identity;
		clone.transform.localScale = Vector3.one;
		clone.SetActive(true);
		// A clone of the tile's label may carry an upgrade blueprint's stat rows
		UpgradeStatChangeView.Clear(clone.transform);
		return clone;
	}

	private UILabel ClonePairLabel(UILabel template, Transform parent, string name, float x, float y)
	{
		var clone = ClonePairWidget(template.gameObject, parent, name, new Vector3(x, y, -1f));
		// Localization components would overwrite the text
		foreach (var behaviour in clone.GetComponents<MonoBehaviour>().Where(b => !(b is UILabel)))
		{
			UnityEngine.Object.Destroy(behaviour);
		}
		var label = clone.GetComponent<UILabel>();
		// Keeps the template's font, size, height and overflow; only the width is the half's text column
		label.rawPivot = UIWidget.Pivot.Left;
		label.width = PairTextWidth;
		label.text = string.Empty;
		return label;
	}

#if UNITY_EDITOR
	// Edit-mode layout preview (Assets/Editor/PairTilePreview.cs): mock texts, no services. Returns the halves' icon textures.
	public UITexture[] PreviewPair(string[] names, string[] perks, string[] stocks, string[] prices, bool[] soldOut)
	{
		m_IsPair = true;
		EnsurePairRoot();
		foreach (Transform child in PairAnimationRoot)
		{
			child.gameObject.SetActive(child.gameObject == m_PairRoot);
		}
		m_PairRoot.SetActive(true);
		for (var i = 0; i < 2; i++)
		{
			var view = m_PairHalves[i];
			view.Name.text = names[i];
			view.Perk.text = perks[i];
			if (view.PerkIcon != null)
			{
				view.PerkIcon.spriteName = "PassiveEffect_SoaringProtection";
			}
			view.Stock.text = stocks[i];
			view.Button.SetActive(!soldOut[i]);
			if (view.Check != null)
			{
				view.Check.SetActive(soldOut[i]);
			}
			if (view.Cost != null)
			{
				view.Cost.gameObject.SetActive(true);
				view.Cost.m_Value.text = prices[i];
			}
		}
		return m_PairHalves.Select(h => h.IconTexture).ToArray();
	}

#endif
	private void ShowPairHalf(PairHalfView view, MerchantShieldUpgrades.Half half, PlayerGameData player)
	{
		var loca = DIContainerInfrastructure.GetLocaService();
		bool soldOut;
		var offer = MerchantShieldUpgrades.GetCurrentOffer(player, half, out soldOut);
		view.Offer = offer;
		var isBlueprint = MerchantShieldUpgrades.IsBlueprintOffer(half, offer);
		UpgradeBlueprintBalancingData definition;
		UpgradeBlueprints.TryGetDefinition(half.BlueprintId, out definition);
		var shield = DIContainerLogic.InventoryService.GenerateNewInventoryItemGameData(1, 1, half.ShieldNameId, 1);
		// The gear's in-game name; blueprints keep their own name, like a regular blueprint tile
		view.Name.text = isBlueprint && definition != null ? UpgradeBlueprints.GetName(definition) : (shield != null ? shield.ItemLocalizedName : half.ShieldNameId);
		view.PerkData = MerchantShieldUpgrades.GetPerkPerPurchase(half);
		if (view.PerkData != null)
		{
			view.Perk.text = loca.GetPerkName(view.PerkData.Type) + " +" + UpgradeBlueprints.FormatValue(view.PerkData.PerkValue) + "%";
			if (view.PerkIcon != null)
			{
				view.PerkIcon.spriteName = EquipmentGameData.GetPerkIconNameByPerk(view.PerkData.Type);
			}
		}
		else
		{
			view.Perk.text = string.Empty;
		}
		var bought = Mathf.Min(MerchantShieldUpgrades.MaxPurchases, MerchantShieldUpgrades.GetDirectCount(player.InventoryGameData, half));
		if (soldOut || offer == null)
		{
			view.Stock.text = loca.Tr("workshop_soldout", "Sold out!");
		}
		else if (isBlueprint)
		{
			var uses = MerchantShieldUpgrades.MaxPurchases - bought;
			view.Stock.text = uses == 1 ? loca.Tr("merchant_blueprint_use", "Blueprint: 1 use") : loca.Tr("merchant_blueprint_uses", "Blueprint: {value_1} uses").Replace("{value_1}", uses.ToString());
		}
		else
		{
			view.Stock.text = loca.Tr("merchant_stock_left", "{value_1} in stock").Replace("{value_1}", (MerchantShieldUpgrades.MaxPurchases - bought).ToString());
		}
		var buyable = !soldOut && offer != null;
		view.Button.SetActive(buyable);
		if (view.Check != null)
		{
			view.Check.SetActive(!buyable);
		}
		view.Trigger.Clicked -= view.ClickHandler;
		if (buyable)
		{
			var requirement = DIContainerLogic.GetShopService().GetBuyResourcesRequirements(player.Data.Level, offer).FirstOrDefault();
			if (requirement != null && view.Cost != null)
			{
				view.Cost.gameObject.SetActive(true);
				SetCostBlindModel(view.Cost, requirement);
			}
			view.Trigger.Clicked += view.ClickHandler;
		}
		ShowPairIcon(view, half.ShieldNameId, isBlueprint);
	}

	private void ShowPairIcon(PairHalfView view, string shieldNameId, bool blueprint)
	{
		ReleasePairIcon(view);
		if (view.IconTexture == null || EquipmentIconPreviewPool.Instance == null)
		{
			return;
		}
		var item = DIContainerLogic.InventoryService.GenerateNewInventoryItemGameData(1, 1, shieldNameId, 1);
		if (item == null)
		{
			return;
		}
		view.Stage = EquipmentIconPreviewPool.Instance.AcquireStage();
		if (view.Stage == null)
		{
			return;
		}
		view.IconAssetName = item.ItemAssetName;
		view.Icon = DIContainerInfrastructure.GetEquipmentAssetProvider().InstantiateObject(item.ItemAssetName, view.Stage.StageRoot, Vector3.zero, Quaternion.identity, false);
		if (view.Icon != null)
		{
			view.Icon.transform.localScale = Vector3.one;
			NGUITools.SetLayer(view.Icon, IconPreviewLayer);
			if (blueprint)
			{
				// Blueprints draw their picture in the recipe tint, like blueprint tiles
				StartCoroutine(SetRecipeShader(view.Icon.GetComponentsInChildren<Renderer>()));
			}
		}
		view.IconTexture.mainTexture = view.Stage.RenderTexture;
		view.IconTexture.gameObject.SetActive(true);
	}

	private void ReleasePairIcon(PairHalfView view)
	{
		if (view.Icon != null)
		{
			DIContainerInfrastructure.GetEquipmentAssetProvider().DestroyObject(view.IconAssetName, view.Icon);
			view.Icon = null;
		}
		if (view.Stage != null && EquipmentIconPreviewPool.Instance != null)
		{
			EquipmentIconPreviewPool.Instance.ReleaseStage(view.Stage);
		}
		view.Stage = null;
		if (view.IconTexture != null)
		{
			view.IconTexture.mainTexture = null;
			view.IconTexture.gameObject.SetActive(false);
		}
	}

	// Sent by the perk icons' ActionOverlayInvoker (tap and hold)
	private void ShowPairPerkTooltip0()
	{
		ShowPairPerkTooltip(0);
	}

	private void ShowPairPerkTooltip1()
	{
		ShowPairPerkTooltip(1);
	}

	private void ShowPairPerkTooltip(int index)
	{
		if (!m_IsPair || m_PairHalves == null || index >= m_PairHalves.Length || m_PairHalves[index].PerkData == null || m_PairHalves[index].PerkIcon == null)
		{
			return;
		}
		DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.ShowPerkOverlay(m_PairHalves[index].PerkIcon.cachedTransform, m_PairHalves[index].PerkData, true);
	}

	private void BuyPairHalfClicked(int index)
	{
		if (!m_IsPair || m_PairHalves == null || index >= m_PairHalves.Length)
		{
			return;
		}
		var offer = m_PairHalves[index].Offer;
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		List<Requirement> failed;
		if (offer == null || !DIContainerLogic.GetShopService().IsOfferBuyable(player, offer, out failed) || DIContainerLogic.GetShopService().BuyShopOffer(player, offer) == null)
		{
			if (m_SoundTriggers)
			{
				m_SoundTriggers.OnTriggerEventFired("purchase_failed");
			}
			return;
		}
		if (m_SoundTriggers)
		{
			m_SoundTriggers.OnTriggerEventFired("purchase_successful");
		}
		// Each purchase number is its own offer, so this half can't be bought again before the refresh shows the next one
		m_PairHalves[index].Trigger.Clicked -= m_PairHalves[index].ClickHandler;
		if (this.ShopOfferBought != null)
		{
			this.ShopOfferBought(offer);
		}
	}
}
