using System;
using System.Collections.Generic;
using UnityEngine;

// Combatant Forge projectiles (Tools/CombatantForge, Projectiles section): uploaded as Resources/ForgeProjectiles/<NameId>.json
// plus one baked picture per component, and flown here with the same physics as the Forge's preview (web/projectiles.js
// flights / shotAt / simulate). SkillBattleDataBase.SpawnProjectile and Ricochet.Launch hand them over instead of
// instantiating a prefab; ForgeSkillAnimation.ProjectileFor finds them by their hashed name.
public static class ForgeProjectiles
{
	public const string Prefix = "ForgeProjectile_";

	private static readonly Dictionary<string, Def> s_Defs = new Dictionary<string, Def>();

	private static List<string> s_Index;

	// the runner the last Spawn made (SkillBattleDataBase waits on its Landed)
	public static ForgeProjectileRunner LastRunner;

	[Serializable]
	public class AlphaKey
	{
		public float t;

		public float a;
	}

	[Serializable]
	public class Vary
	{
		public string key;

		public float range;
	}

	[Serializable]
	public class Comp
	{
		public string uid;
		public string name;
		public string type;
		public string spawn;
		public int of = -1;
		public string image;
		public float pivotX = 0.5f;
		public float pivotY = 0.5f;
		public string ribbonImage;
		public float ribbonWidth = 20f;
		public float ribbonTile = 64f;
		public int count = 1;
		public float interval = 0.08f;
		public float spread;
		public float flightTime = 0.25f;
		public string easing = "even";
		public float accel;
		public float arc;
		public float hops;
		public float hopHeight = 30f;
		public bool align;
		public float spin;
		public float fadeFrom;
		public float fadeTo;
		public float bounces;
		public float bounceArc = 60f;
		public float bounceDistance = 140f;
		public float bounceDecay = 0.6f;
		public float afterSpeed = 1f;
		public float afterAccel;
		public string afterDirection = "onward";
		public bool afterAlign;
		public float afterSpin;
		public float linger;
		public float rate = 30f;
		public float life = 0.4f;
		public float scaleStart = 1f;
		public float scaleEnd = 1f;
		public bool fade = true;
		public float squash;
		public float scatter;
		public float gravity;
		public float slowScale = 0.3f;
		public float slowDuration = 0.5f;
		public float slowEase = 0.1f;
		public float slowEaseOut = -1f;
		// "game": the whole battle slows (ForgeSlowMotion); "projectile": only this projectile's own clock
		public string slowScope = "game";
		public bool trailAlign;
		public bool randomTurn;
		public string style = "pieces";
		public string textureMode = "stretch";
		public AlphaKey[] alphaKeys;
		public Vary[] vary;

		[NonSerialized]
		public Dictionary<string, float> m_Vary;

		[NonSerialized]
		public Sprite m_Sprite;

		[NonSerialized]
		public Material m_RibbonMaterial;
	}

	[Serializable]
	public class Def
	{
		public string name;

		public Comp[] components;
	}

	public static bool Has(string nameId)
	{
		return !string.IsNullOrEmpty(nameId) && nameId.StartsWith(Prefix) && Load(nameId) != null;
	}

