using System;
using System.Collections.Generic;
using ABH.GameDatas;
using UnityEngine;

// Bells hanging off a weapon: an iron ring under the Dark Cupid scythe's blade collar with a big and a small brass bell
// on it. Each bell is a pendulum that rides on the ring (world gravity, driven by how the weapon moves), with a free
// clapper inside. When a clapper hits the lip, or the two bells knock together, the bell rings: it shivers, little
// "ding" marks flare beside it and it plays its own strike from Resources/Sound/Characters/Generic/bell_ring (pitch from
// its size, volume from the impact speed, timed to the physics step; unless sound is muted).
//
// Art + layout come from ArtSource/ScytheBells/bells.py, which writes Assets/Resources/ScytheBells.png (atlas) and
// Assets/Resources/ScytheBellsConfig.json (sprite rects/pivots, the ring's place on the scythe, per-bell numbers).
// Which items carry bells: "items" in that file (EquipmentBalancingData NameIds). The weapon texture itself is the plain
// scythe; everything else is drawn here, in front of the character (like HelmetBeams).
//
// Units: rig px ("px", the game's HR art pixels), seconds. Gravity uses SpringDrone's 200 px per metre.
// Runs after SmoothMoves has posed the rig this frame (otherwise the bells trail the weapon by a frame in fast swings).
[DefaultExecutionOrder(10000)]
public class ScytheBells : MonoBehaviour
{
	[Serializable]
	public class SpriteEntry
	{
		public string name;

		// texels, x/y from the atlas' bottom-left
		public float[] rect;

		// px
		public float[] size;

		// px from the cell's top-left, y down
		public float[] pivot;
	}

	[Serializable]
	public class WeaponEntry
	{
		// the game's own belled items (hand-made entries the Forge leaves alone)
		public bool builtin;

		// matched by the item worn on `slot` (EquipmentBalancingData NameIds)...
		public string[] items;

		// ...or by the texture the slot shows (CharacterAssetController.ShownTexture: Forge_<id>__<Bone>, Forge_<id>_<gear>)
		public string texture;

		public string slot = "MainHand";

		// the ring's sprite in the atlas ("Ring" is cut to pass behind the Dark Cupid's handle, "RingPlain" is whole)
		public string ring = "Ring";

		// in the slot's sprite space (px, y up)
		public float[] ringCorner;

		public float[] ringCentre;

		public float hangRadius = 2.65f;

		// when strikes ring out (sound + "ding" marks): "Always" (whenever they knock), "Attack", "Support", "Rage",
		// "skill:<SkillNameId>". The bells always swing; an ability on this list also gives them a shake.
		public string[] on;

		// Forge: the slot's layers above the bells, baked (Resources path without extension), drawn over them
		public string cover;

		public float[] coverPivot;

		// Forge: each bell's metal by index into "bells" (MaterialEntry names); empty = the bell's own default
		public string[] materials;
	}

	[Serializable]
	public class BellEntry
	{
		public string sprite;

		public string clapper;

		public float scale = 1f;

		// in the bell's own space (px from its hanger, y down)
		public float[] clapperPivot;

		public float clapperLength;

		public float centreOfMass;

		public float mouth;

		public float lip;

		// playback rate of its strike sound: bells of one shape ring at a pitch inversely proportional to their size
		public float pitch = 1f;

		// its strike: a stretch [soundStart, soundEnd) seconds of the Resources audio clip soundClip
		public string soundClip;

		public float soundStart;

		public float soundEnd;

		// amplitude decay time (s) of that recorded strike, and the metal the recording is (MaterialEntry.name)
		public float soundDecay = 0.184f;

		public string material;
	}

	// A bell metal. For one bell shape the pitch goes with sqrt(youngs / density) (the metal's speed of sound), the mass
	// with density, and decay scales how long it rings against the recording's metal (internal damping).
	[Serializable]
	public class MaterialEntry
	{
		public string name;

		public string label;

		// appended to a bell's sprite name for its look ("" = the plain sprite)
		public string suffix;

		// GPa
		public float youngs = 110f;

		// kg/m^3
		public float density = 8530f;

		public float decay = 1f;

		// surface 0 (mirror polish) .. 1 (rough cast): the art shows it; here it is the hanger loop's friction on the ring
		public float roughness = 0.15f;

		// tarnish in the recesses (art only)
		public float patina;

		// bounce of the iron clapper off this metal (its hardness); bell-on-bell knocks scale with it too
		public float restitution = 0.35f;
	}

	[Serializable]
	private class Table
	{
		public string atlas;

		public SpriteEntry[] sprites;

		public WeaponEntry[] weapons;

		public BellEntry[] bells;

		public MaterialEntry[] materials;

		// the metal the recording is (pitch and decay are relative to it)
		public string reference = "Brass";
	}

	private const string ConfigName = "ScytheBellsConfig";

	private const float Gravity = 1962f;

	private const float StepTime = 1f / 600f;

	// --- feel ---
	// Swing damping (1/s) of the bells and of each clapper relative to its bell.
	public static float BellDamping = 4.5f;

	public static float ClapperDamping = 1.5f;

	// Angle the two bells keep apart on the ring (the small one hangs in front and overlaps the big one a little).
	public static float BellSeparation = 30f * Mathf.Deg2Rad;

	// Bells can't swing past this from straight down (the ring stops them).
	public static float MaxSwing = 100f * Mathf.Deg2Rad;

	// Bounciness of clapper-on-lip and bell-on-bell knocks.
	public static float ClapperRestitution = 0.35f;

	public static float BellRestitution = 0.25f;

	// how much a rough hanger loop (roughness 1 vs 0) adds to the swing damping
	public static float LoopFriction = 1f;

	// Relative speed (rad/s) a knock needs to ring, and the speed that rings at full strength.
	public static float RingThreshold = 3f;

	public static float FullRingSpeed = 12f;

	// The shiver of a ringing bell: peak angle (rad) at full strength, frequency, decay time.
	public static float ShiverAngle = 0.09f;

	public static float ShiverHz = 17f;

	public static float ShiverDecay = 0.28f;

	// A ringing bell swells a little (share of its size at full strength).
	public static float RingSwell = 0.07f;

	// The "ding" marks: lifetime and how far out they drift (px, scaled with the bell).
	public static float DingTime = 0.38f;

	public static float DingDrift = 4f;

	// A faint breeze keeps resting bells alive (rad/s^2 at full gust).
	public static float Breeze = 2f;

	// Very large accelerations (teleports, snapped clips) are clamped to this many g.
	public static float MaxAccelerationG = 12f;

	public const string Always = "Always";

