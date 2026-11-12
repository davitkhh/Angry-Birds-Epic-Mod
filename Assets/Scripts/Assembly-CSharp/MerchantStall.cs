using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.Shared.Generic;
using UnityEngine;

// Merchant stall extras. Several merchants exist; McCool's own touches (his peek over the shop board, his stall pig
// leaving) only apply to his stall. A resource node after a merchant that is flagged m_HiddenOnMap lives in that
// merchant's shop menu instead of on the map (WorldMapShopMenuUI).
public static class MerchantStall
{
	public const string McCoolMerchantNameId = "hotspot_008_02_merchant";

	public const string McCoolGateNameId = "hotspot_008_05_battleground";

	public const string McCoolResourceNodeNameId = "hotspot_008_03_resourcenode";

	// Toggle for McCool's merchant gate (Hotspot_008_05):
	// false = the scene's layout: 008 -> merchant -> {008_05 (a normal level), resource node in the shop menu}.
	// true  = 008 -> 008_05 -> merchant -> resource node. 008_05 moves to where the road to the stall splits (the end of
	//         Path_008_05) and blocks the merchant; once beaten it leaves the map and is replayed from McCool's shop menu.
	//         The resource node stands on the map at the end of Path_008_01, where 008_05 stands otherwise.
	// Applied when the world map loads (ApplyLayout); switch before entering the world map.
	public static bool GateLeavesMap = false;

	public static bool IsMcCoolMerchant(HotSpotWorldMapViewBase hotspot)
	{
		return hotspot != null && hotspot.m_nameId == McCoolMerchantNameId;
	}

	// The merchant's menu resource node, once unlocked.
	public static HotSpotWorldMapViewResource GetMenuResourceNode(HotSpotWorldMapViewBase merchant)
	{
		if (merchant == null || merchant.m_outgoingHotSpots == null)
		{
			return null;
		}
		return merchant.m_outgoingHotSpots.OfType<HotSpotWorldMapViewResource>().FirstOrDefault(r => r != null && r.m_HiddenOnMap && r.Model != null && r.Model.Data.UnlockState > HotspotUnlockState.Hidden);
	}

	// GateLeavesMap: the gate is beaten and done celebrating (the first win's ResolvedNew still plays on the map).
	public static bool IsGoneFromMap(HotspotGameData hotspot)
	{
		return GateLeavesMap && hotspot != null && hotspot.BalancingData.NameId == McCoolGateNameId && (hotspot.Data.UnlockState == HotspotUnlockState.Resolved || hotspot.Data.UnlockState == HotspotUnlockState.ResolvedBetter);
	}

	// GateLeavesMap: the merchant's gate battle for its shop menu, once beaten; else null.
	public static HotspotGameData GetBeatenGate(HotSpotWorldMapViewBase merchant)
	{
		HotspotGameData gate;
		if (!GateLeavesMap || !IsMcCoolMerchant(merchant) || !DIContainerInfrastructure.GetCurrentPlayer().WorldGameData.HotspotGameDatas.TryGetValue(McCoolGateNameId, out gate) || gate == null)
		{
			return null;
		}
		return gate.IsCompleted() ? gate : null;
	}

	// WorldMapStateMgr.Awake, before the hotspots sync and initialize. The scene holds the GateLeavesMap = false layout.
	public static void ApplyLayout(HotSpotWorldMapViewBase[] hotspots)
	{
		if (!GateLeavesMap)
		{
			return;
		}
		var byId = new Dictionary<string, HotSpotWorldMapViewBase>();
		foreach (var hotspot in hotspots)
		{
			if (hotspot != null && !string.IsNullOrEmpty(hotspot.m_nameId))
			{
				byId[hotspot.m_nameId] = hotspot;
			}
		}
		HotSpotWorldMapViewBase merchant, gate, node;
		if (!byId.TryGetValue(McCoolMerchantNameId, out merchant) || !byId.TryGetValue(McCoolGateNameId, out gate) || !byId.TryGetValue(McCoolResourceNodeNameId, out node))
		{
			DebugLog.Error("MerchantStall: McCool's merchant, gate or resource node is missing; GateLeavesMap not applied");
			return;
		}
		var before = merchant.m_previousHotSpot;
		var roadToStall = merchant.m_litePath;
		var stallToNode = gate.m_litePath;
		if (before == null || roadToStall == null || stallToNode == null || gate.m_previousHotSpot != merchant)
		{
			DebugLog.Error("MerchantStall: unexpected scene links around McCool's merchant; GateLeavesMap not applied");
			return;
		}

		// The gate stands where the road to the stall meets the stall -> node road.
		var split = roadToStall.GetPositionWorldSpace(1f);
		gate.transform.position = new Vector3(split.x, split.y, gate.transform.position.z);

		// The rest of the way to the stall: the stall -> node road from the split back to its start. Never drawn (that
		// road is the node's), only walked.
		var stallPath = BuildWalkPath(stallToNode, split);

		// before -> gate -> merchant -> node.
		Replace(before.m_outgoingHotSpots, merchant, gate);
		gate.m_previousHotSpot = before;
		gate.m_litePath = roadToStall;
		gate.m_outgoingHotSpots = new List<HotSpotWorldMapViewBase> { merchant };
		merchant.m_previousHotSpot = gate;
		merchant.m_litePath = stallPath;
		merchant.m_outgoingHotSpots.Remove(gate);
		node.m_previousHotSpot = merchant;
		node.m_litePath = stallToNode;
		var resource = node as HotSpotWorldMapViewResource;
		if (resource != null)
		{
			resource.m_HiddenOnMap = false;
		}
	}

	private static void Replace(List<HotSpotWorldMapViewBase> list, HotSpotWorldMapViewBase from, HotSpotWorldMapViewBase to)
	{
		var index = list.IndexOf(from);
		if (index >= 0)
		{
			list[index] = to;
		}
		else
		{
			list.Add(to);
		}
	}

	private static RageSplineLite BuildWalkPath(RageSplineLite source, Vector3 from)
	{
		const int samples = 200;
		var best = 0f;
		var bestDistance = float.MaxValue;
		for (var i = 0; i <= samples; i++)
		{
			var t = i / (float)samples;
			var distance = Vector2.Distance(source.GetPositionWorldSpace(t), from);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = t;
			}
		}
		var path = new GameObject("Path_008_02_Walk");
		path.transform.SetParent(source.transform.parent, false);
		const int steps = 12;
		var points = new Vector3[steps + 1];
		for (var i = 0; i <= steps; i++)
		{
			var world = source.GetPositionWorldSpace(best * (1f - i / (float)steps));
			points[i] = path.transform.InverseTransformPoint(world);
		}
		var lite = path.AddComponent<RageSplineLite>();
		lite.SetPolyline(points);
		path.SetActive(false);
		return lite;
	}
}
