using ABH.GameDatas;
using ABH.GameDatas.Interfaces;
using System.Collections.Generic;
using SmoothMoves;
using UnityEngine;

// A drone pig that bounces on a coil spring instead of flying. The propeller is hidden, a spring hangs from the flat
// bottom of the pod, and the pod rides a spring-loaded-pendulum hopper simulation (a pogo stick):
//   - gravity pulls the pod down; the spring (Hooke's law + viscous damping along its length) can only push, and only
//     while its foot is planted on the ground. Once the spring is longer than its rest length the foot lifts off;
//   - integration is semi-implicit Euler at a fixed 1/600 s step, so the motion doesn't depend on the frame rate;
//   - an actuator in series with the spring keeps it hopping: at the bottom of each stance it extends the spring's seat
//     by just enough to store the energy of the hop it wants next, and retracts in the air. Damping losses are
//     corrected hop by hop from the measured apex. While defeated or stunned the actuator is off, so the bounce dies
//     out on the damping alone and the pod settles at the spring's static sag;
//   - travel is by hops only. The foot stays planted for the whole stance while the pod vaults over it; in the air the
//     spring swings forward to the neutral point (Raibert's foot placement: half the stance's travel ahead), so the
//     pod keeps its speed. At each stance bottom the hop planner picks the next flight's length and speed: the spring
//     can't lean further than MaxLean, which caps the speed over the ground, so long distances take several (higher)
//     hops. Movement tweens (CHMotionTween) of a spring drone are stretched to the time those hops take (AdjustTween),
//     so the battle waits for the drone to arrive.
// The pod's ground position is a world-space point that hops after the character's transform; the difference is drawn
// as an offset of the pod bones and the shadow. The hover bob of Idle/Move clips is replaced by the physics; other
// clips (attacks, hits) play on top of it.
// Lengths are rig units (pixels of the drone's art); PixelsPerMeter sets the scale gravity works on.
[DefaultExecutionOrder(80)]
public class SpringDrone : MonoBehaviour
{
	// Pigs (PigBalancingData NameIds) that ride a spring.
	private static readonly HashSet<string> s_PigIds = new HashSet<string> { "pig_drone_tinker_spring" };

	// Clips whose motion of the pod is only the flying bob/wobble (replaced by the physics).
	private static readonly HashSet<string> s_HoverClips = new HashSet<string> { "Idle", "Idle_Fly", "Move_Once", "Move_Loop" };

	// Clips during which the actuator stops pumping.
	private static readonly string[] s_LimpClips = { "Defeated", "Stunned" };

	// Bones that move with the pod (children of Root). Shadow stays on the ground.
	private static readonly string[] s_PodBones = { "Body", "Headgear", "MainHand", "OffHand", "Center", "Bottom" };

	// Bones hidden on a spring drone (it doesn't fly).
	private static readonly string[] s_HiddenBones = { "PropellerShaft" };

	public float PixelsPerMeter = 200f;

	// Unloaded spring length, from the pod's bottom to the foot.
	public float RestLength = 72f;

	// Coils touch: the spring can't get shorter.
	public float SolidLength = 20f;

	// How far the pod's weight compresses the spring at rest (sets stiffness: k/m = g / StaticSag). 6 = twice as stiff
	// as the first spring (12): shorter, snappier stances.
	public float StaticSag = 6f;

	public float DampingRatio = 0.07f;

	// Clearance of the spring's foot at the top of each hop in place; also the lowest hop when travelling.
	public float HopHeight = 46f;

	// Highest hop the planner uses to cover distance.
	public float MaxHopHeight = 160f;

	// Furthest the spring leans from vertical while its foot is planted (degrees); caps the speed over the ground.
	public float MaxLean = 38f;

	// Closer than this to where it should stand, the drone hops in place.
	public float ArriveDistance = 4f;

	public float MaxExtension = 60f;

	// Velocity kept (reversed) when the spring bottoms out at its solid length.
	public float SolidRestitution = 0.25f;

	// Chunky, like the drone's own parts: few thick coils.
	public int Coils = 4;

	public float CoilRadius = 17f;

	// Full width of the wire, outline included.
	public float WireWidth = 8f;

	// Spring mount on the pod, in Body bone space (the flat bottom of the pod's art).
	public Vector2 MountPoint = new Vector2(0f, -40f);

	// The coil starts this far up inside the pod's flat bottom, so its top end is hidden under the pod (the spring
	// draws behind the rig). Only the drawing; the spring's physical length is from the mount.
	public float TuckDepth = 9f;

	public enum SpringFinish
	{
		// The drone's own lavender steel.
		Lavender = 0,
		// Shiny grey metal, from the game's knight steel.
		Steel = 1
	}

	public SpringFinish Finish;

	// Spring draws behind the rig (larger z is further back).
	public float DepthBehind = 2f;

	// The regular drone's shadow: its Idle lifts the pod 10 units (Bottom 40 -> 50) while the shadow goes 1.0 -> 0.9
	// from full size at its resting hover. Same here from where this drone rests: full size sitting on the spring
	// (static sag), this much smaller per unit above it.
	public float ShadowShrinkPerUnit = 0.01f;

	public float MinShadow = 0.12f;

	public float MaxShadow = 1.2f;

	private const float Step = 1f / 600f;