	// How long after an ability's clip starts its strikes ring out, and the swing it jolts the bells with (rad/s).
	public static float ArmedTime = 2.5f;

	public static float ShakeSpeed = 7f;

	public static bool Sound = true;

	// Volume of a strike at FullVolumeImpact (m/s, the speed the clapper ball or the other bell's lip hits at). Sound
	// pressure grows with impact speed, so volume is linear in it, and with the striker's mass as sqrt(mass).
	public static float SoundVolume = 0.45f;

	public static float FullVolumeImpact = 1.5f;

	// Softer knocks than this (m/s) make no sound.
	public static float MinAudibleImpact = 0.06f;

	// How far strikes pan toward the side of the screen the bells are on.
	public static float PanWidth = 0.6f;

	private const float PixelsPerMeter = 200f;

	private const int VoicesPerBell = 4;

	private static Table s_Table;

	private static Texture2D s_Atlas;


	// strike clips cut out of their source clips, by "clip|start|end"
	private static readonly Dictionary<string, AudioClip> s_Strikes = new Dictionary<string, AudioClip>();

	// start of the frame being simulated (strike times inside it are scheduled at the same offsets)
	private float m_FrameStart;

	private static readonly Dictionary<string, SpriteEntry> s_Sprites = new Dictionary<string, SpriteEntry>();

	private SmoothMoves.BoneAnimation m_Animation;

	// the character's body centre: the bells are drawn a little in front of it
	private Transform m_DepthAnchor;

	private WeaponEntry m_Weapon;

	private string m_TwinBone;

	private HashSet<string> m_TwinClips;

	private GameObject m_Root;

	private Mesh m_Mesh;

	private Material m_Material;

	private Bell[] m_Bells;

	private bool m_Settled;

	private bool m_Mirrored;

	private Vector2 m_LastPivot;

	private Vector2 m_LastVelocity;

	private float m_Seed;

	private float m_ArmedUntil = -10f;

	// which of the character's bells these are ("<slot>#<n>", n = order in the config among that slot's matches);
	// later sets draw in front of earlier ones (and their covers)
	private string m_Key;

	private int m_Order;

	// where the ring was drawn last, and on which frame (probes)
	private Vector3 m_LastRingWorld;

	private int m_LastFrame;

	private GameObject m_Cover;

	private Mesh m_CoverMesh;

	private Texture2D m_CoverTexture;

	private bool m_OwnsCover;

	private readonly List<Ding> m_Dings = new List<Ding>();

	private readonly List<Vector3> m_Vertices = new List<Vector3>();

	private readonly List<Vector2> m_Uvs = new List<Vector2>();

	private readonly List<Color> m_Colors = new List<Color>();

	private readonly List<int> m_Triangles = new List<int>();

	private class Bell
	{
		public BellEntry Entry;

		public MaterialEntry Material;

		// this metal against the recording's: density ratio, pitch ratio (sqrt(E/rho) ratio), ring-length ratio
		public float DensityRatio = 1f;

		public float PitchRatio = 1f;

		// swing damping against the recording's metal (a rougher loop rubs more on the ring), and this metal's bounces
		public float FrictionRatio = 1f;

		public float Restitution = -1f;

		public float RestitutionRatio = 1f;

		// angle from straight down (rad, counter-clockwise), and the clapper's absolute angle
		public float Angle;

		public float Speed;

		public float Clapper;

		public float ClapperSpeed;

		public float Accel;

		public float RingStart = -10f;

		public float RingStrength;

		public float LastDing = -10f;

		public AudioSource[] Voices;

		public int NextVoice;

		public float LastSound = -10f;

		public float Length
		{
			get { return Entry.centreOfMass; }
		}

		// relative to a brass bell of scale 1
		public float Mass
		{
			get { return Entry.scale * Entry.scale * Entry.scale * DensityRatio; }
		}

		public float Inertia
		{
			get { return Mass * Entry.centreOfMass * Entry.centreOfMass; }
		}

		// the clapper (iron, the same in every bell) against the bell's mass
		public float ClapperMass
		{
			get { return Entry.scale * Entry.scale * Entry.scale; }
		}

		// how far the clapper may swing inside the bell before its ball hits the lip
		public float ClapperLimit
		{
			get
			{
				var pivotY = Entry.clapperPivot != null && Entry.clapperPivot.Length >= 2 ? Entry.clapperPivot[1] : 0f;
				return Mathf.Atan2(Entry.mouth * 0.74f, Mathf.Max(1f, Entry.lip - pivotY));
			}
		}
	}

	private class Ding
	{
		public Bell Bell;

		public float Start;

		public float Strength;

		public float Side;
	}

	// --- config ---

	private static Table Config()
	{
#if UNITY_EDITOR
		// In the editor, read the files themselves: rerunning bells.py applies without a reimport.
		s_Table = null;
#endif
		if (s_Table != null)
		{
			return s_Table;
		}
		string text = null;
#if UNITY_EDITOR
		var path = System.IO.Path.Combine(Application.dataPath, "Resources/" + ConfigName + ".json");
		if (System.IO.File.Exists(path))
		{
			text = System.IO.File.ReadAllText(path);
		}
#else
		var asset = Resources.Load<TextAsset>(ConfigName);
		text = asset != null ? asset.text : null;
#endif
		try
		{
			s_Table = string.IsNullOrEmpty(text) ? null : JsonUtility.FromJson<Table>(text);
		}
		catch (Exception e)
		{
			DebugLog.Error("[ScytheBells] " + ConfigName + ".json: " + e.Message);
			s_Table = null;
		}
		s_Table = s_Table ?? new Table();
		s_Sprites.Clear();
		if (s_Table.sprites != null)
		{
			foreach (var sprite in s_Table.sprites)
			{
				if (sprite != null && !string.IsNullOrEmpty(sprite.name) && sprite.rect != null && sprite.rect.Length >= 4)
				{
					s_Sprites[sprite.name] = sprite;
				}
			}
		}
		return s_Table;
	}

	private static WeaponEntry WeaponFor(string itemId, string slot)
	{
		var table = Config();
		if (string.IsNullOrEmpty(itemId) || table.weapons == null)
		{
			return null;
		}
		foreach (var weapon in table.weapons)
		{
			if (weapon != null && weapon.slot == slot && weapon.items != null && Array.IndexOf(weapon.items, itemId) >= 0)
			{
				return weapon;
			}
		}
		return null;
	}

	private static bool Matches(WeaponEntry weapon, CharacterAssetController asset)
	{
		if (weapon == null || string.IsNullOrEmpty(weapon.slot))
		{
			return false;
		}
		var item = asset.ShownItem(weapon.slot);
		if (!string.IsNullOrEmpty(item) && weapon.items != null && Array.IndexOf(weapon.items, item) >= 0)
		{
			return true;
		}
		return !string.IsNullOrEmpty(weapon.texture) && asset.ShownTexture(weapon.slot) == weapon.texture;
	}

