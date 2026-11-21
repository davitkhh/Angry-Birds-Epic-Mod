using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.Shared.Generic;
using UnityEngine;

// World map music by area. Only hotspots the player has unlocked count (areas under the zone clouding stay out). Every hotspot belongs to its zone (the Zone_XX object it sits under); each zone has a theme. Each
// frame the theme of the hotspot nearest the screen centre dominates and the others fade in by how much farther away
// their nearest hotspot is: weight = exp(-extra distance / m_BlendDistance), so on a border both play at 50 % and a little
// inside an area the neighbour is down to about 20 %. Themes play on their own looping sources (loop points from
// MusicLoopPointOverrides). The map is silent before Chuck joins (WorldMapStateMgr.IsWorldMapMusicUnlocked), while an
// area intro is still to come, and while the game's own music source plays something else (title theme, jingles).
public class WorldMapAreaMusic : MonoBehaviour
{
	// Toggle: true = the Cobalt Plateaus play their own theme (WorldMapCobalt), also during their area intro;
	// false = they play the regular world map theme like the unlisted zones.
	public static bool CobaltPlateausOwnTheme = true;

	private const string CobaltPlateausTheme = "WorldMapCobalt";

	// Zone object -> theme: a clip under Resources/Sound/Music, or "" for silence. Zones not listed play the chapter's
	// world map track (the music_worldmap cue's current clip, see WorldMapStateMgr.SelectWorldMapMusic).
	private static readonly Dictionary<string, string> s_ZoneThemes = new Dictionary<string, string>
	{
		// South Beach has no song of its own: whatever was playing keeps playing (see ComputeWeights).
		{ "Zone_01", HoldTheme },
		// Pseudo-zone for the camp hotspot: music fades out around it.
		{ "Camp", "" },
		// Cobalt Plateaus.
		{ "Zone_02", CobaltPlateausTheme }
	};

	// Hotspots kept under a zone's hierarchy that stand elsewhere on the map.
	private static readonly HashSet<string> s_IgnoredHotspots = new HashSet<string> { "Hotspot_002_01_Arena" };

	// Hotspots whose area differs from their zone: hotspot object -> zone whose theme they play.
	private static readonly Dictionary<string, string> s_HotspotZones = new Dictionary<string, string>
	{
		// On the South Beach (Zone_04 in the scene); reachable early as the pirate chain's first side level.
		{ "Hotspot_028_Battleground", "Zone_01" },
		{ "Hotspot_001_Camp", "Camp" }
	};

	private const string DefaultTheme = "<chapter>";

	// Areas with no song: the camera being nearest one of their hotspots changes nothing, m_HeldTheme plays on.
	private const string HoldTheme = "<hold>";

	public static WorldMapAreaMusic Instance { get; private set; }

	// While set (a bird unlock sequence plays the bird's theme), every area theme is cut at once; they fade back in after.
	public static bool Silenced;

	// World units of extra distance that cut a neighbouring area's share to 1/e.
	public float m_BlendDistance = 120f;

	// Volume change per second when the mix shifts (full cross-fade in two seconds).
	public float m_FadeSpeed = 0.5f;

	// While set, this theme plays alone (an area intro), faded in at m_ForcedFadeSpeed.
	public string ForcedTheme;

	public float m_ForcedFadeSpeed = 1f / 3f;

	private class Point
	{
		public Vector2 Position;

		public string Theme;

		public HotSpotWorldMapViewBase View;
	}

	private class Channel
	{
		public AudioSource Source;

		public string ClipId;

		public float Volume;
	}

	private readonly List<Point> m_Points = new List<Point>();

	private readonly Dictionary<string, Channel> m_Channels = new Dictionary<string, Channel>();

	private readonly Dictionary<string, float> m_Weights = new Dictionary<string, float>();

	// The theme of the last area with a song the camera was in (or a forced theme); plays on through HoldTheme areas.
	private string m_HeldTheme;

