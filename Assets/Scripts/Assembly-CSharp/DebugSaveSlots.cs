using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// TEMPORARY testing tool: named snapshots of the player profile so a playtest can jump back to a story point.
//
// A slot is the exact string the game writes to its "player" pref, copied to
// <persistentDataPath>/DebugSaveSlots/<name>.sav (NOT inside prefs_linux, which PlayerPrefs.DeleteAll wipes).
// Loading goes through DIContainerInfrastructure.ResetWithNewProfile, the same path the account sync uses
// to swap profiles, which then reloads the world map (or the intro, for a pre-intro save).
//
// The window is opened by an extra button that Install() clones into the world-map options bar
// (the empty slot left of the daily-reward button). To remove the tool: delete this file and the
// DebugSaveSlots.Install(this) line in OptionsMgr.Start.
public class DebugSaveSlots : MonoBehaviour
{
	private const string AutoBackupName = "_auto_before_load";

	// Body of a "fresh" slot: loading it starts a brand-new game instead of restoring a snapshot.
	private const string FreshMarker = "FRESH_NEW_GAME";

	private const string ButtonIconSprite = "TryAgain";

	private const float VirtualHeight = 720f;

	private class Slot
	{
		public string Name;
		public string Path;
		public string Info;
		public DateTime SavedAt;
	}

	private static DebugSaveSlots s_instance;

	private bool m_visible;

	private bool m_blockedInput;

	private string m_newName = string.Empty;

	private string m_status = string.Empty;

	private string m_pendingDelete;

	private string m_pendingOverwrite;

	private Vector2 m_scroll;

	private List<Slot> m_slots = new List<Slot>();

	private Rect m_windowRect = new Rect(0f, 0f, 860f, 580f);

	private GUIStyle m_labelStyle;

	private GUIStyle m_smallStyle;

	public static string Directory
	{
		get { return System.IO.Path.Combine(Application.persistentDataPath, "DebugSaveSlots"); }
	}

	public static void Install(OptionsMgr options)
	{
		if (options == null || options.m_InfoButton == null || options.m_ButtonGrid == null)
		{
			return;
		}
		try
		{
			var grid = options.m_ButtonGrid.transform;
			var template = options.m_InfoButton.transform.parent;
			var parent = grid.parent;
			if (parent.Find("0_DebugSaveSlots") != null)
			{
				return;
			}
			var clone = Instantiate(template.gameObject);
			clone.name = "0_DebugSaveSlots";
			clone.transform.parent = parent;
			clone.transform.localScale = template.localScale;
			clone.transform.localRotation = template.localRotation;
			// One grid cell left of the first button, i.e. the empty stretch between the gear and the daily reward.
			clone.transform.localPosition = grid.localPosition + new Vector3(-options.m_ButtonGrid.cellWidth, 0f, 0f);
			foreach (var sprite in clone.GetComponentsInChildren<UISprite>(true))
			{
				if (sprite.name == "Icon")
				{
					sprite.spriteName = ButtonIconSprite;
					sprite.MakePixelPerfect();
					var max = Mathf.Max(sprite.width, sprite.height);
					if (max > 56)
					{
						sprite.width = Mathf.RoundToInt(sprite.width * 56f / max);
						sprite.height = Mathf.RoundToInt(sprite.height * 56f / max);
					}
				}
			}
			var trigger = clone.GetComponentInChildren<UIInputTrigger>(true);
			if (trigger != null)
			{
				trigger.Clicked += Toggle;
			}
		}
		catch (Exception ex)
		{
			DebugLog.Error("[DebugSaveSlots] Install failed: " + ex);
		}
	}

	public static void Toggle()
	{
		if (s_instance == null)
		{
			var go = new GameObject("DebugSaveSlots");
			DontDestroyOnLoad(go);
			s_instance = go.AddComponent<DebugSaveSlots>();
		}
		s_instance.SetVisible(!s_instance.m_visible);
	}

	private void SetVisible(bool visible)
	{
		m_visible = visible;
		m_pendingDelete = null;
		m_pendingOverwrite = null;
		var core = DIContainerInfrastructure.GetCoreStateMgr();
		if (visible)
		{
			m_newName = DefaultSlotName();
			m_status = string.Empty;
			RefreshSlots();
			// IMGUI does not stop NGUI from seeing the same clicks, so the map/HUD is blocked while the window is up.
			if (core != null && !core.m_AllInputBlocked)
			{
				core.BlockAllInput(true);
				m_blockedInput = true;
			}
		}
		else if (m_blockedInput)
		{
			m_blockedInput = false;
			if (core != null)
			{
				core.BlockAllInput(false);
			}
		}
	}

