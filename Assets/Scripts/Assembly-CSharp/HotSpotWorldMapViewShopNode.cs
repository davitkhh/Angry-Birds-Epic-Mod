using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using SmoothMoves;
using UnityEngine;

public class HotSpotWorldMapViewShopNode : HotSpotWorldMapViewBase
{
	public ShopMenuType m_ShopMenuType;

	[SerializeField]
	private Vector3 m_bubbleOffset = Vector3.zero;

	public GameObject m_UpdateIndikator;

	private GameObject m_Bubble;

	public GameObject m_SoldOutIndicator;

	[SerializeField]
	private GameObject[] m_activateObjects;

	// Merchant only: McCool at the stall plays this once each time he comes fully on screen while the update indicator shows.
	private const string MerchantUpdateAnimation = "Action_Support";

	// Action_Support fires this trigger, but the stall rig's sound list (minion sounds) had no entry for it.
	private const string MerchantSupportTrigger = "PlaySound_Support";

	private const string MerchantSupportSound = "Character_Minion_Support";

	private BoneAnimation m_StallMerchant;

	private Renderer m_StallMerchantRenderer;

	private bool m_StallMerchantSearched;

	private bool m_StallMerchantReacted;

	public override void ActivateAsset(bool activate)
	{
		var activateObjects = m_activateObjects;
		foreach (var gameObject in activateObjects)
		{
			gameObject.SetActive(true);
		}
	}

	public override bool IsCompleted()
	{
		return base.Model.Data.UnlockState == HotspotUnlockState.Resolved;
	}

	private void Update()
	{
		if (!m_UpdateIndikator || !FindStallMerchant())
		{
			return;
		}
		var react = m_UpdateIndikator.activeInHierarchy && IsFullyOnScreen(m_StallMerchantRenderer);
		if (react && !m_StallMerchantReacted)
		{
			var legacy = m_StallMerchant.GetComponent<Animation>();
			if (legacy != null && legacy.GetClip(MerchantUpdateAnimation) != null)
			{
				m_StallMerchant.Play(MerchantUpdateAnimation);
				m_StallMerchant.PlayQueued("Idle");
			}
		}
		m_StallMerchantReacted = react;
	}

	// The stall's MerchantPig is one of the objects the merchant switches on when it unlocks.
	private bool FindStallMerchant()
	{
		if (!m_StallMerchantSearched && base.Model != null)
		{
			m_StallMerchantSearched = true;
			if (IsMerchantShop() && m_activateObjects != null)
			{
				m_StallMerchant = m_activateObjects.Where(o => o != null).Select(o => o.GetComponentInChildren<BoneAnimation>(true)).FirstOrDefault(b => b != null);
				m_StallMerchantRenderer = m_StallMerchant != null ? m_StallMerchant.GetComponent<Renderer>() : null;
				var sounds = m_StallMerchant != null ? m_StallMerchant.GetComponent<TriggerSoundByBoneAnimation>() : null;
				if (sounds != null && !sounds.m_SoundTriggerPairs.Any(p => p.TriggerId == MerchantSupportTrigger))
				{
					sounds.m_SoundTriggerPairs.Add(new SoundTriggerPair { TriggerId = MerchantSupportTrigger, SoundId = MerchantSupportSound });
				}
			}
		}
		return m_StallMerchant != null;
	}

	public override void HandleMouseButtonUp(bool directMove = false)
	{
		base.HandleMouseButtonUp(true);
	}

	public override void SynchBalancing()
	{
		base.SynchBalancing();
		m_active = false;
	}

	public override void Initialize()
	{
		if (m_SoldOutIndicator)
		{
			m_SoldOutIndicator.SetActive(false);
		}
		if (!base.Model.IsActive())
		{
			return;
		}
		var flag = DIContainerLogic.RequirementService.CheckGenericRequirements(DIContainerInfrastructure.GetCurrentPlayer(), base.Model.BalancingData.EnterRequirements);
		if (m_ShopMenuType == ShopMenuType.Dojo)
		{
			var ts = DIContainerInfrastructure.GetCurrentPlayer().Data.LastClassSwitchTime + (uint)DIContainerBalancing.GameConstantsBalancingDataProvider.TimeForNextClassUpgrade;
			if (DIContainerLogic.GetTimingService().IsAfter(DIContainerLogic.GetTimingService().GetDateTimeFromTimestamp(ts)))
			{
				m_UpdateIndikator.SetActive(true);
			}
		}
		else if (flag)
		{
			CheckForNewMarker();
		}
	}

