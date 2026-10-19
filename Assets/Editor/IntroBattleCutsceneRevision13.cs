using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SmoothMoves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Thirteenth pass on the intro battle's raid (BS_00_PigsRaidCamp_AfterLoss), after IntroBattleCutsceneRevision12,
// reusing its scene objects. The raid is rebuilt from the same timeline with:
// - Red, once up, looking back at Chuck (left, surprised, back right 0.5 s later); BattleMgr turns those marked nodes
//   (param "IntroRedLookBack") into Rage facing right when Chuck already went down in wave 0.
// - Red's rogue winding up only once its run has stopped (the fight and everything after Chuck's carry 0.45 s later).
// - The nest rogues at full size, shrinking to half by the nest (ExtraDepthScale); Wiz facing right at the nest.
// - The guard setting off 0.3 s later along the middle; the carriers playing Affected as they pick Chuck up, Chuck
//   lifted a little to the right, and the carry dragging then stopping in time with Move_Carry (MoveToObject
//   customInt3 1).
// - Chuck's dropped feather (main hand) lying between the waves and the wet sand; the fade starting with Red's Rage.
// One-shot: refuses once MinionPigR1 has an ExtraDepthScale. Menu item, or Temp/IntroBattleCutsceneRevision13.request.
public static class IntroBattleCutsceneRevision13
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision13.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision13.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string BattleMinionPrefab = "Assets/Resources/Prefabs/4_Characters/8_Base/MinionPig.prefab";

	private const string WizPrefab = "Assets/Resources/Prefabs/4_Characters/2_Pigs/WizPig.prefab";

	private const string BubblePath = "Assets/Resources/Prefabs/0_Generic/1_Interface/Hud_Elements/Bubble_StolenEggs.prefab";

	private const string CloudSource = "Assets/Resources/Prefabs/1_WorldMap/2_Props/StorySequences/Prop_MagicCloud.prefab";

	private const string CloudPath = "Assets/Resources/Prefabs/0_Generic/3_Props/Prop_MagicCloud_Battle.prefab";

	private const string CloudEggsClipPath = "Assets/AnimationClip/MagicCloud_ReceiveEggs.anim";

	private const string NestEggsClipPath = "Assets/AnimationClip/IntroNest_EggsTaken.anim";

	private const string MagicFxPath = "Assets/Prefab/SkillFX_Generic_Support_Magic.prefab";

	private const string NestEmptyTexture = "Assets/Texture2D/Intro_NestEmpty.png";

	private const string NestEggsTexture = "Assets/Texture2D/Intro_NestEggs.png";

	// Where the nest patch goes (world), from IntroNestRender's render of the nest (8 pixels per unit).
	private static readonly Vector3 NestCenter = new Vector3(-249.6875f, 224.75f, 0f);

	private const float NestPixelsPerUnit = 8f;

	private const float SwarmSpeed = 343f;

	private const float CarrySpeed = SwarmSpeed * 400f / 500f;

	// Far pigs run slower in world units, as they are drawn smaller (ScaleMgr: about two thirds at the nest's depth).
	private const float FarSwarmSpeed = 240f;

	// World map characters are drawn at 0.4 (m_WorldBirdScale); the cloud and FX keep their size relative to Wiz.
	private const float WorldMapScale = 0.4f;

	private const float RedRunTime = 1.1f;

	// The pigs at the nest are drawn at half their size, as they are far away.
	private const float NestPigScale = 0.5f;

	// Between the waves (Prop_Ocean_Front 595, Prop_Ocean 596) and the wet sand under them (Ocean_Shadow 597).
	private const float FeatherZ = 596.5f;

	// The camp prop is drawn at world z 594 and the blue rock at 596: what goes in front of the camp stays nearer.
	private const float FrontOfCampZ = 585f;

	private const float BehindRockZ = 598f;

	private static readonly Vector3 StickRunEnd = new Vector3(-929.3036f, 0f, 91.38677f);

	private static readonly Vector3 RogueRunEnd = new Vector3(-851.5499f, 0f, 386.5454f);

	private static readonly StringBuilder s_Log = new StringBuilder();

	private static Quaternion s_Plane;

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= PollRequest;
		EditorApplication.update += PollRequest;
	}

	private static void PollRequest()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		File.Delete(RequestFile);
		Revise();
	}

	[MenuItem("Tools/Battle/Revise Intro Battle Cutscenes (13)")]
	public static void Revise()
	{
		s_Log.Length = 0;
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			var opened = false;
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
				opened = true;
			}
			var hadUnsavedChanges = scene.isDirty;
			if (ReviseInternal(scene))
			{
				EditorSceneManager.MarkSceneDirty(scene);
				if (hadUnsavedChanges)
				{
					Log("not saved: the scene already had unsaved changes, save it in the editor");
				}
				else
				{
					EditorSceneManager.SaveScene(scene);
					Log("saved " + ScenePath);
				}
			}
			if (opened && !scene.isDirty)
			{
				EditorSceneManager.CloseScene(scene, true);
			}
		}
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[IntroBattleCutsceneRevision13]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private class TimedNode
	{
		public float Time;

		public ActionNode Node;
	}

	// A point in the reference screenshot (pixels from its top left) as a world point on the ground there; its depth is
	// the battle plane's (world z = y + 384), at most maxZ.
	private static Vector3 Ground(float sx, float sy, float maxZ = FrontOfCampZ)
	{
		var x = -733.7f + 1.3294f * sx;
		var y = 357f - 1.3761f * sy;
		return new Vector3(x, y, Mathf.Min(y + 384f, maxZ));
	}

	private static bool ReviseInternal(Scene scene)
	{
		var sequences = Find(scene, SequencesPath);
		var lost = sequences.Find("BS_00_PigsRaidCamp_AfterLoss").GetComponent<ActionTree>();
		var midground = Find(scene, "Battleground_BirdCamp_01/1_Scenery/2_Landscape/3_Midground");
		var background = Find(scene, "Battleground_BirdCamp_01/1_Scenery/2_Landscape/1_Background");
		var r1Rig = midground.Find("MinionPigR1/Animation/MinionPigR1").gameObject;
		var r2Rig = midground.Find("MinionPigR2/Animation/MinionPigR2").gameObject;
		if (r1Rig.GetComponent<ExtraDepthScale>() != null)
		{
			Log("already revised, unchanged");
			return false;
		}
		var a = midground.Find("MinionPigA").gameObject;
		var aRig = midground.Find("MinionPigA/Animation/MinionPigA").gameObject;
		var eRig = midground.Find("MinionPigE/Animation/MinionPigE").gameObject;
		var b = midground.Find("MinionPigB").gameObject;
		var bRig = midground.Find("MinionPigB/Animation/MinionPigB").gameObject;
		var c = midground.Find("MinionPigC").gameObject;
		var cRig = midground.Find("MinionPigC/Animation/MinionPigC").gameObject;
		var d = midground.Find("MinionPigD").gameObject;
		var dRig = midground.Find("MinionPigD/Animation/MinionPigD").gameObject;
		var guard = midground.Find("MinionPigG").gameObject;
		var bare = midground.Find("MinionPigN").gameObject;
		var nRig = midground.Find("MinionPigN/Animation/MinionPigN").gameObject;
		s_Plane = c.transform.rotation;
		var middle = Find(scene, "BattlegroundSetup");

		var nodes = Ordered(lost);
		var getUp = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "RedBird" && n.text == "KnockedOutEnd");
		var hits = nodes.Where(n => n.type == NodeType.Instantiate && n.objectName.StartsWith("Hit")).GroupBy(n => n.objectName).ToDictionary(g => g.Key, g => g.First());
		var lunge = nodes.FirstOrDefault(n => n.type == NodeType.MoveArc && n.refObject == b);
		var zoomAt = nodes.FindLastIndex(n => n.type == NodeType.ZoomCamera);
		var fade = nodes.FirstOrDefault(n => n.type == NodeType.PlayAnimation && n.text == "ScreenEffect_FadeBlack");
		var storyEnd = nodes.LastOrDefault(n => n.type == NodeType.EnableStorySequence);
		var hideWiz = nodes.FirstOrDefault(n => n.type == NodeType.SetActive && n.objectName == "Wiz");
		if (getUp < 0 || hits.Count != 6 || lunge == null || zoomAt < 1 || nodes[zoomAt - 1].type != NodeType.MoveTo || fade == null || storyEnd == null || hideWiz == null)
		{
			Log("tree is not the expected one, unchanged");
			return false;
		}

		// Scene objects and assets from revision 11.
		var bubble = AssetDatabase.LoadAssetAtPath<GameObject>(BubblePath);
		var cloud = AssetDatabase.LoadAssetAtPath<GameObject>(CloudPath);
		var magicFx = AssetDatabase.LoadAssetAtPath<GameObject>(MagicFxPath);
		var nestEmpty = background.Find("IntroNest_Empty").gameObject;
		var nestEggs = background.Find("IntroNest_Eggs").gameObject;
		var wizShadow = midground.Find("WizPathShadow").gameObject;
		var r1 = r1Rig.transform.parent.parent.gameObject;
		var r2 = r2Rig.transform.parent.parent.gameObject;
		var e = eRig.transform.parent.parent.gameObject;
		if (bubble == null || cloud == null || magicFx == null)
		{
			Log("missing an asset from revision 11, unchanged");
			return false;
		}
		var gRig = midground.Find("MinionPigG/Animation/MinionPigG").gameObject;
		var redPath = new[] { Ground(1110f, 190f), Ground(1029f, 174f), Ground(875f, 151f), Ground(790f, 135f, BehindRockZ), Ground(741f, 125f, BehindRockZ), Ground(700f, 118f, BehindRockZ), Ground(650f, 110f, BehindRockZ), Ground(640f, 108f), Ground(560f, 104f), Ground(406f, 99f) };
		redPath[3].z = redPath[4].z = redPath[5].z = redPath[6].z = BehindRockZ;
		var bluePath = new[] { Ground(-60f, 140f), Ground(150f, 134f), Ground(232f, 126f), Ground(247f, 137f), Ground(257f, 152f), Ground(270f, 165f), Ground(290f, 175f), Ground(325f, 172f), Ground(350f, 162f), Ground(362f, 145f), Ground(365f, 122f), Ground(362f, 115f) };
		foreach (var pair in new[] { new { Rig = r1Rig, Path = redPath }, new { Rig = r2Rig, Path = bluePath } })
		{
			var scaler = pair.Rig.GetComponent<ScaleController>();
			scaler.m_BaseScale = Vector3.one;
			EditorUtility.SetDirty(scaler);
			var extra = pair.Rig.AddComponent<ExtraDepthScale>();
			extra.m_StartDepth = pair.Path[0].y + 384f;
			extra.m_EndDepth = pair.Path[pair.Path.Length - 1].y + 384f;
			extra.m_EndFactor = NestPigScale;
			EditorUtility.SetDirty(extra);
			Log(pair.Rig.name + ": full size, half by the nest (depth " + extra.m_StartDepth.ToString("F0") + " to " + extra.m_EndDepth.ToString("F0") + ")");
		}

		var events = new List<TimedNode>();
		Action<float, ActionNode> at = (time, node) => events.Add(new TimedNode { Time = time, Node = node });
		// The fight, and everything from Red's reaction to Chuck's carry on, waits for the rogue's run to stop.
		const float late = 0.45f;
		Action<float, ActionNode> atLate = (time, node) => at(time + late, node);

		// Red's rogue and the fight (as in revision 10).
		at(0f, Anchor(b, "RedBird", new Vector3(300f, 0f, 15f) - RogueRunEnd));
		at(0f, Clip(b, "MinionPigB_Move"));
		at(0f, Bone(bRig, "Move_Loop", null, true));
		at(0f, LookBack(FaceRt("RedBird", false)));
		at(0f, LookBack(BoneRt("RedBird", "Surprised", "Idle")));
		at(0.5f, LookBack(FaceRt("RedBird", true)));
		at(1f, Clone(hideWiz));
		atLate(1.58f, Bone(bRig, "Action_Attack_2", "Idle", false));
		atLate(1.87f, Clone(lunge));
		atLate(2.08f, Clone(hits["Hit0"]));
		atLate(2.08f, BoneRt("RedBird", "Hit", "Idle"));
		atLate(2.58f, BoneRt("RedBird", "Action_Attack_1", "Idle"));
		atLate(2.88f, Clone(hits["Hit1"]));
		atLate(2.88f, Bone(bRig, "Hit", "Idle", false));
		atLate(4.13f, Bone(bRig, "Action_Attack_2", "Idle", false));
		atLate(4.63f, Clone(hits["Hit2"]));
		atLate(4.63f, BoneRt("RedBird", "Hit", "Idle"));
		atLate(5.68f, BoneRt("RedBird", "Action_Attack_1", "Idle"));
		atLate(5.98f, Clone(hits["Hit3"]));
		atLate(5.98f, Bone(bRig, "Hit", "Idle", false));
		atLate(5.98f, Clone(nodes[zoomAt - 1]));
		atLate(5.98f, Clone(nodes[zoomAt]));
		atLate(6.78f, Bone(bRig, "Action_Attack_1", "Idle", false));
		atLate(6.98f, Clone(hits["Hit5"]));
		atLate(6.98f, BoneRt("RedBird", "Hit", "Idle"));
		atLate(7.68f, BoneRt("RedBird", "Action_Attack_1", "Idle"));
		atLate(7.88f, Clone(hits["Hit4"]));
		atLate(7.88f, Bone(bRig, "Defeated", null, false));

		// The swarm's other runs.
		at(0.75f, Bone(dRig, "Move_Loop", null, true));
		at(0.75f, Clip(d, "MinionPigD_Move"));
		at(3.15f, Bone(dRig, "Action_Fail", "Move_Loop", false));
		at(3.15f, Bone(aRig, "Move_Loop", null, true));
		at(3.15f, Clip(a, "MinionPigA_Move"));

		// The bird catcher and the guard line up on Chuck's body, left and right of it.
		at(3.15f, Bone(cRig, "Move_Loop", null, true));
		at(3.15f, Anchor(c, "YellowBird", new Vector3(70f - 60f, 0f, 90f) - StickRunEnd));
		at(3.15f, Clip(c, "MinionPigC_Move"));
		var catcherStops = MoveTo(c, "YellowBird:Center", OnPlane(new Vector3(-85f, 0f, 0f) - StickRunEnd), SwarmSpeed, "Move_Loop", true);
		catcherStops.param = new[] { "Action_Support", "right" };
		at(5.65f, catcherStops);
		at(4.55f, Anchor(guard, "YellowBird", new Vector3(1000f - 60f, 0f, 80f)));
		at(4.55f, MoveTo(guard, "YellowBird:Center", OnPlane(new Vector3(85f, 0f, -5f)), SwarmSpeed, "Move_Loop", true));

		// Chuck drops his staff and book (his shadow stays on the ground), is lifted and carried off right by the two
		// pigs facing left.
		var drop = Drop("YellowBird", "MainHand,OffHand", "ChuckItem_");
		drop.param = new[] { "MainHand=" + FeatherZ.ToString(CultureInfo.InvariantCulture) };
		at(7.3f, drop);
		at(7.3f, Bone(cRig, "Affected", "Idle", false));
		at(7.3f, Bone(gRig, "Affected", "Idle", false));
		at(7.5f, MoveRt("YellowBird", null, new Vector3(45f, 45f, 0f), 300f, null, 0));
		at(7.85f, Face(c, false));
		at(7.85f, Face(guard, false));
		at(7.9f, Stepped(Carry(c)));
		at(7.9f, Stepped(Carry(guard)));
		at(7.9f, Stepped(MoveRt("YellowBird", null, new Vector3(1600f, 0f, 0f), CarrySpeed, null, 0)));

		// The bare pig picks up the staff and book into its own hands and runs off down and right.
		at(7.55f, Anchor(bare, "YellowBird", new Vector3(-1300f - 60f, 0f, -60f)));
		at(7.55f, MoveTo(bare, "YellowBird:Center", OnPlane(new Vector3(-10f, 0f, -20f)), SwarmSpeed, "Move_Loop", true));
		at(11.35f, Bone(nRig, "Affected", "Idle", false));
		var staff = HandItem(nRig, true);
		staff.customFloat2 = 2f;
		at(11.6f, staff);
		var book = HandItem(nRig, false);
		book.customVec1 = new Vector3(0f, 0f, 90f);
		at(11.6f, book);
		at(11.6f, DestroyRt("ChuckItem_MainHand"));
		at(11.6f, DestroyRt("ChuckItem_OffHand"));
		at(11.95f, MoveTo(bare, null, OnPlane(new Vector3(1800f, 0f, -250f)), SwarmSpeed, "Move_Loop", true));

		// Red: surprised that Chuck is taken, runs to the middle, turns left and sees the eggs go.
		var redSpot = Ground(637f, 320f);
		atLate(8.5f, BoneRt("RedBird", "Surprised", "Idle"));
		atLate(9.4f, MoveRt("RedBird", middle, redSpot, SwarmSpeed, "Move_Loop", 2, false, RedRunTime));
		atLate(9.4f + RedRunTime + 0.5f, FaceRt("RedBird", false));
		atLate(9.4f + RedRunTime + 0.8f, BoneRt("RedBird", "Surprised", "Idle"));
		atLate(9.4f + RedRunTime + 0.8f, Bubble("RedBird", bubble, 3.4f));
		// The dead rogue's gear goes behind Porky once Red has left its side (orthographic camera: depth only sorts).
		atLate(10.6f, MoveTo(b, null, new Vector3(0f, 0f, 30f), 100000f, null, false));

		// Two rogues run to the nest and laugh.
		var r1Time = PathLength(redPath) / FarSwarmSpeed;
		var r2Time = PathLength(bluePath) / FarSwarmSpeed;
		atLate(8.5f, Bone(r1Rig, "Move_Loop", null, true));
		atLate(8.5f, Path(r1, null, redPath, null, r1Time, "Move_Loop", 1, null));
		atLate(8.5f + r1Time, Bone(r1Rig, "Taunt", "Idle", false));
		atLate(9f, Bone(r2Rig, "Move_Loop", null, true));
		atLate(9f, Path(r2, null, bluePath, null, r2Time, "Move_Loop", 1, null));
		atLate(9f + r2Time, Bone(r2Rig, "Taunt", "Idle", false));
		var r1Back = redPath.Reverse().ToArray();
		var r2Back = new[] { bluePath[bluePath.Length - 1], Ground(390f, 108f) }.Concat(redPath.Reverse().Skip(1)).ToArray();

		// Wiz flies down to the nest with his cloud, laughs, takes the eggs onto the cloud and flies off.
		// Lower than the drawn path's end and left of the nest, so all of him is below the cutscene bars.
		var wizDown = Resample(new[] { new Vector2(400f, -80f), new Vector2(372f, 10f), new Vector2(350f, 50f), new Vector2(330f, 82f), new Vector2(318f, 100f), new Vector2(318f, 108f) }, 12);
		var shadowDown = Resample(new[] { new Vector2(348f, 84f), new Vector2(332f, 98f), new Vector2(320f, 109f) }, 12);
		var wizUp = Resample(new[] { new Vector2(318f, 108f), new Vector2(360f, 92f), new Vector2(410f, 72f), new Vector2(445f, 50f), new Vector2(480f, -80f) }, 12);
		var shadowUp = Resample(new[] { new Vector2(320f, 109f), new Vector2(385f, 108f), new Vector2(447f, 106f) }, 12);
		const float wizStart = 10f + late;
		const float flightTime = 1.5f;
		const float wizTaunt = 2f;
		var wizArrives = wizStart + flightTime;
		var cast = wizArrives + wizTaunt + 0.2f;
		var eggsTaken = cast + 0.25f;
		var eggsPlaced = eggsTaken + 0.65f;
		at(wizStart, FindRt("Wiz", "Root", "WizRoot"));
		at(wizStart, SetActiveRt("Wiz", true));
		at(wizStart, BaseScale("Wiz", NestPigScale));
		at(wizStart, InstantiateLocal("MagicCloud", cloud, "WizRoot", new Vector3(110f, 40f, 5f) / WorldMapScale, 1f / WorldMapScale));
		at(wizStart, PlayRt("MagicCloud", "MagicCloud_SetEggs_0"));
		at(wizStart, BoneRt("Wiz", "FlyingState", null));
		at(wizStart, Path(null, "Wiz", AirPath(wizDown, shadowDown), GroundPath(shadowDown), flightTime, "Move_Fly", 1, wizShadow, false));
		at(wizArrives, FaceRig("Wiz", true));
		at(wizArrives, BoneRt("Wiz", "Taunt", "Idle"));
		at(cast, BoneRt("Wiz", "Action_Support", "Idle"));
		at(cast, InstantiateLocal("WizMagicFX", magicFx, "WizRoot", new Vector3(0f, 60f, -5f), 0.5f));
		at(eggsTaken, Show(nestEmpty, true));
		at(eggsTaken, Show(nestEggs, true));
		at(eggsTaken, Clip(nestEggs, "IntroNest_EggsTaken"));
		at(eggsTaken + 0.1f, PlayRt("MagicCloud", "MagicCloud_ReceiveEggs"));
		at(eggsPlaced + 0.2f, Path(null, "Wiz", AirPath(wizUp, shadowUp), GroundPath(shadowUp), flightTime, "Move_Fly", 1, wizShadow, true));
		// The rogues run back right as Wiz leaves.
		at(eggsPlaced + 0.4f, Path(r1, null, r1Back, null, PathLength(r1Back) / FarSwarmSpeed, "Move_Loop", 1, null));
		at(eggsPlaced + 0.65f, Path(r2, null, r2Back, null, PathLength(r2Back) / FarSwarmSpeed, "Move_Loop", 1, null));

		// The corporal runs right to left straight through Red, who is pushed right along the green path while he is in
		// the air in his Hit, then gets angry; Porky laughs.
		const float bump = 15f + late;
		var corporalPath = new[] { redSpot + new Vector3(1000f, 45f, 37f), redSpot + new Vector3(0f, 0f, -8f), redSpot + new Vector3(-1100f, -50f, -50f) };
		var corporalToBump = (Vector3.Distance(corporalPath[0], corporalPath[1]) - 60f) / SwarmSpeed;
		var corporalTime = PathLength(corporalPath) / SwarmSpeed;
		at(bump - corporalToBump, Bone(eRig, "Move_Loop", null, true));
		at(bump - corporalToBump, Path(e, null, corporalPath, null, corporalTime, "Move_Loop", 1, null));
		var push = new[] { redSpot, Ground(680f, 340f), Ground(745f, 349f) };
		at(bump, BoneRt("RedBird", "Hit", "Idle"));
		at(bump + 6f / 24f, Path(null, "RedBird", push, null, 6f / 24f, null, 0, null));
		at(bump + 0.5f + 0.3f, BoneRt("RedBird", "Rage", "Idle"));
		at(bump + 0.8f + 0.5f, BoneRt("Porky", "Taunt", "Idle"));

		at(bump + 0.8f, Clone(fade));
		at(bump + 0.8f + 4.9f, Clone(storyEnd));

		var chain = nodes.Take(getUp + 1).ToList();
		var now = 0f;
		foreach (var timed in events.Select((ev, i) => new { ev, i }).OrderBy(x => x.ev.Time).ThenBy(x => x.i).Select(x => x.ev))
		{
			if (timed.Time > now + 0.001f)
			{
				chain.Add(Delay(timed.Time - now));
				now = timed.Time;
			}
			if (timed.Node.type == NodeType.PlayBoneAnimation || timed.Node.type == NodeType.MoveArc || timed.Node.type == NodeType.MoveToObject || timed.Node.type == NodeType.MovePath || timed.Node.type == NodeType.PlayAnimation)
			{
				timed.Node.customInt = 0;
			}
			chain.Add(timed.Node);
		}
		Finish(lost, chain);
		Log("BS_00_PigsRaidCamp_AfterLoss: raid rebuilt from " + events.Count + " timed events (" + chain.Count + " nodes); rogue runs " + r1Time.ToString("F2") + " s / " + r2Time.ToString("F2") + " s, Red at " + redSpot + ", fade at " + (bump + 0.8f) + " s");
		return true;
	}

	private static float PathLength(Vector3[] points)
	{
		var length = 0f;
		for (var i = 1; i < points.Length; i++)
		{
			length += Vector3.Distance(points[i - 1], points[i]);
		}
		return length;
	}

	// count points evenly spaced along a polyline of screen points.
	private static Vector2[] Resample(Vector2[] points, int count)
	{
		var lengths = new List<float> { 0f };
		for (var i = 1; i < points.Length; i++)
		{
			lengths.Add(lengths[i - 1] + Vector2.Distance(points[i - 1], points[i]));
		}
		var result = new Vector2[count];
		for (var k = 0; k < count; k++)
		{
			var target = lengths[lengths.Count - 1] * k / (count - 1);
			var i = 1;
			while (i < lengths.Count - 1 && lengths[i] < target)
			{
				i++;
			}
			var span = lengths[i] - lengths[i - 1];
			result[k] = Vector2.Lerp(points[i - 1], points[i], span > 0f ? (target - lengths[i - 1]) / span : 1f);
		}
		return result;
	}

	// Wiz's points: where he is in the screenshot, at the depth of the ground point under him, in front of his shadow.
	private static Vector3[] AirPath(Vector2[] air, Vector2[] ground)
	{
		return air.Select((p, i) => { var g = Ground(p.x, p.y); return new Vector3(g.x, g.y, Ground(ground[i].x, ground[i].y).z - 2f); }).ToArray();
	}

	private static Vector3[] GroundPath(Vector2[] ground)
	{
		return ground.Select(p => Ground(p.x, p.y) + new Vector3(0f, 0f, 2f)).ToArray();
	}

	private static string Point(Vector3 p)
	{
		return p.x.ToString("F1", CultureInfo.InvariantCulture) + "," + p.y.ToString("F1", CultureInfo.InvariantCulture) + "," + p.z.ToString("F1", CultureInfo.InvariantCulture);
	}

	private static ActionNode Path(GameObject container, string runtimeName, Vector3[] points, Vector3[] ground, float duration, string clip, int face, GameObject shadow, bool hideShadowAtEnd = false)
	{
		var node = NewNode(NodeType.MovePath);
		if (container != null)
		{
			node.refObject = container;
		}
		else
		{
			node.objectType = 1;
			node.objectName = runtimeName;
		}
		node.param = points.Select((p, i) => ground != null ? Point(p) + ";" + Point(ground[i]) : Point(p)).ToArray();
		node.customFloat2 = duration;
		node.text = clip ?? string.Empty;
		node.customBool2 = clip != null;
		node.customInt2 = face;
		node.refObject2 = shadow;
		node.customBool = hideShadowAtEnd;
		return node;
	}

	private static ActionNode Carry(GameObject container)
	{
		var node = MoveTo(container, null, new Vector3(1600f, 0f, 0f), CarrySpeed, "Move_Carry", false);
		node.customInt2 = 0;
		return node;
	}

	private static ActionNode LookBack(ActionNode node)
	{
		node.param = new[] { "IntroRedLookBack" };
		return node;
	}

	private static ActionNode Stepped(ActionNode node)
	{
		node.customInt3 = 1;
		return node;
	}

	private static ActionNode FaceRig(string runtimeName, bool right)
	{
		var node = NewNode(NodeType.FaceDirection);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.customInt = right ? 1 : 0;
		node.customInt2 = 1;
		return node;
	}

	private static ActionNode BaseScale(string runtimeName, float scale)
	{
		var node = NewNode(NodeType.SetScale);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.customInt3 = 1;
		node.customVec1 = new Vector3(scale, scale, 1f);
		return node;
	}

	private static ActionNode HandItem(GameObject rig, bool mainHand)
	{
		var node = NewNode(NodeType.SetHandItem);
		node.refObject = rig;
		node.customInt = mainHand ? 1 : 0;
		node.customBool = true;
		node.secondaryText = "YellowBird";
		return node;
	}

	private static ActionNode DestroyRt(string runtimeName)
	{
		var node = NewNode(NodeType.Destroy);
		node.objectType = 1;
		node.objectName = runtimeName;
		return node;
	}

	private static ActionNode SetActiveRt(string runtimeName, bool active)
	{
		var node = NewNode(NodeType.SetActive);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.customBool = active;
		return node;
	}

	private static ActionNode Show(GameObject target, bool active)
	{
		var node = NewNode(NodeType.SetActive);
		node.refObject = target;
		node.customBool = active;
		return node;
	}

	private static ActionNode FindRt(string parentName, string childName, string saveAs)
	{
		var node = NewNode(NodeType.FindObject);
		node.objectType = 1;
		node.objectName = parentName;
		node.text = childName;
		node.secondaryText = saveAs;
		return node;
	}

	private static ActionNode PlayRt(string runtimeName, string clip)
	{
		var node = NewNode(NodeType.PlayAnimation);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.text = clip;
		return node;
	}

	private static ActionNode InstantiateLocal(string saveAs, GameObject prefab, string parentName, Vector3 localPosition, float localScale)
	{
		var node = NewNode(NodeType.Instantiate);
		node.refObject = prefab;
		node.objectType = 1;
		node.objectName = saveAs;
		node.text = parentName;
		node.customInt3 = 1;
		node.customVec2 = localPosition;
		node.customFloat = localScale;
		return node;
	}

	private static ActionNode Bubble(string runtimeName, GameObject prefab, float lifetime)
	{
		var node = NewNode(NodeType.ShowBubble);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.refObject = prefab;
		node.customFloat = 1.25f;
		node.customFloat2 = lifetime;
		return node;
	}

	// The stolen eggs bubble shows all five eggs, piled like in the nest.
	private static GameObject UpdateBubble()
	{
		var contents = PrefabUtility.LoadPrefabContents(BubblePath);
		var icon = contents.transform.Find("Animation/Icon").GetComponent<UISprite>();
		foreach (Transform child in icon.transform.parent.Cast<Transform>().ToList())
		{
			if (child.name.StartsWith("Icon_Egg"))
			{
				UnityEngine.Object.DestroyImmediate(child.gameObject);
			}
		}
		var spots = new[] { new Vector2(-32f, 84f), new Vector2(-4f, 90f), new Vector2(24f, 84f), new Vector2(-18f, 62f), new Vector2(10f, 62f) };
		for (var i = 0; i < spots.Length; i++)
		{
			var egg = i == 0 ? icon : UnityEngine.Object.Instantiate(icon.gameObject, icon.transform.parent).GetComponent<UISprite>();
			egg.name = i == 0 ? "Icon" : "Icon_Egg" + (i + 1);
			egg.spriteName = "BirdEgg";
			egg.width = 36;
			egg.height = 36;
			egg.depth = icon.depth + i;
			egg.transform.localPosition = new Vector3(spots[i].x, spots[i].y, 0f);
		}
		var missing = contents.transform.Find("Animation/Icon_Missing");
		if (missing != null)
		{
			missing.localPosition = new Vector3(58f, 80f, missing.localPosition.z);
			missing.GetComponent<UIWidget>().depth = icon.depth + spots.Length;
		}
		var saved = PrefabUtility.SaveAsPrefabAsset(contents, BubblePath);
		PrefabUtility.UnloadPrefabContents(contents);
		Log("Bubble_StolenEggs: five eggs");
		return saved;
	}

	// A battle copy of Wiz's world map egg cloud, not playing its drag-in on its own, with a clip popping the five eggs
	// onto it one after another (the reverse of MagicCloud_HideEgg_*).
	private static GameObject MakeCloud()
	{
		var clip = new AnimationClip { legacy = true, name = "MagicCloud_ReceiveEggs" };
		for (var i = 1; i <= 5; i++)
		{
			var start = (i - 1) * 0.1f;
			foreach (var axis in new[] { "x", "y" })
			{
				var pop = axis == "x" ? 1.25f : 1.5f;
				var curve = new AnimationCurve(new Keyframe(0f, 0.001f), new Keyframe(start + 0.001f, 0.001f), new Keyframe(start + 0.125f, pop), new Keyframe(start + 0.167f, pop), new Keyframe(start + 0.25f, 1f));
				clip.SetCurve("Animation/BirdEggs/Egg_" + i, typeof(Transform), "m_LocalScale." + axis, curve);
			}
			clip.SetCurve("Animation/BirdEggs/Egg_" + i, typeof(Transform), "m_LocalScale.z", AnimationCurve.Constant(0f, 0.65f, 1f));
		}
		AssetDatabase.DeleteAsset(CloudEggsClipPath);
		AssetDatabase.CreateAsset(clip, CloudEggsClipPath);
		var contents = PrefabUtility.LoadPrefabContents(CloudSource);
		contents.name = "Prop_MagicCloud_Battle";
		var animation = contents.GetComponent<Animation>();
		animation.playAutomatically = false;
		animation.AddClip(AssetDatabase.LoadAssetAtPath<AnimationClip>(CloudEggsClipPath), "MagicCloud_ReceiveEggs");
		var folder = System.IO.Path.GetDirectoryName(CloudPath).Replace('\\', '/');
		if (!AssetDatabase.IsValidFolder(folder))
		{
			AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));
		}
		var saved = PrefabUtility.SaveAsPrefabAsset(contents, CloudPath);
		PrefabUtility.UnloadPrefabContents(contents);
		Log("created " + CloudPath + " with MagicCloud_ReceiveEggs");
		return saved;
	}

	// The egg copy pops away like a cloud egg going to a castle (MagicCloud_HideEgg_*).
	private static AnimationClip MakeNestEggsClip()
	{
		var clip = new AnimationClip { legacy = true, name = "IntroNest_EggsTaken" };
		clip.SetCurve(string.Empty, typeof(Transform), "m_LocalScale.x", new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.083f, 1.15f), new Keyframe(0.125f, 1.15f), new Keyframe(0.3f, 0.001f)));
		clip.SetCurve(string.Empty, typeof(Transform), "m_LocalScale.y", new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.083f, 1.3f), new Keyframe(0.125f, 1.3f), new Keyframe(0.3f, 0.001f)));
		clip.SetCurve(string.Empty, typeof(Transform), "m_LocalScale.z", AnimationCurve.Constant(0f, 0.3f, 1f));
		AssetDatabase.DeleteAsset(NestEggsClipPath);
		AssetDatabase.CreateAsset(clip, NestEggsClipPath);
		return AssetDatabase.LoadAssetAtPath<AnimationClip>(NestEggsClipPath);
	}

	// A sprite over the camp's painted nest (hidden until Wiz takes the eggs), at the nest's world rect.
	private static GameObject AddNestSprite(Transform parent, string name, string texturePath, float z, AnimationClip clip)
	{
		var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
		if (importer == null)
		{
			Log("missing " + texturePath);
			return null;
		}
		importer.textureType = TextureImporterType.Sprite;
		importer.spriteImportMode = SpriteImportMode.Single;
		importer.spritePixelsPerUnit = NestPixelsPerUnit;
		importer.mipmapEnabled = false;
		importer.alphaIsTransparency = true;
		importer.textureCompression = TextureImporterCompression.Uncompressed;
		importer.wrapMode = TextureWrapMode.Clamp;
		importer.filterMode = FilterMode.Bilinear;
		importer.SaveAndReimport();
		var sprite = AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(texturePath);
		var holder = new GameObject(name);
		holder.transform.position = new Vector3(NestCenter.x, NestCenter.y, z);
		holder.transform.SetParent(parent, true);
		holder.layer = 9;
		var renderer = holder.AddComponent<SpriteRenderer>();
		renderer.sprite = sprite;
		if (clip != null)
		{
			var animation = holder.AddComponent<Animation>();
			animation.playAutomatically = false;
			animation.AddClip(clip, clip.name);
			animation.clip = clip;
		}
		holder.SetActive(false);
		Log(name + ": " + texturePath + " at " + holder.transform.position);
		return holder;
	}

	// Wiz's ground shadow for his flight to the nest: his rig's Shadow texture at his battle shadow's size.
	private static GameObject AddWizShadow(Transform parent)
	{
		var wiz = AssetDatabase.LoadAssetAtPath<GameObject>(WizPrefab);
		var bones = wiz != null ? wiz.GetComponentInChildren<BoneAnimation>(true) : null;
		var atlas = bones != null ? bones.textureAtlases.FirstOrDefault(t => t != null && t.textureNames.Contains("Shadow")) : null;
		if (atlas == null)
		{
			return null;
		}
		var scaleController = wiz.GetComponentInChildren<ScaleController>(true);
		var baseScale = scaleController != null ? scaleController.m_BaseScale : Vector3.one;
		var shadowBone = wiz.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Shadow");
		var shadowSprite = wiz.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Shadow_Sprite");
		var boneScale = shadowBone != null ? shadowBone.localScale : Vector3.one;
		var spriteScale = shadowSprite != null ? shadowSprite.localScale : Vector3.one;
		var holder = new GameObject("WizPathShadow");
		holder.transform.SetParent(parent, false);
		holder.transform.position = new Vector3(-4000f, 0f, FrontOfCampZ);
		holder.transform.rotation = Quaternion.identity;
		holder.layer = 9;
		var shadow = holder.AddComponent<PathShadow>();
		shadow.m_Atlas = atlas;
		shadow.m_BaseScale = new Vector3(Mathf.Abs(baseScale.x * boneScale.x * spriteScale.x), Mathf.Abs(baseScale.y * boneScale.y * spriteScale.y), 1f);
		holder.SetActive(false);
		Log("WizPathShadow: " + atlas.name + " base scale " + shadow.m_BaseScale);
		return holder;
	}

	// The corporal leaves MinionPigA's run (its clip moves Animation2) for its own container and path.
	private static GameObject MoveCorporal(Transform midground, GameObject like, GameObject rig)
	{
		var container = new GameObject("MinionPigE");
		container.transform.SetParent(midground, false);
		container.transform.localRotation = like.transform.localRotation;
		container.transform.localScale = like.transform.localScale;
		container.transform.localPosition = new Vector3(-4000f, like.transform.localPosition.y, like.transform.localPosition.z);
		var animation = new GameObject("Animation");
		animation.transform.SetParent(container.transform, false);
		var rotation = rig.transform.localRotation;
		var scale = rig.transform.localScale;
		rig.transform.SetParent(animation.transform, false);
		rig.transform.localPosition = Vector3.zero;
		rig.transform.localRotation = rotation;
		rig.transform.localScale = scale;
		UnityHelper.SetLayerRecusively(container, rig.layer);
		Log("MinionPigE: own container");
		return container;
	}

	private static Vector3 OnPlane(Vector3 offset)
	{
		return s_Plane * offset;
	}

	// A battle MinionPig rig under a new container (container/Animation/rig, like the swarm's) in the midground,
	// parked offscreen until its spawn.
	private static GameObject AddPig(Transform midground, string name, GameObject like, GameObject likeRig, SceneRigGear.Swap[] gear)
	{
		var container = new GameObject(name);
		container.transform.SetParent(midground, false);
		container.transform.localRotation = like.transform.localRotation;
		container.transform.localScale = like.transform.localScale;
		container.transform.localPosition = new Vector3(-4000f, like.transform.localPosition.y, like.transform.localPosition.z);
		var animation = new GameObject("Animation");
		animation.transform.SetParent(container.transform, false);
		var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleMinionPrefab);
		var rig = (GameObject)UnityEngine.Object.Instantiate(prefab, animation.transform);
		rig.name = name;
		rig.transform.localPosition = Vector3.zero;
		rig.transform.localRotation = likeRig.transform.localRotation;
		rig.transform.localScale = likeRig.transform.localScale;
		UnityHelper.SetLayerRecusively(container, likeRig.layer);
		var swaps = rig.AddComponent<SceneRigGear>();
		swaps.m_Swaps = gear.ToList();
		EditorUtility.SetDirty(swaps);
		Log(name + ": battle MinionPig with " + string.Join(", ", gear.Select(x => x.TextureName == string.Empty ? "no " + x.BoneName : x.TextureName).ToArray()));
		return rig;
	}

	private static GameObject FindChild(Transform root, string name)
	{
		return root.GetComponentsInChildren<Transform>(true).First(t => t.name == name).gameObject;
	}

	private static SceneRigGear.Swap Gear(string bone, string texture)
	{
		return new SceneRigGear.Swap { BoneName = bone, TextureName = texture };
	}

	// Puts a container where it is planeOffset (battle plane) from a saved object.
	private static ActionNode Anchor(GameObject container, string runtimeName, Vector3 planeOffset)
	{
		var node = NewNode(NodeType.SetPosition);
		node.refObject = container;
		node.customInt = 3;
		node.text = runtimeName;
		node.customVec2 = OnPlane(planeOffset);
		return node;
	}

	private static ActionNode Clip(GameObject container, string clip)
	{
		var node = NewNode(NodeType.PlayAnimation);
		node.refObject = container;
		node.text = clip;
		return node;
	}

	private static ActionNode Bone(GameObject rig, string clip, string queued, bool randomStart)
	{
		var node = NewNode(NodeType.PlayBoneAnimation);
		node.refObject = rig;
		node.text = clip;
		node.QueueIdle = queued != null;
		node.secondaryText = queued ?? string.Empty;
		node.customBool2 = randomStart;
		return node;
	}

	private static ActionNode BoneRt(string runtimeName, string clip, string queued)
	{
		var node = Bone(null, clip, queued, false);
		node.objectType = 1;
		node.objectName = runtimeName;
		return node;
	}

	// Moves a scene pig's container to a saved object (or, without one, from where it is) plus a world offset; its rig
	// turns the way it goes.
	private static ActionNode MoveTo(GameObject container, string runtimeName, Vector3 offset, float speed, string clip, bool randomStart)
	{
		var node = NewNode(NodeType.MoveToObject);
		node.refObject = container;
		node.secondaryText = runtimeName ?? string.Empty;
		node.customVec1 = offset;
		node.customFloat = speed;
		node.text = clip ?? string.Empty;
		node.customBool2 = randomStart;
		node.customInt2 = 1;
		return node;
	}

	// Moves a saved character to target (or from where it is) plus a world offset; face 2 turns it the way it goes
	// (birds). xOnly: only along x. duration > 0: takes that long instead of going at speed.
	private static ActionNode MoveRt(string runtimeName, Transform target, Vector3 offset, float speed, string clip, int face, bool xOnly = false, float duration = 0f)
	{
		var node = NewNode(NodeType.MoveToObject);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.refObject1 = target;
		node.customVec1 = offset;
		node.customFloat = speed;
		node.customFloat2 = duration;
		node.text = clip ?? string.Empty;
		node.customInt2 = face;
		node.customBool = xOnly;
		return node;
	}

	private static ActionNode Face(GameObject container, bool right)
	{
		var node = NewNode(NodeType.FaceDirection);
		node.refObject = container;
		node.customInt = right ? 1 : 0;
		node.customInt2 = 1;
		return node;
	}

	private static ActionNode FaceRt(string runtimeName, bool right)
	{
		var node = NewNode(NodeType.FaceDirection);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.customInt = right ? 1 : 0;
		node.customBool = true;
		return node;
	}

	private static ActionNode Drop(string runtimeName, string bones, string prefix)
	{
		var node = NewNode(NodeType.DropHandItems);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.text = bones;
		node.secondaryText = prefix;
		node.customBool = true;
		return node;
	}

	private static ActionNode CarryItem(string runtimeName, GameObject hand)
	{
		var node = NewNode(NodeType.SetParent);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.customInt = 0;
		node.refObject2 = hand;
		node.QueueIdle = true;
		return node;
	}

	private static ActionNode Delay(float seconds)
	{
		var node = NewNode(NodeType.Delay);
		node.customFloat = seconds;
		return node;
	}

	private static ActionNode NewNode(NodeType type)
	{
		return new ActionNode
		{
			type = type,
			enabled = true,
			text = string.Empty,
			objectName = string.Empty,
			secondaryText = string.Empty,
			param = new string[0],
			nodesIn = new int[0],
			nodesOut = new int[0],
			nodesOutIndex = new int[0]
		};
	}

	private static ActionNode Clone(ActionNode source)
	{
		var copy = new ActionNode();
		foreach (var field in typeof(ActionNode).GetFields(BindingFlags.Public | BindingFlags.Instance))
		{
			var value = field.GetValue(source);
			var array = value as Array;
			field.SetValue(copy, array != null ? array.Clone() : value);
		}
		return copy;
	}

	private static List<ActionNode> Ordered(ActionTree tree)
	{
		var result = new List<ActionNode>();
		var seen = new HashSet<int>();
		var node = tree.GetNodeByID(tree.startNode);
		while (node != null && seen.Add(node.nodeID))
		{
			result.Add(node);
			node = node.nodesOut != null && node.nodesOut.Length > 0 ? tree.GetNodeByID(node.nodesOut[0]) : null;
		}
		return result;
	}

	// Numbers the nodes 1..n in order, lays them out in rows of ten and links them into one chain.
	private static void Finish(ActionTree tree, List<ActionNode> chain)
	{
		for (var i = 0; i < chain.Count; i++)
		{
			chain[i].nodeID = i + 1;
			chain[i].editorRect = new Rect(20f + 230f * (i % 10), 20f + 220f * (i / 10), 200f, 180f);
		}
		for (var i = 0; i < chain.Count; i++)
		{
			chain[i].nodesIn = i > 0 ? new[] { chain[i - 1].nodeID } : new int[0];
			chain[i].nodesOut = i + 1 < chain.Count ? new[] { chain[i + 1].nodeID } : new int[0];
			chain[i].nodesOutIndex = i + 1 < chain.Count ? new[] { 0 } : new int[0];
		}
		tree.nodes = chain.ToArray();
		tree.startNode = 1;
		EditorUtility.SetDirty(tree);
	}

	private static Transform Find(Scene scene, string path)
	{
		var parts = path.Split('/');
		foreach (var root in scene.GetRootGameObjects())
		{
			if (root.name == parts[0])
			{
				var found = parts.Length == 1 ? root.transform : root.transform.Find(string.Join("/", parts.Skip(1).ToArray()));
				if (found != null)
				{
					return found;
				}
			}
		}
		throw new Exception("not found in " + scene.name + ": " + path);
	}
}