	private void OnDestroy()
	{
		if (s_instance == this)
		{
			s_instance = null;
		}
	}

	private void Update()
	{
		if (m_visible && Input.GetKeyDown(KeyCode.Escape))
		{
			SetVisible(false);
		}
	}

	private static string CurrentHotspotId()
	{
		try
		{
			return DIContainerInfrastructure.GetCurrentPlayer().WorldGameData.CurrentHotspotGameData.BalancingData.NameId;
		}
		catch
		{
			return "?";
		}
	}

	private static string DescribeCurrent()
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		if (player == null)
		{
			return "no player";
		}
		return "Lv " + player.Data.Level + "  @ " + CurrentHotspotId();
	}

	private static string DefaultSlotName()
	{
		var hotspot = CurrentHotspotId().Replace("hotspot_", string.Empty);
		return hotspot + " " + DateTime.Now.ToString("MM-dd HHmm");
	}

	private static string Sanitize(string name)
	{
		name = (name ?? string.Empty).Trim();
		foreach (var c in System.IO.Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '_');
		}
		return name;
	}

	private void RefreshSlots()
	{
		m_slots.Clear();
		try
		{
			if (!System.IO.Directory.Exists(Directory))
			{
				return;
			}
			foreach (var file in System.IO.Directory.GetFiles(Directory, "*.sav"))
			{
				var slot = new Slot
				{
					Name = System.IO.Path.GetFileNameWithoutExtension(file),
					Path = file,
					SavedAt = File.GetLastWriteTime(file),
					Info = string.Empty
				};
				using (var reader = new StreamReader(file))
				{
					var header = reader.ReadLine();
					if (header != null && header.StartsWith("#"))
					{
						slot.Info = header.Substring(1);
					}
				}
				m_slots.Add(slot);
			}
			m_slots = m_slots.OrderByDescending(s => s.SavedAt).ToList();
		}
		catch (Exception ex)
		{
			m_status = "Could not read slots: " + ex.Message;
		}
	}

	private bool SaveSlot(string name)
	{
		name = Sanitize(name);
		if (string.IsNullOrEmpty(name))
		{
			m_status = "Enter a slot name first.";
			return false;
		}
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		if (player == null)
		{
			m_status = "No player loaded.";
			return false;
		}
		try
		{
			// Flush the live profile so the slot is exactly what the game would load on restart.
			player.SavePlayerData();
			var serialized = DIContainerInfrastructure.GetPlayerPrefsService().GetString(DIContainerConfig.GetConstants().ProfileKey, string.Empty);
			if (string.IsNullOrEmpty(serialized))
			{
				m_status = "Profile pref is empty, nothing saved.";
				return false;
			}
			System.IO.Directory.CreateDirectory(Directory);
			var path = System.IO.Path.Combine(Directory, name + ".sav");
			File.WriteAllText(path, "#" + DescribeCurrent() + "\n" + serialized + "\n");
			m_status = "Saved \"" + name + "\".";
			DebugLog.Log("[DebugSaveSlots] Saved slot " + path);
			RefreshSlots();
			return true;
		}
		catch (Exception ex)
		{
			m_status = "Save failed: " + ex.Message;
			DebugLog.Error("[DebugSaveSlots] Save failed: " + ex);
			return false;
		}
	}

	private void LoadSlot(Slot slot)
	{
		if (DIContainerInfrastructure.LocationStateMgr && DIContainerInfrastructure.LocationStateMgr.IsBirdWalking())
		{
			m_status = "Wait for the bird to stop walking.";
			return;
		}
		ABH.Shared.Models.PlayerData data = null;
		var fresh = false;
		try
		{
			var serialized = File.ReadAllLines(slot.Path).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("#"));
			if (string.IsNullOrEmpty(serialized))
			{
				m_status = "Slot file is empty.";
				return;
			}
			fresh = serialized.Trim() == FreshMarker;
			if (!fresh)
			{
				data = DIContainerInfrastructure.GetStringSerializer().Deserialize<ABH.Shared.Models.PlayerData>(Uri.UnescapeDataString(serialized));
			}
		}
		catch (Exception ex)
		{
			m_status = "Load failed: " + ex.Message;
			DebugLog.Error("[DebugSaveSlots] Load failed: " + ex);
			return;
		}
		if (data == null && !fresh)
		{
			m_status = "Slot did not deserialize.";
			return;
		}
		if (slot.Name != AutoBackupName)
		{
			SaveSlot(AutoBackupName);
		}
		DebugLog.Log("[DebugSaveSlots] Loading slot " + slot.Path + (fresh ? " (fresh new game)" : string.Empty));
		SetVisible(false);
		if (fresh)
		{
			// Same as a first install: no profile (the game creates a new one and runs the intro) and no
			// "already saw the intro" prefs left over from the previous profile.
			var prefs = DIContainerInfrastructure.GetPlayerPrefsService();
			prefs.DeleteKey("played_intro");
			prefs.DeleteKey(CoreStateMgr.IntroBattleEnteredKey);
		}
		DIContainerInfrastructure.ResetWithNewProfile(data);
	}

	private void CreateFreshSlot(string name)
	{
		name = UniqueName(string.IsNullOrEmpty(Sanitize(name)) ? "New game" : name);
		try
		{
			System.IO.Directory.CreateDirectory(Directory);
			File.WriteAllText(System.IO.Path.Combine(Directory, name + ".sav"), "#Fresh start (new game)\n" + FreshMarker + "\n");
			m_status = "Created fresh slot \"" + name + "\". Load it to start a new game, then Overwrite it to keep progress.";
		}
		catch (Exception ex)
		{
			m_status = "Create failed: " + ex.Message;
		}
		RefreshSlots();
	}

	private string UniqueName(string name)
	{
		name = Sanitize(name);
		if (!m_slots.Any(s => s.Name == name))
		{
			return name;
		}
		for (var i = 2; ; i++)
		{
			var candidate = name + " (" + i + ")";
			if (!m_slots.Any(s => s.Name == candidate))
			{
				return candidate;
			}
		}
	}

	private void DuplicateSlot(Slot slot)
	{
		try
		{
			var name = UniqueName(slot.Name + " copy");
			var path = System.IO.Path.Combine(Directory, name + ".sav");
			File.Copy(slot.Path, path);
			File.SetLastWriteTime(path, DateTime.Now);
			m_status = "Duplicated \"" + slot.Name + "\" as \"" + name + "\".";
		}
		catch (Exception ex)
		{
			m_status = "Duplicate failed: " + ex.Message;
		}
		RefreshSlots();
	}

	private void RenameSlot(Slot slot, string newName)
	{
		newName = Sanitize(newName);
		if (string.IsNullOrEmpty(newName) || newName == slot.Name)
		{
			m_status = "Type the new name in the box at the top, then press Rename.";
			return;
		}
		if (m_slots.Any(s => s.Name == newName))
		{
			m_status = "\"" + newName + "\" already exists.";
			return;
		}
		try
		{
			var time = File.GetLastWriteTime(slot.Path);
			var path = System.IO.Path.Combine(Directory, newName + ".sav");
			File.Move(slot.Path, path);
			File.SetLastWriteTime(path, time);
			m_status = "Renamed \"" + slot.Name + "\" to \"" + newName + "\".";
		}
		catch (Exception ex)
		{
			m_status = "Rename failed: " + ex.Message;
		}
		RefreshSlots();
	}

	private void DeleteSlot(Slot slot)
	{
		try
		{
			File.Delete(slot.Path);
			m_status = "Deleted \"" + slot.Name + "\".";
		}
		catch (Exception ex)
		{
			m_status = "Delete failed: " + ex.Message;
		}
		RefreshSlots();
	}

	private void EnsureStyles()
	{
		if (m_labelStyle != null)
		{
			return;
		}
		m_labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = false };
		m_smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false };
		m_smallStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
	}

	private void OnGUI()
	{
		if (!m_visible)
		{
			return;
		}
		var scale = Screen.height / VirtualHeight;
		GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
		var virtualWidth = Screen.width / scale;
		m_windowRect.x = (virtualWidth - m_windowRect.width) * 0.5f;
		m_windowRect.y = (VirtualHeight - m_windowRect.height) * 0.5f;
		EnsureStyles();
		GUI.depth = -1000;
		// Dim the game behind the window.
		var old = GUI.color;
		GUI.color = new Color(0f, 0f, 0f, 0.5f);
		GUI.DrawTexture(new Rect(0f, 0f, virtualWidth, VirtualHeight), Texture2D.whiteTexture);
		GUI.color = old;
		GUI.ModalWindow(0x5A5E, m_windowRect, DrawWindow, "Save Slots (testing only)");
	}

	private void DrawWindow(int id)
	{
		GUILayout.Space(6f);
		GUILayout.Label("Current: " + DescribeCurrent(), m_labelStyle);
		GUILayout.BeginHorizontal();
		m_newName = GUILayout.TextField(m_newName, 60, GUILayout.Height(30f), GUILayout.ExpandWidth(true));
		if (GUILayout.Button("Save current", GUILayout.Width(120f), GUILayout.Height(30f)))
		{
			if (string.IsNullOrEmpty(Sanitize(m_newName)))
			{
				m_newName = DefaultSlotName();
			}
			SaveSlot(UniqueName(m_newName));
		}
		if (GUILayout.Button("Create fresh", GUILayout.Width(120f), GUILayout.Height(30f)))
		{
			CreateFreshSlot(m_newName == DefaultSlotName() ? string.Empty : m_newName);
		}
		GUILayout.EndHorizontal();
		GUILayout.Space(6f);
		m_scroll = GUILayout.BeginScrollView(m_scroll, GUILayout.ExpandHeight(true));
		if (m_slots.Count == 0)
		{
			GUILayout.Label("No slots yet.", m_smallStyle);
		}
		Slot toLoad = null;
		Slot toDuplicate = null;
		Slot toRename = null;
		foreach (var slot in m_slots.ToList())
		{
			GUILayout.BeginHorizontal(GUI.skin.box);
			GUILayout.BeginVertical();
			GUILayout.Label(slot.Name, m_labelStyle);
			GUILayout.Label(slot.Info + "   " + slot.SavedAt.ToString("yyyy-MM-dd HH:mm"), m_smallStyle);
			GUILayout.EndVertical();
			if (GUILayout.Button("Load", GUILayout.Width(80f), GUILayout.Height(40f)))
			{
				toLoad = slot;
			}
			if (GUILayout.Button("Duplicate", GUILayout.Width(90f), GUILayout.Height(40f)))
			{
				toDuplicate = slot;
			}
			if (GUILayout.Button("Rename", GUILayout.Width(80f), GUILayout.Height(40f)))
			{
				toRename = slot;
			}
			var overwriteLabel = m_pendingOverwrite == slot.Name ? "Sure?" : "Overwrite";
			if (GUILayout.Button(overwriteLabel, GUILayout.Width(90f), GUILayout.Height(40f)))
			{
				if (m_pendingOverwrite == slot.Name)
				{
					m_pendingOverwrite = null;
					SaveSlot(slot.Name);
				}
				else
				{
					m_pendingOverwrite = slot.Name;
					m_pendingDelete = null;
				}
			}
			var deleteLabel = m_pendingDelete == slot.Name ? "Sure?" : "Delete";
			if (GUILayout.Button(deleteLabel, GUILayout.Width(70f), GUILayout.Height(40f)))
			{
				if (m_pendingDelete == slot.Name)
				{
					m_pendingDelete = null;
					DeleteSlot(slot);
				}
				else
				{
					m_pendingDelete = slot.Name;
					m_pendingOverwrite = null;
				}
			}
			GUILayout.EndHorizontal();
		}
		GUILayout.EndScrollView();
		if (toDuplicate != null)
		{
			DuplicateSlot(toDuplicate);
		}
		if (toRename != null)
		{
			RenameSlot(toRename, m_newName);
		}
		if (toLoad != null)
		{
			LoadSlot(toLoad);
		}
		if (!string.IsNullOrEmpty(m_status))
		{
			GUILayout.Label(m_status, m_smallStyle);
		}
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Open folder", GUILayout.Height(32f)))
		{
			System.IO.Directory.CreateDirectory(Directory);
			Application.OpenURL("file://" + Directory);
		}
		if (GUILayout.Button("Close", GUILayout.Height(32f)))
		{
			SetVisible(false);
		}
		GUILayout.EndHorizontal();
	}
}
