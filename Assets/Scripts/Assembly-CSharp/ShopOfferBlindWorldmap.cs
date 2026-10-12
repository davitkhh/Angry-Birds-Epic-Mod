using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ABH.GameDatas;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

public class ShopOfferBlindWorldmap : MonoBehaviour
{
	[SerializeField]
	private List<RequirementRoot> m_RequirementRoots = new List<RequirementRoot>();

	[SerializeField]
	private Transform m_IconRoot;

	[SerializeField]
	private GameObject m_SlotRoot;

	[SerializeField]
	private GameObject m_RankUpEffectPrefab;

	[SerializeField]
	private GameObject m_BuyIndicatorPrefab;

	[SerializeField]
	private GameObject m_RecipeScrollRoot;

	[SerializeField]
	private GameObject m_MasteryBadgeRoot;

	private GameObject m_IconInstanciated;

	// Must match the "IconPreview" layer added in ProjectSettings/TagManager.asset.
	private const int IconPreviewLayer = 25;

	private UITexture m_IconPreviewTexture;

	private EquipmentIconPreviewPool.PreviewStage m_IconPreviewStage;

	[SerializeField]
	private UILabel m_BlindMainLabel;

	[SerializeField]
	private ResourceCostBlind m_CostBlind;

	[SerializeField]
	private ResourceCostBlind m_DiscountCostBlind;

	[SerializeField]
	private UILabel m_DiscountOldCost;

	[SerializeField]
	private UISprite m_AdditionalSprite;

	[SerializeField]
	private UISprite m_ItemIconSprite;

	[SerializeField]
	public UIInputTrigger m_BuyButtonTrigger;

	private BasicShopOfferBalancingData m_Model;

	private IInventoryItemGameData m_Item;

	[SerializeField]
	private StatisticsElement m_ItemStats;

	[SerializeField]
	private UISprite m_PerkSprite;

	[SerializeField]
	private UISprite m_ConsumableEffectSprite;

	[SerializeField]
	private UILabel m_ConsumableEffectInfo;

	[SerializeField]
	private UILabel m_ConsumableLabel;

	[SerializeField]
	private GameObject m_ConsumableEffectRoot;

	[SerializeField]
	private GameObject m_WeaponStatsRoot;

	[SerializeField]
	private GameObject m_MasteryTextRoot;

	[SerializeField]
	private UILabel m_MasteryTextTitle;

	[SerializeField]
	private UILabel m_MasteryTextDescription;

	[SerializeField]
	private UISprite m_MasteryBarCurrent;

	[SerializeField]
	private UISprite m_MasteryBarDelta;

	[SerializeField]
	private UILabel m_MasteryRank;
	
	[SerializeField]
	private UISprite m_MasteryBuyButtonbody;

	[SerializeField]
	private GameObject m_ClassNameRoot;

	[SerializeField]
	private UILabel m_ClassName;

	private SoundTriggerList m_SoundTriggers;

	private bool m_IsHidden = true;

	private WorldMapStateMgr m_StateMgr;

	private CraftingRecipeGameData m_RecipeItem;

	private int m_requirementLevel;

	private bool m_failedLevelRequirement;

	private int m_ClassRanksGained;

	[method: MethodImpl(32)]
	public event Action<BasicShopOfferBalancingData> ShopOfferBought;

	public BasicShopOfferBalancingData GetModel()
	{
		return m_Model;
	}

	public void ShowTooltip()
	{
		if (m_RecipeItem != null)
		{
			if (m_failedLevelRequirement)
			{
				DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.ShowItemOverlay(m_IconRoot, m_RecipeItem, true, false, m_requirementLevel);
			}
			else
			{
				DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.ShowItemOverlay(m_IconRoot, m_RecipeItem, true, false);
			}
		}
		else
		{
			DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.ShowItemOverlay(m_IconRoot, m_Item, true, false);
		}
	}

	public void ShowPerkTooltip()
	{
		var equipmentGameData = m_Item as EquipmentGameData;
		if (equipmentGameData != null && m_PerkSprite)
		{
			DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.ShowPerkOverlay(m_PerkSprite.cachedTransform, equipmentGameData, true);
		}
	}