	private static Texture2D Atlas()
	{
		if (s_Atlas != null)
		{
			return s_Atlas;
		}
		var name = Config().atlas;
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
#if UNITY_EDITOR
		var file = System.IO.Path.Combine(Application.dataPath, "Resources/" + name + ".png");
		if (System.IO.File.Exists(file))
		{
			var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
			if (texture.LoadImage(System.IO.File.ReadAllBytes(file)))
			{
				return s_Atlas = texture;
			}
			Destroy(texture);
		}
#endif
		return s_Atlas = Resources.Load<Texture2D>(name);
	}

	// --- hook ---

	// Called by CharacterAssetController whenever it dresses a bone (SetEquipmentItem, SetForgeFeatures): gives the
	// character one ScytheBells per slot whose item or texture has bells, and drops the ones that no longer match.
	public static void Refresh(CharacterAssetController asset)
	{
		if (asset == null || asset.m_BoneAnimation == null)
		{
			return;
		}
		// every matching entry gets its own bells: several sets may hang in one slot (Forge), keyed slot#n in file order
		var table = Config();
		var wanted = new Dictionary<string, WeaponEntry>();
		var order = new Dictionary<string, int>();
		var perSlot = new Dictionary<string, int>();
		if (table.weapons != null)
		{
			foreach (var weapon in table.weapons)
			{
				if (!Matches(weapon, asset))
				{
					continue;
				}
				int n;
				perSlot.TryGetValue(weapon.slot, out n);
				perSlot[weapon.slot] = n + 1;
				var key = weapon.slot + "#" + n;
				wanted[key] = weapon;
				order[key] = n;
			}
		}
		var anchor = asset.BodyCenter != null ? asset.BodyCenter : asset.transform;
		foreach (var bells in asset.GetComponents<ScytheBells>())
		{
			WeaponEntry weapon;
			if (bells.m_Key == null || !wanted.TryGetValue(bells.m_Key, out weapon))
			{
				Destroy(bells);
				continue;
			}
			bells.Setup(asset.m_BoneAnimation, anchor, weapon, bells.m_Key, order[bells.m_Key]);
			wanted.Remove(bells.m_Key);
		}
		foreach (var pair in wanted)
		{
			asset.gameObject.AddComponent<ScytheBells>().Setup(asset.m_BoneAnimation, anchor, pair.Value, pair.Key, order[pair.Key]);
		}
	}

	// Called as a skill's clip starts (CharacterControllerBattleGroundBase.PlaySupport/Attack/RageSkillAnimation, next to
	// HelmetBeams): bells that ring for this ability start ringing out and get a shake.
	public static void OnSkillClip(CharacterControllerBattleGroundBase character, string kind)
	{
		var asset = character != null ? character.m_AssetController : null;
		if (asset == null)
		{
			return;
		}
		var skill = character.m_ClipSkill;
		var skillId = skill != null && skill.Model != null ? skill.Model.SkillNameId : null;
		foreach (var bells in asset.GetComponents<ScytheBells>())
		{
			if (bells.m_Weapon != null && Has(bells.m_Weapon.on, kind, skillId))
			{
				bells.Shake();
			}
		}
	}

	private static bool Has(string[] on, string kind, string skillId)
	{
		if (on == null)
		{
			return false;
		}
		foreach (var token in on)
		{
			if (token == kind || (!string.IsNullOrEmpty(skillId) && token == HelmetBeams.SkillPrefix + skillId))
			{
				return true;
			}
		}
		return false;
	}

	// Strikes ring out (sound + marks) while this is true; otherwise the bells only swing silently.
	private bool Armed(float time)
	{
		var on = m_Weapon != null ? m_Weapon.on : null;
		return on == null || on.Length == 0 || Array.IndexOf(on, Always) >= 0 || time < m_ArmedUntil;
	}

	// An ability the bells ring for: let strikes ring for its clip and the ring-out after, and jolt the bells.
	public void Shake()
	{
		m_ArmedUntil = Time.time + ArmedTime;
		if (m_Bells == null)
		{
			return;
		}
		var direction = UnityEngine.Random.value < 0.5f ? -1f : 1f;
		foreach (var bell in m_Bells)
		{
			bell.Speed += direction * ShakeSpeed * UnityEngine.Random.Range(0.8f, 1.2f);
			direction = -direction;
		}
	}

	// Gives `host` the bells of item `itemId` worn on bone `boneName` of `animation` (or takes them away when that item
	// has none). For editor probes on bare rigs.
	public static ScytheBells Attach(GameObject host, SmoothMoves.BoneAnimation animation, Transform depthAnchor, string itemId, string boneName)
	{
		var weapon = WeaponFor(itemId, boneName);
		ScytheBells bells = null;
		foreach (var existing in host.GetComponents<ScytheBells>())
		{
			if (existing.m_Weapon != null && existing.m_Weapon.slot == boneName)
			{
				bells = existing;
			}
		}
		if (weapon == null)
		{
			if (bells != null)
			{
				Destroy(bells);
			}
			return null;
		}
		if (bells == null)
		{
			bells = host.AddComponent<ScytheBells>();
		}
		bells.Setup(animation, depthAnchor != null ? depthAnchor : host.transform, weapon, boneName + "#0", 0);
		return bells;
	}

	private void Setup(SmoothMoves.BoneAnimation animation, Transform depthAnchor, WeaponEntry weapon, string key, int order)
	{
		m_Animation = animation;
		m_DepthAnchor = depthAnchor;
		var sameSlot = m_Weapon != null && m_Key == key;
		m_Key = key;
		m_Order = order;
		var coverChanged = m_Weapon == null || m_Weapon.cover != weapon.cover;
		m_Weapon = weapon;
		if (coverChanged)
		{
			DropCover();
		}
		if (sameSlot && m_Bells != null)
		{
			// the same bells (the editor rereads the config each time): keep them swinging, maybe in another metal
			SetMaterials();
			return;
		}
		m_Seed = UnityEngine.Random.value * 100f;
		var entries = Config().bells ?? new BellEntry[0];
		m_Bells = new Bell[entries.Length];
		for (var i = 0; i < entries.Length; i++)
		{
			m_Bells[i] = new Bell { Entry = entries[i] };
		}
		SetMaterials();
		m_Settled = false;
		FindTwin();
	}