	private const float GroundY = 5f;

	private static Shader s_Shader;

	private static bool s_Hooked;

	private Animation m_Animation;

	private Transform m_Rig;

	private Transform m_Root;

	private Transform m_Body;

	private Transform m_Bottom;

	private Transform m_Shadow;

	private Transform m_ShadowSprite;

	private Transform[] m_Pod;

	private Transform[] m_Hidden;

	private Vector3[] m_Base;

	private Vector3[] m_Written;

	private Vector3 m_ShadowBase;

	private Vector3 m_ShadowWritten;

	private Vector3 m_ShadowScaleBase;

	private Vector3 m_ShadowScaleWritten;

	private Vector3 m_ShadowRestScale;

	private bool m_HasWritten;

	private Vector2 m_BodyRest;

	private float m_BottomRestY;

	private float m_RestHeight;

	// Vertical state: height of the mount above the ground under the pod, and its velocity (rig units, seconds).
	private float m_Height;

	private float m_Velocity;

	private float m_Extension;

	private float m_HopBias;

	private float m_HopTarget;

	private float m_Accumulator;

	// Ground-plane state, in world units: where the pod is over the ground, its velocity, and the planted foot.
	private Vector2 m_Ground;

	private Vector2 m_GroundVelocity;

	private Vector2 m_VelocityOut;

	private float m_RampLeft;

	private Vector2 m_Settle;

	private Vector2 m_Foot;

	// Foot offset from the pod while in the air (rig units, world-aligned ground plane).
	private Vector2 m_Swing;

	private bool m_InStance;

	private float m_StanceClock;

	private float m_StanceTime;

	private float m_PreviousLength;

	private float m_PreviousLengthRate;

	// World units per rig unit, this frame.
	private float m_Scale = 1f;

	private CHMotionTween m_Tween;

	private Vector2 m_TweenEnd;

	// A clip (an attack that starts its approach on a Movement event) held still while the drone hops there, so its
	// Impact comes when the drone has arrived. Raw Unity speeds are kept and put back as they were.
	private readonly List<KeyValuePair<AnimationState, float>> m_Held = new List<KeyValuePair<AnimationState, float>>();

	private float m_HoldUntil;

	private Mesh m_Mesh;

	// The shadow, redrawn behind the spring: the rig's own shadow is part of the rig mesh, which draws in front of the
	// spring, so it's hidden and this copy (same atlas sprite, size and place) lies on the ground under the spring.
	private Mesh m_ShadowMesh;

	private Vector2 m_ShadowSize;

	private Rect m_ShadowUV;

	private readonly Vector3[] m_ShadowCorners = new Vector3[4];

	private Material m_Material;

	private readonly List<Vector3> m_Vertices = new List<Vector3>();

	private readonly List<Color> m_Colors = new List<Color>();

	private readonly List<int> m_Triangles = new List<int>();

	private readonly List<Vector3> m_Helix = new List<Vector3>();

	private readonly List<float> m_HelixDepth = new List<float>();

	// Cel-shaded like the drone's art: each part is drawn as bands from its outline inward, each band offset toward the
	// light (up) by Offset. Width and Offset are fractions of the wire's thickness. Colours are sampled from the drone's
	// atlas: plum outline, lavender steel, pinkish glint (Body.png).
	private struct Band
	{
		public float Width;

		public float Offset;

		public Color Color;

		public Band(float width, float offset, int r, int g, int b)
		{
			Width = width;
			Offset = offset;
			Color = new Color(r / 255f, g / 255f, b / 255f, 1f);
		}
	}

	private static readonly Band[] FrontWire =
	{
		new Band(1f, 0f, 85, 69, 101), new Band(0.72f, -0.04f, 89, 98, 146), new Band(0.5f, 0.06f, 127, 127, 169),
		new Band(0.28f, 0.14f, 159, 159, 193), new Band(0.1f, 0.2f, 241, 217, 231)
	};

	private static readonly Band[] BackWire = { new Band(1f, 0f, 85, 69, 101), new Band(0.72f, 0f, 60, 56, 98), new Band(0.34f, 0.1f, 89, 98, 146) };

	// Shiny grey metal, the game's steel (Red's Knight helmet ramp: teal-navy outline 16,33,41 up to 144,168,168; white
	// glint as on the Royal Guard helmet). Chrome look: a bright top with a hard glint, a dark core line, and the
	// ground's light reflected back along the bottom edge.
	private static readonly Band[] SteelFrontWire =
	{
		new Band(1f, 0f, 16, 33, 41), new Band(0.78f, -0.03f, 72, 90, 92), new Band(0.6f, 0.05f, 144, 168, 168), new Band(0.14f, -0.13f, 64, 84, 88),
		new Band(0.26f, 0.17f, 200, 216, 216), new Band(0.1f, 0.24f, 248, 248, 248)
	};

	private static readonly Band[] SteelBackWire = { new Band(1f, 0f, 16, 33, 41), new Band(0.72f, 0f, 48, 66, 70), new Band(0.34f, 0.1f, 96, 116, 118) };

	// Combatant Forge pigs pick the spring as a variant of the drone: their class item is named Forge_<id>_Spring.
	private const string ForgeVariantPrefix = "Forge_";

	private const string ForgeVariantSuffix = "_Spring";