	public void SetModel(BasicShopOfferBalancingData model, WorldMapStateMgr stateMgr, bool applyDojoMasteryBonus = false)
	{
		RegisterEventHandlers();
		m_SoundTriggers = GetComponent<SoundTriggerList>();
		RemoveAsset(m_Item);
		// Free the preview stage from this tile's previous offer, whatever kind of icon replaces it
		ReleaseIconPreviewStage();
		ShowIconPreviewTexture(null);
		m_RecipeItem = null;
		m_Item = null;
		m_Model = model;
		m_StateMgr = stateMgr;
		var shopOfferContent = DIContainerLogic.GetShopService().GetShopOfferContent(DIContainerInfrastructure.GetCurrentPlayer(), m_Model, DIContainerLogic.GetSalesManagerService().GetOfferSaleDetails(m_Model.NameId));
		m_ItemIconSprite.gameObject.SetActive(false);
		RestoreItemIconSize();
		m_requirementLevel = 0;
		m_failedLevelRequirement = false;
		m_WeaponStatsRoot.SetActive(false);
		m_ConsumableEffectRoot.SetActive(false);
		m_MasteryTextRoot.SetActive(false);
		m_ClassNameRoot.SetActive(false);
		m_SlotRoot.SetActive(false);
		m_MasteryBarCurrent.transform.parent.gameObject.SetActive(false);
		m_MasteryBadgeRoot.SetActive(false);
		if (string.IsNullOrEmpty(m_Model.AssetId))
		{
			m_AdditionalSprite.gameObject.SetActive(true);
			var inventoryItemGameData = shopOfferContent.FirstOrDefault();
			if (inventoryItemGameData.ItemBalancing.ItemType == InventoryItemType.CraftingRecipes)
			{
				m_MasteryBadgeRoot.SetActive(false);
				m_RecipeItem = inventoryItemGameData as CraftingRecipeGameData;
				m_RecipeScrollRoot.SetActive(true);
				var craftingRecipeGameData = inventoryItemGameData as CraftingRecipeGameData;
				var loot = DIContainerLogic.GetLootOperationService().GenerateLoot(craftingRecipeGameData.GetResultLoot(), craftingRecipeGameData.Data.Level);
				var itemsFromLoot = DIContainerLogic.GetLootOperationService().GetItemsFromLoot(DIContainerInfrastructure.GetCurrentPlayer(), loot);
				m_Item = itemsFromLoot[0];
				UpgradeBlueprintBalancingData upgradeBlueprint;
				var isUpgradeBlueprint = UpgradeBlueprints.TryGetDefinition(craftingRecipeGameData, out upgradeBlueprint);
				UpgradeBlueprints.SetGoldParchment(m_RecipeScrollRoot, isUpgradeBlueprint);
				UpgradeStatChangeView.Clear(m_ConsumableEffectInfo.transform);
				if (isUpgradeBlueprint)
				{
					// Upgrade blueprints show what they change (old -> new) instead of the equipment's stats
					m_WeaponStatsRoot.SetActive(false);
					m_ConsumableEffectRoot.SetActive(true);
					m_ConsumableLabel.text = UpgradeBlueprints.GetName(upgradeBlueprint);
					m_AdditionalSprite.spriteName = m_Item is EquipmentGameData ? EquipmentGameData.GetRestrictedBirdIcon(m_Item as EquipmentGameData) : ClassItemGameData.GetRestrictedBirdIcon(m_Item.ItemBalancing);
					ShowUpgradeStatChanges(upgradeBlueprint);
				}
				else if (craftingRecipeGameData.BalancingData.RecipeCategoryType == InventoryItemType.Consumable)
				{
					m_WeaponStatsRoot.SetActive(false);
					m_ConsumableEffectRoot.SetActive(true);
					m_AdditionalSprite.gameObject.SetActive(false);
					m_ItemIconSprite.gameObject.SetActive(true);
					if (DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().ContainsAsset("Consumables"))
					{
						var gameObject = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().GetObject("Consumables") as GameObject;
						m_ItemIconSprite.atlas = gameObject.GetComponent<UIAtlas>();
					}
					m_ItemIconSprite.spriteName = m_Item.ItemAssetName;
					var consumableItemGameData = m_Item as ConsumableItemGameData;
					var effectValueString = ConsumableItemGameData.GetEffectValueString(consumableItemGameData.BalancingData, craftingRecipeGameData.Data.Level, "_Large");
					m_ConsumableEffectInfo.text = effectValueString.LocalizedText;
					m_ConsumableLabel.text = consumableItemGameData.ItemLocalizedName;
				}
				else if (craftingRecipeGameData.BalancingData.RecipeCategoryType == InventoryItemType.MainHandEquipment)
				{
					m_WeaponStatsRoot.SetActive(true);
					m_ConsumableEffectRoot.SetActive(false);
					var equip2 = m_Item as EquipmentGameData;
					m_AdditionalSprite.spriteName = EquipmentGameData.GetRestrictedBirdIcon(equip2);
					var birdGameData = DIContainerInfrastructure.GetCurrentPlayer().Birds.FirstOrDefault(b => b.BalancingData.NameId == equip2.BalancingData.RestrictedBirdId);
					if (m_PerkSprite)
					{
						m_PerkSprite.spriteName = EquipmentGameData.GetPerkIcon(equip2);
					}
					m_ItemStats.SetIconSprite("Character_Damage_Large");
					m_ItemStats.RefreshStat(false, birdGameData != null, EquipmentGameData.GetItemMainStat(equip2, 3), birdGameData == null ? 0f : birdGameData.MainHandItem.ItemMainStat);
				}
				else if (craftingRecipeGameData.BalancingData.RecipeCategoryType == InventoryItemType.OffHandEquipment)
				{
					m_WeaponStatsRoot.SetActive(true);
					m_ConsumableEffectRoot.SetActive(false);
					var equip = m_Item as EquipmentGameData;
					m_AdditionalSprite.spriteName = EquipmentGameData.GetRestrictedBirdIcon(equip);
					if (m_PerkSprite)
					{
						m_PerkSprite.spriteName = EquipmentGameData.GetPerkIcon(equip);
					}
					var birdGameData2 = DIContainerInfrastructure.GetCurrentPlayer().Birds.FirstOrDefault(b => b.BalancingData.NameId == equip.BalancingData.RestrictedBirdId);
					m_ItemStats.SetIconSprite("Character_Health_Large");
					m_ItemStats.RefreshStat(false, birdGameData2 != null, EquipmentGameData.GetItemMainStat(equip, 3), birdGameData2 == null ? 0f : birdGameData2.OffHandItem.ItemMainStat);
				}
				InstantiateAsset(m_Item, m_IconRoot);
			}
			else
			{
				m_RecipeScrollRoot.SetActive(false);
				m_Item = inventoryItemGameData;
				if (m_Item is MasteryItemGameData)
				{
					HandleMastery();
				}
				else if (m_Item is ClassItemGameData)
				{
					m_SlotRoot.SetActive(true);
					InstantiateAsset(m_Item, m_IconRoot);
					m_AdditionalSprite.spriteName = ClassItemGameData.GetRestrictedBirdIcon(m_Item.ItemBalancing);
				}
				else if (m_Item.ItemBalancing.ItemType == InventoryItemType.Skin)
				{
					m_SlotRoot.SetActive(true);
					InstantiateAsset(m_Item, m_IconRoot);
					var skinBalancing = m_Item.ItemBalancing as ClassSkinBalancingData;
					var skinClass = DIContainerBalancing.Service.GetBalancingData<ClassItemBalancingData>(skinBalancing.OriginalClass);
					m_AdditionalSprite.spriteName = ClassItemGameData.GetRestrictedBirdIcon(skinClass);
				}
				else if (IsSpriteIconItem(m_Item))
				{
					ReleaseIconPreviewStage();
					m_AdditionalSprite.gameObject.SetActive(false);
					var atlas = GetIconAtlas(m_Item.ItemBalancing.ItemType);
					if (atlas != null)
					{
						m_ItemIconSprite.atlas = atlas;
					}
					m_ItemIconSprite.spriteName = m_Item.ItemAssetName;
					m_ItemIconSprite.gameObject.SetActive(true);
					FitItemIconToSprite();
					// m_BlindMainLabel lives under the equipment root, so use the consumable layout for the name.
					m_ConsumableEffectRoot.SetActive(true);
					m_ConsumableLabel.text = GetOfferItemName(m_Item);
					m_ConsumableEffectInfo.text = GetStockText();
				}
				else
				{
					var equipment = m_Item as EquipmentGameData;
					if (equipment != null)
					{
						ShowEquipmentOffer(equipment);
					}
					InstantiateAsset(m_Item, m_IconRoot);
				}
			}
		}
		else
		{
			m_RecipeScrollRoot.SetActive(false);
			m_ItemIconSprite.gameObject.SetActive(false);
		}
		m_BlindMainLabel.gameObject.SetActive(true);
		var requirement = DIContainerLogic.GetShopService().GetBuyResourcesRequirements(DIContainerInfrastructure.GetCurrentPlayer().Data.Level, m_Model).FirstOrDefault();
		if (m_CostBlind != null && m_Model is BuyableShopOfferBalancingData && DIContainerLogic.GetShopService().IsPriceDiscount(m_Model))
		{
			m_CostBlind.gameObject.SetActive(false);
			m_DiscountCostBlind.gameObject.SetActive(true);
			m_DiscountOldCost.text = m_Model.BuyRequirements.Where(r => r.NameId == "lucky_coin").FirstOrDefault().Value.ToString();
			m_MasteryBuyButtonbody.color = Color.white;
			if (m_Model.SpecialOfferLabelColor != null)
			{
				m_DiscountCostBlind.m_Value.color = new Color(m_Model.SpecialOfferLabelColor[0], m_Model.SpecialOfferLabelColor[1], m_Model.SpecialOfferLabelColor[2]);
			}
			else
			{
				m_DiscountCostBlind.m_Value.color = new Color(0f, 0f, 0f);
			}
			if (m_DiscountCostBlind.m_CostBody != null)
			{
				if (m_Model.SpecialOfferBackgroundColor != null)
				{
					m_DiscountCostBlind.m_CostBody.color = new Color(m_Model.SpecialOfferBackgroundColor[0], m_Model.SpecialOfferBackgroundColor[1], m_Model.SpecialOfferBackgroundColor[2]);
				}
				else
				{
					m_DiscountCostBlind.m_CostBody.color = new Color(0.5f, 1f, 0f);
				}
			}
			if (requirement != null)
			{
				SetCostBlindModel(m_DiscountCostBlind, requirement);
			}
		}

		if (m_CostBlind != null && m_Model is BuyableShopOfferBalancingData && DIContainerLogic.GetShopService().IsValueDiscount(m_Model))
		{
			m_CostBlind.gameObject.SetActive(true);
			m_DiscountCostBlind.gameObject.SetActive(false);
			if (requirement != null)
			{
				SetCostBlindModel(m_CostBlind, requirement);
			}
			m_MasteryBuyButtonbody.color = Color.green;
		}
		else
		{
			m_MasteryBuyButtonbody.color = Color.white;
			if (requirement != null)
			{
				SetCostBlindModel(m_CostBlind, requirement);
			}
			m_CostBlind.gameObject.SetActive(true);
			m_DiscountCostBlind.gameObject.SetActive(false);
		}
		
		var failed = new List<Requirement>();
		m_BuyButtonTrigger.Clicked -= BuyOfferClicked;
		m_BuyButtonTrigger.Clicked += BuyOfferClicked;
		if (DIContainerLogic.GetShopService().IsOfferBuyable(DIContainerInfrastructure.GetCurrentPlayer(), m_Model, out failed))
		{
			RequirementRoot requirementRoot = null;
			foreach (var requirementRoot3 in m_RequirementRoots)
			{
				foreach (var root in requirementRoot3.Roots)
				{
					root.SetActive(false);
				}
				if (requirementRoot3.Type == RequirementType.None)
				{
					requirementRoot = requirementRoot3;
				}
			}
			foreach (var root2 in requirementRoot.Roots)
			{
				root2.SetActive(true);
			}
			SetBlindLabelItemText(m_Item);
		}
		else
		{
			var requirement2 = failed.FirstOrDefault();
			var inventoryItemGameData2 = shopOfferContent.FirstOrDefault();
			RequirementRoot requirementRoot2 = null;
			foreach (var requirementRoot4 in m_RequirementRoots)
			{
				foreach (var root3 in requirementRoot4.Roots)
				{
					root3.SetActive(false);
				}
				if (requirementRoot4.Type == requirement2.RequirementType)
				{
					requirementRoot2 = requirementRoot4;
				}
			}
			if (requirementRoot2 == null)
			{
				requirementRoot2 = GetFallbackRequirementRoot(requirement2.RequirementType);
			}
			foreach (var root4 in requirementRoot2.Roots)
			{
				root4.SetActive(true);
			}
			switch (requirement2.RequirementType)
			{
			case RequirementType.HaveItem:
			case RequirementType.HaveClass:
				requirementRoot2.TextLabel.text = DIContainerInfrastructure.GetLocaService().Tr("req_failed_haveitem", "You do not own this Item");
				break;
			case RequirementType.Level:
				requirementRoot2.TextLabel.text = requirement2.Value.ToString("0");
				m_requirementLevel = (int)requirement2.Value;
				m_failedLevelRequirement = true;
				break;
			case RequirementType.NotHaveItem:
			case RequirementType.NotHaveClass:
				SetBlindLabelItemText(m_Item);
				break;
			case RequirementType.PayItem:
				SetBlindLabelItemText(m_Item);
				break;
			case RequirementType.HaveBird:
				BirdBalancingData balancing = null;
				if (DIContainerBalancing.Service.TryGetBalancingData<BirdBalancingData>(requirement2.NameId, out balancing))
				{
					requirementRoot2.IconSprite.spriteName = balancing.AssetId;
				}
				break;
			}
			HandleMasteryRequirementFailed(requirement2);
		}
		if (applyDojoMasteryBonus)
		{
			m_MasteryTextTitle.color = new Color(0.46f, 1f, 0f);
		}
	}