	private void Awake()
	{
		Instance = this;
		foreach (var hotspot in FindObjectsOfType<HotSpotWorldMapViewBase>(true))
		{
			if (s_IgnoredHotspots.Contains(hotspot.name))
			{
				continue;
			}
			var zone = hotspot.GetComponentsInParent<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Zone_"));
			if (zone == null)
			{
				continue;
			}
			string zoneName;
			if (!s_HotspotZones.TryGetValue(hotspot.name, out zoneName))
			{
				zoneName = zone.name;
			}
			string theme;
			if (!s_ZoneThemes.TryGetValue(zoneName, out theme))
			{
				theme = DefaultTheme;
			}
			theme = Resolve(theme);
			m_Points.Add(new Point { Position = hotspot.transform.position, Theme = theme, View = hotspot });
		}
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
		foreach (var channel in m_Channels.Values)
		{
			MusicLoopPointOverrides.ExtraSources.Remove(channel.Source);
		}
	}

	private void Update()
	{
		var forced = !string.IsNullOrEmpty(ForcedTheme);
		var allowed = forced || (WorldMapStateMgr.IsWorldMapMusicUnlocked() && !WorldMapAreaIntro.IsAnyPending() && !IsGameMusicPlaying());
		ComputeWeights(forced);
		var cue = DIContainerInfrastructure.AudioManager != null ? DIContainerInfrastructure.AudioManager.GetSound("music_worldmap") as Sound : null;
		var baseVolume = cue != null ? cue.Volume : 1f;
		var muted = DIContainerInfrastructure.AudioManager != null && DIContainerInfrastructure.AudioManager.IsMuted(0);
		var speed = forced ? m_ForcedFadeSpeed : m_FadeSpeed;
		foreach (var theme in m_Weights.Keys.Union(m_Channels.Keys).ToList())
		{
			var clipId = ClipFor(theme, cue);
			if (string.IsNullOrEmpty(clipId))
			{
				continue;
			}
			float weight;
			m_Weights.TryGetValue(theme, out weight);
			var target = allowed && !Silenced ? weight : 0f;
			Channel channel;
			if (!m_Channels.TryGetValue(theme, out channel))
			{
				if (target <= 0.001f)
				{
					continue;
				}
				channel = new Channel();
				m_Channels[theme] = channel;
			}
			if (channel.ClipId != clipId && target > 0.001f)
			{
				Load(channel, clipId);
			}
			channel.Volume = Silenced ? 0f : Mathf.MoveTowards(channel.Volume, target, speed * Time.unscaledDeltaTime);
			if (channel.Source == null)
			{
				continue;
			}
			channel.Source.mute = muted;
			channel.Source.volume = channel.Volume * baseVolume;
			if (channel.Volume > 0f && !channel.Source.isPlaying)
			{
				channel.Source.Play();
			}
			else if (channel.Volume <= 0f && target <= 0f && channel.Source.isPlaying)
			{
				channel.Source.Stop();
			}
		}
	}

	// The game's own music source has something of its own on (title theme, a cutscene jingle).
	private static bool IsGameMusicPlaying()
	{
		var primary = DIContainerInfrastructure.PrimaryMusicSource;
		return primary != null && primary.isPlaying && primary.clip != null;
	}