	// Forge_<id>_Springsteel: the same drone with the shiny grey spring.
	private const string ForgeSteelSuffix = "_Springsteel";

	public static bool IsSpringDrone(ICharacter model)
	{
		var pig = model as PigGameData;
		if (pig == null || pig.BalancingData == null)
		{
			return false;
		}
		return s_PigIds.Contains(pig.BalancingData.NameId) || IsSpringItem(pig.ClassItem) || IsSpringItem(pig.ClassSkin);
	}

	private static bool IsSpringItem(IInventoryItemGameData item)
	{
		var name = item == null ? null : item.ItemAssetName;
		return !string.IsNullOrEmpty(name) && name.StartsWith(ForgeVariantPrefix) && (name.EndsWith(ForgeVariantSuffix) || name.EndsWith(ForgeSteelSuffix));
	}

	private static bool IsSteelItem(IInventoryItemGameData item)
	{
		var name = item == null ? null : item.ItemAssetName;
		return !string.IsNullOrEmpty(name) && name.StartsWith(ForgeVariantPrefix) && name.EndsWith(ForgeSteelSuffix);
	}

	public static SpringFinish FinishOf(ICharacter model)
	{
		var pig = model as PigGameData;
		return pig != null && (IsSteelItem(pig.ClassItem) || IsSteelItem(pig.ClassSkin)) ? SpringFinish.Steel : SpringFinish.Lavender;
	}

	public static SpringDrone Attach(CharacterAssetController asset, ICharacter model)
	{
		if (asset == null || asset.HasNoBoneAnimation || !IsSpringDrone(model))
		{
			return null;
		}
		var drone = Attach(asset.m_BoneAnimation);
		if (drone != null)
		{
			drone.Finish = FinishOf(model);
		}
		return drone;
	}

	public static SpringDrone Attach(BoneAnimation bones)
	{
		if (bones == null || bones.GetComponent<SpringDrone>() != null)
		{
			return null;
		}
		var root = bones.GetBoneTransform("Root");
		var body = bones.GetBoneTransform("Body");
		var animation = bones.GetComponent<Animation>();
		if (root == null || body == null || animation == null)
		{
			return null;
		}
		if (!s_Hooked)
		{
			s_Hooked = true;
			CHMotionTween.AdjustDuration += AdjustTween;
		}
		var drone = bones.gameObject.AddComponent<SpringDrone>();
		drone.Init(bones, root, body, animation);
		return drone;
	}

	// CHMotionTween.Play: a tween that moves a spring drone takes as long as the drone's hops to its end point.
	private static float AdjustTween(CHMotionTween tween, Vector3 start, Vector3 end, float duration)
	{
		var drone = tween.GetComponentInChildren<SpringDrone>();
		return drone == null || !drone.isActiveAndEnabled ? duration : Mathf.Max(duration, drone.PlanMove(tween, end));
	}

	private void Init(BoneAnimation bones, Transform root, Transform body, Animation animation)
	{
		m_Animation = animation;
		m_Rig = bones.transform;
		m_Root = root;
		m_Body = body;
		m_Bottom = bones.GetBoneTransform("Bottom");
		m_Shadow = bones.GetBoneTransform("Shadow");
		m_ShadowSprite = m_Shadow != null ? m_Shadow.Find("Shadow_Sprite") : null;
		// This drone's shadow follows its real height (below); the generic hop shrink would fight it.
		var airborne = bones.GetComponent<AirborneShadow>();
		if (airborne != null)
		{
			Destroy(airborne);
		}
		var pod = new List<Transform>();
		foreach (var name in s_PodBones)
		{
			var bone = bones.GetBoneTransform(name);
			if (bone != null && bone.parent == root)
			{
				pod.Add(bone);
			}
		}
		m_Pod = pod.ToArray();
		m_Base = new Vector3[m_Pod.Length];
		m_Written = new Vector3[m_Pod.Length];
		var hidden = new List<Transform>();
		foreach (var name in s_HiddenBones)
		{
			var bone = bones.GetBoneTransform(name);
			if (bone != null)
			{
				hidden.Add(bone);
			}
		}
		m_Hidden = hidden.ToArray();
		// Attached fresh from the prefab: the pod is in its rest pose.
		m_BodyRest = body.localPosition;
		m_BottomRestY = m_Bottom != null ? m_Bottom.localPosition.y : m_BodyRest.y + MountPoint.y;
		m_RestHeight = m_BodyRest.y + MountPoint.y - GroundY;
		if (m_ShadowSprite != null)
		{
			var rest = m_ShadowSprite.localScale;
			m_ShadowRestScale = new Vector3(Mathf.Abs(rest.x) < 0.01f ? 1f : rest.x, Mathf.Abs(rest.y) < 0.01f ? 1f : rest.y, rest.z);
		}
		// Drop in from somewhere in the hop so several spring drones don't bounce in step.
		m_Height = RestLength + Random.Range(0.2f, 1f) * HopHeight;
		m_Velocity = 0f;
		m_HopTarget = HopHeight;
		// Starting guess for the energy the damping takes per hop (refined at every apex).
		m_HopBias = 0.25f * HopHeight;
		m_StanceTime = Mathf.PI / Mathf.Sqrt(Stiffness);
		m_Scale = WorldPerRig();
		m_Ground = RigGround();
		m_Foot = m_Ground;

		if (s_Shader == null)
		{
			s_Shader = Shader.Find("Sprites/Default");
		}
		// The rig's own renderer, looked up before the spring adds one.
		var rigRenderer = bones.GetComponentInChildren<Renderer>();
		var spring = new GameObject("Spring");
		spring.layer = bones.gameObject.layer;
		spring.transform.SetParent(root, false);
		spring.transform.localPosition = new Vector3(0f, 0f, DepthBehind);
		m_Mesh = new Mesh { name = "Spring" };
		m_Mesh.MarkDynamic();
		spring.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
		var renderer = spring.AddComponent<MeshRenderer>();
		m_Material = new Material(s_Shader);
		if (rigRenderer != null && rigRenderer.sharedMaterial != null)
		{
			m_Material.renderQueue = rigRenderer.sharedMaterial.renderQueue;
		}
		renderer.sharedMaterial = m_Material;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		CreateShadow(bones, root);
	}