	// The world shop blinds only author roots for a few requirement types. Show class requirements with
	// the matching item layout ("not owned" / "already owned") and anything else with the default one,
	// rather than throwing and leaving the rest of the shop without click handlers.
	private RequirementRoot GetFallbackRequirementRoot(RequirementType type)
	{
		RequirementType fallbackType;
		switch (type)
		{
		case RequirementType.HaveClass:
			fallbackType = RequirementType.HaveItem;
			break;
		case RequirementType.NotHaveClass:
			fallbackType = RequirementType.NotHaveItem;
			break;
		default:
			fallbackType = RequirementType.None;
			break;
		}
		return m_RequirementRoots.FirstOrDefault(r => r.Type == fallbackType) ?? m_RequirementRoots.First(r => r.Type == RequirementType.None);
	}

	private void HandleMasteryRequirementFailed(Requirement firstFailed)
	{
		switch (firstFailed.RequirementType)
		{
		case RequirementType.None:
		case RequirementType.PayItem:
			break;
		case RequirementType.HaveItem:
		case RequirementType.HaveClass:
			m_MasteryBarCurrent.transform.parent.gameObject.SetActive(false);
			break;
		case RequirementType.NotHaveItem:
		case RequirementType.NotHaveClass:
			m_MasteryBarCurrent.transform.parent.gameObject.SetActive(false);
			break;
		case RequirementType.HaveBird:
		case RequirementType.Level:
		case RequirementType.CooldownFinished:
		case RequirementType.IsSpecificWeekday:
		case RequirementType.IsNotSpecificWeekday:
		case RequirementType.HaveCurrentHotpsotState:
		case RequirementType.HavePassedCycleTime:
		case RequirementType.NotHavePassedCycleTime:
			break;
		case RequirementType.NotHaveItemWithLevel:
			m_MasteryBarCurrent.transform.parent.gameObject.SetActive(false);
			break;
		case RequirementType.HaveItemWithLevel:
			m_MasteryBarCurrent.transform.parent.gameObject.SetActive(false);
			break;
		case RequirementType.UsedFriends:
		case RequirementType.HaveUnlockedHotpsot:
		case RequirementType.NotHaveUnlockedHotpsot:
		case RequirementType.HaveLessThan:
		case RequirementType.HaveBirdCount:
		case RequirementType.IsConverted:
			break;
		default:
			throw new ArgumentOutOfRangeException();
		}
	}