	private static MaterialEntry FindMaterial(string name)
	{
		var materials = Config().materials;
		if (materials == null || string.IsNullOrEmpty(name))
		{
			return null;
		}
		foreach (var material in materials)
		{
			if (material != null && material.name == name)
			{
				return material;
			}
		}
		return null;
	}

	// Each bell's metal: the weapon's choice for it (Forge), else the bell's default, else the recording's.
	private void SetMaterials()
	{
		var reference = FindMaterial(Config().reference);
		for (var i = 0; i < m_Bells.Length; i++)
		{
			var bell = m_Bells[i];
			var chosen = m_Weapon.materials != null && i < m_Weapon.materials.Length ? m_Weapon.materials[i] : null;
			bell.Material = FindMaterial(chosen) ?? FindMaterial(bell.Entry.material) ?? reference;
			if (bell.Material == null || reference == null)
			{
				bell.DensityRatio = 1f;
				bell.PitchRatio = 1f;
				bell.FrictionRatio = 1f;
				bell.Restitution = -1f;
				bell.RestitutionRatio = 1f;
				continue;
			}
			bell.DensityRatio = bell.Material.density / Mathf.Max(1f, reference.density);
			bell.PitchRatio = Mathf.Sqrt(bell.Material.youngs / Mathf.Max(1f, bell.Material.density) / (reference.youngs / Mathf.Max(1f, reference.density)));
			bell.FrictionRatio = Mathf.Max(0.2f, 1f + LoopFriction * (bell.Material.roughness - reference.roughness));
			bell.Restitution = bell.Material.restitution;
			bell.RestitutionRatio = bell.Material.restitution / Mathf.Max(0.01f, reference.restitution);
		}
	}

	// PigPirate's Attack_3 draws the hand gear on a Body-parented twin bone; the bells follow it in those clips.
	private void FindTwin()
	{
		m_TwinBone = null;
		m_TwinClips = null;
		var animation = m_Animation;
		var twin = m_Weapon.slot + "Body";
		if (animation == null || animation.GetBoneTransform(twin) == null)
		{
			return;
		}
		var source = System.Linq.Enumerable.FirstOrDefault(animation.mBoneSource, b => b.boneName == twin);
		if (source == null)
		{
			return;
		}
		var clips = ClipNames(animation);
		m_TwinClips = new HashSet<string>();
		foreach (var frame in animation.triggerFrames)
		{
			var bone = frame.GetTriggerFrameBone(source.boneNodeIndex);
			if (bone != null && !string.IsNullOrEmpty(bone.textureGUID) && bone.triggerEventTypes.Contains(SmoothMoves.TriggerFrameBone.TRIGGER_EVENT_TYPE.ChangeTexture) && frame.clipIndex >= 0 && frame.clipIndex < clips.Count)
			{
				m_TwinClips.Add(clips[frame.clipIndex]);
			}
		}
		if (m_TwinClips.Count > 0)
		{
			m_TwinBone = twin;
		}
	}

	private static List<string> ClipNames(SmoothMoves.BoneAnimation animation)
	{
		const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
		var names = new List<string>();
		var field = animation.GetType().GetField("mAnimationClips", flags);
		var clips = field != null ? field.GetValue(animation) as System.Collections.IList : null;
		if (clips == null)
		{
			return names;
		}
		foreach (var clip in clips)
		{
			var nameField = clip != null ? clip.GetType().GetField("animationName", flags) : null;
			names.Add(nameField != null ? nameField.GetValue(clip) as string : null);
		}
		return names;
	}

	private Transform SpriteNode()
	{
		var animation = m_Animation;
		var bone = m_Weapon.slot;
		if (m_TwinBone != null)
		{
			foreach (var clip in m_TwinClips)
			{
				if (!string.IsNullOrEmpty(clip) && animation.IsPlaying(clip))
				{
					bone = m_TwinBone;
					break;
				}
			}
		}
		if (animation.IsBoneHidden(bone))
		{
			return null;
		}
		var transform = animation.GetBoneTransform(bone);
		if (transform == null)
		{
			return null;
		}
		var sprite = transform.Find(bone + "_Sprite");
		return sprite != null ? sprite : transform;
	}

	// --- simulation ---

	private void LateUpdate()
	{
		if (m_Animation == null || m_Weapon == null || m_Bells == null)
		{
			Destroy(this);
			return;
		}
		var sprite = m_Animation.gameObject.activeInHierarchy ? SpriteNode() : null;
		var ex = sprite != null ? sprite.TransformVector(Vector3.right) : Vector3.zero;
		var ey = sprite != null ? sprite.TransformVector(Vector3.up) : Vector3.zero;
		var unit = 0.5f * (((Vector2)ex).magnitude + ((Vector2)ey).magnitude);
		if (sprite == null || unit < 1e-5f || Atlas() == null)
		{
			Show(false);
			m_Settled = false;
			return;
		}
		var centreWorld = sprite.TransformPoint(Vec(m_Weapon.ringCentre));
		var pivot = new Vector2(centreWorld.x, centreWorld.y) / unit;
		var mirrored = ex.x * ey.y - ex.y * ey.x < 0f;
		var dt = Time.deltaTime;
		if (!m_Settled)
		{
			Settle(mirrored);
			m_LastPivot = pivot;
			m_LastVelocity = Vector2.zero;
			m_Mirrored = mirrored;
			m_Settled = true;
		}
		else if (dt > 1e-5f)
		{
			if (mirrored != m_Mirrored)
			{
				// the character turned round: so do the bells (the big one stays on the blade's side)
				foreach (var bell in m_Bells)
				{
					bell.Angle = -bell.Angle;
					bell.Speed = -bell.Speed;
					bell.Clapper = -bell.Clapper;
					bell.ClapperSpeed = -bell.ClapperSpeed;
				}
				m_Mirrored = mirrored;
				m_LastPivot = pivot;
				m_LastVelocity = Vector2.zero;
			}
			var moved = pivot - m_LastPivot;
			var velocity = moved / dt;
			var acceleration = (velocity - m_LastVelocity) / dt;
			if (moved.magnitude > 250f)
			{
				// teleported (placed, re-parented): don't fling the bells
				velocity = Vector2.zero;
				acceleration = Vector2.zero;
			}
			acceleration = Vector2.ClampMagnitude(acceleration, MaxAccelerationG * Gravity);
			m_LastPivot = pivot;
			m_LastVelocity = velocity;
			Simulate(dt, acceleration);
		}
		Draw(sprite, unit, mirrored);
	}

