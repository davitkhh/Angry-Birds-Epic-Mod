using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using SmoothMoves;
using UnityEngine;

[ExecuteInEditMode]
public class ActionTree : MonoBehaviour
{
	[HideInInspector]
	public Dictionary<string, int> locals = new Dictionary<string, int>();

	[HideInInspector]
	public string characterName = string.Empty;

	[HideInInspector]
	public Vector3 nametagOffset = Vector3.zero;

	[HideInInspector]
	public float maximumDistance = 2f;

	[HideInInspector]
	public bool allowMouseSelection = true;

	[HideInInspector]
	public string[] registeredTargets = new string[0];

	[HideInInspector]
	public int registeredTargetsSize;

	[HideInInspector]
	public bool foldoutTargets;

	[HideInInspector]
	public ActionTree[] registeredTargetsGO;

	//[HideInInspector]
	public ActionNode[] nodes;

	//[HideInInspector]
	public int startNode;

	//[HideInInspector]
	public int currentNode;

	//[HideInInspector]
	public int choice;

	//[HideInInspector]
	public int selectionGridInt;

	//[HideInInspector]
	public ActionNode node;

	//[HideInInspector]
	public string text;

	//[HideInInspector]
	private bool skipDisabled;

	//[HideInInspector]
	public bool isFinished;

	private Dictionary<string, GameObject> m_runtimeInstantiateDictionary = new Dictionary<string, GameObject>();

	private List<GameObject> m_destroyList = new List<GameObject>();

	private BaseLocationStateManager m_WorldMapStatMgr;

	// MoveByClip: where each moved object ends up (its position before the first MoveByClip), and the hidden object the
	// clips are sampled on.
	private readonly Dictionary<GameObject, Vector3> m_clipAnchors = new Dictionary<GameObject, Vector3>();

	private GameObject m_clipSampler;

	private Vector3 m_clipSampledScale = Vector3.one;

	// When set, a KillBattlePigs node calls this (once) instead of killing the pigs.
	[NonSerialized]
	public Action KillBattlePigsOverride;

	public bool TryGetRuntimeObject(string objectName, out GameObject runtimeObject)
	{
		return m_runtimeInstantiateDictionary.TryGetValue(objectName, out runtimeObject);
	}

	// Ends the tree after the current node; the remaining nodes and pending waits are skipped.
	public void EndNow()
	{
		StopAllCoroutines();
		skipDisabled = false;
		node = null;
		isFinished = true;
	}

	// Restarts a tree stopped with EndNow at the given node, keeping its runtime objects (e.g. a story state loop).
	public void ResumeAt(int nodeId)
	{
		StopAllCoroutines();
		skipDisabled = false;
		isFinished = false;
		Load(nodeId);
	}

	// Pre-binds a runtime name before the tree runs; FindObject/Instantiate nodes skip names already bound.
	public void SetRuntimeObject(string objectName, GameObject runtimeObject)
	{
		m_runtimeInstantiateDictionary[objectName] = runtimeObject;
	}

	public List<string> m_PreInstantiatedCharacterAssetIds = new List<string>();

	private void Awake()
	{
		currentNode = startNode;
		if (nodes != null && nodes.Length > 0)
		{
			node = GetNodeByID(currentNode);
			GetText(node.text);
		}
		if (!locals.ContainsKey("counter"))
		{
			locals["counter"] = 0;
		}
	}

	private void OnDestroy()
	{
		if (m_clipSampler != null)
		{
			DestroyImmediate(m_clipSampler);
		}
	}

	private string GetText(string ident)
	{
		return ident;
	}

	public ActionNode GetNodeByID(int id)
	{
		ActionNode result = null;
		var array = nodes;
		foreach (var actionNode in array)
		{
			if (actionNode.nodeID == id)
			{
				result = actionNode;
				break;
			}
		}
		return result;
	}

	public int GetAnswerCountForNode(ActionNode an)
	{
		var num = 0;
		var nodesOut = an.nodesOut;
		foreach (var id in nodesOut)
		{
			if (GetNodeByID(id).enabled)
			{
				num++;
			}
		}
		return num;
	}

	public string GetAnswerStringForNode(ActionNode an, int index)
	{
		var num = 0;
		var nodesOut = an.nodesOut;
		foreach (var id in nodesOut)
		{
			if (GetNodeByID(id).enabled)
			{
				if (index == num)
				{
					return GetNodeByID(id).text;
				}
				num++;
			}
		}
		return string.Empty;
	}

	public void SelectAnswerByOrder(ActionNode an, int answerIndex)
	{
		var num = 0;
		var nodesOut = an.nodesOut;
		foreach (var id in nodesOut)
		{
			if (GetNodeByID(id).enabled)
			{
				if (answerIndex == num)
				{
					choice = id;
				}
				num++;
			}
		}
	}

	public bool NextNode()
	{
		try
		{
			if (skipDisabled)
			{
				DebugLog.Log("skip disabled");
				return true;
			}
			var nextNode = GetNextNode();
			if (nextNode < 0)
			{
				DebugLog.Log("Next id < 0 ");
				Exit();
				return false;
			}
			return Load(nextNode);
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			FailSafeReset();
			isFinished = true;
			Exit();
			return false;
		}
	}

	private void Exit()
	{
		if (locals.ContainsKey("counter"))
		{
			var num = locals["counter"];
			num = locals["counter"] = num + 1;
		}
		node = null;
	}

	public void StopExecution()
	{
		DebugLog.Log("STOP ALL COROUTINES");
		StopAllCoroutines();
	}

	public int GetNextNode()
	{
		if (node == null)
		{
			DebugLog.Log("node is null");
			return -1;
		}
		var result = -1;
		if (node.nodesOut != null)
		{
			var enabledOutNodes = GetEnabledOutNodes();
			if (enabledOutNodes.Length > 0)
			{
				result = enabledOutNodes[UnityEngine.Random.Range(0, enabledOutNodes.Length)];
			}
		}
		else
		{
			DebugLog.Log("nodes out is null");
		}
		return result;
	}

	private int[] GetEnabledOutNodes()
	{
		var array = new int[node.nodesOut.Length];
		var num = 0;
		for (var i = 0; i < node.nodesOut.Length; i++)
		{
			var nodeByID = GetNodeByID(node.nodesOut[i]);
			if (nodeByID.enabled)
			{
				array[num] = node.nodesOut[i];
				num++;
			}
		}
		var array2 = new int[num];
		for (var j = 0; j < num; j++)
		{
			array2[j] = array[j];
		}
		return array2;
	}

	public bool Load()
	{
		return Load(startNode);
	}

