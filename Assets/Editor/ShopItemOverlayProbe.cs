using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Play-mode check of the shop tap-and-hold overlay: with a shop window open, touch Temp/ShopItemOverlayProbe.request
// (optional content: tile index). It calls ShowTooltip on that tile, waits for the enter animation, writes the overlay's
// state to Temp/ShopItemOverlayProbe.log and a screenshot to Temp/ShopItemOverlayProbe.png, then hides the tooltip.
public static class ShopItemOverlayProbe
{
	private const string RequestFile = "Temp/ShopItemOverlayProbe.request";

	private const string LogFile = "Temp/ShopItemOverlayProbe.log";

	private static double s_CaptureAt = -1;

	private static double s_HideAt = -1;

	private static StringBuilder s_Log;

	[InitializeOnLoadMethod]
	private static void Init()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!EditorApplication.isPlaying)
		{
			return;
		}
		if (s_CaptureAt > 0 && EditorApplication.timeSinceStartup >= s_CaptureAt)
		{
			s_CaptureAt = -1;
			Dump();
			ScreenCapture.CaptureScreenshot("Temp/ShopItemOverlayProbe.png");
			s_HideAt = EditorApplication.timeSinceStartup + 1.0;
			return;
		}
		if (s_HideAt > 0 && EditorApplication.timeSinceStartup >= s_HideAt)
		{
			s_HideAt = -1;
			DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.HideAllTooltips();
			File.WriteAllText(LogFile, s_Log.ToString());
			return;
		}
		if (!File.Exists(RequestFile))
		{
			return;
		}
		var request = File.ReadAllText(RequestFile).Trim();
		File.Delete(RequestFile);
		s_Log = new StringBuilder();
		var tiles = Object.FindObjectsByType<ShopOfferBlindWorldmap>(FindObjectsSortMode.None).Where(t => t.gameObject.activeInHierarchy && t.GetModel() != null).ToList();
		s_Log.AppendLine("tiles: " + string.Join(", ", tiles.Select(t => t.name + "=" + t.GetModel().NameId).ToArray()));
		if (request == "hit")
		{
			// What a touch on each tile's icon / centre reaches, nearest first (TouchInputHandler takes the nearest)
			var camera = DIContainerInfrastructure.GetCoreStateMgr().m_InterfaceCamera;
			foreach (var hitTile in tiles)
			{
				var iconRoot = typeof(ShopOfferBlindWorldmap).GetField("m_IconRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(hitTile) as Transform;
				foreach (var point in new[] { iconRoot != null ? iconRoot.position : hitTile.transform.position, hitTile.transform.position })
				{
					var ray = camera.ScreenPointToRay(camera.WorldToScreenPoint(point));
					var hits = Physics.RaycastAll(ray, 10000f).OrderBy(h => h.distance).Take(4);
					s_Log.AppendLine(hitTile.name + " @" + point + ": " + string.Join(" | ", hits.Select(h => Path(h.collider.transform) + " (layer " + LayerMask.LayerToName(h.collider.gameObject.layer) + ", d " + h.distance.ToString("0.0") + ")").ToArray()));
					// TouchInputHandler.EvaluateMouse: secondary handlers from last to first, the first index with a hit wins
					var primary = DIContainerInfrastructure.GetCoreStateMgr().GetComponent<TouchInputHandler>();
					var handlers = primary != null ? primary.m_SecondaryInputHandlers : new List<TouchInputHandler>();
					Transform target = null;
					for (var h = handlers.Count - 1; h >= 0; h--)
					{
						RaycastHit hit;
						if (handlers[h] != null && handlers[h].GetRayCastHit(camera.WorldToScreenPoint(point), out hit))
						{
							target = hit.transform;
							s_Log.AppendLine("    handler " + h + " " + handlers[h].name + " hits " + Path(hit.transform));
						}
					}
					s_Log.AppendLine("    => touch goes to " + (target != null ? Path(target) : "nothing"));
				}
			}
			File.WriteAllText(LogFile, s_Log.ToString());
			return;
		}
		int index;
		if (!int.TryParse(request, out index))
		{
			index = 0;
		}
		if (tiles.Count == 0)
		{
			File.WriteAllText(LogFile, s_Log.ToString());
			return;
		}
		var tile = tiles[Mathf.Clamp(index, 0, tiles.Count - 1)];
		s_Log.AppendLine("showing " + tile.name + " " + tile.GetModel().NameId);
		try
		{
			tile.ShowTooltip();
		}
		catch (System.Exception e)
		{
			s_Log.AppendLine("EXCEPTION " + e);
		}
		s_CaptureAt = EditorApplication.timeSinceStartup + 1.0;
	}

	private static string Path(Transform t)
	{
		var path = t.name;
		for (var p = t.parent; p != null && path.Count(c => c == '/') < 4; p = p.parent)
		{
			path = p.name + "/" + path;
		}
		return path;
	}

	private static void Dump()
	{
		var mgr = DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays;
		foreach (Transform overlay in mgr.m_EquipmentOverlay.transform.parent)
		{
			if (overlay.gameObject.activeSelf)
			{
				s_Log.AppendLine("active overlay: " + overlay.name);
			}
		}
		var shop = mgr.GetComponentsInChildren<ShopItemInfoOverlay>(true).FirstOrDefault();
		if (shop == null)
		{
			s_Log.AppendLine("no ShopItemInfoOverlay");
			return;
		}
		var t = shop.transform;
		s_Log.AppendLine("shop overlay active " + shop.gameObject.activeInHierarchy + " layer " + shop.gameObject.layer + " pos " + t.position + " local " + t.localPosition + " size " + shop.Size);
		var animation = t.Find("Animation");
		s_Log.AppendLine("Animation scale " + animation.localScale + " Bubble local " + t.Find("Animation/Bubble").localPosition);
		var widgets = t.GetComponentsInChildren<UIWidget>(true);
		s_Log.AppendLine("widgets " + widgets.Length + " visible " + widgets.Count(w => w.isVisible) + " enabled " + widgets.Count(w => w.enabled && w.gameObject.activeInHierarchy));
		foreach (var w in widgets.Take(30))
		{
			s_Log.AppendLine("  " + w.name + " " + w.GetType().Name + " panel " + (w.panel != null ? w.panel.name + " a" + w.panel.alpha : "null") + " alpha " + w.finalAlpha + " depth " + w.depth + " size " + w.width + "x" + w.height + " world " + w.transform.position + (w is UILabel ? " '" + ((UILabel)w).text + "'" : string.Empty));
		}
		var camera = NGUITools.FindCameraForLayer(shop.gameObject.layer);
		s_Log.AppendLine("camera " + (camera != null ? camera.name + " " + camera.transform.position + " ortho " + camera.orthographicSize : "null"));
	}
}