	// Rest pose: the bells hang straight down, pushed apart on the ring.
	private void Settle(bool mirrored)
	{
		var order = mirrored ? -1f : 1f;
		for (var i = 0; i < m_Bells.Length; i++)
		{
			var bell = m_Bells[i];
			// resting against each other on the ring, the heavier one nearer the bottom
			bell.Angle = order * (i == 0 ? -0.25f : 0.75f + (i - 1)) * BellSeparation;
			bell.Speed = 0f;
			bell.Clapper = bell.Angle;
			bell.ClapperSpeed = 0f;
		}
	}

	private void Simulate(float dt, Vector2 acceleration)
	{
		m_FrameStart = Time.time - dt;
		var steps = Mathf.Clamp(Mathf.CeilToInt(dt / StepTime), 1, 40);
		var h = dt / steps;
		var hang = m_Weapon.hangRadius;
		for (var s = 0; s < steps; s++)
		{
			var time = Time.time - dt + h * s;
			foreach (var bell in m_Bells)
			{
				var length = Mathf.Max(1f, hang + bell.Length);
				var sin = Mathf.Sin(bell.Angle);
				var cos = Mathf.Cos(bell.Angle);
				var breeze = Breeze * (Mathf.PerlinNoise(m_Seed + bell.Entry.pitch * 7f, time * 0.6f) - 0.5f) * 2f;
				bell.Accel = -(Gravity * sin + acceleration.x * cos + acceleration.y * sin) / length - BellDamping * bell.FrictionRatio / bell.DensityRatio * bell.Speed + breeze;
				bell.Speed += bell.Accel * h;
				bell.Angle += bell.Speed * h;
				if (Mathf.Abs(bell.Angle) > MaxSwing)
				{
					bell.Angle = Mathf.Sign(bell.Angle) * MaxSwing;
					if (bell.Angle * bell.Speed > 0f)
					{
						bell.Speed *= -0.3f;
					}
				}
			}
			Knock(time);
			foreach (var bell in m_Bells)
			{
				SwingClapper(bell, acceleration, h, time);
			}
		}
	}

	// The bells share the ring: keep them BellSeparation apart, the big one (first) on the blade's side.
	private void Knock(float time)
	{
		var order = m_Mirrored ? -1f : 1f;
		for (var i = 0; i + 1 < m_Bells.Length; i++)
		{
			var a = m_Bells[i];
			var b = m_Bells[i + 1];
			var gap = order * (b.Angle - a.Angle) - BellSeparation;
			if (gap >= 0f)
			{
				continue;
			}
			var ia = 1f / Mathf.Max(1e-4f, a.Inertia);
			var ib = 1f / Mathf.Max(1e-4f, b.Inertia);
			a.Angle += order * gap * ia / (ia + ib);
			b.Angle -= order * gap * ib / (ia + ib);
			var closing = order * (b.Speed - a.Speed);
			if (closing >= 0f)
			{
				continue;
			}
			// two metals knocking: the softer one soaks up more
			var bounce = BellRestitution * 0.5f * (a.RestitutionRatio + b.RestitutionRatio);
			var impulse = -(1f + bounce) * closing / (ia + ib);
			a.Speed -= order * impulse * ia;
			b.Speed += order * impulse * ib;
			// they meet around their shoulders/lips
			var lever = m_Weapon.hangRadius + 0.8f * Mathf.Min(a.Entry.lip, b.Entry.lip);
			var impact = -closing * lever / PixelsPerMeter;
			Strike(a, -closing * 0.6f, impact, b.Mass, time);
			Strike(b, -closing * 0.6f, impact, a.Mass, time);
		}
	}

	// The clapper hangs from the bell's crown; it swings on its own until its ball hits the lip.
	private void SwingClapper(Bell bell, Vector2 acceleration, float h, float time)
	{
		var entry = bell.Entry;
		var crown = m_Weapon.hangRadius + (entry.clapperPivot != null && entry.clapperPivot.Length >= 2 ? entry.clapperPivot[1] : 0f);
		var down = new Vector2(Mathf.Sin(bell.Angle), -Mathf.Cos(bell.Angle));
		var side = new Vector2(Mathf.Cos(bell.Angle), Mathf.Sin(bell.Angle));
		// the crown moves with the ring and swings with the bell
		var crownAcceleration = acceleration + crown * (bell.Accel * side - bell.Speed * bell.Speed * down);
		var length = Mathf.Max(1f, entry.clapperLength);
		var sin = Mathf.Sin(bell.Clapper);
		var cos = Mathf.Cos(bell.Clapper);
		var accel = -(Gravity * sin + crownAcceleration.x * cos + crownAcceleration.y * sin) / length - ClapperDamping * (bell.ClapperSpeed - bell.Speed);
		bell.ClapperSpeed += accel * h;
		bell.Clapper += bell.ClapperSpeed * h;
		var limit = bell.ClapperLimit;
		var relative = Mathf.DeltaAngle(bell.Angle * Mathf.Rad2Deg, bell.Clapper * Mathf.Rad2Deg) * Mathf.Deg2Rad;
		if (Mathf.Abs(relative) <= limit)
		{
			return;
		}
		var sign = Mathf.Sign(relative);
		bell.Clapper = bell.Angle + sign * limit;
		var hit = bell.ClapperSpeed - bell.Speed;
		if (hit * sign <= 0f)
		{
			return;
		}
		bell.ClapperSpeed = bell.Speed - (bell.Restitution >= 0f ? bell.Restitution : ClapperRestitution) * hit;
		// the light clapper nudges the bell (less so a heavier metal)
		bell.Speed += 0.06f / bell.DensityRatio * hit;
		// the ball meets the lip at the clapper's angular speed (relative to the bell) times its length
		Strike(bell, Mathf.Abs(hit), Mathf.Abs(hit) * length / PixelsPerMeter, bell.ClapperMass, time);
	}

	// speed: rad/s (drives the look); impact: m/s at the contact; strikerMass: what hit it (Bell.Mass units).
	private void Strike(Bell bell, float speed, float impact, float strikerMass, float time)
	{
		if (!Armed(time))
		{
			return;
		}
		if (impact >= MinAudibleImpact && time - bell.LastSound >= 0.03f)
		{
			bell.LastSound = time;
			// louder with the striker's mass; a denser bell of the same size answers the same knock a little quieter
			PlayStrike(bell, Mathf.Clamp01(impact / FullVolumeImpact) * Mathf.Sqrt(strikerMass / bell.DensityRatio), time);
		}
		if (speed < RingThreshold)
		{
			return;
		}
		var strength = Mathf.Clamp01((speed - RingThreshold) / Mathf.Max(0.1f, FullRingSpeed - RingThreshold) * 0.85f + 0.15f);
		var ringing = Ringing(bell, time);
		if (strength > ringing)
		{
			bell.RingStart = time;
			bell.RingStrength = strength;
		}
		if (time - bell.LastDing < 0.15f)
		{
			return;
		}
		bell.LastDing = time;
		if (strength >= 0.4f)
		{
			m_Dings.Add(new Ding { Bell = bell, Start = time, Strength = strength, Side = 1f });
			m_Dings.Add(new Ding { Bell = bell, Start = time, Strength = strength, Side = -1f });
		}
	}

