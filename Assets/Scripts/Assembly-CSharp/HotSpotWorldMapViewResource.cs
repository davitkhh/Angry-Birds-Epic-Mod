using System;
using System.Collections;
using System.Runtime.CompilerServices;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

public class HotSpotWorldMapViewResource : HotSpotWorldMapViewBase
{
	private bool m_resourcesAvaible;

	private int m_resourceAmount;

	private bool m_harvestingStarted;

	[SerializeField]
	private GameObject[] m_resourceObjects;

	[SerializeField]
	private Vector3 m_SpawnOffset = new Vector3(0f, 0f, -20f);

	public BattleLootVisualization m_ResourcePrefab;

	[SerializeField]
	private GameObject[] m_activateObjects;

	[method: MethodImpl(32)]
	public event Action OnDespawnResource;

	public override void ActivateAsset(bool activate)
	{
		var activateObjects = m_activateObjects;
		foreach (var gameObject in activateObjects)
		{
			gameObject.SetActive(true);
		}
	}

	public override void Initialize()
	{
		if (IsCompleted())
		{
			var resourceObjects = m_resourceObjects;
			foreach (var gameObject in resourceObjects)
			{
				gameObject.SetActive(false);
			}
		}
	}

	// Off the world map: the node lives in the shop menu of the merchant it follows (MerchantStall, WorldMapShopMenuUI),
	// where it is shown and tapped like on the map. It still unlocks, respawns and harvests as a normal node.
	public bool m_HiddenOnMap;

	private Transform m_MapParent;

	private Vector3 m_MapLocalPosition;

	private Vector3 m_MapLocalScale;

	private int m_MapLayer;

	private bool m_InMenu;

	private void SetShownOnMap(bool shown)
	{
		if (!m_HiddenOnMap)
		{
			return;
		}
		shown &= m_InMenu;
		foreach (Transform child in base.transform)
		{
			child.gameObject.SetActive(shown);
		}
		foreach (var collider in GetComponents<Collider>())
		{
			collider.enabled = shown;
		}
	}

	// Moves the node into a menu: under parent (so it moves with it), at worldPosition, drawn by the interface camera
	// (layer) above the menu's NGUI widgets (sortingOrder).
	public void ShowInMenu(Transform parent, Vector3 worldPosition, float scale, int layer, int sortingOrder)
	{
		if (!m_InMenu)
		{
			m_MapParent = base.transform.parent;
			m_MapLocalPosition = base.transform.localPosition;
			m_MapLocalScale = base.transform.localScale;
			m_MapLayer = base.gameObject.layer;
		}
		m_InMenu = true;
		base.transform.SetParent(parent, true);
		base.transform.position = worldPosition;
		base.transform.localScale = m_MapLocalScale * scale;
		SetLayer(layer, sortingOrder);
		SetShownOnMap(true);
		if (!IsResourceAvaible())
		{
			foreach (var resource in m_resourceObjects)
			{
				resource.SetActive(false);
			}
		}
	}

	public void ReturnToMap()
	{
		if (!m_InMenu)
		{
			return;
		}
		m_InMenu = false;
		SetShownOnMap(false);
		base.transform.SetParent(m_MapParent, false);
		base.transform.localPosition = m_MapLocalPosition;
		base.transform.localScale = m_MapLocalScale;
		SetLayer(m_MapLayer, 0);
	}

	private void SetLayer(int layer, int sortingOrder)
	{
		foreach (var t in GetComponentsInChildren<Transform>(true))
		{
			t.gameObject.layer = layer;
		}
		foreach (var r in GetComponentsInChildren<Renderer>(true))
		{
			r.sortingOrder = sortingOrder;
		}
	}

	protected override void InitialSetupHotspot()
	{
		base.InitialSetupHotspot();
		SetShownOnMap(base.Model.Data.UnlockState > HotspotUnlockState.Hidden);
		if (base.Model.Data.UnlockState != HotspotUnlockState.Hidden && base.Model.Data.UnlockState != 0)
		{
			DIContainerLogic.GetResourceNodeManager().AddResourceSpot(this);
			m_resourceAmount = ResourceNodeSupply.Refresh(DIContainerInfrastructure.GetCurrentPlayer(), base.Model, NodeType);
		}
		if (base.Model.Data.UnlockState == HotspotUnlockState.Active || base.Model.Data.UnlockState == HotspotUnlockState.ResolvedNew)
		{
			PlayActiveIdleAnimation();
		}
	}