	public bool Load(int id)
	{
		currentNode = id;
		node = GetNodeByID(currentNode);
		m_WorldMapStatMgr = UnityEngine.Object.FindObjectOfType(typeof(BaseLocationStateManager)) as BaseLocationStateManager;
		if (node == null)
		{
			DebugLog.Log("node with id " + id + " is null");
			Exit();
			return false;
		}

		switch (node.type)
		{
			case NodeType.PlayAnimation:
			{
				var gameObjectFromNode = GetGameObjectFromNode(node);
				if (gameObjectFromNode == null)
				{
					DebugLog.Log("GameObject reference Null");
					return NextNode();
				}

				var componentInChildren = gameObjectFromNode.GetComponentInChildren<Animator>();
				var num = 0f;
				if (componentInChildren == null)
				{
					var componentInChildren2 = gameObjectFromNode.GetComponentInChildren<Animation>();
					if (componentInChildren2 == null)
					{
						DebugLog.Log("Animation Component Null");
						return NextNode();
					}

					num = componentInChildren2[node.text].clip.length;

					componentInChildren2.Play(node.text, PlayMode.StopSameLayer);
				}
				else
				{
					num = componentInChildren.gameObject.PlayAnimationOrAnimatorState(node.text);
				}

				if (node.customInt == 0)
				{
					return NextNode();
				}

				if (node.customInt == 1)
				{
					StartCoroutine(WaitTillNextNode(num));
					return true;
				}

				if (node.customInt == 2)
				{
					StartCoroutine(WaitTillNextNode(node.customFloat));
					return true;
				}

				break;
			}
			case NodeType.Instantiate:
			{
				if (m_runtimeInstantiateDictionary.ContainsKey(node.objectName))
				{
					DebugLog.Warn("trying to add object to dictionary with name " + node.objectName + " that is already used in dictionary ");
					break;
				}
				
				var gameObject = UnityEngine.Object.Instantiate((GameObject)node.refObject);
				m_runtimeInstantiateDictionary.Add(node.objectName, gameObject);
				GameObject value = null;
				if (node.objectType == 0 && node.refObject2 != null)
				{
					value = (GameObject)node.refObject2;
				}
				else if (node.objectType == 1 && node.text != string.Empty && !m_runtimeInstantiateDictionary.TryGetValue(node.text, out value))
				{
					DebugLog.Warn("could not find " + node.text + " in instantiated objectList");
				}
				
				if (value == null)
				{
					gameObject.transform.position = node.customVec1;
				}
				else if (node.customInt3 == 1)
				{
					// Placed in the parent's own space (e.g. a prop held by a rig, which flips and scales with it):
					// customVec2 is its local position, customFloat its local scale (0: 1).
					gameObject.transform.parent = value.transform;
					gameObject.transform.localPosition = node.customVec2;
					gameObject.transform.localRotation = Quaternion.identity;
					var localScale = node.customFloat > 0f ? node.customFloat : 1f;
					gameObject.transform.localScale = new Vector3(localScale, localScale, 1f);
				}
				else
				{
					gameObject.transform.parent = value.transform;
					gameObject.transform.position = value.transform.position + node.customVec1;
					gameObject.transform.localScale = Vector3.one;
					gameObject.transform.localRotation = Quaternion.identity;
				}

				break;
			}
			case NodeType.InstatiateProp:
			{
				if (m_runtimeInstantiateDictionary.ContainsKey(node.objectName))
				{
					DebugLog.Warn("trying to add object to dictionary with name " + node.objectName + " that is already used in dictionary ");
					break;
				}
				
				var gameObject2 = DIContainerInfrastructure.PropLiteAssetProvider().InstantiateObject(node.text, null, Vector3.zero, Quaternion.identity);
				m_runtimeInstantiateDictionary.Add(node.objectName, gameObject2);
				GameObject value2 = null;
				if (node.objectType == 0)
				{
					var transform = (Transform)node.refObject2;
					if (transform)
					{
						value2 = transform.gameObject;
					}
				}
				else if (node.objectType == 1 && node.text != string.Empty && !m_runtimeInstantiateDictionary.TryGetValue(node.secondaryText, out value2))
				{
					DebugLog.Warn("could not find " + node.text + " in instantiated objectList");
				}

				if (value2 == null)
				{
					gameObject2.transform.position = node.customVec1;
				}
				else
				{
					gameObject2.transform.parent = value2.transform;
					gameObject2.transform.position = value2.transform.position + node.customVec1;
					gameObject2.transform.localScale = Vector3.one;
					gameObject2.transform.localRotation = Quaternion.identity;
				}

				break;
			}
			case NodeType.SetPosition:
			{
				var gameObjectFromNode2 = GetGameObjectFromNode(node);
				gameObjectFromNode2.SetActive(true);
				if (node.customInt == 0)
				{
					var transform2 = node.refObject2 as Transform;
					gameObjectFromNode2.transform.position = transform2.position + node.customVec2;
					if (node.customBool)
					{
						gameObjectFromNode2.transform.parent = transform2;
					}
				}
				else if (node.customInt == 1)
				{
					gameObjectFromNode2.transform.position = node.customVec1;
				}
				else if (node.customInt == 2)
				{
					gameObjectFromNode2.transform.Translate(node.customVec1);
				}
				else if (node.customInt == 3)
				{
					GameObject value3;
					if (!m_runtimeInstantiateDictionary.TryGetValue(node.text, out value3))
					{
						DebugLog.Warn("could not find " + node.text + " in instantiated objectList");
					}
					else
					{
						gameObjectFromNode2.transform.position = value3.transform.position + node.customVec2;
						if (node.customBool)
						{
							gameObjectFromNode2.transform.parent = value3.transform;
						}
					}
				}

				break;
			}
			case NodeType.MoveTo:
			{
				var gameObjectFromNode3 = GetGameObjectFromNode(node);
				if (gameObjectFromNode3 == null)
				{
					DebugLog.Log("GameObject Reference Null");
					return NextNode();
				}

				var componentInChildren3 = gameObjectFromNode3.GetComponentInChildren<CHMotionTween>();
				if (node.refObject1 != null)
				{
					componentInChildren3.m_StartTransform = (Transform)node.refObject1;
				}

				componentInChildren3.m_StartOffset = node.customVec1;
				if (node.refObject2 != null)
				{
					componentInChildren3.m_EndTransform = (Transform)node.refObject2;
				}

				componentInChildren3.m_EndOffset = node.customVec2;
				componentInChildren3.m_Timing = (CHMotionTween.TimingTypes)node.customInt;
				componentInChildren3.m_DurationInSeconds = componentInChildren3.m_SpeedInUnitsSecond = node.customFloat;
				componentInChildren3.Play();
				if (node.customInt2 == 1)
				{
					StartCoroutine(WaitForTweenFinished(componentInChildren3));
					return true;
				}

				break;
			}
			case NodeType.MoveAlongPath:
			{
				var gameObjectFromNode4 = GetGameObjectFromNode(node);
				var componentInChildren4 = gameObjectFromNode4.GetComponentInChildren<Animation>();
				var list = WorldMapStateMgr.CalculatePath((HotSpotWorldMapViewBase)node.refObject1, (HotSpotWorldMapViewBase)node.refObject2);
				DebugLog.Log("Path List contains " + list.Count + " elements");
				var nodeText = node.text != string.Empty ? node.text : "Move_Loop";

				if (node.customInt == 1)
				{
					PathMovingService.Instance.WalkAlongPath(list, gameObjectFromNode4, componentInChildren4, node.customFloat, 0, 0f, this, "NextNode", node.customBool, nodeText, true);
					return true;
				}

				PathMovingService.Instance.WalkAlongPath(list, gameObjectFromNode4, componentInChildren4, node.customFloat, 
					0, 0f, null, string.Empty, node.customBool, nodeText, true);
				break;
			}
			case NodeType.Delay:
				StartCoroutine(WaitTillNextNode(node.customFloat));
				return true;
			case NodeType.SetActive:
			{
				// A missing object (e.g. a FindObject that found nothing) would abort the whole cutscene.
				var target = GetGameObjectFromNode(node);
				if (target != null)
				{
					target.SetActive(node.customBool);
				}
				else
				{
					DebugLog.Warn("SetActive: no object for " + node.objectName);
				}
				break;
			}
			case NodeType.Destroy:
			{
				UnityEngine.Object.Destroy(GetGameObjectFromNode(node));
				if (node.objectType == 1 && m_runtimeInstantiateDictionary.ContainsKey(node.objectName))
				{
					m_runtimeInstantiateDictionary.Remove(node.objectName);
				}

				break;
			}
			case NodeType.SetScale:
			{
				var gameObjectFromNode5 = GetGameObjectFromNode(node);
				if (node.customInt3 == 1)
				{
					// The base scale of the character's ScaleController (its depth scaling rewrites the scale itself).
					var scaleController = gameObjectFromNode5 != null ? gameObjectFromNode5.GetComponentInChildren<ScaleController>() : null;
					if (scaleController != null)
					{
						scaleController.m_BaseScale = node.customVec1;
					}
					break;
				}
				gameObjectFromNode5.transform.localScale = node.customVec1;
				break;
			}
			case NodeType.EnableStorySequence:
			{
				ScreenElements.Instance.EnableStorySequence(node.customBool);
				StartCoroutine(WaitTillNextNodeAndSetFinished(0f, !node.customBool));
				break;
			}
			case NodeType.GetWorldMapCharacter:
			{
				if (m_runtimeInstantiateDictionary.ContainsKey(node.objectName))
				{
					DebugLog.Warn("trying to add object to dictionary with name " + node.objectName + " that is already used in dictionary ");
					break;
				}
				
				var bird = m_WorldMapStatMgr.GetBird(node.text);
				if (bird == null)
				{
					DebugLog.Warn("could not find worldmap character " + node.text);
				}
				else
				{
					m_runtimeInstantiateDictionary.Add(node.objectName, bird);
				}

				break;
			}
			case NodeType.InstantiateCharacter:
			{
				if (m_runtimeInstantiateDictionary.ContainsKey(node.objectName))
				{
					DebugLog.Warn("trying to add object to dictionary with name " + node.objectName + " that is already used in dictionary ");
					break;
				}
				
				var gameObject3 = new GameObject(node.text);
				gameObject3.AddComponent<CHMotionTween>();
				var gameObject4 = UnityEngine.Object.Instantiate((GameObject)node.refObject);
				gameObject4.transform.parent = gameObject3.transform;
				var component = gameObject4.GetComponent<CharacterControllerWorldMap>();
				if (component != null)
				{
					component.SetModel(node.text);
				}
				else
				{
					var component2 = gameObject4.GetComponent<CharacterControllerCamp>();
					component2.SetModel(node.text);
				}

				gameObject4.transform.localPosition = Vector3.zero;
				// Rigs cull their animation by renderer visibility; SmoothMoves skinned bounds can be stale, so cutscene
				// characters froze mid-pose (e.g. a walk cycle stuck on its squash frame). Always animate them.
				foreach (var clips in gameObject4.GetComponentsInChildren<Animation>(true))
				{
					clips.cullingType = AnimationCullingType.AlwaysAnimate;
				}
				// Rigs are pooled and SmoothMoves applies texture swaps on its next mesh update, so the first frame showed the
				// previous owner's gear (e.g. the Lolly Popper in a rogue hood). Keep the character hidden until it has updated.
				StartCoroutine(HideWhileSettingUp(gameObject4));
				if (m_WorldMapStatMgr != null)
				{
					gameObject4.transform.localScale = m_WorldMapStatMgr.GetWorldBirdScale();
				}

				m_runtimeInstantiateDictionary.Add(node.objectName, gameObject3);
				GetGameObjectFromNode(node);
				var gameObjectFromNodeSecondaryText = GetGameObjectFromNodeSecondaryText(node);
				if (gameObjectFromNodeSecondaryText == null)
				{
					gameObject3.transform.position = node.customVec1;
				}
				else
				{
					gameObject3.transform.parent = gameObjectFromNodeSecondaryText.transform;
					gameObject3.transform.position = gameObjectFromNodeSecondaryText.transform.position + node.customVec1;
					gameObject4.transform.localRotation = Quaternion.identity;
				}

				break;
			}
			case NodeType.PlayBoneAnimation:
			{
				var gameObjectFromNode6 = GetGameObjectFromNode(node);
				if (gameObjectFromNode6 == null)
				{
					DebugLog.Log("GameObject reference Null");
					return NextNode();
				}

				var componentInChildren5 = gameObjectFromNode6.GetComponentInChildren<BoneAnimation>();
				if (componentInChildren5 == null)
				{
					DebugLog.Log("Animation Component Null");
					return NextNode();
				}

				componentInChildren5.Play(node.text, PlayMode.StopSameLayer);
				// customBool2: start the clip at a random point (e.g. a crowd's walk cycles out of step).
				if (node.customBool2 && componentInChildren5[node.text] != null)
				{
					componentInChildren5[node.text].normalizedTime = UnityEngine.Random.value;
				}
				// customFloat2: start that many seconds into the clip (e.g. skip a wind-up), fast-forwarded rather than
				// jumped, so swapped bone textures (a rogue's hood) survive.
				var skipSeconds = 0f;
				if (node.customFloat2 > 0f && componentInChildren5[node.text] != null)
				{
					skipSeconds = Mathf.Min(node.customFloat2, componentInChildren5[node.text].length);
					StartCoroutine(SkipClipAhead(componentInChildren5, node.text, skipSeconds));
				}
				if (node.QueueIdle)
				{
					componentInChildren5.PlayQueued(node.secondaryText);
				}

				if (node.customInt == 0)
				{
					return NextNode();
				}

				if (node.customInt == 1)
				{
					StartCoroutine(WaitTillNextNode(componentInChildren5[node.text].length - skipSeconds));
					return true;
				}

				if (node.customInt == 2)
				{
					StartCoroutine(WaitTillNextNode(node.customFloat));
					return true;
				}

				break;
			}
			case NodeType.PlaySound:
			{
				// No world map music before Chuck joins (WorldMapStateMgr.IsWorldMapMusicUnlocked).
				// The world map's music is WorldMapAreaMusic's (none before Chuck joins).
				if (node.text == "music_worldmap" && (!WorldMapStateMgr.IsWorldMapMusicUnlocked() || WorldMapAreaMusic.Instance != null))
				{
					// A cutscene going back to map music: clear its own music (not the title theme) so the area mix returns.
					var primaryMusic = DIContainerInfrastructure.PrimaryMusicSource;
					if (WorldMapAreaMusic.Instance != null && primaryMusic != null && primaryMusic.isPlaying && (primaryMusic.clip == null || primaryMusic.clip.name != "TitleTheme"))
					{
						primaryMusic.Stop();
					}
					return NextNode();
				}
				var sound = node.refObject != null
					? DIContainerInfrastructure.AudioManager.GetSound(node.text, node.refObject as GameObject)
					: DIContainerInfrastructure.AudioManager.GetSound(node.text);
				sound.Time = node.customFloat2;
				sound.Start();
				if (node.customInt == 0)
				{
					return NextNode();
				}

				if (node.customInt == 1)
				{
					StartCoroutine(WaitTillNextNode(sound.Length));
					return true;
				}

				if (node.customInt == 2)
				{
					StartCoroutine(WaitTillNextNode(node.customFloat));
					return true;
				}

				break;
			}
			case NodeType.ZoomCamera:
			{
				GameObject value4 = null;
				if (node.customInt3 == 0)
				{
					value4 = ((Camera)node.refObject).gameObject;
				}
				else if (node.customInt3 == 1 && !m_runtimeInstantiateDictionary.TryGetValue(node.text, out value4))
				{
					DebugLog.Error("Could not find " + node.text + " in runtime dictionary");
				}

				if (!value4)
				{
					DebugLog.Error("Referenced Object for Zoom Camera " + node.text + " in runtime dictionary");
				}

				var componentInChildren6 = value4.GetComponentInChildren<Camera>();
				StartCoroutine(ZoomCamera(componentInChildren6, node.customFloat, node.customFloat2, (InterpolationType)node.customInt, (NodeWaitCondition)node.customInt2));
				if (node.customInt2 == 0)
				{
					return NextNode();
				}

				return true;
			}
			case NodeType.KillBattlePigs:
			{
				if (KillBattlePigsOverride != null)
				{
					var killBattlePigsOverride = KillBattlePigsOverride;
					KillBattlePigsOverride = null;
					killBattlePigsOverride();
					break;
				}

				var currentBattleGameData = ClientInfo.CurrentBattleGameData;
				var value5 = new List<ICombatant>();
				if (currentBattleGameData != null && currentBattleGameData.m_CombatantsPerFaction != null)
				{
					currentBattleGameData.m_CombatantsPerFaction.TryGetValue(Faction.Pigs, out value5);
					foreach (var item in value5)
					{
						if (item.IsAlive)
						{
							item.RaiseHealthChanged(item.CurrentHealth, 0f);
							var scoreForPig = DIContainerLogic.GetBattleService().GetScoreForPig(item, currentBattleGameData, true);
							StartCoroutine(item.CombatantView.DefeatCharacter(scoreForPig, false));
						}
					}
				}

				break;
			}
			case NodeType.FindSceneObject:
			{
				if (m_runtimeInstantiateDictionary.ContainsKey(node.objectName))
				{
					DebugLog.Warn("trying to add object to dictionary with name " + node.objectName + " that is already used in dictionary ");
					break;
				}
				
				var transform3 = ((Transform)node.refObject).Find(node.text);
				if (transform3 != null)
				{
					m_runtimeInstantiateDictionary.Add(node.objectName, transform3.gameObject);
				}
				else
				{
					DebugLog.Warn("could not find transform " + node.text + " in parent " + node.refObject.name);
				}

				break;
			}
			case NodeType.FindObject:
			{
				if (m_runtimeInstantiateDictionary.ContainsKey(node.secondaryText))
				{
					DebugLog.Warn("trying to add object to dictionary with name " + node.objectName + " that is already used in dictionary ");
					break;
				}
				
				var gameObjectFromNode7 = GetGameObjectFromNode(node);
				var transform4 = FindTransformRecursively(gameObjectFromNode7.transform, node.text);
				if (transform4 != null)
				{
					m_runtimeInstantiateDictionary.Add(node.secondaryText, transform4.gameObject);
				}
				else
				{
					DebugLog.Error("could not find transform " + node.text + " in parent " + gameObjectFromNode7.transform.name);
				}

				break;
			}
			case NodeType.FindObjectByTag:
			{
				if (m_runtimeInstantiateDictionary.ContainsKey(node.objectName))
				{
					DebugLog.Warn("trying to add object to dictionary with name " + node.objectName + " that is already used in dictionary ");
					break;
				}
				
				var gameObject5 = GameObject.FindGameObjectWithTag(node.text);
				if (gameObject5 != null)
				{
					var transform5 = gameObject5.transform;
					m_runtimeInstantiateDictionary.Add(node.secondaryText, transform5.gameObject);
				}
				else
				{
					DebugLog.Warn("could not find transform with tag " + node.text + " in parent " + node.refObject.name);
				}

				break;
			}
			case NodeType.FinishCurrentChronicleCave:
			{
				var chronicleCaveStateMgr = DIContainerInfrastructure.LocationStateMgr as ChronicleCaveStateMgr;
				if (chronicleCaveStateMgr)
				{
					StartCoroutine(chronicleCaveStateMgr.PlayGotoNextCaveAnimation());
				}

				break;
			}
			case NodeType.TimeScale:
			{
				StartCoroutine(FadeTimeScale(node.customFloat, node.customFloat2, node.customInt == 0));
				if (node.customInt == 1)
				{
					return NextNode();
				}

				return true;
			}
			case NodeType.SetParent:
			{
				var gameObjectFromNode8 = GetGameObjectFromNode(node);
				GameObject value6 = null;
				if (node.customInt == 0)
				{
					value6 = (GameObject)node.refObject2;
				}
				else if (node.customInt == 1 && !m_runtimeInstantiateDictionary.TryGetValue(node.text, out value6))
				{
					DebugLog.Error("Could not find " + node.text + " in runtime dictionary");
				}

				gameObjectFromNode8.transform.parent = value6.transform;
				if (node.QueueIdle)
				{
					gameObjectFromNode8.transform.localPosition = Vector3.zero;
				}

				break;
			}
			case NodeType.SetRotation:
			{
				var gameObjectFromNode9 = GetGameObjectFromNode(node);
				gameObjectFromNode9.transform.localRotation = Quaternion.Euler(node.customVec1);
				break;
			}
			case NodeType.SetBirdsToHotspot:
			{
				var gameObject6 = (GameObject)node.refObject;
				var component3 = gameObject6.GetComponent<HotSpotWorldMapViewBase>();
				DIContainerLogic.WorldMapService.TravelToHotSpot(DIContainerInfrastructure.GetCurrentPlayer(), component3.Model);
				m_WorldMapStatMgr.ResetBirdPositions();
				break;
			}
			// Shows/hides a character's hand item (hidden on the world map by default). customInt: 0 = off hand,
			// 1 = main hand. With secondaryText set, shows that runtime character's item instead of its own.
			case NodeType.SetHandItem:
			{
				var character = GetGameObjectFromNode(node);
				var asset = character != null ? character.GetComponentInChildren<CharacterAssetController>() : null;
				if (asset == null)
				{
					DebugLog.Warn("SetHandItem: no character found for " + node.objectName);
					break;
				}
				CharacterAssetController itemOwner = null;
				GameObject itemOwnerObject;
				if (!string.IsNullOrEmpty(node.secondaryText))
				{
					if (m_runtimeInstantiateDictionary.TryGetValue(node.secondaryText, out itemOwnerObject))
					{
						itemOwner = itemOwnerObject.GetComponentInChildren<CharacterAssetController>();
					}
					else
					{
						DebugLog.Warn("SetHandItem: could not find " + node.secondaryText + " in instantiated objectList");
					}
				}
				// customFloat2: how far behind the hand an item from another rig is drawn (0: just in front of the carrier);
				// customVec1.z: its turn (degrees).
				asset.ShowHandItem(node.customInt == 1 ? InventoryItemType.MainHandEquipment : InventoryItemType.OffHandEquipment, node.customBool, itemOwner, node.customFloat2 != 0f ? node.customFloat2 : -1f, node.customVec1.z);
				break;
			}
			// Keeps the object turned towards the runtime object named in secondaryText (empty: stop). customBool: the object
			// faces right at scale x = +1 (birds), otherwise left (pig rigs).
			case NodeType.FaceTarget:
			{
				var subject = GetGameObjectFromNode(node);
				if (subject == null)
				{
					DebugLog.Warn("FaceTarget: no object for " + node.objectName);
					break;
				}
				var tracker = subject.GetComponent<FaceTargetTracker>();
				GameObject faceTarget = null;
				if (!string.IsNullOrEmpty(node.secondaryText) && !m_runtimeInstantiateDictionary.TryGetValue(node.secondaryText, out faceTarget))
				{
					DebugLog.Warn("FaceTarget: could not find " + node.secondaryText + " in instantiated objectList");
				}
				if (faceTarget == null)
				{
					if (tracker != null)
					{
						UnityEngine.Object.Destroy(tracker);
					}
					break;
				}
				if (tracker == null)
				{
					tracker = subject.AddComponent<FaceTargetTracker>();
				}
				tracker.Track(faceTarget.transform, node.customBool);
				break;
			}
			// Flies a saved object along a curve that dips customFloat2 units at the x of the saved object named in
			// secondaryText (the lowest point) and ends customVec1.x past it, over customFloat seconds, looping the bone
			// clip in text (e.g. Wiz Pig swooping offscreen over a knocked out bird). customInt 1 waits for the end.
			case NodeType.MoveArc:
			{
				var mover = GetGameObjectFromNode(node);
				GameObject dipAt = null;
				if (mover == null || !m_runtimeInstantiateDictionary.TryGetValue(node.secondaryText, out dipAt))
				{
					DebugLog.Warn("MoveArc: no object for " + node.objectName + " or " + node.secondaryText);
					break;
				}
				if (!string.IsNullOrEmpty(node.text))
				{
					var bones = mover.GetComponentInChildren<BoneAnimation>();
					if (bones != null && bones[node.text] != null)
					{
						bones.Play(node.text, PlayMode.StopSameLayer);
					}
				}
				var arc = StartCoroutine(MoveArc(mover.transform, dipAt.transform.position.x, node.customFloat2, node.customVec1.x, node.customFloat));
				if (node.customInt == 1)
				{
					StartCoroutine(WaitForCoroutineThenNextNode(arc));
					return true;
				}
				break;
			}
			// Plays the position (and scale) motion of a legacy clip (refObject; the curves on path text, positions in
			// refObject1's space) on a saved object. customInt2 0: as an offset that ends where the object stood when a
			// MoveByClip node first moved it (a combatant makes a cutscene prop's entrance and lands on its battle slot).
			// customInt2 1: starts where the object is and blends onto the clip's own path, ending where the clip ends (a
			// prop's exit). Scale curves scale the object's current scale. customBool: only put it at the clip's start pose
			// (mode 0). customInt 1 waits for the end.
			case NodeType.MoveByClip:
			{
				var mover = GetGameObjectFromNode(node);
				var clip = node.refObject as AnimationClip;
				var space = node.refObject1 as Transform;
				if (mover == null || clip == null || space == null)
				{
					DebugLog.Warn("MoveByClip: missing object, clip or space for " + node.objectName);
					break;
				}
				Vector3 clipAnchor;
				if (!m_clipAnchors.TryGetValue(mover, out clipAnchor))
				{
					clipAnchor = mover.transform.position;
					m_clipAnchors[mover] = clipAnchor;
				}
				if (node.customBool)
				{
					mover.transform.position = clipAnchor + ClipOffset(clip, node.text, space, 0f);
					break;
				}
				var motion = StartCoroutine(MoveByClip(mover.transform, clip, node.text, space, clipAnchor, node.customInt2 == 1));
				if (node.customInt == 1)
				{
					StartCoroutine(WaitForCoroutineThenNextNode(motion));
					return true;
				}
				break;
			}
			// Moves a saved object (or refObject) at customFloat units a second to a point: the saved object named in
			// secondaryText (or refObject1; neither: the mover itself) plus customVec1 (world). customBool: only along x, to
			// that point's x. text: bone clip looped on the way (customBool2: from a random point), then Idle. customInt2:
			// face the way it moves, 1 = pig rig (faces left at +1, the rig flips), 2 = bird (faces right at +1, the object
			// flips). customInt 1 waits until it arrives.
			case NodeType.MoveToObject:
			{
				var mover = GetGameObjectFromNode(node);
				if (mover == null)
				{
					DebugLog.Warn("MoveToObject: no object for " + node.objectName);
					break;
				}
				Transform reference = mover.transform;
				GameObject named;
				// "Name:Bone": the target's x is that bone's (e.g. where a knocked out bird's body lies), its y and z the object's.
				var referenceName = node.secondaryText ?? string.Empty;
				var boneName = string.Empty;
				if (referenceName.IndexOf(':') >= 0)
				{
					boneName = referenceName.Substring(referenceName.IndexOf(':') + 1);
					referenceName = referenceName.Substring(0, referenceName.IndexOf(':'));
				}
				// "A+B": the point between saved objects A and B (e.g. two dropped items).
				Vector3? between = null;
				if (referenceName.IndexOf('+') >= 0)
				{
					var parts = referenceName.Split('+');
					var sum = Vector3.zero;
					var found = 0;
					foreach (var part in parts)
					{
						GameObject partObject;
						if (m_runtimeInstantiateDictionary.TryGetValue(part, out partObject) && partObject != null)
						{
							sum += partObject.transform.position;
							found++;
						}
					}
					if (found > 0)
					{
						between = sum / found;
					}
				}
				else if (!string.IsNullOrEmpty(referenceName) && m_runtimeInstantiateDictionary.TryGetValue(referenceName, out named))
				{
					reference = named.transform;
				}
				else if (node.refObject1 is Transform)
				{
					reference = (Transform)node.refObject1;
				}
				else if (node.refObject1 is GameObject)
				{
					reference = ((GameObject)node.refObject1).transform;
				}
				var from = mover.transform.position;
				var to = (between ?? reference.position) + node.customVec1;
				Transform referenceBone = null;
				if (!string.IsNullOrEmpty(boneName))
				{
					var referenceRig = reference.GetComponentInChildren<BoneAnimation>();
					referenceBone = referenceRig != null ? referenceRig.GetBoneTransform(boneName) : null;
					referenceBone = referenceBone ?? FindTransformRecursively(reference, boneName);
				}
				if (referenceBone != null)
				{
					to.x = referenceBone.position.x + node.customVec1.x;
				}
				if (node.customBool)
				{
					to = new Vector3(to.x, from.y, from.z);
				}
				var rig = mover.GetComponentInChildren<BoneAnimation>();
				if (node.customInt2 != 0 && Mathf.Abs(to.x - from.x) > 1f)
				{
					var pig = node.customInt2 == 1;
					FacingLock.Set(pig ? (rig != null ? rig.transform : null) : mover.transform, to.x > from.x, !pig);
				}
				if (!string.IsNullOrEmpty(node.text) && rig != null && rig[node.text] != null)
				{
					rig.Play(node.text, PlayMode.StopSameLayer);
					if (node.customBool2)
					{
						rig[node.text].normalizedTime = UnityEngine.Random.value;
					}
				}
				// customFloat2 > 0: the move takes that long (e.g. to land on a timed beat) instead of going at customFloat.
				var speed = node.customFloat2 > 0f ? Vector3.Distance(from, to) / node.customFloat2 : node.customFloat;
				// param: on arrival, [0] a bone clip to play (then Idle), [1] "left" or "right" to face (as customInt2).
				var arrivalClip = node.param != null && node.param.Length > 0 ? node.param[0] : null;
				var arrivalFacing = node.param != null && node.param.Length > 1 ? node.param[1] : null;
				var faceMode = node.customInt2;
				Action arrive = () =>
				{
					if (!string.IsNullOrEmpty(arrivalFacing) && faceMode != 0)
					{
						FacingLock.Set(faceMode == 1 ? (rig != null ? rig.transform : null) : mover.transform, arrivalFacing == "right", faceMode != 1);
					}
					if (!string.IsNullOrEmpty(arrivalClip) && rig != null && rig[arrivalClip] != null)
					{
						rig.Play(arrivalClip, PlayMode.StopSameLayer);
						if (rig["Idle"] != null)
						{
							rig.PlayQueued("Idle", QueueMode.CompleteOthers);
						}
					}
				};
				// customInt3 1: drags then stops like Move_Carry (moves in the first 7/12 of each loop of the clip, or of 0.5 s
				// without one, at the same average speed), so a carried object keeps in step with its carriers.
				var stepPeriod = 0f;
				if (node.customInt3 == 1)
				{
					stepPeriod = !string.IsNullOrEmpty(node.text) && rig != null && rig[node.text] != null ? rig[node.text].length : 0.5f;
				}
				var walk = StartCoroutine(MoveToPoint(mover.transform, from, to, speed, string.IsNullOrEmpty(node.text) ? null : rig, node.text, arrive, stepPeriod));
				if (node.customInt == 1)
				{
					StartCoroutine(WaitForCoroutineThenNextNode(walk));
					return true;
				}
				break;
			}
			// Turns a saved object (or refObject) to face right (customInt 1) or left (0) and keeps it that way.
			// customBool: it faces right at scale +1 (birds). customInt2 1: flip its rig (pig rigs) instead of the object.
			case NodeType.FaceDirection:
			{
				var subject = GetGameObjectFromNode(node);
				if (subject == null)
				{
					DebugLog.Warn("FaceDirection: no object for " + node.objectName);
					break;
				}
				var rig = subject.GetComponentInChildren<BoneAnimation>();
				FacingLock.Set(node.customInt2 == 1 && rig != null ? rig.transform : subject.transform, node.customInt == 1, node.customBool);
				break;
			}
			// Shows the bubble prefab in refObject over a saved battle character, where its speech bubbles appear, scaled by
			// customFloat (0: 1); removed after customFloat2 seconds (0: kept).
			case NodeType.ShowBubble:
			{
				var speaker = GetGameObjectFromNode(node);
				var prefab = node.refObject as GameObject;
				if (speaker == null || prefab == null)
				{
					DebugLog.Warn("ShowBubble: no character or bubble for " + node.objectName);
					break;
				}
				var bubble = UnityEngine.Object.Instantiate(prefab);
				var view = speaker.GetComponentInParent<CharacterControllerBattleGroundBase>();
				if (view != null)
				{
					view.PositionBubble(view, bubble);
				}
				else
				{
					bubble.transform.SetParent(speaker.transform, false);
				}
				if (node.customFloat > 0f)
				{
					bubble.transform.localScale *= node.customFloat;
				}
				if (node.customFloat2 > 0f)
				{
					UnityEngine.Object.Destroy(bubble, node.customFloat2);
				}
				break;
			}
			// A saved battle bird drops its hand items (text: bones, e.g. "MainHand,OffHand"): each becomes a separate quad
			// where it is, saved as secondaryText + bone name, and the rig's own sprite is collapsed. customBool also
			// moves its shadow onto the ground under it (CarriedShadow, e.g. while it is lifted and carried off).
			case NodeType.DropHandItems:
			{
				var owner = GetGameObjectFromNode(node);
				var view = owner != null ? owner.GetComponentInParent<CharacterControllerBattleGroundBase>() : null;
				if (view == null || view.m_AssetController == null)
				{
					DebugLog.Warn("DropHandItems: no battle character for " + node.objectName);
					break;
				}
				var bones = view.m_AssetController.m_BoneAnimation;
				var character = view.GetModel().CharacterModel;
				foreach (var boneName in node.text.Split(','))
				{
					var bone = bones.GetBoneTransform(boneName.Trim());
					var item = boneName.Trim() == "MainHand" ? character.MainHandItem : boneName.Trim() == "OffHand" ? character.OffHandItem : null;
					if (bone == null || item == null || string.IsNullOrEmpty(item.ItemAssetName))
					{
						continue;
					}
					var textureName = item.ItemAssetName.Replace("_Ancient", string.Empty);
					var atlas = bones.textureAtlases.FirstOrDefault(a => a != null && a.textureNames.Contains(textureName));
					if (atlas == null)
					{
						continue;
					}
					var sprite = bone.Find(boneName.Trim() + "_Sprite") ?? bone;
					var dropped = new GameObject(node.secondaryText + boneName.Trim());
					dropped.transform.SetParent(view.transform.parent, false);
					dropped.transform.position = sprite.position;
					dropped.transform.rotation = sprite.rotation;
					var quad = new GameObject("Quad");
					quad.transform.SetParent(dropped.transform, false);
					quad.transform.localScale = sprite.lossyScale;
					var mesh = quad.AddComponent<CHMeshSprite>();
					mesh.m_AtlasType = AtlasTypes.SmoothMoves;
					mesh.m_SmoothMovesAtlas = atlas;
					mesh.m_SpriteName = textureName;
					mesh.UpdateSprite(true, true);
					UnityHelper.SetLayerRecusively(dropped, view.m_AssetController.gameObject.layer);
					// param "Bone=z": that item lies at world depth z (e.g. between the waves and the wet sand).
					var depthEntry = node.param == null ? null : node.param.FirstOrDefault(p => p.StartsWith(boneName.Trim() + "="));
					float depth;
					if (depthEntry != null && float.TryParse(depthEntry.Substring(depthEntry.IndexOf('=') + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out depth))
					{
						dropped.transform.position = new Vector3(dropped.transform.position.x, dropped.transform.position.y, depth);
					}
					BoneCollapse.Add(sprite);
					AddItemShadow(dropped, bones);
					m_runtimeInstantiateDictionary[dropped.name] = dropped;
				}
				if (node.customBool)
				{
					CarriedShadow.Attach(owner.transform, bones);
				}
				break;
			}
			// Moves a saved object (or refObject) through world points, smoothed into a curve: param holds one "x,y,z" per
			// point, optionally followed by ";x,y,z", the point on the ground under it. customFloat: speed, or customFloat2:
			// duration. text: bone clip looped on the way (customBool2: from a random point), then Idle. customInt2: face
			// the way it goes overall (1 = pig rig, 2 = bird). refObject2: a shadow object moved along the ground points in
			// step (the mover's own shadow is collapsed and its GroundShadow turned off); customBool hides it at the end.
			// customInt 1 waits until it arrives.
			case NodeType.MovePath:
			{
				var mover = GetGameObjectFromNode(node);
				if (mover == null || node.param == null || node.param.Length < 2)
				{
					DebugLog.Warn("MovePath: no object or fewer than two points for " + node.objectName);
					break;
				}
				var points = new List<Vector3>();
				var groundPoints = new List<Vector3>();
				foreach (var entry in node.param)
				{
					var parts = entry.Split(';');
					points.Add(ParsePathPoint(parts[0]));
					if (parts.Length > 1)
					{
						groundPoints.Add(ParsePathPoint(parts[1]));
					}
				}
				var shadow = groundPoints.Count == points.Count ? node.refObject2 as GameObject : null;
				var rig = mover.GetComponentInChildren<BoneAnimation>();
				var first = points[0];
				var last = points[points.Count - 1];
				if (node.customInt2 != 0 && Mathf.Abs(last.x - first.x) > 1f)
				{
					var pig = node.customInt2 == 1;
					FacingLock.Set(pig ? (rig != null ? rig.transform : null) : mover.transform, last.x > first.x, !pig);
				}
				if (shadow != null)
				{
					GroundShadow.Suppress(mover.transform);
					var ownShadow = rig != null ? rig.GetBoneTransform("Shadow") : null;
					BoneCollapse.Add(ownShadow != null ? ownShadow.Find("Shadow_Sprite") ?? ownShadow : null);
					shadow.SetActive(true);
				}
				if (!string.IsNullOrEmpty(node.text) && rig != null && rig[node.text] != null)
				{
					rig.Play(node.text, PlayMode.StopSameLayer);
					if (node.customBool2)
					{
						rig[node.text].normalizedTime = UnityEngine.Random.value;
					}
				}
				var path = new SmoothPath(points);
				// secondaryText: a bone kept on the path instead of the root (BonePinnedPath), after holding customFloat s;
				// customVec2.x > 1 eases it out with that power (fast at first: a hard knock).
				var pinnedBone = !string.IsNullOrEmpty(node.secondaryText) && rig != null ? rig.GetBoneTransform(node.secondaryText) : null;
				if (pinnedBone != null)
				{
					BonePinnedPath.Begin(mover.transform, pinnedBone, path, node.customFloat, node.customFloat2, node.customVec2.x);
					break;
				}
				var groundPath = shadow != null ? new SmoothPath(groundPoints) : null;
				var duration = node.customFloat2 > 0f ? node.customFloat2 : node.customFloat > 0f ? path.Length / node.customFloat : 0f;
				var flight = StartCoroutine(FollowPath(mover.transform, path, shadow != null ? shadow.transform : null, groundPath, duration, node.customBool, string.IsNullOrEmpty(node.text) ? null : rig, node.text));
				if (node.customInt == 1)
				{
					StartCoroutine(WaitForCoroutineThenNextNode(flight));
					return true;
				}
				break;
			}
			// Goes on to the next node while the code toggle named in secondaryText (StoryToggles) is on; otherwise jumps to
			// node customInt, skipping a part of the cutscene (e.g. McCool's robbery).
			case NodeType.Branch:
			{
				if (!StoryToggles.IsOn(node.secondaryText))
				{
					if (GetNodeByID(node.customInt) != null)
					{
						return Load(node.customInt);
					}
					DebugLog.Warn("Branch: toggle " + node.secondaryText + " is off but there is no node " + node.customInt + " to jump to");
				}
				break;
			}
			// Stops another tree (customBool off), e.g. a story state's idle loop during a cutscene, or resumes it
			// at node customInt (customBool on).
			case NodeType.ControlActionTree:
			{
				var other = node.refObject as ActionTree;
				if (other == null && node.refObject is GameObject)
				{
					other = ((GameObject)node.refObject).GetComponent<ActionTree>();
				}
				if (other == null || other == this)
				{
					DebugLog.Warn("ControlActionTree: no other ActionTree referenced");
					break;
				}
				if (node.customBool)
				{
					other.ResumeAt(node.customInt);
				}
				else
				{
					other.EndNow();
				}
				break;
			}
		}

		return NextNode();
	}

	private GameObject GetGameObjectFromNode(ActionNode node)
	{
		GameObject value = null;
		if (node.objectType == 0)
		{
			value = node.refObject as GameObject;
		}
		else if (node.objectType == 1 && !m_runtimeInstantiateDictionary.TryGetValue(node.objectName, out value))
		{
			DebugLog.Warn("could not find " + node.objectName + " in instantiated objectList");
		}
		return value;
	}

	private GameObject GetGameObjectFromNodeSecondaryText(ActionNode node)
	{
		GameObject value = null;
		if (node.objectType == 0)
		{
			value = ((Transform)node.refObject2).gameObject;
		}
		else if (node.objectType == 1 && !m_runtimeInstantiateDictionary.TryGetValue(node.secondaryText, out value))
		{
			DebugLog.Warn("could not find " + node.objectName + " in instantiated objectList");
		}
		return value;
	}

	private float ExponentialEaseInOut(float t, float start, float end)
	{
		t = Mathf.Clamp01(t);
		t *= 2f;
		if (t < 1f)
		{
			return end / 2f * Mathf.Pow(2f, 10f * (t - 1f)) + start - 1f;
		}
		t -= 1f;
		return end / 2f * (0f - Mathf.Pow(2f, -10f * t) + 2f) + start - 1f;
	}

	private float LinearInterpolation(float t, float start, float end)
	{
		t = Mathf.Clamp01(t);
		return t * end + (1f - t) * start;
	}

	private float SinusoidalEaseInOut(float t, float start, float end)
	{
		t = Mathf.Clamp01(t);
		return (0f - end) / 2f * (Mathf.Cos((float)Math.PI * t) - 1f) + start - 1f;
	}

	private IEnumerator FadeTimeScale(float targetTimeScale, float duration, bool triggerNextNode)
	{
		if (Time.timeScale == targetTimeScale)
		{
			if (triggerNextNode)
			{
				NextNode();
			}
			yield break;
		}
		var deltaPerSecond = (targetTimeScale - Time.timeScale) / duration;
		do
		{
			DIContainerInfrastructure.TimeScaleMgr.AddTimeScale("FadeTimeScale", DIContainerInfrastructure.TimeScaleMgr.GetTimeScale("FadeTimeScale") + deltaPerSecond * Time.deltaTime);
			duration -= Time.deltaTime;
			yield return new WaitForEndOfFrame();
		}
		while (duration > 0f);
		DIContainerInfrastructure.TimeScaleMgr.AddTimeScale("FadeTimeScale", targetTimeScale);
		if (triggerNextNode)
		{
			NextNode();
		}
	}

	private IEnumerator WaitCoroutineTillNextNode(IEnumerator otherCoroutine)
	{
		skipDisabled = true;
		yield return otherCoroutine;
		skipDisabled = false;
		NextNode();
	}

	private IEnumerator WaitTillNextNode(float time)
	{
		skipDisabled = true;
		yield return new WaitForSeconds(time);
		skipDisabled = false;
		NextNode();
	}

	private IEnumerator WaitTillNextNodeAndSetFinished(float time, bool finish)
	{
		skipDisabled = true;
		yield return new WaitForSeconds(time);
		skipDisabled = false;
		isFinished = finish;
		NextNode();
	}

	private IEnumerator HideWhileSettingUp(GameObject character)
	{
		var renderers = character.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
		foreach (var renderer in renderers)
		{
			renderer.enabled = false;
		}
		yield return null;
		yield return null;
		foreach (var renderer in renderers)
		{
			if (renderer != null)
			{
				renderer.enabled = true;
			}
		}
	}

	private IEnumerator WaitForTweenFinished(CHMotionTween tween)
	{
		do
		{
			yield return null;
		}
		while (tween.IsPlaying);
		NextNode();
	}

	private IEnumerator MoveByClip(Transform mover, AnimationClip clip, string path, Transform space, Vector3 clipAnchor, bool exit)
	{
		var baseScale = mover.localScale;
		var start = mover.position;
		var startGap = start - space.TransformPoint(SampleClipPosition(clip, path, 0f));
		for (var t = 0f; mover != null; t += Time.deltaTime)
		{
			var time = Mathf.Min(t, clip.length);
			if (exit)
			{
				mover.position = space.TransformPoint(SampleClipPosition(clip, path, time)) + startGap * (1f - time / clip.length);
			}
			else
			{
				mover.position = clipAnchor + ClipOffset(clip, path, space, time);
			}
			mover.localScale = Vector3.Scale(baseScale, m_clipSampledScale);
			if (time >= clip.length)
			{
				break;
			}
			yield return null;
		}
	}

	// The clip's position at time minus its end position, in world space.
	private Vector3 ClipOffset(AnimationClip clip, string path, Transform space, float time)
	{
		// The end first, so the sampled scale is the one at time.
		var end = SampleClipPosition(clip, path, clip.length);
		return space.TransformVector(SampleClipPosition(clip, path, time) - end);
	}

	private Vector3 SampleClipPosition(AnimationClip clip, string path, float time)
	{
		if (m_clipSampler == null)
		{
			m_clipSampler = new GameObject("ClipSampler");
			m_clipSampler.hideFlags = HideFlags.HideAndDontSave;
		}
		var target = m_clipSampler.transform;
		if (!string.IsNullOrEmpty(path))
		{
			foreach (var part in path.Split('/'))
			{
				var child = target.Find(part);
				if (child == null)
				{
					child = new GameObject(part).transform;
					child.SetParent(target, false);
				}
				target = child;
			}
		}
		// Curves the clip doesn't have keep these values.
		for (var reset = target; reset != null; reset = reset == m_clipSampler.transform ? null : reset.parent)
		{
			reset.localPosition = Vector3.zero;
			reset.localScale = Vector3.one;
		}
		clip.SampleAnimation(m_clipSampler, time);
		m_clipSampledScale = target.localScale;
		return target.localPosition;
	}

	private IEnumerator MoveToPoint(Transform mover, Vector3 from, Vector3 to, float speed, BoneAnimation idleAfter, string walkClip = null, Action arrive = null, float stepPeriod = 0f)
	{
		var duration = speed > 0f ? Vector3.Distance(from, to) / speed : 0f;
		for (var t = 0f; t < duration && mover != null; t += Time.deltaTime)
		{
			mover.position = Vector3.Lerp(from, to, stepPeriod > 0f ? SteppedProgress(t, stepPeriod) * stepPeriod / duration : t / duration);
			yield return null;
		}
		if (mover != null)
		{
			mover.position = to;
		}
		// Idle unless something else (e.g. a clip played on a timed beat) took over from the walk.
		if (idleAfter != null && idleAfter["Idle"] != null && (string.IsNullOrEmpty(walkClip) || idleAfter.IsPlaying(walkClip)))
		{
			idleAfter.Play("Idle", PlayMode.StopSameLayer);
		}
		if (arrive != null && mover != null)
		{
			arrive();
		}
	}

	// A ground shadow (the rig's Shadow texture) under a dropped item, about as wide as it, just behind it.
	private static void AddItemShadow(GameObject item, BoneAnimation bones)
	{
		var atlas = bones.textureAtlases.FirstOrDefault(a => a != null && a.textureNames.Contains("Shadow"));
		var itemRenderer = item.GetComponentInChildren<Renderer>();
		if (atlas == null || itemRenderer == null)
		{
			return;
		}
		var itemBounds = itemRenderer.bounds;
		var shadow = new GameObject(item.name + "_Shadow");
		shadow.transform.SetParent(item.transform.parent, false);
		var mesh = shadow.AddComponent<CHMeshSprite>();
		mesh.m_AtlasType = AtlasTypes.SmoothMoves;
		mesh.m_SmoothMovesAtlas = atlas;
		mesh.m_SpriteName = "Shadow";
		mesh.UpdateSprite(true, true);
		var shadowRenderer = shadow.GetComponent<Renderer>();
		var shadowWidth = shadowRenderer != null ? Mathf.Max(shadowRenderer.bounds.size.x, 1f) : 1f;
		var width = itemBounds.size.x * 0.9f / shadowWidth;
		shadow.transform.localScale = new Vector3(width, width * 0.8f, 1f);
		shadow.transform.position = new Vector3(itemBounds.center.x, itemBounds.min.y + itemBounds.size.y * 0.2f, item.transform.position.z + 0.3f);
		UnityHelper.SetLayerRecusively(shadow, item.layer);
		ItemGroundShadow.Attach(item.transform, shadow);
	}

	// Runs a playing bone clip fast for a frame or two until it reaches the given time, then at normal speed. Setting
	// the time directly would re-apply the clip's own bone textures over any swaps.
	private IEnumerator SkipClipAhead(BoneAnimation rig, string clip, float seconds)
	{
		var state = rig[clip];
		for (var frames = 0; frames < 5 && rig != null && rig.IsPlaying(clip) && state.time < seconds - 0.005f; frames++)
		{
			state.speed = (seconds - state.time) / Mathf.Max(Time.deltaTime, 0.001f);
			yield return null;
		}
		if (rig != null && rig[clip] != null)
		{
			rig[clip].speed = 1f;
		}
	}

	private static Vector3 ParsePathPoint(string text)
	{
		var values = text.Split(',');
		return new Vector3(float.Parse(values[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(values[2], System.Globalization.CultureInfo.InvariantCulture));
	}

	private IEnumerator FollowPath(Transform mover, SmoothPath path, Transform shadow, SmoothPath groundPath, float duration, bool hideShadowAtEnd, BoneAnimation idleAfter, string walkClip)
	{
		for (var t = 0f; t < duration && mover != null; t += Time.deltaTime)
		{
			mover.position = path.At(t / duration);
			if (shadow != null)
			{
				shadow.position = groundPath.At(t / duration);
			}
			yield return null;
		}
		if (mover != null)
		{
			mover.position = path.At(1f);
		}
		if (shadow != null)
		{
			shadow.position = groundPath.At(1f);
			if (hideShadowAtEnd)
			{
				shadow.gameObject.SetActive(false);
			}
		}
		if (idleAfter != null && idleAfter["Idle"] != null && (string.IsNullOrEmpty(walkClip) || idleAfter.IsPlaying(walkClip)))
		{
			idleAfter.Play("Idle", PlayMode.StopSameLayer);
		}
	}

	// A Catmull-Rom curve through points, walked by distance along it.
	public class SmoothPath
	{
		private const int SamplesPerSegment = 16;

		private readonly List<Vector3> m_Samples = new List<Vector3>();

		private readonly List<float> m_Distances = new List<float>();

		public float Length
		{
			get { return m_Distances[m_Distances.Count - 1]; }
		}

		public SmoothPath(List<Vector3> points)
		{
			for (var i = 0; i < points.Count - 1; i++)
			{
				var p0 = points[Mathf.Max(i - 1, 0)];
				var p1 = points[i];
				var p2 = points[i + 1];
				var p3 = points[Mathf.Min(i + 2, points.Count - 1)];
				for (var k = i == 0 ? 0 : 1; k <= SamplesPerSegment; k++)
				{
					var u = (float)k / SamplesPerSegment;
					m_Samples.Add(0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (3f * p1 - p0 - 3f * p2 + p3) * u * u * u));
				}
			}
			m_Distances.Add(0f);
			for (var i = 1; i < m_Samples.Count; i++)
			{
				m_Distances.Add(m_Distances[i - 1] + Vector3.Distance(m_Samples[i - 1], m_Samples[i]));
			}
		}

		public Vector3 At(float fraction)
		{
			var distance = Mathf.Clamp01(fraction) * Length;
			for (var i = 1; i < m_Samples.Count; i++)
			{
				if (m_Distances[i] >= distance)
				{
					var span = m_Distances[i] - m_Distances[i - 1];
					return Vector3.Lerp(m_Samples[i - 1], m_Samples[i], span > 0f ? (distance - m_Distances[i - 1]) / span : 1f);
				}
			}
			return m_Samples[m_Samples.Count - 1];
		}
	}

	// Loops covered by time t when each loop moves only from 1/12 to 7/12 of the way through (eased), as in Move_Carry.
	private static float SteppedProgress(float t, float period)
	{
		var loops = Mathf.Floor(t / period);
		var u = Mathf.InverseLerp(1f / 12f, 7f / 12f, t / period - loops);
		return loops + u * u * (3f - 2f * u);
	}

	private IEnumerator WaitForCoroutineThenNextNode(Coroutine routine)
	{
		yield return routine;
		NextNode();
	}

	// x moves at a constant speed from the start to dipX + endOffsetX; y dips by dip at dipX and rises back to the start
	// height at the end (a parabola on each side of the lowest point). z is kept.
	private IEnumerator MoveArc(Transform mover, float dipX, float dip, float endOffsetX, float duration)
	{
		var start = mover.position;
		var endX = dipX + endOffsetX;
		for (var t = 0f; t <= duration && mover != null; t += Time.deltaTime)
		{
			var x = Mathf.Lerp(start.x, endX, Mathf.Clamp01(t / duration));
			var span = (x - dipX) * (start.x - dipX) >= 0f ? start.x - dipX : endX - dipX;
			var k = Mathf.Abs(span) > 1f ? (x - dipX) / span : 0f;
			mover.position = new Vector3(x, start.y - dip + dip * k * k, start.z);
			yield return null;
		}
		if (mover != null)
		{
			mover.position = new Vector3(endX, start.y, start.z);
		}
	}

	private IEnumerator ZoomCamera(Camera cam, float delta, float time, InterpolationType type, NodeWaitCondition waitCondition)
	{
		var startSize = cam.orthographicSize;
		var percent2 = 0f;
		var currTimeElapsed = 0f;
		do
		{
			currTimeElapsed += Time.smoothDeltaTime;
			percent2 = Mathf.Clamp01(currTimeElapsed / time);
			switch (type)
			{
			case InterpolationType.Linear:
				cam.orthographicSize = startSize + LinearInterpolation(percent2, 0f, delta);
				break;
			case InterpolationType.ExponentialEaseInOut:
				cam.orthographicSize = startSize + ExponentialEaseInOut(percent2, 0f, delta);
				break;
			case InterpolationType.SinusoidalEaseInOut:
				cam.orthographicSize = startSize + ExponentialEaseInOut(percent2, 0f, delta);
				break;
			default:
				DebugLog.Error("undefined InterpolationType in ZoomCamera");
				break;
			}
			yield return null;
		}
		while (currTimeElapsed < time);
		cam.orthographicSize = startSize + delta;
		if (waitCondition == NodeWaitCondition.WaitUntilCompleted)
		{
			NextNode();
		}
	}

	private Transform FindTransformRecursively(Transform parent, string name)
	{
		var text = name;
		text = !text.Contains("(Clone") ? text + "_low" : text.Replace("(Clone)", "_low(Clone)");
		if (parent.name.Equals(name) || parent.name.Equals(text))
		{
			return parent;
		}
		for (var i = 0; i < parent.childCount; i++)
		{
			var transform = FindTransformRecursively(parent.GetChild(i), name);
			if (transform != null)
			{
				return transform;
			}
		}
		return null;
	}

	public void FailSafeReset()
	{
		ScreenElements.Instance.EnableStorySequence(node.customBool);
		StartCoroutine(WaitTillNextNodeAndSetFinished(0f, !node.customBool));
		DIContainerInfrastructure.GetAsynchStatusService().ShowError(DIContainerInfrastructure.GetLocaService().Tr("toast_error_brokencutscene", "We are sorry but an unexpected Error occured in the Cutscene. It will be aborted and reloaded now"));
		if (DIContainerInfrastructure.LocationStateMgr != null)
		{
			DIContainerInfrastructure.LocationStateMgr.m_HadCutsceneError = true;
		}
	}
}