	private void HandleMastery()
	{
		m_AdditionalSprite.gameObject.SetActive(false);
		m_MasteryBadgeRoot.SetActive(true);
		m_MasteryTextRoot.SetActive(true);
		var masteryItemGameData = m_Item as MasteryItemGameData;
		if (masteryItemGameData == null)
		{
			return;
		}
		var associatedClass = masteryItemGameData.BalancingData.AssociatedClass;
		if (associatedClass.Equals("ALL") || associatedClass.StartsWith("bird_"))
		{
			m_ItemIconSprite.gameObject.SetActive(true);
			m_MasteryBarCurrent.transform.parent.gameObject.SetActive(false);
			if (DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().ContainsAsset("GenericElements"))
			{
				var gameObject = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().GetObject("GenericElements") as GameObject;
				if (gameObject != null)
				{
					m_ItemIconSprite.atlas = gameObject.GetComponent<UIAtlas>();
				}
				m_ItemIconSprite.spriteName = m_Item.ItemAssetName;
				m_ItemIconSprite.MakePixelPerfect();
			}
		}
		else
		{
			InstantiateAsset(m_Item, m_IconRoot);
			if (m_IconInstanciated != null)
			{
				m_IconInstanciated.transform.localScale = Vector3.one * 0.6f;
			}
			m_ItemIconSprite.gameObject.SetActive(false);
			IInventoryItemGameData data;
			if (DIContainerLogic.InventoryService.TryGetItemGameData(DIContainerInfrastructure.GetCurrentPlayer().InventoryGameData, masteryItemGameData.BalancingData.AssociatedClass, out data))
			{
				var classItemGameData = data as ClassItemGameData;
				m_MasteryBarCurrent.transform.parent.gameObject.SetActive(true);
				if (classItemGameData != null)
				{
					m_MasteryBarCurrent.fillAmount = classItemGameData.MasteryProgressNextRank();
					m_MasteryBarDelta.fillAmount = classItemGameData.MasteryProgressNextRank(masteryItemGameData.ItemValue);
					m_MasteryRank.text = DIContainerInfrastructure.GetFormatProvider().GetResourceAmountFormat(classItemGameData.Data.Level);
				}
			}
			else
			{
				m_MasteryBarCurrent.transform.parent.gameObject.SetActive(false);
			}
		}
		var dictionary = new Dictionary<string, string>();
		dictionary.Add("{value_1}", DIContainerInfrastructure.GetFormatProvider().GetResourceAmountFormat(masteryItemGameData.ItemValue));
		m_MasteryTextTitle.text = DIContainerInfrastructure.GetLocaService().Tr("offer_mastery_increase", dictionary);
		m_MasteryTextDescription.text = m_Item.ItemLocalizedDesc.Replace("{value_2}", masteryItemGameData.GetMasteryClassName(DIContainerInfrastructure.GetCurrentPlayer()));
	}