	private void ComputeWeights(bool forced)
	{
		m_Weights.Clear();
		if (forced)
		{
			m_Weights[ForcedTheme] = 1f;
			m_HeldTheme = ForcedTheme;
			return;
		}
		var camera = DIContainerInfrastructure.LocationStateMgr != null ? DIContainerInfrastructure.LocationStateMgr.SceneryCamera : null;
		if (camera == null || m_Points.Count == 0)
		{
			m_Weights[DefaultTheme] = 1f;
			return;
		}
		Vector2 centre = camera.transform.position;
		var nearest = new Dictionary<string, float>();
		// Only hotspots the player has reached count, so areas still under the zone clouding never take over.
		var reached = m_Points.Where(IsReached).ToList();
		foreach (var point in reached.Count > 0 ? reached : m_Points)
		{
			var distance = Vector2.Distance(centre, point.Position);
			float best;
			if (!nearest.TryGetValue(point.Theme, out best) || distance < best)
			{
				nearest[point.Theme] = distance;
			}
		}
		var closest = nearest.Values.Min();
		var closestTheme = nearest.First(p => p.Value == closest).Key;
		// In a songless area (or at the silent camp) other songs don't fade in: only the held song and the silence mix.
		// Before any song was heard here (just loaded onto the beach), the held song is the nearest reached one.
		var songless = closestTheme == HoldTheme || closestTheme.Length == 0;
		if (!songless)
		{
			m_HeldTheme = closestTheme;
		}
		else if (m_HeldTheme == null && nearest.Keys.Any(IsSong))
		{
			m_HeldTheme = nearest.Where(p => IsSong(p.Key)).OrderBy(p => p.Value).First().Key;
		}
		var total = 0f;
		foreach (var pair in nearest)
		{
			if (songless ? IsSong(pair.Key) : pair.Key == HoldTheme)
			{
				continue;
			}
			var weight = Mathf.Exp(-(pair.Value - closest) / Mathf.Max(1f, m_BlendDistance));
			if (weight <= 0.01f)
			{
				continue;
			}
			total += weight;
			if (pair.Key != HoldTheme)
			{
				m_Weights[pair.Key] = weight;
			}
			else if (m_HeldTheme != null)
			{
				m_Weights[m_HeldTheme] = weight;
			}
		}
		foreach (var key in m_Weights.Keys.ToList())
		{
			m_Weights[key] /= total;
		}
	}

	// Applies the theme toggles.
	public static string Resolve(string theme)
	{
		return theme == CobaltPlateausTheme && !CobaltPlateausOwnTheme ? DefaultTheme : theme;
	}

	private static bool IsSong(string theme)
	{
		return theme != HoldTheme && theme.Length > 0;
	}

	private static bool IsReached(Point point)
	{
		return point.View != null && point.View.Model != null && point.View.Model.Data.UnlockState >= HotspotUnlockState.Active;
	}

	private static string ClipFor(string theme, Sound cue)
	{
		if (theme != DefaultTheme)
		{
			return theme;
		}
		return cue != null && cue.AssetNames != null && cue.AssetNames.Count > 0 ? cue.AssetNames[0] : "WorldMap";
	}

	private void Load(Channel channel, string clipId)
	{
		var clip = Resources.Load<AudioClip>("Sound/Music/" + clipId);
		if (clip == null)
		{
			DebugLog.Error(GetType(), "world map theme not found: Sound/Music/" + clipId);
			channel.ClipId = clipId;
			return;
		}
		if (channel.Source == null)
		{
			channel.Source = gameObject.AddComponent<AudioSource>();
			channel.Source.playOnAwake = false;
			channel.Source.loop = true;
			channel.Source.spatialBlend = 0f;
			channel.Source.ignoreListenerPause = true;
			MusicLoopPointOverrides.ExtraSources.Add(channel.Source);
		}
		channel.Source.Stop();
		channel.Source.clip = clip;
		channel.ClipId = clipId;
	}
}

// One-time area introductions: a large title over the screen while the camera slowly pans through the area and its theme
// starts. Played right after the cutscene of the hotspot that opens the area, before its popups (HotSpotWorldMapViewBattle).
public static class WorldMapAreaIntro
{
	private class Intro
	{
		public string Key;

		public string Title;

		public Color TitleColor;

		public string Theme;

		// The intro plays when this hotspot is first beaten while the player owns RequiredBird.
		public string TriggerHotspotId;

		public string RequiredBird;

		// The pan, in world coordinates, starting from wherever the camera is.
		public Vector2[] Waypoints;
	}