	private void CreateShadow(BoneAnimation bones, Transform root)
	{
		if (m_ShadowSprite == null || bones.textureAtlases == null)
		{
			return;
		}
		foreach (var atlas in bones.textureAtlases)
		{
			var index = atlas == null ? -1 : atlas.textureNames.IndexOf("Shadow");
			if (index < 0 || atlas.material == null || index >= atlas.uvs.Count || index >= atlas.textureSizes.Count)
			{
				continue;
			}
			m_ShadowUV = atlas.uvs[index];
			m_ShadowSize = atlas.textureSizes[index];
			var shadow = new GameObject("SpringShadow");
			shadow.layer = bones.gameObject.layer;
			shadow.transform.SetParent(root, false);
			// Behind the spring (larger z is further back).
			shadow.transform.localPosition = new Vector3(0f, 0f, DepthBehind + 1f);
			m_ShadowMesh = new Mesh { name = "SpringShadow" };
			m_ShadowMesh.MarkDynamic();
			m_ShadowMesh.vertices = new Vector3[4];
			m_ShadowMesh.uv = new[]
			{
				new Vector2(m_ShadowUV.xMin, m_ShadowUV.yMin), new Vector2(m_ShadowUV.xMax, m_ShadowUV.yMin),
				new Vector2(m_ShadowUV.xMax, m_ShadowUV.yMax), new Vector2(m_ShadowUV.xMin, m_ShadowUV.yMax)
			};
			m_ShadowMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
			shadow.AddComponent<MeshFilter>().sharedMesh = m_ShadowMesh;
			var renderer = shadow.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = atlas.material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			return;
		}
	}

	// Lays the copy where the rig's shadow sprite is, at the scale it would have, and hides the rig's one.
	private void PlaceShadow(Vector3 scale)
	{
		if (m_ShadowMesh == null)
		{
			m_ShadowSprite.localScale = scale;
			m_ShadowScaleWritten = scale;
			return;
		}
		m_ShadowSprite.localScale = Vector3.zero;
		m_ShadowScaleWritten = Vector3.zero;
		var parent = m_ShadowSprite.parent;
		var centre = m_Root.InverseTransformPoint(m_ShadowSprite.position);
		var across = m_Root.InverseTransformVector(parent.TransformVector(m_ShadowSprite.localRotation * new Vector3(m_ShadowSize.x * 0.5f * scale.x, 0f, 0f)));
		var up = m_Root.InverseTransformVector(parent.TransformVector(m_ShadowSprite.localRotation * new Vector3(0f, m_ShadowSize.y * 0.5f * scale.y, 0f)));
		centre.z = 0f;
		across.z = 0f;
		up.z = 0f;
		m_ShadowCorners[0] = centre - across - up;
		m_ShadowCorners[1] = centre + across - up;
		m_ShadowCorners[2] = centre + across + up;
		m_ShadowCorners[3] = centre - across + up;
		m_ShadowMesh.vertices = m_ShadowCorners;
		m_ShadowMesh.RecalculateBounds();
	}

	private float Gravity
	{
		get { return 9.81f * PixelsPerMeter; }
	}

	// Spring stiffness and damping per unit of pod mass.
	private float Stiffness
	{
		get { return Gravity / Mathf.Max(1f, StaticSag); }
	}

	private float Damping
	{
		get { return 2f * DampingRatio * Mathf.Sqrt(Stiffness); }
	}

	// Fastest the pod can go over the ground: the planted foot can sweep from MaxLean ahead to MaxLean behind in a stance.
	private float MaxSpeed
	{
		get { return 2f * RestLength * Mathf.Sin(MaxLean * Mathf.Deg2Rad) / Mathf.Max(0.05f, m_StanceTime); }
	}

	private float FlightTime(float hop)
	{
		return 2f * Mathf.Sqrt(2f * Mathf.Max(0f, hop) / Gravity);
	}

	private float WorldPerRig()
	{
		var scale = Mathf.Abs(m_Root.lossyScale.y);
		return scale > 1e-6f ? scale : 1f;
	}

	private Vector2 RigGround()
	{
		var p = m_Rig.position;
		return new Vector2(p.x, p.y);
	}

	private bool Pumping()
	{
		foreach (var clip in s_LimpClips)
		{
			if (m_Animation.IsPlaying(clip))
			{
				return false;
			}
		}
		return true;
	}