	public void OnDestroy()
	{
		if (DIContainerLogic.GetResourceNodeManager() != null)
		{
			DIContainerLogic.GetResourceNodeManager().RemoveResourceSpot(this);
		}
	}

	public override bool IsCompleted()
	{
		return base.Model.Data.UnlockState == HotspotUnlockState.Resolved;
	}

	// Material type of the node for its stars-per-refill setting (ResourceNodeSupply.StarsPerRefill): the animation
	// prefix without the trailing "_" (Rock, TreeStump, Seashell).
	public string NodeType
	{
		get { return string.IsNullOrEmpty(AnimationPrefix) ? string.Empty : AnimationPrefix.TrimEnd('_'); }
	}

	public bool IsResourceAvaible()
	{
		return base.Model.Data.UnlockState == HotspotUnlockState.Active || base.Model.Data.UnlockState == HotspotUnlockState.ResolvedNew;
	}

	public override void HandleMouseButtonUp(bool directMove = false)
	{
		DebugLog.Log("ResourceNode " + base.gameObject.name + " has state " + base.Model.Data.UnlockState);
		if (m_resourcesAvaible)
		{
			ShowContentView();
		}
	}

	public override void ShowContentView()
	{
		Requirement firstFailedReq = null;
		if (!DIContainerLogic.WorldMapService.CanTravelToHotspot(DIContainerInfrastructure.GetCurrentPlayer(), base.Model, out firstFailedReq))
		{
			if (firstFailedReq.RequirementType == RequirementType.HaveItem)
			{
				var inventoryItemGameData = DIContainerLogic.InventoryService.GenerateNewInventoryItemGameData(1, 1, firstFailedReq.NameId, (int)firstFailedReq.Value);
				UIAtlas uIAtlas = null;
				if (DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().ContainsAsset(inventoryItemGameData.ItemIconAtlasName))
				{
					var gameObject = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().GetObject(inventoryItemGameData.ItemIconAtlasName) as GameObject;
					uIAtlas = gameObject.GetComponent<UIAtlas>();
				}
			}
			return;
		}
		// One tap = one harvest from the saved supply (ResourceNodeSupply); the node empties on the last one and stays
		// empty until enough stars are earned for a refill.
		if (m_resourceAmount <= 0)
		{
			DespawnResource();
			return;
		}
		m_resourceAmount = ResourceNodeSupply.Consume(DIContainerInfrastructure.GetCurrentPlayer(), base.Model, NodeType);
		base.ShowContentView();
		DIContainerLogic.GetResourceNodeManager().SpawnResource(base.gameObject, m_SpawnOffset);
		PlayUsedAnimation();
		if (m_resourceAmount <= 0)
		{
			DespawnResource();
		}
	}