	public void CheckForNewMarker()
	{
		ShopBalancingData balancing = null;
		var list = new List<BasicShopOfferBalancingData>();
		if (DIContainerBalancing.Service.TryGetBalancingData<ShopBalancingData>(base.Model.BalancingData.HotspotContents.Keys.FirstOrDefault(), out balancing))
		{
			list = DIContainerLogic.GetShopService().GetShopOffers(DIContainerInfrastructure.GetCurrentPlayer(), base.Model.BalancingData.HotspotContents.Keys.FirstOrDefault());
		}
		var flag = false;
		var showUpdate = false;
		var onlyLimitedStock = IsMerchantShop();
		if (onlyLimitedStock)
		{
			MerchantStock.Sync(DIContainerInfrastructure.GetCurrentPlayer(), list);
		}
		if (m_ShopMenuType != ShopMenuType.Dojo)
		{
			foreach (var item in list)
			{
				if (DIContainerLogic.GetShopService().IsOfferShowable(DIContainerInfrastructure.GetCurrentPlayer(), item) && DIContainerLogic.GetShopService().IsOfferBuyableIgnoreCost(DIContainerInfrastructure.GetCurrentPlayer(), item))
				{
					flag = true;
					if (!onlyLimitedStock || IsLimitedStockOffer(item))
					{
						showUpdate = true;
						break;
					}
				}
			}
		}
		if (onlyLimitedStock)
		{
			// Restocking offers are only ever sold out until their next restock, so the merchant never counts as finished.
			flag |= list.Any(MerchantStock.IsRestockable);
			showUpdate |= MerchantStock.HasRestockNotice(DIContainerInfrastructure.GetCurrentPlayer(), list);
		}
		if (m_UpdateIndikator)
		{
			m_UpdateIndikator.SetActive(showUpdate);
		}
		if (m_SoldOutIndicator)
		{
			m_SoldOutIndicator.SetActive(!flag);
		}
	}

	// Merchants flag one-time offers while they are in stock, and restocking offers only when a restock refilled a sold-out offer.
	private bool IsMerchantShop()
	{
		var shopId = base.Model.BalancingData.HotspotContents.Keys.FirstOrDefault();
		return shopId != null && shopId.StartsWith("merchant_");
	}

	// Limited offers can only be bought X times: they grant a counter item that a NotHaveItem requirement checks.
	private static bool IsLimitedStockOffer(BasicShopOfferBalancingData offer)
	{
		if (MerchantStock.IsRestockable(offer))
		{
			return false;
		}
		return (offer.BuyRequirements != null && offer.BuyRequirements.Any(r => r.RequirementType == RequirementType.NotHaveItem)) || (offer.ShowRequirements != null && offer.ShowRequirements.Any(r => r.RequirementType == RequirementType.NotHaveItem));
	}

	public override void ShowContentView()
	{
		Requirement firstFailedReq = null;
		if (!DIContainerLogic.WorldMapService.CanTravelToHotspot(DIContainerInfrastructure.GetCurrentPlayer(), base.Model, out firstFailedReq))
		{
			if (firstFailedReq.RequirementType == RequirementType.HaveItem)
			{
				var inventoryItemGameData = DIContainerLogic.InventoryService.GenerateNewInventoryItemGameData(1, 1, firstFailedReq.NameId, (int)firstFailedReq.Value);
				UIAtlas atlas = null;
				if (DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().ContainsAsset(inventoryItemGameData.ItemIconAtlasName))
				{
					var gameObject = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().GetObject(inventoryItemGameData.ItemIconAtlasName) as GameObject;
					atlas = gameObject.GetComponent<UIAtlas>();
				}
				if (!m_Bubble)
				{
					m_Bubble = DIContainerInfrastructure.LocationStateMgr.GetEmoteBubble(inventoryItemGameData.ItemAssetName, m_bubbleOffset, base.transform, atlas);
				}
			}
			return;
		}
		base.ShowContentView();
		// Merchant hotspots are serialized as Workshop; route them to the merchant layout instead of the anvil one.
		switch (IsMerchantShop() ? ShopMenuType.Merchant : m_ShopMenuType)
		{
		case ShopMenuType.Workshop:
			DIContainerInfrastructure.LocationStateMgr.ShowWorkshopScreen(base.Model.BalancingData.HotspotContents.Keys.FirstOrDefault(), this);
			break;
		case ShopMenuType.Witchhut:
			DIContainerInfrastructure.LocationStateMgr.ShowWitchHutScreen(base.Model.BalancingData.HotspotContents.Keys.FirstOrDefault(), this);
			break;
		case ShopMenuType.Trainer:
			DIContainerInfrastructure.LocationStateMgr.ShowTrainerScreen(base.Model.BalancingData.HotspotContents.Keys.FirstOrDefault(), this);
			break;
		case ShopMenuType.Dojo:
			DIContainerInfrastructure.LocationStateMgr.ShowDojoScreen(base.Model.BalancingData.HotspotContents.Keys.FirstOrDefault(), this);
			break;
		case ShopMenuType.Merchant:
			DIContainerInfrastructure.LocationStateMgr.ShowMerchantScreen(base.Model.BalancingData.HotspotContents.Keys.FirstOrDefault(), this);
			break;
		}
	}

	public override IEnumerator ActivateFollowUpStagesAsync(HotSpotWorldMapViewBase parentHotSpot, HotSpotWorldMapViewBase activateTo, bool instant = false)
	{
		var wasHidden = base.Model.Data.UnlockState < HotspotUnlockState.Resolved;
		yield return StartCoroutine(base.ActivateFollowUpStagesAsync(parentHotSpot, activateTo, instant));
		if (wasHidden && base.Model.Data.UnlockState == HotspotUnlockState.Resolved)
		{
			PlayActiveAnimation();
			CheckForNewMarker();
		}
		else if (base.Model.Data.UnlockState == HotspotUnlockState.Resolved)
		{
			PlayActiveIdleAnimation();
		}
	}
}