	private void SetCostBlindModel(ResourceCostBlind costBlind, Requirement requirement)
	{
		var balancing = DIContainerBalancing.GetInventoryItemBalancingDataPovider().GetBalancingData(requirement.NameId);
		costBlind.SetModel(balancing.AssetBaseId, GetIconAtlas(balancing.ItemType), requirement.Value, string.Empty);
	}

	private static bool IsSpriteIconItem(IInventoryItemGameData item)
	{
		switch (item.ItemBalancing.ItemType)
		{
		case InventoryItemType.Resources:
		case InventoryItemType.Ingredients:
		case InventoryItemType.PlayerStats:
		case InventoryItemType.Premium:
			return true;
		default:
			return false;
		}
	}

	// Cost blinds are reused across offers, so always hand back an atlas rather than null;
	// otherwise a Resources atlas set for one offer would stick to the next offer's coin icon.
	private static UIAtlas GetIconAtlas(InventoryItemType itemType)
	{
		string atlasName;
		switch (itemType)
		{
		case InventoryItemType.Resources:
			atlasName = "Resources";
			break;
		case InventoryItemType.Ingredients:
			atlasName = "Ingredients";
			break;
		case InventoryItemType.Consumable:
			atlasName = "Consumables";
			break;
		default:
			atlasName = "GenericElements";
			break;
		}
		var atlasProvider = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider();
		if (!atlasProvider.ContainsAsset(atlasName))
		{
			return null;
		}
		var atlasObject = atlasProvider.GetObject(atlasName) as GameObject;
		return atlasObject != null ? atlasObject.GetComponent<UIAtlas>() : null;
	}

	// Draws the old -> new stat rows in the effect text's place, shrunk to fit its box; falls back to the description.
	private void ShowUpgradeStatChanges(UpgradeBlueprintBalancingData upgradeBlueprint)
	{
		var changes = UpgradeBlueprints.GetStatChanges(upgradeBlueprint, DIContainerInfrastructure.GetCurrentPlayer().InventoryGameData);
		if (changes.Count == 0)
		{
			m_ConsumableEffectInfo.text = UpgradeBlueprints.GetDescription(upgradeBlueprint);
			return;
		}
		UpgradeStatChangeView.ShowInShopTile(m_ConsumableEffectInfo, changes);
	}