	// Where the pod is headed, in world units: the end of the tween moving it, else where the character stands.
	private Vector2 Target()
	{
		return m_Tween != null && m_Tween.IsPlaying ? m_TweenEnd : RigGround();
	}

	// Next flight's hop height and ground speed (rig units) to cover the distance left, given the current ground speed.
	// From a stance bottom, the pod covers v0*ts/4 while the speed ramps, then each hop (flight + stance) at v1, and
	// v1*ts/4 more while stopping on the last stance: so per hop v1*(tf + ts), the total minus v0*ts/4.
	// A last bit shorter than a small lean is covered without another hop: the pod sweeps it over the planted foot
	// (settle, rig units per second, for the rest of the stance).
	private void PlanHop(Vector2 toTarget, Vector2 v0, out float hop, out Vector2 v1, out Vector2 settle)
	{
		var ts = m_StanceTime;
		var rest = toTarget - v0 * (ts * 0.25f);
		var distance = rest.magnitude;
		settle = Vector2.zero;
		if (distance < ArriveDistance)
		{
			hop = HopHeight;
			v1 = Vector2.zero;
			return;
		}
		if (distance < RestLength * Mathf.Sin(MaxLean * Mathf.Deg2Rad) * 0.6f)
		{
			hop = HopHeight;
			v1 = Vector2.zero;
			settle = rest / (ts * 0.5f);
			return;
		}
		var vmax = MaxSpeed;
		var tfMin = FlightTime(HopHeight);
		var tfMax = FlightTime(MaxHopHeight);
		var hops = Mathf.Max(1, Mathf.CeilToInt(distance / (vmax * (tfMax + ts))));
		var each = distance / hops;
		var tf = Mathf.Clamp(each / vmax - ts, tfMin, tfMax);
		hop = Gravity * tf * tf / 8f;
		v1 = rest / distance * Mathf.Min(vmax, each / (tf + ts));
	}

	// How long the hops to end will take from the current state (seconds); the move starts now.
	private float PlanMove(CHMotionTween tween, Vector3 end)
	{
		m_Scale = WorldPerRig();
		m_Tween = tween;
		m_TweenEnd = new Vector2(end.x, end.y);
		ReleaseHeld();
		var toTarget = (m_TweenEnd - m_Ground) / m_Scale;
		if (toTarget.magnitude < ArriveDistance)
		{
			return 0f;
		}
		var g = Gravity;
		var ts = m_StanceTime;
		// Time to the next stance bottom, where the first hop is planned.
		float wait;
		if (m_InStance)
		{
			wait = m_PreviousLengthRate < 0f ? Mathf.Max(0f, ts * 0.5f - m_StanceClock) : ts - m_StanceClock + FlightTime(m_HopTarget) + ts * 0.5f;
		}
		else
		{
			var above = Mathf.Max(0f, m_Height - RestLength);
			wait = (m_Velocity + Mathf.Sqrt(m_Velocity * m_Velocity + 2f * g * above)) / g + ts * 0.5f;
		}
		float hop;
		Vector2 v1;
		Vector2 settle;
		PlanHop(toTarget, Vector2.zero, out hop, out v1, out settle);
		if (v1 == Vector2.zero)
		{
			return wait;
		}
		var speed = v1.magnitude;
		var hops = speed > 0f ? toTarget.magnitude / (speed * (FlightTime(hop) + ts)) : 0f;
		var time = wait + Mathf.Ceil(hops - 0.01f) * (FlightTime(hop) + ts) + ts * 0.5f;
		HoldActionClips(time);
		return time;
	}

	private void HoldActionClips(float time)
	{
		foreach (AnimationState state in m_Animation)
		{
			if (state.enabled && state.weight > 0f && !s_HoverClips.Contains(state.name) && state.speed != 0f)
			{
				m_Held.Add(new KeyValuePair<AnimationState, float>(state, state.speed));
				state.speed = 0f;
			}
		}
		m_HoldUntil = Time.time + time + 1f;
	}

	private void ReleaseHeld()
	{
		foreach (var held in m_Held)
		{
			if (held.Key != null && held.Key.speed == 0f)
			{
				held.Key.speed = held.Value;
			}
		}
		m_Held.Clear();
	}

	private void ReleaseHeldOnArrival()
	{
		if (m_Held.Count == 0)
		{
			return;
		}
		var arrived = (m_TweenEnd - m_Ground).magnitude / m_Scale < ArriveDistance * 2f && m_InStance;
		if (arrived || Time.time > m_HoldUntil || !(m_Tween != null && m_Tween.IsPlaying) && (RigGround() - m_Ground).magnitude / m_Scale < ArriveDistance * 2f)
		{
			ReleaseHeld();
		}
	}