	// For probes/logs: each bell's swing and clapper angle (degrees) and when it last rang.
	public string Describe()
	{
		if (m_Bells == null)
		{
			return "no bells";
		}
		var parts = new List<string>();
		foreach (var bell in m_Bells)
		{
			parts.Add(bell.Entry.sprite + (bell.Material != null ? "/" + bell.Material.name + " pitch " + (bell.Entry.pitch * bell.PitchRatio).ToString("F3") : string.Empty) + " swing " + (bell.Angle * Mathf.Rad2Deg).ToString("F1") + " clapper " + ((bell.Clapper - bell.Angle) * Mathf.Rad2Deg).ToString("F1") + " lastDing " + bell.LastDing.ToString("F2"));
		}
		return string.Join(" | ", parts.ToArray()) + " dings " + m_Dings.Count + " ring@" + m_LastRingWorld.ToString("F1") + " f" + m_LastFrame;
	}

	private static float Ringing(Bell bell, float time)
	{
		var t = time - bell.RingStart;
		return t < 0f ? 0f : bell.RingStrength * Mathf.Exp(-t / ShiverDecay);
	}

	// --- drawing ---

	private void Draw(Transform sprite, float unit, bool mirrored)
	{
		if (m_Root == null)
		{
			Build();
		}
		Show(true);
		m_Vertices.Clear();
		m_Uvs.Clear();
		m_Colors.Clear();
		m_Triangles.Clear();
		// One mesh can't slot in between the rig's parts: in front of the whole character, or behind it while the weapon
		// is drawn behind the body (bone z is the rig's draw depth, larger = further back).
		var depth = (m_DepthAnchor != null ? m_DepthAnchor : transform).position.z + (BehindBody(sprite) ? 2f : -2f) - 0.25f * m_Order;
		var time = Time.time;
		// the ring rides on the weapon
		SpriteEntry ringSprite;
		if (s_Sprites.TryGetValue(string.IsNullOrEmpty(m_Weapon.ring) ? "Ring" : m_Weapon.ring, out ringSprite) && m_Weapon.ringCorner != null && m_Weapon.ringCorner.Length >= 2)
		{
			var corner = Vec(m_Weapon.ringCorner);
			var w = ringSprite.size[0];
			var h = ringSprite.size[1];
			Quad(ringSprite, Color.white,
				W(sprite.TransformPoint(corner + new Vector3(0f, -h, 0f)), depth),
				W(sprite.TransformPoint(corner + new Vector3(w, -h, 0f)), depth),
				W(sprite.TransformPoint(corner), depth),
				W(sprite.TransformPoint(corner + new Vector3(w, 0f, 0f)), depth));
		}
		var centre = (Vector2)sprite.TransformPoint(Vec(m_Weapon.ringCentre));
		m_LastRingWorld = centre;
		m_LastFrame = Time.frameCount;
		foreach (var bell in m_Bells)
		{
			var ringing = Ringing(bell, time);
			var t = Mathf.Max(0f, time - bell.RingStart);
			var shiver = ShiverAngle * ringing * Mathf.Sin(t * ShiverHz * Mathf.PI * 2f);
			var angle = bell.Angle + shiver;
			var down = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
			var side = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
			var hanger = centre + down * (m_Weapon.hangRadius * unit);
			var swell = 1f + RingSwell * ringing;
			// clapper first: the bell covers its top
			SpriteEntry clapper;
			if (s_Sprites.TryGetValue(bell.Entry.clapper ?? string.Empty, out clapper))
			{
				var pivotY = bell.Entry.clapperPivot != null && bell.Entry.clapperPivot.Length >= 2 ? bell.Entry.clapperPivot[1] : 0f;
				var pivotX = bell.Entry.clapperPivot != null && bell.Entry.clapperPivot.Length >= 2 ? bell.Entry.clapperPivot[0] : 0f;
				var clapperTop = hanger + (down * pivotY + side * pivotX) * (unit * swell);
				var clapperAngle = bell.Clapper + shiver * 0.5f;
				Hang(clapper, clapperTop, clapperAngle, unit, depth, Color.white);
			}
			SpriteEntry body;
			if (s_Sprites.TryGetValue((bell.Entry.sprite ?? string.Empty) + (bell.Material != null ? bell.Material.suffix : string.Empty), out body) ||
				s_Sprites.TryGetValue(bell.Entry.sprite ?? string.Empty, out body))
			{
				Hang(body, hanger, angle, unit * swell, depth, Color.white);
			}
		}
		DrawDings(time, centre, unit, depth);
		m_Mesh.Clear();
		m_Mesh.SetVertices(m_Vertices);
		m_Mesh.SetUVs(0, m_Uvs);
		m_Mesh.SetColors(m_Colors);
		m_Mesh.SetTriangles(m_Triangles, 0);
		m_Mesh.RecalculateBounds();
		PlaceCover(sprite, depth - 0.1f);
	}

	// Forge: the slot's layers that sit above the bells in its stack, drawn over them (where the art draws:
	// centred at -pivot * size in sprite space).
	private void PlaceCover(Transform sprite, float depth)
	{
		if (string.IsNullOrEmpty(m_Weapon.cover))
		{
			return;
		}
		if (m_Cover == null)
		{
			m_CoverTexture = LoadCover(m_Weapon.cover, out m_OwnsCover);
			if (m_CoverTexture == null)
			{
				return;
			}
			m_Cover = new GameObject("ScytheBellsCover");
			m_Cover.layer = gameObject.layer;
			m_CoverMesh = new Mesh { name = "ScytheBellsCover" };
			m_CoverMesh.MarkDynamic();
			m_Cover.AddComponent<MeshFilter>().sharedMesh = m_CoverMesh;
			var renderer = m_Cover.AddComponent<MeshRenderer>();
			// drawn only over the bells' own pixels (stencil): the art above them hides them, and nothing else is redrawn
			renderer.sharedMaterial = new Material(Shader.Find("Hidden/ScytheBells/Cover") ?? Shader.Find("Sprites/Default")) { mainTexture = m_CoverTexture, renderQueue = m_Material.renderQueue };
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}
		if (!m_Cover.activeSelf)
		{
			m_Cover.SetActive(true);
		}
		var pivot = Vec(m_Weapon.coverPivot);
		var size = new Vector3(m_CoverTexture.width, m_CoverTexture.height, 0f);
		var centre = new Vector3(-pivot.x * size.x, -pivot.y * size.y, 0f);
		var corners = new Vector3[4];
		var index = 0;
		foreach (var v in new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) })
		{
			corners[index++] = W(sprite.TransformPoint(centre + Vector3.Scale(v, size)), depth);
		}
		m_CoverMesh.Clear();
		m_CoverMesh.vertices = corners;
		m_CoverMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
		m_CoverMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
		m_CoverMesh.RecalculateBounds();
	}

	private static Texture2D LoadCover(string path, out bool owned)
	{
		owned = false;
#if UNITY_EDITOR
		var file = System.IO.Path.Combine(Application.dataPath, "Resources/" + path + ".png");
		if (System.IO.File.Exists(file))
		{
			var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = path, wrapMode = TextureWrapMode.Clamp };
			if (texture.LoadImage(System.IO.File.ReadAllBytes(file)))
			{
				owned = true;
				return texture;
			}
			Destroy(texture);
		}
