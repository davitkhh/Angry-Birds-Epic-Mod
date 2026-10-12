using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PathMovingService : MonoBehaviour
{
	public Camera m_Cam;

	// Jumps with a looping animation (the Blues) cover the arc this many times faster than walking it.
	public float m_JumpSpeedMultiplier = 1f;

	private static PathMovingService _instance;

	private Bounds m_CamRect;

	private readonly Dictionary<AnimationClip, Vector2> m_AirborneWindows = new Dictionary<AnimationClip, Vector2>();

	public static PathMovingService Instance
	{
		get
		{
			return _instance;
		}
	}

	public void Awake()
	{
		_instance = this;
	}

	public void WalkAlongPath(List<HotSpotWorldMapViewBase> pathList, GameObject bird, Animation anim, float speed, int index, float delay, MonoBehaviour callbackScript, string callbackFunction, bool mirror = false, string moveAnimation = "Move_Loop", bool ignoreOutOfScreen = false, string jumpAnimation = null)
	{
		StartCoroutine(WalkToTarget(pathList, bird, anim, speed, index, delay, callbackScript, callbackFunction, mirror, moveAnimation, ignoreOutOfScreen, jumpAnimation));
	}

	private IEnumerator WalkToTarget(List<HotSpotWorldMapViewBase> pathList, GameObject movingObject, Animation anim, float speed, int index, float delay, MonoBehaviour callbackScript, string callbackFunction, bool mirror = false, string moveAnimation = "Move_Loop", bool ignoreOutOfScreen = false, string jumpAnimation = null)
	{
		if (delay > 0f)
		{
			yield return new WaitForSeconds(delay);
		}
		else
		{
			yield return null;
		}
		if (pathList == null || pathList.Count == 0)
		{
			DebugLog.Error("[PathMovingService] empty path for " + (movingObject != null ? movingObject.name : "null"));
			if (callbackScript != null && callbackFunction != string.Empty)
			{
				callbackScript.SendMessage(callbackFunction, index);
			}
			yield break;
		}
		var listIndex = GetStartIndex(pathList, 0, ignoreOutOfScreen);
		var splinePosition = 0f;
		var lastIndex = 0;
		var currentPath = GetNewPath(ref listIndex, pathList);
		var reversePath = CalculateSplinePositionIndex(ref splinePosition, listIndex, pathList);
		var currentIndex2 = listIndex;
		if (anim != null)
		{
			anim.CrossFade(moveAnimation);
		}
		var startingOffset = movingObject.transform.position - pathList[0].transform.position;
		yield return StartCoroutine(WalkToPosition(movingObject, pathList[0].transform.position, speed));
		if (movingObject == null)
		{
			yield break;
		}
		if (listIndex < pathList.Count)
		{
			var previousHotspot2 = pathList[0];
			var hotspot2 = pathList[currentIndex2];
			previousHotspot2.HandleMovingObjectVisibility(movingObject, hotspot2);
		}
		do
		{
			if (currentPath == null)
			{
				currentPath = GetNewPath(ref listIndex, pathList);
				reversePath = CalculateSplinePositionIndex(ref splinePosition, listIndex, pathList);
			}
			else
			{
				if (pathList[listIndex].m_jumpPath)
				{
					yield return StartCoroutine(JumpAlongPath(currentPath, reversePath, movingObject, anim, speed, mirror, jumpAnimation ?? moveAnimation, moveAnimation));
					if (movingObject == null)
					{
						yield break;
					}
					// Past the end with no overshoot, so the next segment starts at its beginning.
					splinePosition = !reversePath ? 2f : -1f;
				}
				else
				{
					var lastPosition = movingObject.transform.position;
					movingObject.transform.position = currentPath.GetPositionWorldSpace(splinePosition) + new Vector3(0f, 0f, -20f);
					FaceMovementDirection(movingObject, lastPosition.x, movingObject.transform.position.x, mirror);
					splinePosition = !reversePath ? splinePosition + speed / currentPath.GetLength() * Time.deltaTime : splinePosition - speed / currentPath.GetLength() * Time.deltaTime;
				}
				if (splinePosition > 1f || splinePosition < 0f)
				{
					lastIndex = listIndex + (reversePath ? 1 : 0);
					currentPath = GetNewPath(ref listIndex, pathList);
					currentIndex2 = listIndex + (reversePath ? 1 : 0);
					if (currentIndex2 < pathList.Count)
					{
						if (!IsInFrustrum(pathList[currentIndex2 - 1]) && !ignoreOutOfScreen)
						{
							listIndex = GetStartIndex(pathList, listIndex, ignoreOutOfScreen);
							lastIndex = listIndex + (reversePath ? 1 : 0);
							currentPath = GetNewPath(ref listIndex, pathList);
							currentIndex2 = listIndex + (reversePath ? 1 : 0);
						}
						if (currentIndex2 < pathList.Count)
						{
							var previousHotspot = pathList[lastIndex];
							var hotspot = pathList[currentIndex2];
							hotspot.HandleMovingObjectVisibility(movingObject, hotspot);
						}
					}
					reversePath = CalculateSplinePositionIndex(ref splinePosition, listIndex, pathList);
				}
			}
			yield return null;
			if (movingObject == null)
			{
				yield break;
			}
		}
		while (listIndex < pathList.Count);
		yield return null;
		if (movingObject == null)
		{
			yield break;
		}
		var lastHotspot = pathList[pathList.Count - 1];
		lastHotspot.HandleMovingObjectVisibility(movingObject, lastHotspot);
		var offset = lastHotspot.m_HotSpotPositions.Length <= index ? Vector3.zero : lastHotspot.m_HotSpotPositions[index];
		yield return StartCoroutine(WalkToPosition(movingObject, pathList[pathList.Count - 1].transform.position + offset, speed));
		if (movingObject == null)
		{
			yield break;
		}
		if (anim != null)
		{
			anim.CrossFade("Idle");
		}
		if (callbackScript != null && callbackFunction != string.Empty)
		{
			DebugLog.Log("[PathMovingService] Invoking callback with param " + index);
			if (callbackScript is BaseLocationStateManager)
			{
				(callbackScript as BaseLocationStateManager).m_movementTargetIndex = index;
			}
			callbackScript.SendMessage(callbackFunction, index);
		}
	}

	private IEnumerator JumpAlongPath(RageSplineLite path, bool reversePath, GameObject movingObject, Animation anim, float speed, bool mirror, string jumpAnimation, string moveAnimation)
	{
		var arcDuration = path.GetLength() / Mathf.Max(speed * m_JumpSpeedMultiplier, 0.01f);
		AnimationState jumpState = null;
		if (anim != null)
		{
			if (anim[jumpAnimation] == null)
			{
				jumpAnimation = moveAnimation;
			}
			jumpState = anim[jumpAnimation];
		}
		var start = path.GetPositionWorldSpace(!reversePath ? 0f : 1f);
		var end = path.GetPositionWorldSpace(!reversePath ? 1f : 0f);
		FaceMovementDirection(movingObject, start.x, end.x, mirror);
		var frontZ = GetJumpFrontZ(path);
		var oneShot = jumpState != null && jumpAnimation != moveAnimation && !jumpAnimation.EndsWith("_Loop");
		if (!oneShot)
		{
			if (jumpState != null)
			{
				anim.CrossFade(jumpAnimation);
			}
			var t = 0f;
			while (t < 1f)
			{
				t = Mathf.Min(1f, t + Time.deltaTime / arcDuration);
				SetJumpPosition(movingObject, path, reversePath, t, frontZ);
				yield return null;
				if (movingObject == null)
				{
					yield break;
				}
			}
			SetJumpPosition(movingObject, path, reversePath, 1f);
			yield break;
		}
		// Travel takes exactly as long as the bird is off the ground in its animation; wind-up and recovery play in place.
		var window = GetAirborneWindow(anim, jumpState);
		var takeoffTime = window.x * jumpState.length;
		var airClipTime = (window.y - window.x) * jumpState.length;
		// Leave jumpState.speed alone: SmoothMoves sets each Unity state's speed to the clip's fps, so 1 is not natural speed.
		anim.Stop();
		anim.Play(jumpAnimation);
		while (anim.IsPlaying(jumpAnimation) && jumpState.time < takeoffTime)
		{
			SetJumpPosition(movingObject, path, reversePath, 0f);
			yield return null;
			if (movingObject == null)
			{
				yield break;
			}
		}
		var progress = 0f;
		while (progress < 1f)
		{
			progress = anim.IsPlaying(jumpAnimation) && airClipTime > 0f ? Mathf.Clamp01((jumpState.time - takeoffTime) / airClipTime) : 1f;
			SetJumpPosition(movingObject, path, reversePath, progress, frontZ);
			if (progress < 1f)
			{
				yield return null;
				if (movingObject == null)
				{
					yield break;
				}
			}
		}
		// Landed: back to the normal depth.
		SetJumpPosition(movingObject, path, reversePath, 1f);
		while (anim.IsPlaying(jumpAnimation) && jumpState.time < jumpState.length)
		{
			yield return null;
			if (movingObject == null)
			{
				yield break;
			}
		}
		anim.CrossFade(moveAnimation);
	}

	// frontZ: while airborne, the bird is pulled in front of this depth (see GetJumpFrontZ).
	private static void SetJumpPosition(GameObject movingObject, RageSplineLite path, bool reversePath, float t, float frontZ = float.PositiveInfinity)
	{
		var position = path.GetPositionWorldSpace(!reversePath ? t : 1f - t) + new Vector3(0f, 0f, -20f);
		position.z = Mathf.Min(position.z, frontZ);
		movingObject.transform.position = position;
	}

	// Depth just in front of the WorldMapJumpForeground visuals (with m_BirdsJumpInFront) that the jump passes over, or
	// +infinity when there are none. The area covers the path plus room for the bird and its jump animation's lift.
	private static float GetJumpFrontZ(RageSplineLite path)
	{
		var area = new Bounds(path.GetPositionWorldSpace(0f), Vector3.zero);
		for (var i = 1; i <= 16; i++)
		{
			area.Encapsulate(path.GetPositionWorldSpace(i / 16f));
		}
		area.min -= new Vector3(150f, 50f, 0f);
		area.max += new Vector3(150f, 300f, 0f);
		var frontZ = float.PositiveInfinity;
		foreach (var foreground in WorldMapJumpForeground.Active)
		{
			if (foreground.m_BirdsJumpInFront)
			{
				frontZ = Mathf.Min(frontZ, foreground.GetFrontZ(area));
			}
		}
		return float.IsPositiveInfinity(frontZ) ? frontZ : frontZ - 1f;
	}

	// Normalized (takeoff, landing) times of a clip, found by sampling how high its Body bone rises.
	private Vector2 GetAirborneWindow(Animation anim, AnimationState state)
	{
		Vector2 window;
		if (state.clip != null && m_AirborneWindows.TryGetValue(state.clip, out window))
		{
			return window;
		}
		window = new Vector2(0f, 1f);
		var body = FindBone(anim.transform, "Body") ?? FindBone(anim.transform, "Body_A");
		if (body != null && state.length > 0f)
		{
			var steps = Mathf.Max(2, Mathf.CeilToInt(state.length * 96f));
			var heights = new float[steps + 1];
			anim.Stop();
			state.enabled = true;
			state.weight = 1f;
			for (var i = 0; i <= steps; i++)
			{
				state.time = state.length * i / steps;
				anim.Sample();
				heights[i] = body.localPosition.y;
			}
			state.enabled = false;
			var maxRise = 0f;
			for (var i = 0; i <= steps; i++)
			{
				maxRise = Mathf.Max(maxRise, heights[i] - heights[0]);
			}
			if (maxRise > 0f)
			{
				var threshold = heights[0] + maxRise * 0.1f;
				var first = -1;
				var last = -1;
				for (var i = 0; i <= steps; i++)
				{
					if (heights[i] > threshold)
					{
						if (first < 0)
						{
							first = i;
						}
						last = i;
					}
				}
				window = new Vector2((float)Mathf.Max(0, first - 1) / steps, (float)Mathf.Min(steps, last + 1) / steps);
			}
		}
		if (state.clip != null)
		{
			m_AirborneWindows[state.clip] = window;
		}
		return window;
	}

	private static Transform FindBone(Transform root, string boneName)
	{
		if (root.name == boneName)
		{
			return root;
		}
		for (var i = 0; i < root.childCount; i++)
		{
			var bone = FindBone(root.GetChild(i), boneName);
			if (bone != null)
			{
				return bone;
			}
		}
		return null;
	}

	private static void FaceMovementDirection(GameObject movingObject, float fromX, float toX, bool mirror)
	{
		if (fromX > toX)
		{
			movingObject.transform.localScale = new Vector3(!mirror ? -1f : 1f, 1f, 1f);
		}
		else if (fromX < toX)
		{
			movingObject.transform.localScale = new Vector3(!mirror ? 1f : -1f, 1f, 1f);
		}
	}

	private IEnumerator WalkToPosition(GameObject bird, Vector3 target, float speed)
	{
		var delta = bird.transform.position - target;
		if (delta.magnitude > 1f)
		{
			var targetReached = false;
			do
			{
				yield return null;
				// Cutscenes may destroy a character mid-walk.
				if (bird == null)
				{
					yield break;
				}
				var stepSize = delta.normalized * (speed / 5f * Time.deltaTime);
				if (stepSize.sqrMagnitude > delta.sqrMagnitude)
				{
					targetReached = true;
					bird.transform.position = target;
				}
				else
				{
					delta -= stepSize;
					bird.transform.Translate(-stepSize);
				}
			}
			while (!targetReached);
		}
		else
		{
			bird.transform.position = target;
		}
	}

	private int GetStartIndex(List<HotSpotWorldMapViewBase> pathList, int startAt, bool ignoreOutOfScreen)
	{
		var num = startAt - 1;
		if (startAt < 0 || startAt >= pathList.Count)
		{
			DebugLog.Error("Wrong StartIndex for path search: " + startAt);
			return pathList.Count - 1;
		}
		var zero = Vector2.zero;
		m_CamRect = default(Bounds);
		if (m_Cam)
		{
			zero = new Vector2(m_Cam.orthographicSize * ((float)Screen.width / (float)Screen.height), m_Cam.orthographicSize);
			m_CamRect = new Bounds(m_Cam.transform.position, new Vector3(2f * zero.x, 2f * zero.y));
		}
		if (pathList.Count > 2)
		{
			for (var i = startAt; i < pathList.Count; i++)
			{
				var hotSpotWorldMapViewBase = pathList[i];
				if (RectangleIntersect(m_CamRect, hotSpotWorldMapViewBase.GetBoundingBox()) || ignoreOutOfScreen)
				{
					break;
				}
				num++;
			}
		}
		if (num > 0 && pathList[num] == pathList[num - 1].GetPreviousHotspot())
		{
			num--;
		}
		return num;
	}

	private RageSplineLite GetNewPath(ref int listIndex, List<HotSpotWorldMapViewBase> pathList)
	{
		listIndex++;
		if (listIndex >= pathList.Count)
		{
			return null;
		}
		if (listIndex == 0)
		{
			if (pathList[listIndex].GetPreviousHotspot() != pathList[listIndex + 1])
			{
				return GetNewPath(ref listIndex, pathList);
			}
		}
		else if (listIndex != pathList.Count - 1)
		{
		}
		if (listIndex < pathList.Count - 1 && pathList[listIndex].GetPreviousHotspot() == pathList[listIndex + 1])
		{
			return pathList[listIndex].GetPath();
		}
		if (listIndex > 0 && pathList[listIndex].GetPreviousHotspot() == pathList[listIndex - 1])
		{
			return pathList[listIndex].GetPath();
		}
		return GetNewPath(ref listIndex, pathList);
	}

	private bool CalculateSplinePositionIndex(ref float splinePosition, int listIndex, List<HotSpotWorldMapViewBase> pathList)
	{
		if (listIndex >= pathList.Count)
		{
			return false;
		}
		if (pathList[listIndex].GetPath() == null)
		{
			return false;
		}
		var num = 0f;
		if (listIndex < pathList.Count - 1)
		{
			if (pathList[listIndex].GetPreviousHotspot() == pathList[listIndex + 1])
			{
				num = 1f;
				splinePosition = num - Mathf.Abs(splinePosition) % 1f;
			}
			else
			{
				splinePosition = Mathf.Abs(splinePosition) % 1f;
			}
		}
		else if (listIndex > 0)
		{
			if (pathList[listIndex].GetPreviousHotspot() == pathList[listIndex - 1])
			{
				splinePosition = Mathf.Abs(splinePosition) % 1f;
			}
			else
			{
				num = 1f;
				splinePosition = num - Mathf.Abs(splinePosition) % 1f;
			}
		}
		return num == 1f;
	}

	private bool RectangleIntersect(Bounds rectA, Bounds rectB)
	{
		var bounds = new Bounds(new Vector3(rectA.center.x, rectA.center.y, 100f), new Vector3(rectA.size.x, rectA.size.y, 100f));
		var bounds2 = new Bounds(new Vector3(rectB.center.x, rectB.center.y, 100f), new Vector3(rectB.size.x, rectB.size.y, 100f));
		return bounds.Intersects(bounds2);
	}

	private bool IsInFrustrum(HotSpotWorldMapViewBase spot)
	{
		var zero = Vector2.zero;
		m_CamRect = default(Bounds);
		if (m_Cam)
		{
			zero = new Vector2(m_Cam.orthographicSize * ((float)Screen.width / (float)Screen.height), m_Cam.orthographicSize);
			m_CamRect = new Bounds(m_Cam.transform.position, new Vector3(2f * zero.x, 2f * zero.y));
		}
		if (RectangleIntersect(m_CamRect, spot.GetBoundingBox()))
		{
			return true;
		}
		return false;
	}
}