	private void Simulate(float dt, bool pumping)
	{
		var g = Gravity;
		var k = Stiffness;
		var c = Damping;
		var s = m_Scale;
		var previousVelocity = m_Velocity;
		var acceleration = -g;
		var rest = RestLength + m_Extension;
		var velocityRig = m_GroundVelocity / s;

		if (m_InStance)
		{
			m_StanceClock += dt;
			var lean = (m_Ground - m_Foot) / s;
			// A foot dragged past the furthest lean slides along (the pod outran it).
			var maxLean = RestLength * Mathf.Sin(60f * Mathf.Deg2Rad);
			if (lean.magnitude > maxLean)
			{
				lean = lean.normalized * maxLean;
				m_Foot = m_Ground - lean * s;
			}
			var length = Mathf.Sqrt(lean.sqrMagnitude + m_Height * m_Height);
			var rate = (length - m_PreviousLength) / dt;
			var force = k * (rest - length) - c * rate;
			if (force <= 0f)
			{
				// Lift-off: the spring is back at its length and the ground can't pull.
				m_InStance = false;
				m_StanceTime = Mathf.Clamp(m_StanceClock, 0.08f, 1f);
				m_Swing = -lean;
				// The push is over: whatever speed change this stance was making is done (a stiff spring can leave
				// before the planned half stance has passed).
				if (m_RampLeft > 0f)
				{
					m_Ground += m_Settle * (s * m_RampLeft);
					m_GroundVelocity = m_VelocityOut;
					m_RampLeft = 0f;
				}
			}
			else
			{
				acceleration += force * m_Height / Mathf.Max(1f, length);
				if (m_RampLeft > 0f)
				{
					m_GroundVelocity += (m_VelocityOut - m_GroundVelocity) * Mathf.Min(1f, dt / m_RampLeft);
					m_Ground += m_Settle * (s * dt);
					m_RampLeft -= dt;
				}
				if (m_PreviousLengthRate < 0f && rate >= 0f)
				{
					StanceBottom(length, pumping);
				}
			}
			m_PreviousLength = length;
			m_PreviousLengthRate = rate;
		}
		else
		{
			if (m_Extension != 0f && m_Height > RestLength + Mathf.Max(0f, m_Extension))
			{
				// In the air the actuator goes back to neutral.
				m_Extension = 0f;
			}
			// Swing the foot to the neutral point: half a stance's travel ahead, as far as the spring can lean.
			var reach = RestLength * Mathf.Sin(MaxLean * Mathf.Deg2Rad);
			var want = Vector2.ClampMagnitude(velocityRig * (m_StanceTime * 0.5f), reach);
			m_Swing += (want - m_Swing) * (1f - Mathf.Exp(-dt / 0.05f));
			var reachDown = Mathf.Sqrt(Mathf.Max(0f, RestLength * RestLength - m_Swing.sqrMagnitude));
			if (m_Velocity < 0f && m_Height <= reachDown)
			{
				// Touch-down: the foot is planted where it is.
				m_InStance = true;
				m_StanceClock = 0f;
				m_Foot = m_Ground + m_Swing * s;
				m_PreviousLength = Mathf.Sqrt(m_Swing.sqrMagnitude + m_Height * m_Height);
				m_PreviousLengthRate = -1f;
			}
		}

		m_Velocity += acceleration * dt;
		m_Height += m_Velocity * dt;
		m_Ground += m_GroundVelocity * dt;
		if (m_Height < SolidLength)
		{
			m_Height = SolidLength;
			if (m_Velocity < 0f)
			{
				m_Velocity = -m_Velocity * SolidRestitution;
			}
		}
		if (!pumping)
		{
			m_Extension = 0f;
		}
		if (!m_InStance && previousVelocity > 0f && m_Velocity <= 0f && pumping)
		{
			// Apex: correct the next hop for the energy damping took.
			var error = m_HopTarget - (m_Height - RestLength);
			m_HopBias = Mathf.Clamp(m_HopBias + 0.8f * error, -HopHeight, MaxHopHeight);
		}
	}

	// Bottom of a stance: plan the next hop, start changing ground speed, and load the spring for it.
	private void StanceBottom(float length, bool pumping)
	{
		var s = m_Scale;
		float hop;
		Vector2 v1;
		Vector2 settle;
		if (pumping)
		{
			PlanHop((Target() - m_Ground) / s, m_GroundVelocity / s, out hop, out v1, out settle);
		}
		else
		{
			hop = 0f;
			v1 = Vector2.zero;
			settle = Vector2.zero;
		}
		m_Settle = settle;
		m_VelocityOut = v1 * s;
		m_RampLeft = m_StanceTime * 0.5f;
		m_HopTarget = hop;
		if (!pumping)
		{
			return;
		}
		// Energy per unit mass at the top of the hop: g * (rest length + clearance); now: height, upward speed, spring.
		var g = Gravity;
		var k = Stiffness;
		// A leaning spring spends part of its push sideways: give it that much more.
		var leanOut = Mathf.Asin(Mathf.Min(0.9f, v1.magnitude * m_StanceTime * 0.5f / RestLength));
		var need = (g * (RestLength + hop + m_HopBias) - g * m_Height - 0.5f * m_Velocity * m_Velocity) / Mathf.Max(0.5f, Mathf.Cos(leanOut));
		var compressed = Mathf.Sqrt(2f * Mathf.Max(0f, need) / k);
		var current = RestLength + m_Extension - length;
		// The actuator can also give way (shorten the seat) to soak up a landing that carries more than the next hop needs.
		m_Extension = Mathf.Clamp(m_Extension + (compressed - current), -0.5f * MaxExtension, MaxExtension);
	}