#endif
		return Resources.Load<Texture2D>(path);
	}

	private void DropCover()
	{
		if (m_Cover != null)
		{
			Destroy(m_Cover.GetComponent<MeshRenderer>().sharedMaterial);
			Destroy(m_CoverMesh);
			Destroy(m_Cover);
		}
		if (m_OwnsCover && m_CoverTexture != null)
		{
			Destroy(m_CoverTexture);
		}
		m_Cover = null;
		m_CoverMesh = null;
		m_CoverTexture = null;
		m_OwnsCover = false;
	}

	private void DrawDings(float time, Vector2 centre, float unit, float depth)
	{
		SpriteEntry mark;
		if (!s_Sprites.TryGetValue("Ding", out mark))
		{
			m_Dings.Clear();
			return;
		}
		for (var i = m_Dings.Count - 1; i >= 0; i--)
		{
			var ding = m_Dings[i];
			var t = (time - ding.Start) / DingTime;
			if (t >= 1f || t < -0.01f)
			{
				m_Dings.RemoveAt(i);
				continue;
			}
			var bell = ding.Bell;
			var angle = bell.Angle;
			var down = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
			var side = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
			var hanger = centre + down * (m_Weapon.hangRadius * unit);
			var scale = bell.Entry.scale;
			var ease = 1f - (1f - t) * (1f - t);
			// two arcs per side, the outer one a beat later and smaller
			for (var k = 0; k < 2; k++)
			{
				var local = Mathf.Clamp01((t - k * 0.18f) / (1f - k * 0.18f));
				if (local <= 0f)
				{
					continue;
				}
				var reach = (bell.Entry.mouth + 1.5f + k * 2.6f * scale + DingDrift * scale * ease) * ding.Side;
				var at = hanger + (down * (bell.Entry.lip * 0.62f) + side * reach) * unit;
				var size = (0.75f + 0.25f * ding.Strength) * Mathf.Lerp(0.7f, 1f, scale) * (k == 0 ? 1f : 0.75f) * Mathf.Lerp(0.85f, 1.15f, local);
				var alpha = Mathf.Clamp01(ding.Strength * 1.4f) * (1f - local * local) * Mathf.Clamp01(local * 8f);
				// the art bulges toward +x; the left side's marks are mirrored
				var across = side * (ding.Side * unit * size);
				var along = -down * (unit * size);
				var w = mark.size[0];
				var h = mark.size[1];
				var origin = at - along * (h * 0.5f) - across * (w * 0.5f);
				Quad(mark, new Color(1f, 1f, 1f, alpha),
					W(origin, depth), W(origin + across * w, depth),
					W(origin + along * h, depth), W(origin + across * w + along * h, depth));
			}
		}
	}

	// A sprite hanging from `top` (its pivot), its own "down" turned to `angle` from world down.
	private void Hang(SpriteEntry sprite, Vector2 top, float angle, float unit, float depth, Color color)
	{
		var down = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * unit;
		var side = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * unit;
		var left = -sprite.pivot[0];
		var right = sprite.size[0] - sprite.pivot[0];
		var upper = -sprite.pivot[1];
		var lower = sprite.size[1] - sprite.pivot[1];
		Quad(sprite, color,
			W(top + side * left + down * lower, depth),
			W(top + side * right + down * lower, depth),
			W(top + side * left + down * upper, depth),
			W(top + side * right + down * upper, depth));
	}

	private bool BehindBody(Transform sprite)
	{
		var body = m_Animation.GetBoneTransform("Body");
		if (body == null)
		{
			return false;
		}
		var bodySprite = body.Find("Body_Sprite");
		return sprite.position.z > (bodySprite != null ? bodySprite : body).position.z + 0.01f;
	}

	// Corners: bottom-left, bottom-right, top-left, top-right of the art.
	private void Quad(SpriteEntry sprite, Color color, Vector3 bl, Vector3 br, Vector3 tl, Vector3 tr)
	{
		var first = m_Vertices.Count;
		m_Vertices.Add(bl);
		m_Vertices.Add(br);
		m_Vertices.Add(tl);
		m_Vertices.Add(tr);
		var tw = (float)s_Atlas.width;
		var th = (float)s_Atlas.height;
		var r = sprite.rect;
		m_Uvs.Add(new Vector2(r[0] / tw, r[1] / th));
		m_Uvs.Add(new Vector2((r[0] + r[2]) / tw, r[1] / th));
		m_Uvs.Add(new Vector2(r[0] / tw, (r[1] + r[3]) / th));
		m_Uvs.Add(new Vector2((r[0] + r[2]) / tw, (r[1] + r[3]) / th));
		for (var i = 0; i < 4; i++)
		{
			m_Colors.Add(color);
		}
		m_Triangles.Add(first);
		m_Triangles.Add(first + 2);
		m_Triangles.Add(first + 1);
		m_Triangles.Add(first + 1);
		m_Triangles.Add(first + 2);
		m_Triangles.Add(first + 3);
	}

	private static Vector3 W(Vector3 p, float depth)
	{
		return new Vector3(p.x, p.y, depth);
	}

	private static Vector3 W(Vector2 p, float depth)
	{
		return new Vector3(p.x, p.y, depth);
	}

	private static Vector3 Vec(float[] p)
	{
		return p != null && p.Length >= 2 ? new Vector3(p[0], p[1], 0f) : Vector3.zero;
	}

	private void Build()
	{
		m_Root = new GameObject("ScytheBells");
		m_Root.layer = gameObject.layer;
		m_Mesh = new Mesh { name = "ScytheBells" };
		m_Mesh.MarkDynamic();
		m_Root.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
		var rigRenderer = m_Animation.GetComponentInChildren<Renderer>();
		// marks the stencil where the bells draw, so a cover can hide them without being drawn over the character
		m_Material = new Material(Shader.Find("Hidden/ScytheBells/Bells") ?? Shader.Find("Sprites/Default")) { mainTexture = Atlas() };
		m_Material.renderQueue = rigRenderer != null && rigRenderer.sharedMaterial != null ? rigRenderer.sharedMaterial.renderQueue : 3000;
		var renderer = m_Root.AddComponent<MeshRenderer>();
		renderer.sharedMaterial = m_Material;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
	}

	private void Show(bool shown)
	{
		if (m_Root != null && m_Root.activeSelf != shown)
		{
			m_Root.SetActive(shown);
		}
		if (!shown && m_Cover != null && m_Cover.activeSelf)
		{
			m_Cover.SetActive(false);
		}
	}

	// --- sound ---

	// Plays the bell's strike at `time` (a moment inside the frame just simulated). Strikes are scheduled on the audio clock
	// at the same offsets they had in the simulation, so a rattle keeps its rhythm; the whole frame is heard one frame late.
	private void PlayStrike(Bell bell, float amplitude, float time)
	{
		if (!Sound || !Application.isPlaying || amplitude <= 0f)
		{
			return;
		}
		var audio = DIContainerInfrastructure.AudioManager;
		if (audio == null || audio.IsMuted(1))
		{
			return;
		}
		var clip = StrikeClip(bell.Entry, bell.Material);
		if (clip == null)
		{
			return;
		}
		if (bell.Voices == null)
		{
			// several voices per bell: a new strike doesn't cut off the one still ringing (real bells add up)
			bell.Voices = new AudioSource[VoicesPerBell];
			for (var i = 0; i < VoicesPerBell; i++)
			{
				var voice = gameObject.AddComponent<AudioSource>();
				voice.playOnAwake = false;
				voice.loop = false;
				voice.spatialBlend = 0f;
				bell.Voices[i] = voice;
			}
		}
		var source = bell.Voices[bell.NextVoice];
		bell.NextVoice = (bell.NextVoice + 1) % VoicesPerBell;
		source.Stop();
		source.clip = clip;
		source.pitch = bell.Entry.pitch * bell.PitchRatio;
		source.volume = Mathf.Clamp01(SoundVolume * amplitude);
		source.panStereo = Pan();
		var offset = Mathf.Clamp(time - m_FrameStart, 0f, 0.25f);
		source.PlayScheduled(AudioSettings.dspTime + offset);
	}

	private float Pan()
	{
		var camera = Camera.main;
		if (camera == null || m_Root == null)
		{
			return 0f;
		}
		var viewport = camera.WorldToViewportPoint(m_Animation.transform.position);
		return Mathf.Clamp((viewport.x * 2f - 1f) * PanWidth, -1f, 1f);
	}

	// The bell's own strike, cut out of its source clip, with a short fade so the cut doesn't click, and its ring made
	// longer or shorter for its metal: the recorded decay e^(-t/tau) becomes e^(-t/(tau * decay)).
	private static AudioClip StrikeClip(BellEntry entry, MaterialEntry material)
	{
		if (string.IsNullOrEmpty(entry.soundClip) || entry.soundEnd <= entry.soundStart)
		{
			return null;
		}
		var decay = material != null && material.decay > 0f ? material.decay : 1f;
		var key = entry.soundClip + "|" + entry.soundStart + "|" + entry.soundEnd + "|" + decay;
		AudioClip clip;
		if (s_Strikes.TryGetValue(key, out clip))
		{
			return clip;
		}
		var source = Resources.Load<AudioClip>(entry.soundClip);
		if (source == null)
		{
			DebugLog.Error("[ScytheBells] no audio clip Resources/" + entry.soundClip);
			s_Strikes[key] = null;
			return null;
		}
		if (source.loadState != AudioDataLoadState.Loaded && !source.LoadAudioData())
		{
			return null;
		}
		if (source.loadState != AudioDataLoadState.Loaded)
		{
			// still loading: try again on the next strike
			return null;
		}
		var rate = source.frequency;
		var channels = source.channels;
		var first = Mathf.Clamp(Mathf.RoundToInt(entry.soundStart * rate), 0, source.samples - 1);
		var count = Mathf.Clamp(Mathf.RoundToInt((entry.soundEnd - entry.soundStart) * rate), 1, source.samples - first);
		var data = new float[count * channels];
		if (!source.GetData(data, first))
		{
			DebugLog.Error("[ScytheBells] can't read Resources/" + entry.soundClip + " (load type must decompress on load)");
			s_Strikes[key] = null;
			return null;
		}
		var tau = Mathf.Max(0.01f, entry.soundDecay);
		var reshape = 1f / tau - 1f / (tau * decay);
		var fadeIn = Mathf.Min(count, Mathf.RoundToInt(0.001f * rate));
		var fadeOut = Mathf.Min(count, Mathf.RoundToInt(0.08f * rate));
		for (var i = 0; i < count; i++)
		{
			var gain = Mathf.Exp(reshape * i / (float)rate);
			if (i < fadeIn)
			{
				gain = (float)i / fadeIn;
			}
			var fromEnd = count - 1 - i;
			if (fromEnd < fadeOut)
			{
				// the recording's next strike follows; let this one's tail die away smoothly instead
				gain *= Mathf.Sin(0.5f * Mathf.PI * fromEnd / fadeOut);
			}
			for (var c = 0; c < channels; c++)
			{
				data[i * channels + c] *= gain;
			}
		}
		clip = AudioClip.Create("ScytheBell_" + entry.sprite + (material != null ? material.suffix : string.Empty), count, channels, rate, false);
		clip.SetData(data, 0);
		s_Strikes[key] = clip;
		return clip;
	}

	private void OnDisable()
	{
		Show(false);
		m_Settled = false;
		m_Dings.Clear();
	}

	private void OnEnable()
	{
		if (m_Root != null)
		{
			Show(true);
		}
	}

	private void OnDestroy()
	{
		DropCover();
		if (m_Root != null)
		{
			Destroy(m_Root);
		}
		if (m_Mesh != null)
		{
			Destroy(m_Mesh);
		}
		if (m_Material != null)
		{
			Destroy(m_Material);
		}
		if (m_Bells != null)
		{
			foreach (var bell in m_Bells)
			{
				if (bell.Voices != null)
				{
					foreach (var voice in bell.Voices)
					{
						if (voice != null)
						{
							Destroy(voice);
						}
					}
				}
			}
		}
	}
}
