using System.IO;
using UnityEditor;
using UnityEngine;

// Lets Combatant Forge (Tools/CombatantForge, publish.encode) encode the balancing container and the English text in
// the open editor when the JSON watcher missed its write: it drops Temp/ForgeEncode.request ("balancing" and/or
// "loca" lines) and this runs the same menu commands, without the editor window needing focus.
public static class ForgeEncodeRequest
{
	private const string RequestFile = "Temp/ForgeEncode.request";

	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating)
		{
			return;
		}
		string what;
		try
		{
			what = File.ReadAllText(RequestFile);
			File.Delete(RequestFile);
		}
		catch (IOException)
		{
			return;  // still being written
		}
		if (what.Contains("balancing"))
		{
			BalancingDataContainerHelper.CreateNonEventContainerFromJsons();
		}
		if (what.Contains("loca"))
		{
			BalancingDataContainerHelper.CreateLocaContainersFromJsons();
		}
	}
}