	private static readonly Intro[] s_Intros = new Intro[]
	{
		new Intro
		{
			Key = "cobalt_plateaus",
			Title = "Cobalt Plateaus",
			TitleColor = new Color(53f / 255f, 161f / 255f, 161f / 255f, 1f),
			Theme = "WorldMapCobalt",
			TriggerHotspotId = "hotspot_006_battleground",
			RequiredBird = "bird_yellow",
			// 006 -> 010 -> 011 -> 012 -> 013 -> castle 1; the farm beyond the castle is still under Sector_1's clouds.
			Waypoints = new[] { new Vector2(-560f, -1560f), new Vector2(-320f, -1480f), new Vector2(-420f, -1330f), new Vector2(-640f, -1210f), new Vector2(-900f, -1080f) }
		}
	};

	public const float PanSeconds = 16f;

	public const float TitleFadeSeconds = 3f;

	public const float ReturnSeconds = 2f;

	private const string ShownKeyPrefix = "areaintro:";

	public static bool IsAnyPending()
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		return s_Intros.Any(i => IsPending(player, i));
	}

	private static bool IsPending(PlayerGameData player, Intro intro)
	{
		if (player == null || player.WorldGameData == null || (player.Data.EquippedSkins != null && player.Data.EquippedSkins.ContainsKey(ShownKeyPrefix + intro.Key)))
		{
			return false;
		}
		HotspotGameData hotspot;
		if (!player.WorldGameData.HotspotGameDatas.TryGetValue(intro.TriggerHotspotId, out hotspot) || hotspot.Data.UnlockState != HotspotUnlockState.ResolvedNew)
		{
			return false;
		}
		return string.IsNullOrEmpty(intro.RequiredBird) || (player.Birds != null && player.Birds.Any(b => b.Name == intro.RequiredBird));
	}

	public static IEnumerator PlayIfDue(HotSpotWorldMapViewBase hotspot)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		var intro = s_Intros.FirstOrDefault(i => hotspot.Model != null && i.TriggerHotspotId == hotspot.Model.BalancingData.NameId && IsPending(player, i));
		if (intro == null)
		{
			yield break;
		}
		yield return Play(intro, player);
	}

	// Replays an intro on the world map whatever the save says (testing: Temp/AreaIntroReplay.request in play mode).
	public static IEnumerator Replay(string key)
	{
		var intro = s_Intros.FirstOrDefault(i => i.Key == key) ?? s_Intros[0];
		yield return Play(intro, DIContainerInfrastructure.GetCurrentPlayer());
	}

	private static IEnumerator Play(Intro intro, PlayerGameData player)
	{
		var state = DIContainerInfrastructure.LocationStateMgr as WorldMapStateMgr;
		var music = WorldMapAreaMusic.Instance;
		if (state == null || state.m_CamRoot == null)
		{
			MarkShown(player, intro);
			yield break;
		}
		// The intro can run before the world map has started its music (hotspots activate first).
		if (music == null)
		{
			music = state.gameObject.AddComponent<WorldMapAreaMusic>();
		}
		state.EnableInput(false);
		var drag = state.m_CamRoot.GetComponentInChildren<DragController>(true) ?? state.m_CamRoot.GetComponentInParent<DragController>();
		var dragWasEnabled = drag != null && drag.enabled;
		if (drag != null)
		{
			drag.enabled = false;
		}
		if (music != null)
		{
			music.ForcedTheme = WorldMapAreaMusic.Resolve(intro.Theme);
		}
		var label = CreateTitle(intro);
		var camRoot = state.m_CamRoot;
		var start = camRoot.position;
		var points = new List<Vector3> { start };
		points.AddRange(intro.Waypoints.Select(w => new Vector3(w.x, w.y, start.z)));
		var path = new ActionTree.SmoothPath(points);
		// Whatever the game's own music source still plays (e.g. a cutscene's music) gives way to the theme.
		var primary = DIContainerInfrastructure.PrimaryMusicSource;
		var primaryVolume = primary != null ? primary.volume : 1f;
		var fadePrimary = primary != null && primary.isPlaying;
		for (var t = 0f; t < PanSeconds; t += Time.deltaTime)
		{
			if (fadePrimary && primary != null)
			{
				primary.volume = Mathf.Lerp(primaryVolume, 0f, t / TitleFadeSeconds);
				if (t >= TitleFadeSeconds)
				{
					primary.Stop();
					primary.volume = primaryVolume;
					fadePrimary = false;
				}
			}
			camRoot.position = path.At(Mathf.SmoothStep(0f, 1f, t / PanSeconds));
			if (label != null)
			{
				label.alpha = Mathf.Clamp01(Mathf.Min(t / TitleFadeSeconds, (PanSeconds - t) / TitleFadeSeconds));
			}
			yield return null;
		}
		if (label != null)
		{
			Object.Destroy(label.transform.parent.gameObject);
		}
		var end = camRoot.position;
		for (var t = 0f; t < ReturnSeconds; t += Time.deltaTime)
		{
			camRoot.position = Vector3.Lerp(end, start, Mathf.SmoothStep(0f, 1f, t / ReturnSeconds));
			yield return null;
		}
		camRoot.position = start;
		MarkShown(player, intro);
		if (music != null)
		{
			music.ForcedTheme = null;
		}
		if (drag != null)
		{
			drag.enabled = dragWasEnabled;
		}
		state.EnableInput(true);
	}

	private static void MarkShown(PlayerGameData player, Intro intro)
	{
		if (player.Data.EquippedSkins == null)
		{
			player.Data.EquippedSkins = new Dictionary<string, string>();
		}
		player.Data.EquippedSkins[ShownKeyPrefix + intro.Key] = "1";
		player.SavePlayerData();
	}

	// A large title on its own panel above the interface, in the game's header font. The game has no UIRoot: interface
	// widgets sit in world space in front of the interface camera (orthographic size 360, 1 unit = 1 pixel of 720).
	private static UILabel CreateTitle(Intro intro)
	{
		var core = DIContainerInfrastructure.GetCoreStateMgr();
		var camera = core != null ? core.m_InterfaceCamera : null;
		var font = Resources.FindObjectsOfTypeAll<UIFont>().FirstOrDefault(f => f.name == "Header_Large") ?? Resources.FindObjectsOfTypeAll<UIFont>().FirstOrDefault(f => f.name.StartsWith("Header"));
		if (camera == null || font == null)
		{
			DebugLog.Warn(typeof(WorldMapAreaIntro), "no interface camera or header font for the area title");
			return null;
		}
		var holder = new GameObject("AreaIntroTitle");
		holder.layer = camera.gameObject.layer;
		holder.transform.position = new Vector3(camera.transform.position.x, camera.transform.position.y, 0f);
		holder.transform.localRotation = Quaternion.identity;
		holder.transform.localScale = Vector3.one;
		var panel = holder.AddComponent<UIPanel>();
		panel.depth = 1000;
		var go = new GameObject("Title");
		go.layer = holder.layer;
		go.transform.parent = holder.transform;
		go.transform.localPosition = new Vector3(0f, 190f, 0f);
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		var label = go.AddComponent<UILabel>();
		label.rawPivot = UIWidget.Pivot.Center;
		label.bitmapFont = font;
		label.fontSize = 96;
		label.overflowMethod = UILabel.Overflow.ResizeFreely;
		label.supportEncoding = false;
		label.color = intro.TitleColor;
		label.effectStyle = UILabel.Effect.Outline;
		label.effectColor = Color.black;
		label.effectDistance = new Vector2(3f, 3f);
		label.text = intro.Title;
		label.MakePixelPerfect();
		label.alpha = 0f;
		return label;
	}
}