	private float HoverWeight()
	{
		var hover = 0f;
		var total = 0f;
		foreach (AnimationState state in m_Animation)
		{
			if (!state.enabled || state.weight <= 0f)
			{
				continue;
			}
			total += state.weight;
			if (s_HoverClips.Contains(state.name))
			{
				hover += state.weight;
			}
		}
		return total > 0f ? hover / total : 0f;
	}

	private static Vector3 Animated(Vector3 current, Vector3 written, ref Vector3 animated, bool hasWritten)
	{
		if (!hasWritten || current != written)
		{
			animated = current;
		}
		return animated;
	}

	private void LateUpdate()
	{
		if (m_Root == null || m_Body == null)
		{
			return;
		}
		var pumping = Pumping();
		m_Scale = WorldPerRig();
		if (!(m_Tween != null && m_Tween.IsPlaying) && (RigGround() - m_Ground).magnitude / m_Scale > 600f)
		{
			// Placed somewhere new without a move (spawned, reset): start there.
			m_Ground = RigGround();
			m_GroundVelocity = Vector2.zero;
			m_VelocityOut = Vector2.zero;
			m_Foot = m_Ground;
		}
		m_Accumulator += Mathf.Min(Time.deltaTime, 0.1f);
		while (m_Accumulator >= Step)
		{
			Simulate(Step, pumping);
			m_Accumulator -= Step;
		}
		ReleaseHeldOnArrival();

		// What the animation wrote this frame; a bone still holding our last write wasn't animated.
		for (var i = 0; i < m_Pod.Length; i++)
		{
			Animated(m_Pod[i].localPosition, m_Written[i], ref m_Base[i], m_HasWritten);
		}
		var bodyIndex = System.Array.IndexOf(m_Pod, m_Body);
		var body = bodyIndex >= 0 ? (Vector2)m_Base[bodyIndex] : m_BodyRest;
		var hover = HoverWeight() * (body - m_BodyRest);
		// Where the pod is over the ground, relative to the character, in Root space.
		var away = m_Root.InverseTransformVector(new Vector3(m_Ground.x - m_Rig.position.x, m_Ground.y - m_Rig.position.y, 0f));
		var offset = new Vector3(away.x - hover.x, away.y + m_Height - m_RestHeight - hover.y, 0f);
		for (var i = 0; i < m_Pod.Length; i++)
		{
			var position = m_Base[i] + offset;
			m_Pod[i].localPosition = position;
			m_Written[i] = position;
		}
		if (m_Shadow != null)
		{
			var position = Animated(m_Shadow.localPosition, m_ShadowWritten, ref m_ShadowBase, m_HasWritten) + new Vector3(away.x, away.y, 0f);
			m_Shadow.localPosition = position;
			m_ShadowWritten = position;
		}
		if (m_ShadowSprite != null)
		{
			// The regular drone's ratio, from how high the pod really is (bounce + clip) above where it rests on the spring.
			var animated = Animated(m_ShadowSprite.localScale, m_ShadowScaleWritten, ref m_ShadowScaleBase, m_HasWritten);
			var shape = Vector3.Lerp(animated, m_ShadowRestScale, HoverWeight());
			var restingLift = RestLength - StaticSag - m_RestHeight;
			var rise = (m_Bottom != null ? m_Bottom.localPosition.y - m_BottomRestY - away.y : m_Height - m_RestHeight) - restingLift;
			var size = Mathf.Clamp(1f - ShadowShrinkPerUnit * rise, MinShadow, MaxShadow);
			PlaceShadow(new Vector3(shape.x * size, shape.y * size, shape.z));
		}
		m_HasWritten = true;
		foreach (var bone in m_Hidden)
		{
			bone.localScale = Vector3.zero;
		}

		BuildMesh();
	}

	private void BuildMesh()
	{
		// Root space. The top follows the pod (and its tilt in attacks); the foot is planted on the ground in a stance and
		// swings under the pod in the air.
		var top = m_Root.InverseTransformPoint(m_Body.TransformPoint(MountPoint));
		top.z = 0f;
		Vector3 foot;
		if (m_InStance)
		{
			foot = m_Root.InverseTransformPoint(new Vector3(m_Foot.x, m_Foot.y, m_Rig.position.z)) + new Vector3(0f, GroundY, 0f);
		}
		else
		{
			var swing = m_Root.InverseTransformVector(new Vector3(m_Swing.x, m_Swing.y, 0f) * m_Scale);
			var down = Mathf.Sqrt(Mathf.Max(0f, RestLength * RestLength - m_Swing.sqrMagnitude));
			foot = top + new Vector3(swing.x, swing.y - down, 0f);
			// A clip pushing the pod down doesn't push the hanging foot into the ground.
			foot.y = Mathf.Max(foot.y, GroundY + swing.y);
		}
		foot.z = 0f;
		var axis = foot - top;
		var length = axis.magnitude;
		axis = length > 0.001f ? axis / length : Vector3.down;
		length = Mathf.Max(length, SolidLength);
		var side = new Vector3(-axis.y, axis.x, 0f);

		m_Vertices.Clear();
		m_Colors.Clear();
		m_Triangles.Clear();

		// Helix: seen slightly from above (the front of each turn dips). The last turn is closed (flat), so the spring ends
		// in a round ring standing on the ground; the front of that ring is the lowest point.
		var ring = length - WireWidth * 0.6f - CoilRadius * 0.22f;
		var pitched = Mathf.Max(1, Coils - 1) / (float)Mathf.Max(1, Coils);
		m_Helix.Clear();
		m_HelixDepth.Clear();
		const int perTurn = 20;
		var turns = Coils;
		var count = turns * perTurn;
		var end = WireWidth * 0.6f;
		for (var i = 0; i <= count; i++)
		{
			var t = (float)i / count;
			var angle = t * turns * Mathf.PI * 2f;
			var along = Mathf.Lerp(-TuckDepth, Mathf.Max(end, ring), Mathf.Min(1f, t / pitched));
			var point = top + axis * along + side * (Mathf.Cos(angle) * CoilRadius) + axis * (Mathf.Sin(angle) * CoilRadius * 0.22f);
			m_Helix.Add(point);
			m_HelixDepth.Add(Mathf.Sin(angle));
		}
		// Back half first so the front half draws over it.
		var steel = Finish == SpringFinish.Steel;
		AddHelix(false, steel ? SteelBackWire : BackWire);
		AddHelix(true, steel ? SteelFrontWire : FrontWire);

		m_Mesh.Clear();
		m_Mesh.SetVertices(m_Vertices);
		m_Mesh.SetColors(m_Colors);
		m_Mesh.SetTriangles(m_Triangles, 0);
		m_Mesh.RecalculateBounds();
	}