	// Pays out refills earned by stars since the last check. Shows the resources again if the node was empty; a node
	// with supply left just gets more. Returns true if the supply grew (caller saves).
	public bool ApplyStarRefills()
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		if (!ResourceNodeSupply.IsUnlocked(base.Model))
		{
			return false;
		}
		var supply = ResourceNodeSupply.Refresh(player, base.Model, NodeType);
		if (supply <= m_resourceAmount && (supply <= 0 || m_resourcesAvaible))
		{
			m_resourceAmount = supply;
			return false;
		}
		var wasEmpty = !m_resourcesAvaible;
		m_resourceAmount = supply;
		if (wasEmpty)
		{
			DebugLog.Log(base.gameObject.name + " refilled by stars");
			Respawn();
		}
		return true;
	}

	public void DespawnResource()
	{
		m_resourceAmount = 0;
		m_resourcesAvaible = false;
		PlayInactiveResourceAnimation();
		var resourceObjects = m_resourceObjects;
		foreach (var gameObject in resourceObjects)
		{
			gameObject.SetActive(false);
		}
		if (this.OnDespawnResource != null)
		{
			this.OnDespawnResource();
		}
	}

	public void PlayInactiveResourceAnimation()
	{
		if (GetComponent<Animation>()[AnimationPrefix + "Idle_Active"])
		{
			GetComponent<Animation>().Stop(AnimationPrefix + "Idle_Active");
		}
		StartCoroutine(PlayInactiveAnimationWhenUsedDone());
	}

	private IEnumerator PlayInactiveAnimationWhenUsedDone()
	{
		while (GetComponent<Animation>().isPlaying)
		{
			yield return new WaitForEndOfFrame();
		}
		if (GetComponent<Animation>()[AnimationPrefix + "Idle_Inactive"])
		{
			GetComponent<Animation>().Play(AnimationPrefix + "Idle_Inactive");
		}
	}

	public override void SynchBalancing()
	{
		base.SynchBalancing();
		m_active = false;
	}

	public override IEnumerator ActivateFollowUpStagesAsync(HotSpotWorldMapViewBase parentHotSpot, HotSpotWorldMapViewBase activateTo, bool instant = false)
	{
		DIContainerLogic.GetResourceNodeManager().AddResourceSpot(this);
		if (!DIContainerLogic.WorldMapService.IsHotspotVisible(DIContainerInfrastructure.GetCurrentPlayer(), base.Model))
		{
			yield break;
		}
		if (!m_active && !m_HiddenOnMap && m_litePath != null && !m_litePath.gameObject.activeInHierarchy)
		{
			m_litePath.gameObject.SetActive(true);
			var timer = 0f;
			if (!instant && !instant && m_litePath.GetComponent<Animation>() != null && m_litePath.GetComponent<Animation>()["Path_Show"] != null)
			{
				m_litePath.GetComponent<Animation>().Play("Path_Show");
				yield return new WaitForSeconds(m_litePath.GetComponent<Animation>()["Path_Show"].length);
			}
		}
		if (!m_active)
		{
			SetShownOnMap(true);
			var activateObjects = m_activateObjects;
			foreach (var go in activateObjects)
			{
				go.SetActive(true);
			}
			if (!instant && GetComponent<Animation>() && GetComponent<Animation>()["SetActive"] != null)
			{
				GetComponent<Animation>().Play("SetActive");
				yield return new WaitForSeconds(GetComponent<Animation>()["SetActive"].length);
			}
			DIContainerLogic.WorldMapService.UnlockHotSpot(DIContainerInfrastructure.GetCurrentPlayer(), base.Model);
		}
		else if (base.Model.Data.UnlockState == HotspotUnlockState.ResolvedNew || base.Model.Data.UnlockState == HotspotUnlockState.ResolvedBetter)
		{
			DIContainerLogic.WorldMapService.UnlockHotSpot(DIContainerInfrastructure.GetCurrentPlayer(), base.Model);
		}
		m_active = true;
		if (IsCompleted() || m_active)
		{
			foreach (var hotspot in m_outgoingHotSpots)
			{
				if (hotspot == parentHotSpot || (activateTo != null && hotspot == activateTo))
					continue;
				
				yield return StartCoroutine(hotspot.ActivateFollowUpStagesAsync(this, activateTo, instant));
			}
		}
		if (base.Model.Data.UnlockState == HotspotUnlockState.ResolvedNew || base.Model.Data.UnlockState == HotspotUnlockState.Active)
		{
			Respawn();
			DIContainerInfrastructure.TutorialMgr.ShowTutorialGuideIfNecessary("resourceSpot_respawn", base.Model.BalancingData.NameId);
		}
	}

	protected override void HotSpotChanged(bool startUpSetting)
	{
	}

	// Shows the node's resources with its saved supply (ResourceNodeSupply). Does nothing while the node is empty.
	public void Respawn()
	{
		m_resourceAmount = ResourceNodeSupply.Refresh(DIContainerInfrastructure.GetCurrentPlayer(), base.Model, NodeType);
		if (m_resourceAmount <= 0)
		{
			return;
		}
		var resourceObjects = m_resourceObjects;
		foreach (var gameObject in resourceObjects)
		{
			gameObject.SetActive(true);
		}
		m_resourcesAvaible = true;
		m_harvestingStarted = false;
		DebugLog.Log("node respawned");
		PlayActiveAnimation();
	}
}
