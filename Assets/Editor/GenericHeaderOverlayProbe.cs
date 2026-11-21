using System.IO;
using System.Reflection;
using System.Text;
using ABH.GameDatas;
using UnityEditor;
using UnityEngine;

// Play-mode check of an inventory item's tap-and-hold overlay: write an item NameId (default: the Brilliant
// Annotations upgrade blueprint) to Temp/GenericHeaderOverlayProbe.request while the game runs. It shows the item's
// overlay at screen left, dumps the header overlay's labels to Temp/GenericHeaderOverlayProbe.log and captures
// Temp/GenericHeaderOverlayProbe.png, then hides it.
public static class GenericHeaderOverlayProbe
{
	private const string RequestFile = "Temp/GenericHeaderOverlayProbe.request";

	private const string LogFile = "Temp/GenericHeaderOverlayProbe.log";

	private static double s_CaptureAt = -1;

	private static double s_HideAt = -1;

	private static StringBuilder s_Log;

	private static GameObject s_Anchor;

	// Set when the probe entered play mode itself; it leaves play mode again after the capture
	private const string AutoPlayKey = "GenericHeaderOverlayProbe.AutoPlay";

	private static double s_ReadySince = -1;

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
			if (File.Exists(RequestFile) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
			{
				SessionState.SetBool(AutoPlayKey, true);
				EditorApplication.EnterPlaymode();
			}
			return;
		}
		CoreStateMgr core = null;
		try
		{
			core = DIContainerInfrastructure.GetCoreStateMgr();
		}
		catch (System.Exception)
		{
		}
		var mgr = core != null ? core.m_InfoOverlays : null;
		if (mgr == null || mgr.m_GenericOverlayWithHeader == null)
		{
			s_ReadySince = -1;
			return;
		}
		if (s_ReadySince < 0)
		{
			s_ReadySince = EditorApplication.timeSinceStartup;
		}
		// Let the boot settle (balancing, player) before showing anything
		if (EditorApplication.timeSinceStartup - s_ReadySince < 8.0)
		{
			return;
		}
		if (s_CaptureAt > 0 && EditorApplication.timeSinceStartup >= s_CaptureAt)
		{
			s_CaptureAt = -1;
			Dump(mgr);
			ScreenCapture.CaptureScreenshot("Temp/GenericHeaderOverlayProbe.png");
			s_HideAt = EditorApplication.timeSinceStartup + 1.0;
			return;
		}
		if (s_HideAt > 0 && EditorApplication.timeSinceStartup >= s_HideAt)
		{
			s_HideAt = -1;
			mgr.HideAllTooltips();
			if (s_Anchor != null)
			{
				Object.Destroy(s_Anchor);
			}
			File.WriteAllText(LogFile, s_Log.ToString());
			if (SessionState.GetBool(AutoPlayKey, false))
			{
				SessionState.SetBool(AutoPlayKey, false);
				EditorApplication.ExitPlaymode();
			}
			return;
		}
		if (!File.Exists(RequestFile))
		{
			return;
		}
		var request = File.ReadAllText(RequestFile).Trim();
		File.Delete(RequestFile);
		s_Log = new StringBuilder();
		var nameId = string.IsNullOrEmpty(request) ? "upgrade_offhand_yellow_book_start" : request;
		var item = new CraftingRecipeGameData(nameId);
		var camera = DIContainerInfrastructure.GetCoreStateMgr().m_InterfaceCamera;
		s_Anchor = new GameObject("GenericHeaderOverlayProbeAnchor");
		s_Anchor.transform.position = camera.ViewportToWorldPoint(new Vector3(0.3f, 0.5f, 1f));
		var show = typeof(InfoOverlayMgr).GetMethod("ShowItemOverlay", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Transform), typeof(IInventoryItemGameData), typeof(bool), typeof(bool), typeof(int) }, null);
		show.Invoke(mgr, new object[] { s_Anchor.transform, item, true, false, 0 });
		s_Log.AppendLine("shown " + nameId);
		s_CaptureAt = EditorApplication.timeSinceStartup + 1.0;
	}

	private static void Dump(InfoOverlayMgr mgr)
	{
		var overlay = mgr.m_GenericOverlayWithHeader;
		s_Log.AppendLine("container " + overlay.m_ContainerControl.m_Size + " @" + overlay.m_ContainerControl.transform.localPosition);
		foreach (var label in overlay.GetComponentsInChildren<UILabel>(false))
		{
			s_Log.AppendLine(label.name + " parent " + label.transform.parent.name + " local " + label.transform.localPosition + " pivot " + label.pivot + " overflow " + label.overflowMethod + " w " + label.width + " h " + label.height + " printed " + label.printedSize + " text [" + label.text.Replace("\n", "|") + "]");
		}
		var rows = overlay.m_TextBox.Label.transform.Find("UpgradeStatChanges");
		s_Log.AppendLine("rows " + (rows != null ? rows.localPosition.ToString() : "none"));
	}
}