	private readonly List<int> m_RunStarts = new List<int>();

	private readonly List<int> m_RunEnds = new List<int>();

	private void AddHelix(bool front, Band[] bands)
	{
		// The half's continuous runs of wire (between the coil's sides).
		m_RunStarts.Clear();
		m_RunEnds.Clear();
		var open = -1;
		for (var i = 0; i < m_Helix.Count - 1; i++)
		{
			var depth = (m_HelixDepth[i] + m_HelixDepth[i + 1]) * 0.5f;
			if (depth >= 0f == front)
			{
				if (open < 0)
				{
					open = i;
				}
			}
			else if (open >= 0)
			{
				m_RunStarts.Add(open);
				m_RunEnds.Add(i);
				open = -1;
			}
		}
		if (open >= 0)
		{
			m_RunStarts.Add(open);
			m_RunEnds.Add(m_Helix.Count - 1);
		}
		// Band by band over the whole half, so inner bands cover the neighbouring run's outline where runs meet.
		foreach (var band in bands)
		{
			for (var r = 0; r < m_RunStarts.Count; r++)
			{
				AddStrip(m_RunStarts[r], m_RunEnds[r], WireWidth, band);
			}
		}
	}

	// The side of the wire facing up gets the light, whichever way the wire runs.
	private static Vector3 UpNormal(Vector3 a, Vector3 b)
	{
		var direction = (b - a).normalized;
		var up = new Vector3(-direction.y, direction.x, 0f);
		return up.y < 0f ? -up : up;
	}

	// One continuous ribbon along helix points first..last: mitred at the joints (no spikes), capped at both ends.
	private void AddStrip(int first, int last, float thickness, Band band)
	{
		if (last <= first)
		{
			return;
		}
		var width = thickness * band.Width;
		var previousLow = Vector3.zero;
		var previousHigh = Vector3.zero;
		for (var j = first; j <= last; j++)
		{
			var before = j > first ? UpNormal(m_Helix[j - 1], m_Helix[j]) : Vector3.zero;
			var after = j < last ? UpNormal(m_Helix[j], m_Helix[j + 1]) : Vector3.zero;
			var normal = (before + after).normalized;
			var reference = j > first ? before : after;
			var miter = 1f / Mathf.Max(0.5f, Vector3.Dot(normal, reference));
			var point = m_Helix[j];
			if (j == first)
			{
				point -= (m_Helix[j + 1] - m_Helix[j]).normalized * (width * 0.5f);
			}
			else if (j == last)
			{
				point += (m_Helix[j] - m_Helix[j - 1]).normalized * (width * 0.5f);
			}
			var centre = point + normal * (thickness * band.Offset);
			var half = normal * (width * 0.5f * miter);
			var low = centre - half;
			var high = centre + half;
			if (j > first)
			{
				AddQuad(previousLow, previousHigh, high, low, band.Color);
			}
			previousLow = low;
			previousHigh = high;
		}
	}

	private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
	{
		var start = m_Vertices.Count;
		m_Vertices.Add(a);
		m_Vertices.Add(b);
		m_Vertices.Add(c);
		m_Vertices.Add(d);
		m_Colors.Add(color);
		m_Colors.Add(color);
		m_Colors.Add(color);
		m_Colors.Add(color);
		m_Triangles.Add(start);
		m_Triangles.Add(start + 1);
		m_Triangles.Add(start + 2);
		m_Triangles.Add(start);
		m_Triangles.Add(start + 2);
		m_Triangles.Add(start + 3);
	}

	private void OnDisable()
	{
		ReleaseHeld();
	}

	private void OnDestroy()
	{
		if (m_Mesh != null)
		{
			Destroy(m_Mesh);
		}
		if (m_Material != null)
		{
			Destroy(m_Material);
		}
		if (m_ShadowMesh != null)
		{
			Destroy(m_ShadowMesh);
		}
	}
}