	public static Def Load(string nameId)
	{
		Def def;
		if (s_Defs.TryGetValue(nameId, out def))
		{
			return def;
		}
		try
		{
			var text = Resources.Load<TextAsset>("ForgeProjectiles/" + nameId);
			if (text != null)
			{
				def = JsonUtility.FromJson<Def>(text.text);
				foreach (var c in def.components)
				{
					c.m_Vary = new Dictionary<string, float>();
					if (c.vary != null)
					{
						foreach (var v in c.vary)
						{
							c.m_Vary[v.key] = v.range;
						}
					}
					if (!string.IsNullOrEmpty(c.image))
					{
						var tex = Resources.Load<Texture2D>(c.image);
						if (tex != null)
						{
							// one texture pixel is one unit of the shooter's rig, as the game's projectile meshes are
							c.m_Sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(c.pivotX, c.pivotY), 1f);
						}
					}
					if (!string.IsNullOrEmpty(c.ribbonImage))
					{
						var tex = Resources.Load<Texture2D>(c.ribbonImage);
						var shader = Shader.Find("Sprites/Default");
						if (tex != null && shader != null)
						{
							tex.wrapMode = c.textureMode == "tile" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
							c.m_RibbonMaterial = new Material(shader);
							c.m_RibbonMaterial.mainTexture = tex;
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			DebugLog.Error("Forge projectile " + nameId + " failed to load: " + ex);
			def = null;
		}
		s_Defs[nameId] = def;
		return def;
	}

	public static List<string> Index()
	{
		if (s_Index == null)
		{
			s_Index = new List<string>();
			var text = Resources.Load<TextAsset>("ForgeProjectiles/index");
			if (text != null)
			{
				foreach (var line in text.text.Split('\n'))
				{
					var name = line.Trim();
					if (name.Length > 0)
					{
						s_Index.Add(name);
					}
				}
			}
		}
		return s_Index;
	}

	// The uploaded projectile whose name has this ForgeSkillAnimation.Hash, or null.
	public static string FindByHash(int hash)
	{
		foreach (var name in Index())
		{
			if (ForgeSkillAnimation.Hash(name) == hash)
			{
				return name;
			}
		}
		return null;
	}

	// Fires the projectile from `from`: a fired component with "How many" above 1 aims at the targets in turn, otherwise at
	// the one target (or the centre of several, as the game's projectiles do). Returns the seconds until its first hit.
	// A ricochet (Ricochet.Launch) is the same shot flying on, not a new one, so it skips the "fired" (launch) pieces.
	public static float Spawn(string nameId, Transform parent, Vector3 from, List<Vector3> targets, Vector3 centre, Vector3 rigScale, int layer, bool ricochet = false, Action<int, Vector3> onEvent = null)
	{
		var def = Load(nameId);
		if (def == null || targets == null || targets.Count == 0)
		{
			return 0f;
		}
		var go = new GameObject(nameId);
		if (parent != null)
		{
			go.transform.parent = parent;
		}
		go.transform.position = new Vector3(0f, 0f, from.z);
		go.layer = layer;
		var runner = go.AddComponent<ForgeProjectileRunner>();
		LastRunner = runner;
		runner.OnEvent = onEvent;
		return runner.Begin(def, from, targets, centre, rigScale, layer, ricochet);
	}
}

public class ForgeProjectileRunner : MonoBehaviour
{
	private class Hop
	{
		public Vector2 from;
		public Vector2 to;
		public float h;
		public float t;
		public float dur;
	}

	private class Ev
	{
		public string kind;
		public Vector2 p;
		public float t;
		public float rot;
	}

	private class Flight
	{
		public ForgeProjectiles.Comp c;
		public int ci;
		public int k;
		public Vector2 origin;
		public Vector2 end;
		public float t0;
		public float T;
		public float dir;
		public float gone;
		public float arc;
		public float hopHeight;
		public int hopsN;
		public float spin;
		public float afterSpin;
		public float accelK;
		public readonly List<Hop> hops = new List<Hop>();
		public readonly List<Ev> events = new List<Ev>();
		public float stopT;
		public Vector2 stopAt;
		public float lingerT;
		public TrailRenderer ribbon;
	}

	private struct Shot
	{
		public Vector2 p;
		public float rot;
		public float alpha;
	}

	private ForgeProjectiles.Def m_Def;
	private float m_Scale = 1f;
	private Vector3 m_RigScale = Vector3.one;
	private float m_Z;
	private int m_Layer;
	private float m_T0;

	// this projectile's own clock (seconds since fired), slowed by its projectile-wide slow motion components
	private float m_Clock;

	private class LocalSlow
	{
		public float start;
		public float scale;
		public float duration;
		public float ease;
		public float easeOut;
	}

	private readonly List<LocalSlow> m_LocalSlows = new List<LocalSlow>();

	private float m_FirstHit;

	// a fired shot's launch / hit / bounce / end and slow motion (ForgeEffectExtras.Timing*), at its world position
	public Action<int, Vector3> OnEvent;

	private readonly HashSet<long> m_EventsDone = new HashSet<long>();

	private void Emit(int timing, Vector2 p, long key)
	{
		if (OnEvent != null && m_EventsDone.Add(key))
		{
			try
			{
				OnEvent(timing, new Vector3(p.x * m_Scale, p.y * m_Scale, m_Z));
			}
			catch (Exception ex)
			{
				DebugLog.Error("Forge projectile event: " + ex);
			}
		}
	}

	// whether its first shot has reached its target (later than planned when it was slowed down on the way)
	public bool Landed
	{
		get { return this == null || m_Clock >= m_FirstHit; }
	}

	// the slowest of the projectile-wide slow-downs going on now (eased in and out over real time)
	private float LocalScale()
	{
		var result = 1f;
		var now = Time.unscaledTime;
		for (var i = m_LocalSlows.Count - 1; i >= 0; i--)
		{
			var s = m_LocalSlows[i];
			var e = now - s.start;
			float k;
			if (e < s.ease)
			{
				k = Mathf.Lerp(1f, s.scale, s.ease > 0f ? e / s.ease : 1f);
			}
			else if (e < s.ease + s.duration)
			{
				k = s.scale;
			}
			else if (e < s.ease + s.duration + s.easeOut)
			{
				k = Mathf.Lerp(s.scale, 1f, (e - s.ease - s.duration) / s.easeOut);
			}
			else
			{
				m_LocalSlows.RemoveAt(i);
				continue;
			}
			result = Mathf.Min(result, k);
		}
		return result;
	}
	private float m_Seed;
	private float m_Over;
	private bool m_Ricochet;
	private Dictionary<int, List<Flight>> m_Shots = new Dictionary<int, List<Flight>>();
	private readonly Dictionary<int, List<SpriteRenderer>> m_Pools = new Dictionary<int, List<SpriteRenderer>>();
	private readonly Dictionary<int, int> m_Used = new Dictionary<int, int>();

	// slow motion components that have gone off (component, shot, event)
	private readonly HashSet<int> m_SlowDone = new HashSet<int>();

	private static float Rand(float a, float b, float c)
	{
		var x = Math.Sin(a * 127.1 + b * 311.7 + c * 74.7) * 43758.5453;
		return (float)(x - Math.Floor(x));
	}

	private static float KeySeed(string key)
	{
		var a = 7;
		foreach (var ch in key)
		{
			a = (a * 31 + ch) % 9973;
		}
		return a;
	}

	private static float Base(ForgeProjectiles.Comp c, string key)
	{
		switch (key)
		{
			case "count": return c.count;
			case "spread": return c.spread;
			case "flightTime": return c.flightTime;
			case "accel": return c.accel;
			case "arc": return c.arc;
			case "hops": return c.hops;
			case "hopHeight": return c.hopHeight;
			case "spin": return c.spin;
			case "bounces": return c.bounces;
			case "bounceArc": return c.bounceArc;
			case "bounceDistance": return c.bounceDistance;
			case "bounceDecay": return c.bounceDecay;
			case "afterSpeed": return c.afterSpeed;
			case "afterAccel": return c.afterAccel;
			case "afterSpin": return c.afterSpin;
			case "linger": return c.linger;
			case "life": return c.life;
			case "scaleStart": return c.scaleStart;
			case "scaleEnd": return c.scaleEnd;
			case "scatter": return c.scatter;
			case "gravity": return c.gravity;
		}
		return 0f;
	}

	private float Range(ForgeProjectiles.Comp c, string key)
	{
		float r;
		return c.m_Vary != null && c.m_Vary.TryGetValue(key, out r) ? r : 0f;
	}

	// A value with its ± variation picked for one shot (k) and sub-part (i, e.g. the bounce)
	private float Pv(ForgeProjectiles.Comp c, string key, int ci, int k, int i = 0)
	{
		var r = Range(c, key);
		var b = Base(c, key);
		return r != 0f ? b + (Rand(ci * 13.1f + m_Seed * 0.0137f, k * 7.3f + i * 1.91f, KeySeed(key)) * 2f - 1f) * r : b;
	}

	// A trail or effect piece's own value within its ± range
	private float Pick(ForgeProjectiles.Comp c, string key, float seed, float k, float salt)
	{
		var r = Range(c, key);
		return Base(c, key) + (r != 0f ? (Rand(seed + m_Seed * 0.0137f, k, salt) * 2f - 1f) * r : 0f);
	}

	private static float Ease(ForgeProjectiles.Comp c, float u)
	{
		u = Mathf.Clamp01(u);
		switch (c.easing)
		{
			case "accel": return u * u;
			case "decel": return 1f - (1f - u) * (1f - u);
			case "ease": return u * u * (3f - 2f * u);
		}
		return u;
	}

	private static float Progress(Flight f, float u)
	{
		var s = Ease(f.c, u);
		return f.accelK != 0f ? (1f - f.accelK) * s + f.accelK * s * s : s;
	}

	private static float Uneased(Flight f, float s)
	{
		float lo = 0f, hi = 1f;
		for (var i = 0; i < 30; i++)
		{
			var m = (lo + hi) / 2f;
			if (Progress(f, m) < s)
			{
				lo = m;
			}
			else
			{
				hi = m;
			}
		}
		return (lo + hi) / 2f;
	}

	private static Vector2 FlightPoint(Flight f, float s)
	{
		var hop = f.hopsN > 0 ? f.hopHeight * Mathf.Abs(Mathf.Sin(Mathf.PI * f.hopsN * s)) : 0f;
		return new Vector2(Mathf.Lerp(f.origin.x, f.end.x, s), Mathf.Lerp(f.origin.y, f.end.y, s) + f.arc * 4f * s * (1f - s) + hop);
	}

	public float Begin(ForgeProjectiles.Def def, Vector3 from, List<Vector3> targets, Vector3 centre, Vector3 rigScale, int layer, bool ricochet = false)
	{
		m_Def = def;
		m_Ricochet = ricochet;
		m_RigScale = rigScale;
		// the Forge works in units of the shooter's rig; the battle in world units (the rig's scale under the battle area's)
		var areaScale = transform.parent != null ? Mathf.Abs(transform.parent.lossyScale.y) : 1f;
		m_Scale = Mathf.Max(0.0001f, Mathf.Abs(rigScale.y) * (areaScale > 0f ? areaScale : 1f));
		m_Z = from.z;
		m_Layer = layer;
		m_T0 = Time.time;
		m_Seed = UnityEngine.Random.Range(0, 100000);
		Vector2 origin = from / m_Scale;
		var list = new List<Vector2>();
		foreach (var t in targets)
		{
			list.Add(t / m_Scale);
		}
		var firstHit = float.MaxValue;
		for (var ci = 0; ci < def.components.Length; ci++)
		{
			var c = def.components[ci];
			if (c.spawn != "fire")
			{
				continue;
			}
			var n = Mathf.Max(1, Mathf.RoundToInt(Pv(c, "count", ci, 0)));
			var aims = n > 1 || list.Count == 1 ? list : new List<Vector2> { (Vector2)(centre / m_Scale) };
			var flights = Flights(c, ci, n, aims, origin);
			m_Shots[ci] = flights;
			foreach (var f in flights)
			{
				if (f.gone == 0f)
				{
					firstHit = Mathf.Min(firstHit, f.t0 + f.T);
				}
			}
		}
		m_Over = 0f;
		Simulate(0f);
		m_FirstHit = firstHit == float.MaxValue ? 0f : firstHit;
		return m_FirstHit;
	}

	private List<Flight> Flights(ForgeProjectiles.Comp c, int ci, int n, List<Vector2> list, Vector2 origin)
	{
		var outList = new List<Flight>();
		for (var k = 0; k < n; k++)
		{
			var tgt = list[k % list.Count];
			var jitter = Range(c, "spread") != 0f ? Pv(c, "spread", ci, k) - c.spread : 0f;
			var ang = ((k - (n - 1) / 2f) * c.spread + jitter) * Mathf.Deg2Rad;
			var dx = tgt.x - origin.x;
			var dy = tgt.y - origin.y;
			var end = new Vector2(origin.x + dx * Mathf.Cos(ang) - dy * Mathf.Sin(ang), origin.y + dx * Mathf.Sin(ang) + dy * Mathf.Cos(ang));
			var t0 = k * c.interval;
			var ft = Pv(c, "flightTime", ci, k);
			var T = Mathf.Max(0.02f, ft != 0f ? ft : 0.25f);
			var dir = Mathf.Sign(end.x - origin.x);
			if (end.x == origin.x)
			{
				dir = -1f;
			}
			var dist = Mathf.Max(1f, Vector2.Distance(origin, end));
			var speed = dist / T;
			var gone = c.fadeTo > 0f ? Mathf.Min(1f, c.fadeTo) : 0f;
			var f = new Flight
			{
				c = c, ci = ci, k = k, origin = origin, end = end, t0 = t0, T = T, dir = dir, gone = gone,
				arc = Pv(c, "arc", ci, k), hopHeight = Pv(c, "hopHeight", ci, k), hopsN = Mathf.Max(0, Mathf.RoundToInt(Pv(c, "hops", ci, k))),
				spin = Pv(c, "spin", ci, k), afterSpin = Pv(c, "afterSpin", ci, k)
			};
			f.accelK = Mathf.Clamp(Pv(c, "accel", ci, k) * T * T / (2f * dist), -1f, 1f);
			var away = c.afterDirection == "back" ? -dir : dir;
			var after = Mathf.Max(0.05f, Pv(c, "afterSpeed", ci, k)) * speed;
			var afterAccel = Pv(c, "afterAccel", ci, k);
			var at = end;
			var t = t0 + T;
			var decay = 1f;
			if (gone == 0f)
			{
				var nb = Mathf.Max(0, Mathf.RoundToInt(Pv(c, "bounces", ci, k)));
				for (var i = 0; i < nb; i++)
				{
					var d = Mathf.Max(0f, Pv(c, "bounceDistance", ci, k, i + 1)) * decay;
					var hgt = Mathf.Max(0f, Pv(c, "bounceArc", ci, k, i + 1)) * decay;
					decay *= Mathf.Max(0f, Pv(c, "bounceDecay", ci, k, i + 1));
					var sp = Mathf.Max(after * 0.05f, after + afterAccel * (t - t0 - T));
					var dur = Mathf.Max(0.06f, d / sp * 1.6f);
					var to = new Vector2(at.x + away * d, at.y);
					f.hops.Add(new Hop { from = at, to = to, h = hgt, t = t, dur = dur });
					at = to;
					t += dur;
				}
			}
			if (!m_Ricochet)
			{
				f.events.Add(new Ev { kind = "launch", p = origin, t = t0, rot = Mathf.Atan2(end.y - origin.y, end.x - origin.x) - Mathf.Atan2(0f, dir) });
			}
			for (var j = 1; j < f.hopsN; j++)
			{
				var sj = (float)j / f.hopsN;
				if (gone > 0f && sj >= gone)
				{
					break;
				}
				f.events.Add(new Ev { kind = "bounce", p = FlightPoint(f, sj), t = t0 + Uneased(f, sj) * T });
			}
			if (gone == 0f)
			{
				f.events.Add(new Ev { kind = "hit", p = end, t = t0 + T });
			}
			foreach (var hp in f.hops)
			{
				f.events.Add(new Ev { kind = "bounce", p = hp.to, t = hp.t + hp.dur });
			}
			f.stopT = gone > 0f ? t0 + Uneased(f, gone) * T : t;
			f.stopAt = gone > 0f ? FlightPoint(f, gone) : at;
			f.events.Add(new Ev { kind = "end", p = f.stopAt, t = f.stopT });
			f.lingerT = gone > 0f ? 0f : Mathf.Max(0f, Pv(c, "linger", ci, k));
			outList.Add(f);
		}
		return outList;
	}

	private static float FlightRot(Flight f, float t, Vector2 vel)
	{
		var rot = f.spin * (t - f.t0) * Mathf.Deg2Rad;
		if (f.c.align)
		{
			rot += Mathf.Atan2(vel.y, vel.x) - Mathf.Atan2(0f, f.dir);
		}
		return rot;
	}

	private static void FlightAt(Flight f, float t, out Vector2 pos, out Vector2 vel, out float u)
	{
		u = Mathf.Clamp01((t - f.t0) / f.T);
		var s = Progress(f, u);
		var s2 = Progress(f, Mathf.Min(1f, u + 0.01f));
		pos = FlightPoint(f, s);
		var nx = FlightPoint(f, Mathf.Max(s2, s + 0.0001f));
		vel = new Vector2(nx.x - pos.x != 0f ? nx.x - pos.x : f.end.x - f.origin.x, nx.y - pos.y);
	}

	private static bool ShotAt(Flight f, float t, out Shot shot)
	{
		shot = default(Shot);
		var c = f.c;
		if (t < f.t0 || t > f.stopT + f.lingerT)
		{
			return false;
		}
		var hitT = f.t0 + f.T;
		Vector2 pos, vel;
		float u;
		if (t <= hitT || f.gone > 0f)
		{
			FlightAt(f, Mathf.Min(t, f.stopT), out pos, out vel, out u);
			var alpha = 1f;
			if (c.fadeTo > 0f && u > c.fadeFrom)
			{
				alpha = Mathf.Max(0f, 1f - (u - c.fadeFrom) / Mathf.Max(0.001f, c.fadeTo - c.fadeFrom));
			}
			shot = new Shot { p = pos, rot = FlightRot(f, Mathf.Min(t, f.stopT), vel), alpha = alpha };
			return true;
		}
		Vector2 hitPos, hitVel;
		FlightAt(f, hitT, out hitPos, out hitVel, out u);
		var rot0 = FlightRot(f, hitT, hitVel);
		vel = new Vector2(f.dir, 0f);
		var a2 = 1f;
		if (t <= f.stopT && f.hops.Count > 0)
		{
			var hp = f.hops[f.hops.Count - 1];
			foreach (var x in f.hops)
			{
				if (t <= x.t + x.dur)
				{
					hp = x;
					break;
				}
			}
			var v = Mathf.Min(1f, (t - hp.t) / hp.dur);
			pos = new Vector2(Mathf.Lerp(hp.from.x, hp.to.x, v), Mathf.Lerp(hp.from.y, hp.to.y, v) + hp.h * 4f * v * (1f - v));
			vel = new Vector2(hp.to.x - hp.from.x, hp.h * 4f * (1f - 2f * v));
		}
		else
		{
			pos = f.stopAt;
			if (f.lingerT > 0f)
			{
				a2 = Mathf.Min(1f, 2.5f * (1f - (t - f.stopT) / f.lingerT));
			}
		}
		var spun = f.afterSpin * (Mathf.Min(t, f.stopT) - hitT) * Mathf.Deg2Rad;
		var rot = c.afterAlign && t <= f.stopT ? Mathf.Atan2(vel.y, vel.x) - Mathf.Atan2(0f, f.dir) + spun : rot0 + spun;
		shot = new Shot { p = pos, rot = rot, alpha = a2 };
		return true;
	}

	private void Update()
	{
		m_Clock += Time.deltaTime * LocalScale();
		var t = m_Clock;
		Simulate(t);
		if (t > m_Over + 0.1f)
		{
			Destroy(gameObject);
		}
	}

	private void Draw(int ci, Vector2 p, float rot, float sx, float sy, float alpha)
	{
		var c = m_Def.components[ci];
		if (c.m_Sprite == null)
		{
			return;
		}
		List<SpriteRenderer> pool;
		if (!m_Pools.TryGetValue(ci, out pool))
		{
			pool = new List<SpriteRenderer>();
			m_Pools[ci] = pool;
		}
		int used;
		m_Used.TryGetValue(ci, out used);
		if (used >= pool.Count)
		{
			var go = new GameObject(c.name);
			go.layer = m_Layer;
			go.transform.parent = transform;
			var sr = go.AddComponent<SpriteRenderer>();
			sr.sprite = c.m_Sprite;
			pool.Add(sr);
		}
		var r = pool[used];
		m_Used[ci] = used + 1;
		r.enabled = true;
		// components further down the list are drawn on top (the Forge draws them in order)
		r.transform.position = new Vector3(p.x * m_Scale, p.y * m_Scale, m_Z - ci * 0.01f);
		r.transform.rotation = Quaternion.Euler(0f, 0f, rot * Mathf.Rad2Deg);
		r.transform.localScale = new Vector3(m_RigScale.x * sx, m_RigScale.y * sy, 1f);
		r.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
	}

	private void Piece(ForgeProjectiles.Comp c, int ci, Vector2 p, float age, float seed, float k, float turn)
	{
		var own = Mathf.Max(0.02f, Pick(c, "life", seed, k, 5));
		if (age > own || age < 0f)
		{
			return;
		}
		var a = Mathf.Min(1f, age / own);
		var ang = Rand(seed, k, 1) * Mathf.PI * 2f;
		var sp = Mathf.Max(0f, Pick(c, "scatter", seed, k, 6)) * (Range(c, "scatter") != 0f ? 1f : 0.5f + Rand(seed, k, 2));
		var px = p.x + Mathf.Cos(ang) * sp * age;
		var py = p.y + Mathf.Sin(ang) * sp * age - 0.5f * Pick(c, "gravity", seed, k, 7) * age * age;
		var sc = Mathf.Lerp(Pick(c, "scaleStart", seed, k, 8), Pick(c, "scaleEnd", seed, k, 9), a);
		var q = c.squash * Mathf.Exp(-5f * a) * Mathf.Cos(14f * a);
		var randomTurn = c.randomTurn ? Rand(seed, k, 3) * Mathf.PI * 2f : 0f;
		Draw(ci, new Vector2(px, py), turn + randomTurn + Pick(c, "spin", seed, k, 10) * age * Mathf.Deg2Rad, sc * (1f + q), sc * (1f - q), c.fade ? 1f - a : 1f);
	}

	private void Simulate(float t)
	{
		m_Used.Clear();
		var comps = m_Def.components;
		for (var ci = 0; ci < comps.Length; ci++)
		{
			var c = comps[ci];
			if (c.spawn == "fire")
			{
				List<Flight> own;
				if (!m_Shots.TryGetValue(ci, out own))
				{
					continue;
				}
				foreach (var f in own)
				{
					m_Over = Mathf.Max(m_Over, f.stopT + f.lingerT);
					for (var e = 0; e < f.events.Count; e++)
					{
						var ev = f.events[e];
						if (t < ev.t)
						{
							continue;
						}
						var code = ev.kind == "launch" ? ForgeEffectExtras.TimingProjectileLaunch : ev.kind == "hit" ? ForgeEffectExtras.TimingProjectileHit
							: ev.kind == "bounce" ? ForgeEffectExtras.TimingProjectileBounce : ForgeEffectExtras.TimingProjectileEnd;
						Emit(code, ev.p, ((long)ci * 1000 + f.k) * 1000 + e);
					}
					Shot s;
					if (ShotAt(f, t, out s))
					{
						Draw(ci, s.p, s.rot, 1f, 1f, s.alpha);
					}
				}
				continue;
			}
			List<Flight> parents;
			if (c.of < 0 || !m_Shots.TryGetValue(c.of, out parents))
			{
				continue;
			}
			if (c.type == "slowmo")
			{
				// nothing to draw: the whole battle slows when its shot fires, hits, bounces or stops (ForgeSlowMotion)
				foreach (var f in parents)
				{
					for (var e = 0; e < f.events.Count; e++)
					{
						var ev = f.events[e];
						if (ev.kind != c.spawn)
						{
							continue;
						}
						m_Over = Mathf.Max(m_Over, ev.t);
						if (t >= ev.t && m_SlowDone.Add(ci * 100000 + f.k * 1000 + e))
						{
							Emit(ForgeEffectExtras.TimingSlowMotion, ev.p, -1 - (((long)ci * 1000 + f.k) * 1000 + e));
							if (c.slowScope == "projectile")
							{
								m_LocalSlows.Add(new LocalSlow { start = Time.unscaledTime, scale = Mathf.Clamp(c.slowScale, 0.01f, 1f), duration = Mathf.Max(0f, c.slowDuration), ease = Mathf.Max(0f, c.slowEase), easeOut = c.slowEaseOut < 0f ? Mathf.Max(0f, c.slowEase) : c.slowEaseOut });
							}
							else
							{
								ForgeSlowMotion.Play(c.slowScale, c.slowDuration, c.slowEase, c.slowEaseOut);
							}
						}
					}
				}
				continue;
			}
			var life = Mathf.Max(0.02f, c.life + Range(c, "life"));
			if (c.spawn == "trail" && c.style == "ribbon")
			{
				foreach (var f in parents)
				{
					m_Over = Mathf.Max(m_Over, f.stopT + life);
					Ribbon(c, ci, f, t, life);
				}
				continue;
			}
			if (c.spawn == "trail")
			{
				var rate = Mathf.Max(1f, c.rate);
				foreach (var f in parents)
				{
					m_Over = Mathf.Max(m_Over, f.stopT + life);
					var first = Mathf.CeilToInt(Mathf.Max(f.t0, t - life) * rate);
					var last = Mathf.FloorToInt(Mathf.Min(f.stopT, t) * rate);
					for (var j = first; j <= last; j++)
					{
						Shot s;
						if (!ShotAt(f, j / rate, out s))
						{
							continue;
						}
						var turn = 0f;
						if (c.trailAlign)
						{
							Shot s2;
							if (!ShotAt(f, j / rate + 0.004f, out s2))
							{
								s2 = s;
							}
							var ddx = s2.p.x - s.p.x;
							turn = Mathf.Atan2(s2.p.y - s.p.y, ddx != 0f ? ddx : f.dir) - Mathf.Atan2(0f, f.dir);
						}
						Piece(c, ci, s.p, t - j / rate, f.ci * 31 + f.k, j, turn);
					}
				}
				continue;
			}
			foreach (var f in parents)
			{
				foreach (var ev in f.events)
				{
					if (ev.kind != c.spawn)
					{
						continue;
					}
					m_Over = Mathf.Max(m_Over, ev.t + life);
					var age = t - ev.t;
					if (age < 0f || age > life)
					{
						continue;
					}
					var seed = f.ci * 31 + f.k + ev.t * 7f;
					var many = Mathf.Max(1, Mathf.RoundToInt(Pick(c, "count", seed, 0, 11)));
					for (var k = 0; k < many; k++)
					{
						Piece(c, ci, ev.p, age, seed, k, ev.rot);
					}
				}
			}
		}
		foreach (var pair in m_Pools)
		{
			int used;
			m_Used.TryGetValue(pair.Key, out used);
			for (var i = used; i < pair.Value.Count; i++)
			{
				pair.Value[i].enabled = false;
			}
		}
	}

	// A ribbon trail is a TrailRenderer following the shot, as the game's own projectile trails are.
	private void Ribbon(ForgeProjectiles.Comp c, int ci, Flight f, float t, float life)
	{
		if (c.m_RibbonMaterial == null || t < f.t0)
		{
			return;
		}
		Shot s;
		var alive = ShotAt(f, Mathf.Min(t, f.stopT), out s);
		if (f.ribbon == null)
		{
			if (!alive)
			{
				return;
			}
			var go = new GameObject(c.name);
			go.layer = m_Layer;
			go.transform.parent = transform;
			go.transform.position = new Vector3(s.p.x * m_Scale, s.p.y * m_Scale, m_Z + 0.01f);
			var tr = go.AddComponent<TrailRenderer>();
			tr.sharedMaterial = c.m_RibbonMaterial;
			tr.time = life;
			tr.minVertexDistance = 2f * m_Scale;
			tr.widthMultiplier = c.ribbonWidth * m_Scale;
			tr.widthCurve = AnimationCurve.Linear(0f, c.scaleStart, 1f, c.scaleEnd);
			tr.textureMode = c.textureMode == "tile" ? LineTextureMode.Tile : LineTextureMode.Stretch;
			if (c.textureMode == "tile")
			{
				tr.textureScale = new Vector2(1f / Mathf.Max(1f, c.ribbonTile * m_Scale), 1f);
			}
			var gradient = new Gradient();
			var keys = c.alphaKeys != null && c.alphaKeys.Length > 0 ? c.alphaKeys : null;
			GradientAlphaKey[] alphas;
			if (keys != null)
			{
				alphas = new GradientAlphaKey[Mathf.Min(8, keys.Length)];
				for (var i = 0; i < alphas.Length; i++)
				{
					alphas[i] = new GradientAlphaKey(keys[i].a, keys[i].t);
				}
			}
			else
			{
				alphas = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(c.fade ? 0f : 1f, 1f) };
			}
			gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphas);
			tr.colorGradient = gradient;
			tr.Clear();
			f.ribbon = tr;
		}
		if (alive && t <= f.stopT)
		{
			f.ribbon.transform.position = new Vector3(s.p.x * m_Scale, s.p.y * m_Scale, m_Z + 0.01f);
		}
		f.ribbon.emitting = t <= f.stopT;
	}
}