	private void ShowEquipmentOffer(EquipmentGameData equipment)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		int rewardLevel;
		if (DIContainerLogic.GetShopService().TryGetOfferItemRewardLevel(player, m_Model, equipment.BalancingData.NameId, out rewardLevel))
		{
			// Preview what the purchase actually grants: name, model and stats all depend on level.
			equipment.Data.Level = rewardLevel;
		}
		m_WeaponStatsRoot.SetActive(true);
		m_ConsumableEffectRoot.SetActive(false);
		m_AdditionalSprite.spriteName = EquipmentGameData.GetRestrictedBirdIcon(equipment);
		if (m_PerkSprite)
		{
			m_PerkSprite.spriteName = EquipmentGameData.GetPerkIcon(equipment);
		}
		var isMainHand = equipment.BalancingData.ItemType == InventoryItemType.MainHandEquipment;
		var bird = player.Birds.FirstOrDefault(b => b.BalancingData.NameId == equipment.BalancingData.RestrictedBirdId);
		var equipped = bird == null ? null : isMainHand ? bird.MainHandItem : bird.OffHandItem;
		m_ItemStats.SetIconSprite(isMainHand ? "Character_Damage_Large" : "Character_Health_Large");
		m_ItemStats.RefreshStat(false, equipped != null, EquipmentGameData.GetItemMainStat(equipment), equipped == null ? 0f : equipped.ItemMainStat);
	}

	// Restocking merchant offers show how many purchases are left until they sell out.
	private string GetStockText()
	{
		int remaining;
		if (!MerchantStock.TryGetRemaining(DIContainerInfrastructure.GetCurrentPlayer(), m_Model, out remaining) || remaining <= 0)
		{
			return string.Empty;
		}
		return DIContainerInfrastructure.GetLocaService().Tr("merchant_stock_left", "{value_1} in stock").Replace("{value_1}", remaining.ToString());
	}

	private static string GetOfferItemName(IInventoryItemGameData item)
	{
		if (item.ItemValue > 1)
		{
			var singular = item.ItemLocalizedName;
			var plural = DIContainerInfrastructure.GetLocaService().Tr(item.ItemBalancing.LocaBaseId + "_name_plural", singular);
			return DIContainerInfrastructure.GetFormatProvider().GetResourceAmountFormat(item.ItemValue) + " " + plural;
		}
		return item.ItemLocalizedName;
	}

	// Consumable and resource icons are 120px sprites with built-in margins; coin sprites are
	// tightly cropped 80px ones, so scale the widget by sprite size to keep them visually matched.
	private const float ItemIconReferenceSpriteSize = 120f;

	private Vector2 m_ItemIconDefaultSize;

	private void RestoreItemIconSize()
	{
		if (m_ItemIconDefaultSize == Vector2.zero)
		{
			m_ItemIconDefaultSize = new Vector2(m_ItemIconSprite.width, m_ItemIconSprite.height);
		}
		m_ItemIconSprite.width = (int)m_ItemIconDefaultSize.x;
		m_ItemIconSprite.height = (int)m_ItemIconDefaultSize.y;
	}

	private void FitItemIconToSprite()
	{
		var spriteData = m_ItemIconSprite.GetAtlasSprite();
		if (spriteData == null)
		{
			return;
		}
		var scale = Mathf.Min(1f, Mathf.Max(spriteData.width, spriteData.height) / ItemIconReferenceSpriteSize);
		m_ItemIconSprite.width = Mathf.RoundToInt(m_ItemIconDefaultSize.x * scale);
		m_ItemIconSprite.height = Mathf.RoundToInt(m_ItemIconDefaultSize.y * scale);
	}

	private void SetBlindLabelItemText(IInventoryItemGameData item)
	{
		m_BlindMainLabel.text = item.ItemLocalizedName;
		// A class drawn on an upgrade blueprint isn't the thing being sold, so no class name under the title
		if (m_RecipeItem == null && (item is ClassItemGameData || item.ItemBalancing.ItemType == InventoryItemType.Skin))
		{
			m_ClassNameRoot.SetActive(true);
			m_ClassName.text = item.ItemLocalizedName;
		}
	}

	private void BuyCoinsClicked()
	{
		var allModifiedBuyRequirements = DIContainerLogic.GetShopService().GetAllModifiedBuyRequirements(DIContainerInfrastructure.GetCurrentPlayer(), m_Model);
		var requirement = allModifiedBuyRequirements.FirstOrDefault(r => r.RequirementType == RequirementType.PayItem);
		if (requirement != null && requirement.RequirementType == RequirementType.PayItem)
		{
			IInventoryItemGameData data = null;
			if (DIContainerLogic.InventoryService.TryGetItemGameData(DIContainerInfrastructure.GetCurrentPlayer().InventoryGameData, requirement.NameId, out data))
			{
				DIContainerInfrastructure.GetCoreStateMgr().m_MissingCurrencyPopup.EnterPopup(data.ItemBalancing.NameId, requirement.Value);
			}
		}
	}

	private void BuyOfferClicked()
	{
		var failed = new List<Requirement>();
		if (!DIContainerLogic.GetShopService().IsOfferBuyable(DIContainerInfrastructure.GetCurrentPlayer(), m_Model, out failed))
		{
			if (m_SoundTriggers)
			{
				m_SoundTriggers.OnTriggerEventFired("purchase_failed");
			}
			if (failed.Any(r => r.RequirementType == RequirementType.PayItem))
			{
				BuyCoinsClicked();
			}
			return;
		}
		if (m_StateMgr.m_WorkShopUI != null && m_StateMgr.m_WorkShopUI.m_MenuType == ShopMenuType.Dojo)
		{
			DIContainerInfrastructure.GetCurrentPlayer().ClassRankedUp -= ClassRankedUp;
			DIContainerInfrastructure.GetCurrentPlayer().ClassRankedUp += ClassRankedUp;
		}
		if (DIContainerLogic.GetShopService().BuyShopOffer(DIContainerInfrastructure.GetCurrentPlayer(), m_Model) == null)
		{
			DebugLog.Error("Failed to buy Offer!");
			if (m_SoundTriggers)
			{
				m_SoundTriggers.OnTriggerEventFired("purchase_failed");
			}
			return;
		}
		if (m_StateMgr.m_WorkShopUI != null && m_StateMgr.m_WorkShopUI.m_MenuType == ShopMenuType.Dojo)
		{
			var flag = false;
			foreach (var buyRequirement in m_Model.BuyRequirements)
			{
				if (buyRequirement.RequirementType == RequirementType.PayItem && buyRequirement.NameId == "lucky_coin" && buyRequirement.Value > 0f)
				{
					flag = true;
					break;
				}
			}
			if (flag)
			{
				DIContainerInfrastructure.GetCurrentPlayer().Data.DojoOffersBought = 0;
			}
			else
			{
				DIContainerInfrastructure.GetCurrentPlayer().Data.DojoOffersBought++;
			}
			if (m_ClassRanksGained > 0)
			{
				var gameObject = UnityEngine.Object.Instantiate(m_RankUpEffectPrefab, base.transform.position, Quaternion.identity) as GameObject;
				if (gameObject != null)
				{
					var componentInChildren = gameObject.GetComponentInChildren<UILabel>();
					componentInChildren.text = m_ClassRanksGained != 1 ? DIContainerInfrastructure.GetLocaService().Tr("dojo_rankup_plural", new Dictionary<string, string> { 
					{
						"{value_1}",
						m_ClassRanksGained.ToString("0")
					} }) : DIContainerInfrastructure.GetLocaService().Tr("dojo_rankup_singular", "Rank Up!");
					UnityEngine.Object.Destroy(gameObject, gameObject.PlayAnimationOrAnimatorState("RankUpEffect"));
				}
				m_ClassRanksGained = 0;
			}
			for (var i = 0; i < DIContainerInfrastructure.GetCurrentPlayer().Data.CurrentClassUpgradeShopOffers.Count; i++)
			{
				var text = DIContainerInfrastructure.GetCurrentPlayer().Data.CurrentClassUpgradeShopOffers[i];
				if (text == m_Model.NameId)
				{
					DIContainerInfrastructure.GetCurrentPlayer().Data.CurrentClassUpgradeShopOffers[i] = string.Empty;
				}
			}
			DIContainerInfrastructure.GetCurrentPlayer().ClassRankedUp -= ClassRankedUp;
		}
		if (m_SoundTriggers)
		{
			m_SoundTriggers.OnTriggerEventFired("purchase_successful");
		}
		// Repeatable trades stay clickable so quick repeat purchases are not dropped while the shop refreshes.
		// Dojo offers are swapped out after a purchase, so they always lock until the refresh.
		var isDojo = m_StateMgr.m_WorkShopUI != null && m_StateMgr.m_WorkShopUI.m_MenuType == ShopMenuType.Dojo;
		if (isDojo || !CanBeBoughtAgain(m_Model))
		{
			DeRegisterEventHandlers();
		}
		if (this.ShopOfferBought != null)
		{
			this.ShopOfferBought(m_Model);
		}
	}

	// True when nothing but the price stops the offer from being bought again (e.g. merchant resource trades).
	public static bool CanBeBoughtAgain(BasicShopOfferBalancingData offer)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		return offer != null && DIContainerLogic.GetShopService().IsOfferShowable(player, offer) && DIContainerLogic.GetShopService().IsOfferBuyableIgnoreCost(player, offer);
	}

	private void ClassRankedUp()
	{
		m_ClassRanksGained++;
	}

	private void RegisterEventHandlers()
	{
		DeRegisterEventHandlers();
	}

	private void DeRegisterEventHandlers()
	{
		if (m_BuyButtonTrigger)
		{
			m_BuyButtonTrigger.Clicked -= BuyCoinsClicked;
			m_BuyButtonTrigger.Clicked -= BuyOfferClicked;
		}
	}

	private void OnDestroy()
	{
		if (m_IconInstanciated)
		{
		}
		ReleaseIconPreviewStage();
		DeRegisterEventHandlers();
	}

	private void RemoveAsset(IInventoryItemGameData item)
	{
		if (m_IconInstanciated && item != null)
		{
			switch (item.ItemBalancing.ItemType)
			{
			case InventoryItemType.Class:
				DIContainerInfrastructure.GetClassAssetProvider().DestroyObject(item.ItemAssetName, m_IconInstanciated);
				break;
			case InventoryItemType.Consumable:
				break;
			case InventoryItemType.CraftingRecipes:
				DIContainerInfrastructure.GetEquipmentAssetProvider().DestroyObject(item.ItemAssetName, m_IconInstanciated);
				break;
			case InventoryItemType.Ingredients:
				break;
			case InventoryItemType.MainHandEquipment:
				DIContainerInfrastructure.GetEquipmentAssetProvider().DestroyObject(item.ItemAssetName, m_IconInstanciated);
				break;
			case InventoryItemType.OffHandEquipment:
				DIContainerInfrastructure.GetEquipmentAssetProvider().DestroyObject(item.ItemAssetName, m_IconInstanciated);
				break;
			case InventoryItemType.PlayerStats:
			case InventoryItemType.PlayerToken:
			case InventoryItemType.Points:
			case InventoryItemType.Premium:
			case InventoryItemType.Resources:
			case InventoryItemType.Story:
				break;
			case InventoryItemType.Mastery:
				DIContainerInfrastructure.GetClassAssetProvider().DestroyObject(item.ItemBalancing.AssetBaseId, m_IconInstanciated);
				break;
			case InventoryItemType.EventBattleItem:
			case InventoryItemType.EventCollectible:
				break;
			}
		}
	}

	// Head-gear prefabs are drawn larger than the weapon icons they share a row with.
	private const float SkinIconScale = 0.75f;
	private const float SkinIconYOffset = -30f;

	private void InstantiateAsset(IInventoryItemGameData item, Transform root)
	{
		if (m_IconInstanciated)
		{
			RemoveAsset(item);
		}
		ReleaseIconPreviewStage();
		if (item == null)
		{
			return;
		}
		switch (item.ItemBalancing.ItemType)
		{
		case InventoryItemType.Class:
			if (m_RecipeItem != null)
			{
				// Class on an upgrade blueprint: render it to a texture like equipment so the scroll panel clips it,
				// shrunk to fit the parchment and tinted like other blueprint pictures
				m_IconPreviewStage = EquipmentIconPreviewPool.Instance != null ? EquipmentIconPreviewPool.Instance.AcquireStage() : null;
				m_IconInstanciated = DIContainerInfrastructure.GetClassAssetProvider().InstantiateObject(item.ItemAssetName, m_IconPreviewStage != null ? m_IconPreviewStage.StageRoot : m_IconRoot, Vector3.zero, Quaternion.identity, false);
				var displayFactor = 1f;
				if (m_IconInstanciated != null)
				{
					displayFactor = UpgradeStatChangeView.FitIconToStage(m_IconInstanciated, m_IconPreviewStage != null ? m_IconPreviewStage.PreviewCamera : null);
					StartCoroutine(SetRecipeShader(m_IconInstanciated.GetComponentsInChildren<Renderer>()));
				}
				ShowIconPreviewTexture(m_IconPreviewStage, displayFactor);
				break;
			}
			m_IconInstanciated = DIContainerInfrastructure.GetClassAssetProvider().InstantiateObject(item.ItemAssetName, m_IconRoot, Vector3.zero, Quaternion.identity, false);
			break;
		case InventoryItemType.CraftingRecipes:
			m_IconInstanciated = DIContainerInfrastructure.GetEquipmentAssetProvider().InstantiateObject(item.ItemAssetName, m_IconRoot, Vector3.zero, Quaternion.identity, false);
			break;
		case InventoryItemType.MainHandEquipment:
			m_IconPreviewStage = EquipmentIconPreviewPool.Instance != null ? EquipmentIconPreviewPool.Instance.AcquireStage() : null;
			m_IconInstanciated = DIContainerInfrastructure.GetEquipmentAssetProvider().InstantiateObject(item.ItemAssetName, m_IconPreviewStage != null ? m_IconPreviewStage.StageRoot : m_IconRoot, Vector3.zero, Quaternion.identity, false);
			ShowIconPreviewTexture(m_IconPreviewStage);
			if (m_RecipeItem != null)
			{
				StartCoroutine(SetRecipeShader(m_IconInstanciated.GetComponentsInChildren<Renderer>()));
			}
			break;
		case InventoryItemType.OffHandEquipment:
			m_IconPreviewStage = EquipmentIconPreviewPool.Instance != null ? EquipmentIconPreviewPool.Instance.AcquireStage() : null;
			m_IconInstanciated = DIContainerInfrastructure.GetEquipmentAssetProvider().InstantiateObject(item.ItemAssetName, m_IconPreviewStage != null ? m_IconPreviewStage.StageRoot : m_IconRoot, Vector3.zero, Quaternion.identity, false);
			ShowIconPreviewTexture(m_IconPreviewStage);
			if (m_RecipeItem != null)
			{
				StartCoroutine(SetRecipeShader(m_IconInstanciated.GetComponentsInChildren<Renderer>()));
			}
			break;
		case InventoryItemType.Skin:
			m_IconInstanciated = DIContainerInfrastructure.GetClassAssetProvider().InstantiateObject(item.ItemBalancing.AssetBaseId, m_IconRoot, Vector3.zero, Quaternion.identity, false);
			break;
		case InventoryItemType.Mastery:
			var assetBaseId = item.ItemBalancing.AssetBaseId;
			var currentPlayer = DIContainerInfrastructure.GetCurrentPlayer();
			var associatedClass = (item as MasteryItemGameData).BalancingData.AssociatedClass;
			if (currentPlayer.Data.EquippedSkins.ContainsKey(associatedClass))
			{
				assetBaseId = DIContainerBalancing.Service.GetBalancingData<ClassSkinBalancingData>(currentPlayer.Data.EquippedSkins[associatedClass]).AssetBaseId;
			}
			m_IconInstanciated = DIContainerInfrastructure.GetClassAssetProvider().InstantiateObject(assetBaseId, m_IconRoot, Vector3.zero, Quaternion.identity, false);
			break;
		}
		
		if (m_IconInstanciated)
		{
			// Class pictures on upgrade blueprints were already shrunk to fit the preview stage
			if (!(item.ItemBalancing.ItemType == InventoryItemType.Class && m_RecipeItem != null))
			{
				m_IconInstanciated.transform.localScale = item.ItemBalancing.ItemType == InventoryItemType.Skin ? Vector3.one * SkinIconScale : Vector3.one;
			}
			if (item.ItemBalancing.ItemType == InventoryItemType.Skin)
			{
				m_IconInstanciated.transform.localPosition = new Vector3(0f, SkinIconYOffset, 0f);
			}
			if (m_IconPreviewStage != null)
			{
				NGUITools.SetLayer(m_IconInstanciated, IconPreviewLayer);
			}
		}
	}

	private Vector2 m_IconPreviewTextureSize;

	// displayFactor > 1 widens the display for a picture whose stage view was widened by the same factor.
	private void ShowIconPreviewTexture(EquipmentIconPreviewPool.PreviewStage stage, float displayFactor = 1f)
	{
		if (m_IconPreviewTexture == null && m_IconRoot != null)
		{
			m_IconPreviewTexture = m_IconRoot.GetComponentInChildren<UITexture>(true);
		}
		if (m_IconPreviewTexture == null)
		{
			return;
		}
		if (m_IconPreviewTextureSize == Vector2.zero)
		{
			m_IconPreviewTextureSize = new Vector2(m_IconPreviewTexture.width, m_IconPreviewTexture.height);
		}
		m_IconPreviewTexture.width = Mathf.RoundToInt(m_IconPreviewTextureSize.x * displayFactor);
		m_IconPreviewTexture.height = Mathf.RoundToInt(m_IconPreviewTextureSize.y * displayFactor);
		m_IconPreviewTexture.mainTexture = stage != null ? stage.RenderTexture : null;
		m_IconPreviewTexture.gameObject.SetActive(stage != null);
	}

	private void ReleaseIconPreviewStage()
	{
		if (m_IconPreviewStage != null && EquipmentIconPreviewPool.Instance != null)
		{
			EquipmentIconPreviewPool.Instance.ReleaseStage(m_IconPreviewStage);
		}
		m_IconPreviewStage = null;
		if (m_IconPreviewTexture != null)
		{
			m_IconPreviewTexture.gameObject.SetActive(false);
		}
	}

	private IEnumerator SetRecipeShader(Renderer[] renderers)
	{
		yield return new WaitForEndOfFrame();
		foreach (var rend in renderers)
		{
			if (rend.material.shader == DIContainerLogic.GetVisualEffectsBalancing().m_RecipeItemMaterial.shader)
				continue;

			rend.material = new Material(rend.sharedMaterial);
			rend.material.shader = DIContainerLogic.GetVisualEffectsBalancing().m_RecipeItemMaterial.shader;
			rend.material.color = DIContainerLogic.GetVisualEffectsBalancing().m_RecipeItemMaterial.color;
		}
	}

	public float ShowBoughtIndicator()
	{
		var gameObject = UnityEngine.Object.Instantiate(m_BuyIndicatorPrefab);
		UnityHelper.SetLayerRecusively(gameObject, LayerMask.NameToLayer("Interface"));
		gameObject.transform.localPosition = base.transform.position;
		UnityEngine.Object.Destroy(gameObject, gameObject.GetComponent<Animation>().clip.length);
		return gameObject.GetComponent<Animation>().clip.length;
	}

	public float ShowInstant()
	{
		if (m_IsHidden)
		{
			m_IsHidden = false;
			if (GetComponent<Animation>() != null && GetComponent<Animation>()["WorldShopOffer_Show"])
			{
				GetComponent<Animation>().Play("WorldShopOffer_Show");
				GetComponent<Animation>()["WorldShopOffer_Show"].time = GetComponent<Animation>()["WorldShopOffer_Show"].length;
				return 0f;
			}
		}
		return 0f;
	}

	public float Show()
	{
		if (m_IsHidden)
		{
			m_IsHidden = false;
			if (GetComponent<Animation>() != null && GetComponent<Animation>()["WorldShopOffer_Show"])
			{
				GetComponent<Animation>().Play("WorldShopOffer_Show");
				return GetComponent<Animation>()["WorldShopOffer_Show"].length;
			}
		}
		return 0f;
	}

	internal float Hide()
	{
		var result = 0f;
		if (!m_IsHidden)
		{
			if (GetComponent<Animation>() != null && GetComponent<Animation>()["WorldShopOffer_Hide"])
			{
				GetComponent<Animation>().Play("WorldShopOffer_Hide");
				result = GetComponent<Animation>()["WorldShopOffer_Hide"].length;
			}
			m_IsHidden = true;
		}
		return result;
	}

	public IEnumerator Disable(float delay)
	{
		yield return new WaitForSeconds(delay);
		base.gameObject.SetActive(false);
	}

	public float HideInstant()
	{
		var result = 0f;
		if (!m_IsHidden)
		{
			if (GetComponent<Animation>() != null && GetComponent<Animation>()["WorldShopOffer_Hide"])
			{
				GetComponent<Animation>().Play("WorldShopOffer_Hide");
				GetComponent<Animation>()["WorldShopOffer_Hide"].time = GetComponent<Animation>()["WorldShopOffer_Hide"].length;
			}
			m_IsHidden = true;
		}
		return result;
	}
}
