using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds Assets/Scenes/2_Camp/2_Interface/Window_EquipmentPresets.unity out of parts of existing game windows:
//  - Window_BirdManager: window root, header with close button, category tabs and the item grid
//  - Window_BattlePreparation: the bird lineup (CharacterSlot birds with blue circles), its ground, round buttons
//  - Popup_EnterName: the name input field
// Run from Tools > Equipment Presets > Rebuild Window Scene. Writes preview renders to Temp/EquipmentPresetPreview_*.png.
public static class EquipmentPresetSceneBuilder
{
	private const string ScenePath = "Assets/Scenes/2_Camp/2_Interface/Window_EquipmentPresets.unity";

	private const string WorkPath = "Assets/Scenes/2_Camp/2_Interface/Window_EquipmentPresets_Building.unity";

	private const string BirdManagerPath = "Assets/Scenes/2_Camp/2_Interface/Window_BirdManager.unity";

	private const string BattlePrepPath = "Assets/Scenes/1_WorldMap/2_Interface/Window_BattlePreparation.unity";

	private const string EnterNamePath = "Assets/Scenes/8_Arena/2_Interface/Popup_EnterName.unity";

	private const string GenericAtlasPath = "Assets/Prefab/GenericElements.prefab";

	private const string CampAtlasPath = "Assets/Prefab/CampElements.prefab";

	private const string AdditionalAtlasPath = "Assets/Prefab/GenericElements_Additional.prefab";

	private const string PencilPath = "Assets/Texture2D/Icon_EditPreset.png";

	private const string BarTexturePath = "Assets/Texture2D/PresetBar.png";

	private const string ButtonIconPath = "Assets/Texture2D/Icon_EquipmentPresets.png";

	// The droppable "open presets" button (EquipmentPresetButton.PrefabResource).
	private const string ButtonPrefabPath = "Assets/Resources/EquipmentPresets/Button_EquipmentPresets.prefab";

	private const string CharacterSlotPath = "Assets/Prefab/CharacterSlot.prefab";

	// Touch this file (from outside Unity) to trigger a rebuild in an open editor.
	private const string RequestFile = "Temp/EquipmentPresetBuild.request";

	private const string LogFile = "Temp/EquipmentPresetBuild.log";

	private const string RefreshRequestFile = "Temp/EquipmentPresetRefresh.request";

	private const float BarWidth = 980f;

	private const float BarHeight = 150f;

	private const float ListTop = 262f;

	// Bottom edge of the header plank; the list is visible from here down, so bars scroll out under the header.
	private const float HeaderBottom = 280f;

	// The list's clip reaches this far up under the header plank (which is drawn over the bars), so its fade is hidden there.
	private const float ClipTop = HeaderBottom + 20f;

	private const float ListSoftness = 10f;

	private const float ListBottom = -262f;

	private const float LineupY = 30f;

	private const float NamePlankWidth = 476f;

	// The bird manager keeps its item info box under the item strip; without it the panel slides down this far.
	private const float CategoriesDrop = 190f;

	// Trims wood off the top of the equipment board so it hugs the item slots (tabs move down with it).
	private const float BoardTopTrim = 10f;

	private static StringBuilder s_Log;

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= PollRequest;
		EditorApplication.update += PollRequest;
	}

	private static void PollRequest()
	{
		// Lets a terminal make Unity pick up changed scripts without focusing the editor window.
		if (File.Exists(RefreshRequestFile) && !EditorApplication.isCompiling && !EditorApplication.isPlaying)
		{
			File.Delete(RefreshRequestFile);
			AssetDatabase.Refresh();
		}
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
		{
			return;
		}
		File.Delete(RequestFile);
		Build();
	}

	[MenuItem("Tools/Equipment Presets/Rebuild Window Scene")]
	public static void Build()
	{
		s_Log = new StringBuilder();
		if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
		{
			Debug.LogWarning("[EquipmentPresetSceneBuilder] Leave play mode first.");
			File.WriteAllText(LogFile, "skipped: play mode\n");
			return;
		}
		var opened = new List<Scene>();
		var previousActive = SceneManager.GetActiveScene();
		try
		{
			BuildInternal(opened);
		}
		catch (Exception e)
		{
			Log("EXCEPTION " + e);
			Debug.LogException(e);
		}
		finally
		{
			if (previousActive.IsValid() && previousActive.isLoaded)
			{
				SceneManager.SetActiveScene(previousActive);
			}
			foreach (var scene in opened)
			{
				if (scene.IsValid() && scene.isLoaded && scene != previousActive)
				{
					EditorSceneManager.CloseScene(scene, true);
				}
			}
			Log("done");
			File.WriteAllText(LogFile, s_Log.ToString());
			Debug.Log("[EquipmentPresetSceneBuilder]\n" + s_Log);
		}
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static Scene OpenAdditive(string path, List<Scene> opened)
	{
		var existing = SceneManager.GetSceneByPath(path);
		if (existing.IsValid() && existing.isLoaded)
		{
			return existing;
		}
		var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
		opened.Add(scene);
		return scene;
	}

	private static void BuildInternal(List<Scene> opened)
	{
		for (var i = 0; i < SceneManager.sceneCount; i++)
		{
			var loaded = SceneManager.GetSceneAt(i);
			Log("open scene: '" + loaded.path + "' name '" + loaded.name + "' loaded " + loaded.isLoaded + " dirty " + loaded.isDirty);
		}
		// Leftovers from an interrupted build (e.g. play mode started mid-build).
		for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
		{
			var loaded = SceneManager.GetSceneAt(i);
			if (loaded.path == WorkPath || loaded.name == "Window_EquipmentPresets_Building")
			{
				EditorSceneManager.CloseScene(loaded, true);
				Log("closed leftover " + loaded.name);
			}
		}
		var existing = SceneManager.GetSceneByPath(ScenePath);
		if (existing.IsValid() && existing.isLoaded)
		{
			EditorSceneManager.CloseScene(existing, true);
		}
		// Build in a scratch scene; the real one is only replaced once everything succeeded.
		AssetDatabase.DeleteAsset(WorkPath);
		AssetDatabase.CopyAsset(BirdManagerPath, WorkPath);
		var target = OpenAdditive(WorkPath, opened);
		var prep = OpenAdditive(BattlePrepPath, opened);
		var enterName = OpenAdditive(EnterNamePath, opened);
		SceneManager.SetActiveScene(target);

		var genericAtlas = LoadAtlas(GenericAtlasPath);
		var campAtlas = LoadAtlas(CampAtlasPath);
		var additionalAtlas = LoadAtlas(AdditionalAtlasPath);
		var pencil = AssetDatabase.LoadAssetAtPath<Texture2D>(PencilPath);
		var characterSlot = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSlotPath);

		// ---- Take over the bird manager's root.
		var root = FindRoot(target, "Window_BirdManager");
		var birdWindow = root.GetComponent<BirdWindowUI>();
		var birdWindowData = new SerializedObject(birdWindow);
		var itemSlotPrefab = Ref<InventoryItemSlot>(birdWindowData, "ItemSlotPrefab");
		var classSlotPrefab = Ref<InventoryItemSlot>(birdWindowData, "ClassSlotPrefab");
		var equipmentSlotPrefab = Ref<InventoryItemSlot>(birdWindowData, "EquipmentSlotPrefab");
		var itemGrid = Ref<UIGrid>(birdWindowData, "m_ItemGrid");
		var itemPanel = Ref<UIScrollView>(birdWindowData, "m_ItemPanel");
		var headerAnimation = Ref<Animation>(birdWindowData, "m_HeaderAnimation");
		var categoriesAnimation = Ref<Animation>(birdWindowData, "m_ItemCategoryButtonsAnimation");
		var itemGridAnimation = Ref<Animation>(birdWindowData, "m_ItemGridAnimation");
		var closeButton = Ref<UIInputTrigger>(birdWindowData, "m_ButtonClose");
		var tabs = new List<OpenInventoryButton>();
		var tabList = birdWindowData.FindProperty("m_OpenInventoryButtons");
		for (var i = 0; i < tabList.arraySize; i++)
		{
			tabs.Add(tabList.GetArrayElementAtIndex(i).objectReferenceValue as OpenInventoryButton);
		}
		Log("header anim: " + PathOf(headerAnimation) + ", categories anim: " + PathOf(categoriesAnimation) + ", grid anim: " + PathOf(itemGridAnimation));
		Log("item panel: " + PathOf(itemPanel) + " clip " + itemPanel.GetComponent<UIPanel>().baseClipRegion + ", grid " + PathOf(itemGrid));
		Log("tabs: " + string.Join(", ", tabs.Select(t => t.name + "=" + t.m_ItemType).ToArray()));
		UnityEngine.Object.DestroyImmediate(birdWindow);
		root.name = "Window_EquipmentPresets";
		var rootPanel = root.GetComponent<UIPanel>();
		Log("root panel depth " + rootPanel.depth + ", layer " + root.layer);

		UnityEngine.Object.DestroyImmediate(root.transform.Find("2_CharacterDisplay").gameObject);
		var info = root.transform.Find("3_Categories/Animation/3_CategoryContent/Animation/3_Info");
		if (info)
		{
			UnityEngine.Object.DestroyImmediate(info.gameObject);
		}
		var header = root.transform.Find("1_Header");
		var headerLabel = header.Find("Animation/2_Header/Animation/Label").GetComponent<UILabel>();
		headerLabel.text = "Equipment Presets";

		var listScreen = NewChild(root.transform, "ListScreen", Vector3.zero);
		var editScreen = NewChild(root.transform, "EditScreen", Vector3.zero);

		// ---- Edit screen: categories from the bird manager at the bottom.
		var categories = root.transform.Find("3_Categories");
		categories.SetParent(editScreen, false);
		categories.localPosition += new Vector3(0f, -CategoriesDrop, 0f);
		// The window slides the whole panel in itself, so drop the bird manager's "waiting to enter" offset.
		categories.Find("Animation").localPosition = Vector3.zero;
		// Tabs are pinned to the screen's right edge at runtime (EquipmentPresetWindowUI.m_TabsRoot).
		var tabsRoot = categories.Find("Animation/2_Tabs");
		tabsRoot.localPosition += new Vector3(0f, -BoardTopTrim, 0f);
		var boardFill = categories.Find("Animation/1_Body/Fill").GetComponent<UIWidget>();
		boardFill.transform.localPosition += new Vector3(0f, -BoardTopTrim, 0f);
		boardFill.height -= (int)BoardTopTrim;
		foreach (Transform edge in categories.Find("Animation/1_Body"))
		{
			if (edge.name == "Edge" && edge.localPosition.y > 0f)
			{
				edge.localPosition += new Vector3(0f, -BoardTopTrim, 0f);
			}
		}
		tabsRoot.localScale = Vector3.one * 0.9f;

		// ---- Edit screen: bird lineup + ground from battle preparation, raised to the middle of the screen.
		var lineup = NewChild(editScreen, "Lineup", new Vector3(0f, LineupY, 0f));
		var scenery = CopyFrom(prep, "Window_BattlePreparation/3_CharacterSelection/Animation/Scenery", lineup, "Scenery");
		var characterRoot = CopyFrom(prep, "Window_BattlePreparation/3_CharacterSelection/Animation/CharacterRoot", lineup, "CharacterRoot");
		characterRoot.localPosition = new Vector3(0f, characterRoot.localPosition.y, characterRoot.localPosition.z);
		var lineupSorting = characterRoot.GetComponent<UiSortBehaviour>();
		var sortData = new SerializedObject(lineupSorting);
		sortData.FindProperty("m_orderWidget").objectReferenceValue = scenery.Find("Bottom").GetComponent<UIWidget>();
		sortData.ApplyModifiedPropertiesWithoutUndo();
		var focusMarker = AddSprite(lineup.gameObject, "FocusMarker", campAtlas, "Button_Arrow", 75, 102, 30, new Vector3(0f, 193f, -20f));
		focusMarker.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

		// ---- Edit screen: back button, name field and save button along the top, right of the level icon
		// (DisplayElements/1_LevelDisplay sits 48 in and 50 down from the top-left corner, ~88 wide).
		var topLeft = NewChild(editScreen, "TopLeft", new Vector3(-640f, 384f, -20f));
		const float roundScale = 0.85f;
		const float roundHalf = 63f * roundScale;
		const float clusterY = -58f;
		var x = 48f + 44f + 18f + roundHalf;
		var backButton = CopyFrom(prep, "Window_BattlePreparation/4_Options/2_Back/Animation/Button_Back", topLeft, "Button_Back");
		backButton.localPosition = new Vector3(x, clusterY, 0f);
		backButton.localScale = Vector3.one * roundScale;
		x += roundHalf + 18f + NamePlankWidth * 0.5f;
		var nameField = CopyFrom(enterName, "Popup_EnterName/2_Popup/Animation/3_CodeInput", topLeft, "NameInput");
		nameField.localPosition = new Vector3(x, clusterY, 0f);
		// Rounded wooden plank (same texture as the preset bars) framing the name box.
		var nameWidgets = nameField.GetComponentsInChildren<UIWidget>(true);
		var namePlank = NewChild(nameField, "Plank", new Vector3(0f, 0f, 1f)).gameObject.AddComponent<UITexture>();
		namePlank.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BarTexturePath);
		namePlank.shader = Shader.Find("Unlit/Transparent Colored");
		namePlank.border = new Vector4(32f, 32f, 32f, 32f);
		namePlank.type = UIBasicSprite.Type.Sliced;
		namePlank.width = (int)NamePlankWidth;
		namePlank.height = 72;
		namePlank.depth = nameWidgets.Min(w => w.depth) - 1;
		// The name sits straight on the wood: drop the input's flat brown box and its edge lines.
		foreach (var part in new[] { "Box", "Shadow_Top", "Shadow_Left" })
		{
			var piece = nameField.Find(part);
			if (piece)
			{
				UnityEngine.Object.DestroyImmediate(piece.gameObject);
			}
		}
		var nameLabel = nameField.Find("Value").GetComponent<UILabel>();
		nameLabel.effectStyle = UILabel.Effect.Shadow;
		nameLabel.effectColor = new Color(0.35f, 0.17f, 0.05f);
		nameLabel.effectDistance = new Vector2(2f, 2f);
		// Sized to its text; EquipmentPresetWindowUI.LayoutNameText centres it or tucks it left of the pencil.
		nameLabel.overflowMethod = UILabel.Overflow.ResizeFreely;
		nameLabel.autoResizeBoxCollider = false;
		nameLabel.transform.localPosition = new Vector3(0f, nameLabel.transform.localPosition.y, nameLabel.transform.localPosition.z);
		var nameCollider = nameLabel.GetComponent<BoxCollider>();
		if (nameCollider)
		{
			nameCollider.center = Vector3.zero;
			nameCollider.size = new Vector3(namePlank.width, namePlank.height, nameCollider.size.z);
		}
		var pencilX = NamePlankWidth * 0.5f - 30f;
		var namePencil = NewChild(nameField, "Pencil", new Vector3(pencilX, 0f, -2f)).gameObject.AddComponent<UITexture>();
		namePencil.mainTexture = pencil;
		namePencil.shader = Shader.Find("Unlit/Transparent Colored");
		namePencil.width = 44;
		namePencil.height = 44;
		namePencil.depth = nameLabel.depth + 1;
		x += NamePlankWidth * 0.5f + 18f + roundHalf;
		var saveButton = CopyFrom(prep, "Window_BattlePreparation/4_Options/2_Back/Animation/Button_Back", topLeft, "Button_Save");
		saveButton.localPosition = new Vector3(x, clusterY, 0f);
		saveButton.localScale = Vector3.one * roundScale;
		SetSprite(saveButton.Find("Animation/Icon").GetComponent<UISprite>(), genericAtlas, "Check_Large", 72, 72);

		// ---- List screen: scrolling list of preset bars.
		var listRoot = NewChild(listScreen, "PresetList", Vector3.zero);
		var listPanel = listRoot.gameObject.AddComponent<UIPanel>();
		listPanel.depth = rootPanel.depth + 1;
		listPanel.clipping = UIDrawCall.Clipping.SoftClip;
		listPanel.baseClipRegion = new Vector4(0f, (ClipTop + ListBottom) * 0.5f, BarWidth + 40f, ClipTop - ListBottom);
		listPanel.clipSoftness = new Vector2(0f, ListSoftness);
		// The header gets its own panel above the list, so bars scroll out underneath the plank.
		var headerPanel = header.gameObject.AddComponent<UIPanel>();
		headerPanel.depth = listPanel.depth + 2;
		var scrollView = listRoot.gameObject.AddComponent<UIScrollView>();
		scrollView.movement = UIScrollView.Movement.Vertical;
		scrollView.dragEffect = UIScrollView.DragEffect.MomentumAndSpring;
		scrollView.restrictWithinPanel = true;
		scrollView.disableDragIfFits = true;
		scrollView.contentPivot = UIWidget.Pivot.Top;
		// Same wheel speed as the game's shops and bird manager.
		scrollView.scrollWheelFactor = 300f;
		// Invisible widget at the header's edge: the scroll view treats it as the top of the content, so the first bar
		// opens (and scrolls back to) ListTop rather than being pulled up against the header.
		var topSpacer = NewChild(listRoot, "TopSpacer", new Vector3(0f, ClipTop - ListSoftness - 1f, 0f)).gameObject.AddComponent<UIWidget>();
		topSpacer.width = 2;
		topSpacer.height = 2;
		var grid = NewChild(listRoot, "Grid", new Vector3(0f, ListTop - BarHeight * 0.5f - 6f, 0f)).gameObject.AddComponent<UIGrid>();
		grid.arrangement = UIGrid.Arrangement.Vertical;
		grid.cellWidth = BarWidth;
		grid.cellHeight = BarHeight + 14f;
		grid.pivot = UIWidget.Pivot.Top;
		grid.sorting = UIGrid.Sorting.Alphabetic;

		// Catches drags and the scroll wheel in the gaps between bars; sits outside the scrolling panel so it stays put.
		var dragArea = NewChild(listScreen, "DragArea", new Vector3(0f, (ListTop + ListBottom) * 0.5f, 10f)).gameObject;
		var dragWidget = dragArea.AddComponent<UIWidget>();
		dragWidget.width = (int)BarWidth + 40;
		dragWidget.height = (int)(ListTop - ListBottom);
		dragWidget.depth = 0;
		dragArea.AddComponent<BoxCollider>().size = new Vector3(dragWidget.width, dragWidget.height, 1f);
		dragArea.AddComponent<UIDragScrollView>().scrollView = scrollView;

		var bar = BuildBar(listRoot, scrollView, prep, genericAtlas, campAtlas, pencil, AssetDatabase.LoadAssetAtPath<Texture2D>(BarTexturePath), headerLabel);

		var createButton = CopyFrom(prep, "Window_BattlePreparation/4_Options/2_Back/Animation/Button_Back", listScreen, "Button_Create");
		// Centered between the bottom of the list and the bottom of the screen (the interface camera shows +-360).
		createButton.localPosition = new Vector3(0f, (ListBottom - 360f) * 0.5f, -10f);
		var createBody = createButton.Find("Animation/Body").GetComponent<UISprite>();
		SetSprite(createBody, additionalAtlas, "Button_Green", 380, 86);
		createBody.type = UIBasicSprite.Type.Sliced;
		UnityEngine.Object.DestroyImmediate(createButton.Find("Animation/Icon").gameObject);
		var createLabel = CopyLabel(headerLabel, createButton.Find("Animation"), "Label", "Create Preset", new Vector3(0f, 2f, -2f));
		createLabel.depth = createBody.depth + 2;
		ResizeCollider(createButton, 380f, 86f);

		var emptyLabel = CopyLabel(headerLabel, listScreen, "EmptyLabel", "No presets yet - create one to save your birds' gear.", new Vector3(0f, 40f, -5f));

		// ---- Wire the controller.
		var window = root.AddComponent<EquipmentPresetWindowUI>();
		var data = new SerializedObject(window);
		Set(data, "m_HeaderRoot", header.gameObject);
		Set(data, "m_HeaderAnimation", headerAnimation);
		Set(data, "m_HeaderLabel", headerLabel);
		Set(data, "m_CloseButton", closeButton);
		Set(data, "m_ListScreen", listScreen.gameObject);
		Set(data, "m_ListScrollView", scrollView);
		Set(data, "m_ListGrid", grid);
		Set(data, "m_BarTemplate", bar);
		Set(data, "m_CreateButton", createButton.GetComponent<UIInputTrigger>());
		Set(data, "m_CreateButtonBody", createBody);
		Set(data, "m_EmptyLabel", emptyLabel);
		Set(data, "m_CampBirdPrefab", new SerializedObject(characterSlot.GetComponent<BattlePrepCharacterButton>()).FindProperty("m_CampViewController").objectReferenceValue);
		Set(data, "m_EditScreen", editScreen.gameObject);
		Set(data, "m_LineupGrid", characterRoot.GetComponent<UIGrid>());
		Set(data, "m_LineupSorting", lineupSorting);
		Set(data, "m_CharacterSlotPrefab", characterSlot);
		Set(data, "m_FocusMarker", focusMarker.transform);
		Set(data, "m_TopLeftAnchor", topLeft);
		SetFloat(data, "m_ListBirdTopInset", ClipTop - HeaderBottom);
		Set(data, "m_NameLabel", nameField.Find("Value").GetComponent<UILabel>());
		SetFloat(data, "m_NameTextLeft", -NamePlankWidth * 0.5f + 12f);
		SetFloat(data, "m_NameTextRight", NamePlankWidth * 0.5f - 30f - 22f - 8f);
		SetFloat(data, "m_NamePlankWidth", NamePlankWidth);
		Set(data, "m_TabsRoot", tabsRoot);
		Set(data, "m_BackButton", backButton.GetComponent<UIInputTrigger>());
		Set(data, "m_SaveButton", saveButton.GetComponent<UIInputTrigger>());
		Set(data, "m_NameInput", nameField.GetComponentInChildren<UIInput>(true));
		Set(data, "m_LineupRoot", lineup);
		Set(data, "m_CategoriesRoot", categories);
		Set(data, "m_ItemScrollView", itemPanel);
		Set(data, "m_ItemGrid", itemGrid);
		Set(data, "m_ItemSlotPrefab", itemSlotPrefab);
		Set(data, "m_ClassSlotPrefab", classSlotPrefab);
		Set(data, "m_EquipmentSlotPrefab", equipmentSlotPrefab);
		var tabsProperty = data.FindProperty("m_TabButtons");
		tabsProperty.arraySize = tabs.Count;
		for (var i = 0; i < tabs.Count; i++)
		{
			tabsProperty.GetArrayElementAtIndex(i).objectReferenceValue = tabs[i];
		}
		data.ApplyModifiedPropertiesWithoutUndo();

		SetLayerRecursively(listScreen.gameObject, root.layer);
		BuildButtonPrefab(prep, root.layer);
		var presetIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(ButtonIconPath);
		SetLayerRecursively(editScreen.gameObject, root.layer);

		RenderPreviews(target, root, listScreen.gameObject, editScreen.gameObject, bar, grid, header, categories);

		EditorSceneManager.MarkSceneDirty(target);
		EditorSceneManager.SaveScene(target);
		EditorSceneManager.CloseScene(target, true);
		opened.Remove(target);
		// Copy the file contents over the real scene so it keeps its GUID across rebuilds.
		if (File.Exists(ScenePath))
		{
			File.Copy(WorkPath, ScenePath, true);
			AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
		}
		else
		{
			AssetDatabase.CopyAsset(WorkPath, ScenePath);
		}
		AssetDatabase.DeleteAsset(WorkPath);
		Log("saved " + ScenePath);
		AddToBuildSettings();
		AddPresetButtonToBattlePreparation(prep, presetIcon, opened.Contains(prep));
	}

	// Copies battle preparation's class-switch button (layout, swing animation, press feedback) one slot above it,
	// with the presets icon, and hands it to BattlePreperationUI (m_PresetButton / m_PresetButtonAnimation).
	private static void AddPresetButtonToBattlePreparation(Scene prep, Texture2D icon, bool openedByBuilder)
	{
		// Never save over a copy the user has open in the editor (it may hold unsaved work).
		if (!openedByBuilder)
		{
			Log("WARNING " + prep.path + " is open in the editor - preset button not added; close it and rebuild");
			return;
		}
		var options = FindPath(prep, "Window_BattlePreparation/4_Options");
		var old = options.Find("9_EquipmentPresets");
		if (old)
		{
			UnityEngine.Object.DestroyImmediate(old.gameObject);
		}
		var source = options.Find("1_SwitchClasses");
		var copy = UnityEngine.Object.Instantiate(source.gameObject, options);
		copy.name = "9_EquipmentPresets";
		var layout = copy.GetComponent<LayoutControl>();
		var position = layout.m_LayoutSet.m_v3Position;
		// Bottom-left anchored: class button at y 205, back button at 80; one more step up.
		layout.m_LayoutSet.m_v3Position = new Vector3(position.x, position.y + 115f, position.z);
		var button = copy.transform.Find("Animation/Button_SwitchClasses");
		button.name = "Button_EquipmentPresets";
		var iconSprite = button.Find("Animation/Icon").GetComponent<UISprite>();
		ReplaceIconWithTexture(iconSprite, icon, Mathf.Max(iconSprite.width, iconSprite.height) + 8);
		var ui = FindRoot(prep, "Window_BattlePreparation").GetComponent<BattlePreperationUI>();
		var data = new SerializedObject(ui);
		Set(data, "m_PresetButton", button.GetComponent<UIInputTrigger>());
		Set(data, "m_PresetButtonAnimation", copy.GetComponent<Animation>());
		data.ApplyModifiedPropertiesWithoutUndo();
		EditorSceneManager.MarkSceneDirty(prep);
		EditorSceneManager.SaveScene(prep);
		Log("added preset button to " + prep.path);
	}

	// Yellow round game button with the sword and shield icon, built from battle preparation's back button root so it
	// carries the game's BackButton_Enter / BackButton_Leave swing. Rebuilt inside the existing prefab root (when there
	// is one) so instances already placed in scenes stay linked.
	private static void BuildButtonPrefab(Scene prep, int layer)
	{
		if (!AssetDatabase.IsValidFolder("Assets/Resources/EquipmentPresets"))
		{
			AssetDatabase.CreateFolder("Assets/Resources", "EquipmentPresets");
		}
		var existing = File.Exists(ButtonPrefabPath);
		var root = existing ? PrefabUtility.LoadPrefabContents(ButtonPrefabPath) : new GameObject("Button_EquipmentPresets");
		try
		{
			for (var i = root.transform.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
			}
			var source = FindPath(prep, "Window_BattlePreparation/4_Options/2_Back");
			// Behaves like the camp's world map button: no swing, and windows (backdrop depth 1) cover it and take the taps.
			var oldAnimation = root.GetComponent<Animation>();
			if (oldAnimation != null)
			{
				UnityEngine.Object.DestroyImmediate(oldAnimation);
			}
			var panel = GetOrAdd<UIPanel>(root);
			panel.depth = 0;
			var container = UnityEngine.Object.Instantiate(source.Find("Animation").gameObject, root.transform);
			container.name = "Animation";
			container.transform.localPosition = Vector3.zero;
			var button = container.transform.Find("Button_Back");
			button.name = "Button";
			ReplaceIconWithTexture(button.Find("Animation/Icon").GetComponent<UISprite>(), AssetDatabase.LoadAssetAtPath<Texture2D>(ButtonIconPath), 100);
			var component = GetOrAdd<EquipmentPresetButton>(root);
			component.m_Trigger = button.GetComponent<UIInputTrigger>();
			component.m_Animation = null;
			component.m_PinToScreen = true;
			component.m_HideWhenWindowOpen = false;
			component.m_Anchor = TextAnchor.LowerRight;
			// World map button: centre 70 in from the edges (radius 63).
			component.m_Offset = new Vector2(7f, 7f);
			SetLayerRecursively(root, layer);
			PrefabUtility.SaveAsPrefabAsset(root, ButtonPrefabPath);
		}
		finally
		{
			if (existing)
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}
		Log("saved " + ButtonPrefabPath);
	}

	// Unity's "missing component" placeholder isn't C# null, so ?? can't be used here.
	private static T GetOrAdd<T>(GameObject go) where T : Component
	{
		var component = go.GetComponent<T>();
		return component != null ? component : go.AddComponent<T>();
	}

	private static EquipmentPresetBar BuildBar(Transform listRoot, UIScrollView scrollView, Scene prep, UIAtlas genericAtlas, UIAtlas campAtlas, Texture2D pencil, Texture2D barTexture, UILabel labelSource)
	{
		var barTransform = NewChild(listRoot.parent, "BarTemplate", Vector3.zero);
		var barObject = barTransform.gameObject;
		var collider = barObject.AddComponent<BoxCollider>();
		collider.size = new Vector3(BarWidth, BarHeight, 1f);
		barObject.AddComponent<UIDragScrollView>().scrollView = scrollView;

		// Rounded-corner plank made from the camp atlas wood (Window_Fill); sliced so the corners never stretch.
		var plank = NewChild(barTransform, "Plank", Vector3.zero).gameObject.AddComponent<UITexture>();
		plank.mainTexture = barTexture;
		plank.shader = Shader.Find("Unlit/Transparent Colored");
		plank.border = new Vector4(32f, 32f, 32f, 32f);
		plank.type = UIBasicSprite.Type.Sliced;
		plank.width = (int)BarWidth;
		plank.height = (int)BarHeight;
		plank.depth = 1;

		var name = CopyLabel(labelSource, barTransform, "Name", "Preset", new Vector3(-BarWidth * 0.5f + 52f, BarHeight * 0.5f - 32f, -1f));
		name.pivot = UIWidget.Pivot.Left;
		name.transform.localPosition = new Vector3(-BarWidth * 0.5f + 52f, BarHeight * 0.5f - 32f, -1f);
		name.transform.localScale = Vector3.one * 0.8f;
		name.depth = 6;

		var missing = AddSprite(barObject, "MissingMarker", genericAtlas, "MissingWarning", 24, 46, 7, new Vector3(-BarWidth * 0.5f + 28f, BarHeight * 0.5f - 32f, -1f));

		var birdRoot = NewChild(barTransform, "Birds", new Vector3(-BarWidth * 0.5f + 85f, -BarHeight * 0.5f + 22f, -2f));

		const float buttonSize = 104f;
		var equip = MakeSquareButton(prep, barTransform, "Button_Equip", new Vector3(BarWidth * 0.5f - 350f, -6f, -3f), campAtlas, "Button_Square_Large", Color.white, buttonSize);
		SetSprite(equip.Find("Animation/Icon").GetComponent<UISprite>(), genericAtlas, "Continue", 58, 58);
		var edit = MakeSquareButton(prep, barTransform, "Button_Edit", new Vector3(BarWidth * 0.5f - 225f, -6f, -3f), campAtlas, "Button_Square_Large", Color.white, buttonSize);
		ReplaceIconWithTexture(edit.Find("Animation/Icon").GetComponent<UISprite>(), pencil, 66);
		var delete = MakeSquareButton(prep, barTransform, "Button_Delete", new Vector3(BarWidth * 0.5f - 100f, -6f, -3f), campAtlas, "Button_Square_Large_D", new Color(1f, 0.32f, 0.26f), buttonSize);
		SetSprite(delete.Find("Animation/Icon").GetComponent<UISprite>(), campAtlas, "ScrapItem", 66, 70);
		foreach (var button in new[] { equip, edit, delete })
		{
			button.gameObject.AddComponent<UIDragScrollView>().scrollView = scrollView;
			button.localScale = Vector3.one * 0.9f;
			button.Find("Animation/Icon").localScale = Vector3.one * 1.5f;
		}

		var status = CopyLabel(labelSource, barTransform, "Status", "Equipped!", new Vector3(BarWidth * 0.5f - 350f, BarHeight * 0.5f - 16f, -6f));
		status.transform.localScale = Vector3.one * 0.7f;
		status.depth = 12;
		status.gameObject.SetActive(false);

		var component = barObject.AddComponent<EquipmentPresetBar>();
		component.m_NameLabel = name;
		component.m_BirdRoot = birdRoot;
		component.m_EquipButton = equip.GetComponent<UIInputTrigger>();
		component.m_EquipButtonBody = equip.Find("Animation/Body").GetComponent<UISprite>();
		component.m_EditButton = edit.GetComponent<UIInputTrigger>();
		component.m_DeleteButton = delete.GetComponent<UIInputTrigger>();
		component.m_MissingMarker = missing.gameObject;
		component.m_StatusLabel = status;
		barObject.SetActive(false);
		return component;
	}

	private static Transform MakeSquareButton(Scene prep, Transform parent, string name, Vector3 position, UIAtlas atlas, string sprite, Color color, float size)
	{
		var button = CopyFrom(prep, "Window_BattlePreparation/4_Options/2_Back/Animation/Button_Back", parent, name);
		button.localPosition = position;
		var body = button.Find("Animation/Body").GetComponent<UISprite>();
		SetSprite(body, atlas, sprite, (int)size, (int)size);
		body.type = UIBasicSprite.Type.Sliced;
		body.color = color;
		body.depth = 8;
		button.Find("Animation/Icon").GetComponent<UIWidget>().depth = 10;
		ResizeCollider(button, size, size);
		return button;
	}

	private static void ReplaceIconWithTexture(UISprite icon, Texture2D texture, int size)
	{
		var go = icon.gameObject;
		var depth = icon.depth;
		UnityEngine.Object.DestroyImmediate(icon);
		var widget = go.AddComponent<UITexture>();
		widget.mainTexture = texture;
		widget.shader = Shader.Find("Unlit/Transparent Colored");
		widget.width = size;
		widget.height = size;
		widget.depth = depth;
	}

	private static void ResizeCollider(Transform button, float width, float height)
	{
		var box = button.GetComponent<BoxCollider>();
		if (box)
		{
			box.center = Vector3.zero;
			box.size = new Vector3(width, height, box.size.z);
		}
	}

	// ---------------------------------------------------------------- previews

	private static void RenderPreviews(Scene scene, GameObject root, GameObject listScreen, GameObject editScreen, EquipmentPresetBar bar, UIGrid grid, Transform header, Transform categories)
	{
		var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
		if (camera == null)
		{
			Log("no preview camera");
			return;
		}
		// Undo the hidden "before enter animation" offsets for the render only.
		var headerAnim = header.Find("Animation");
		var categoriesAnim = categories.Find("Animation");
		var headerPos = headerAnim.localPosition;
		var categoriesPos = categoriesAnim.localPosition;
		headerAnim.localPosition = Vector3.zero;
		categoriesAnim.localPosition = Vector3.zero;

		var samples = new List<GameObject>();
		for (var i = 0; i < 4; i++)
		{
			var sample = UnityEngine.Object.Instantiate(bar.gameObject, grid.transform);
			sample.name = "Sample_" + i;
			sample.SetActive(true);
			sample.GetComponent<EquipmentPresetBar>().m_NameLabel.text = "Preset " + (i + 1);
			samples.Add(sample);
		}
		grid.Reposition();

		listScreen.SetActive(true);
		editScreen.SetActive(false);
		RenderTo(camera, root, "Temp/EquipmentPresetPreview_List.png");
		listScreen.SetActive(false);
		header.gameObject.SetActive(false);
		editScreen.SetActive(true);
		// Stand-in for the game's level icon (DisplayElements, not part of this scene) to check spacing.
		var levelIcon = AddSprite(editScreen.transform.Find("TopLeft").gameObject, "PreviewLevelIcon", LoadAtlas(GenericAtlasPath), "Resource_XP", 88, 88, 50, new Vector3(48f, -50f, 0f));
		var anchor = editScreen.transform.Find("TopLeft");
		var anchorPos = anchor.localPosition;
		anchor.localPosition = new Vector3(-1365f * 0.5f, 384f, anchorPos.z);
		// Mimic runtime: tabs pinned to the right edge (preview camera shows 1280 units across), sample item slots in the grid.
		var tabs = categories.Find("Animation/2_Tabs");
		var tabsPos = tabs.localPosition;
		tabs.localPosition = new Vector3(640f - 262f * tabs.localScale.x, tabsPos.y, tabsPos.z);
		var itemGrid = categories.GetComponentsInChildren<UIGrid>(true).First();
		var slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Button_ItemSlot_Equipment.prefab");
		var slots = new List<GameObject>();
		for (var i = 0; i < 4; i++)
		{
			var slot = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, itemGrid.transform);
			slots.Add(slot);
		}
		itemGrid.Reposition();
		RenderTo(camera, root, "Temp/EquipmentPresetPreview_Edit.png");
		tabs.localPosition = tabsPos;
		foreach (var slot in slots)
		{
			UnityEngine.Object.DestroyImmediate(slot);
		}
		anchor.localPosition = anchorPos;
		UnityEngine.Object.DestroyImmediate(levelIcon.gameObject);

		header.gameObject.SetActive(true);
		listScreen.SetActive(true);
		editScreen.SetActive(true);
		foreach (var sample in samples)
		{
			UnityEngine.Object.DestroyImmediate(sample);
		}
		headerAnim.localPosition = headerPos;
		categoriesAnim.localPosition = categoriesPos;
	}

	private static void RenderTo(Camera camera, GameObject root, string path)
	{
		foreach (var panel in root.GetComponentsInChildren<UIPanel>(true))
		{
			panel.Refresh();
		}
		// NGUI only rebuilds draw calls once per frame; in the editor the frame count doesn't move between these calls.
		var updateFrame = typeof(UIPanel).GetField("mUpdateFrame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
		var lateUpdate = typeof(UIPanel).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		// Panels in scenes opened by script never got OnEnable/Update in edit mode, so register them by hand.
		var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
		foreach (var rect in root.GetComponentsInChildren<UIRect>().OrderBy(r => r is UIPanel ? 0 : 1))
		{
			foreach (var method in new[] { "OnEnable", "Start", "Update" })
			{
				var m = rect.GetType().GetMethod(method, flags, null, Type.EmptyTypes, null);
				if (m != null)
				{
					try { m.Invoke(rect, null); } catch (Exception) { }
				}
			}
		}
		for (var pass = 0; pass < 3 && UIPanel.list.Count > 0; pass++)
		{
			foreach (var panel in UIPanel.list.ToList())
			{
				updateFrame.SetValue(updateFrame.IsStatic ? null : panel, -1);
			}
			lateUpdate.Invoke(UIPanel.list[0], null);
		}
		Log("panels " + UIPanel.list.Count + ", in window " + root.GetComponentsInChildren<UIPanel>().Length + ", draw calls " + UIDrawCall.activeList.size + ", widgets " + root.GetComponentsInChildren<UIWidget>().Length);
		foreach (var panel in root.GetComponentsInChildren<UIPanel>())
		{
			Log("  panel " + panel.name + " depth " + panel.depth + " drawcalls " + panel.drawCalls.Count + " alpha " + panel.alpha);
		}
		var sampleCall = UIDrawCall.activeList.size > 0 ? UIDrawCall.activeList[0] : null;
		if (sampleCall != null)
		{
			Log("  first drawcall layer " + sampleCall.gameObject.layer + " pos " + sampleCall.transform.position + " verts " + sampleCall.GetComponent<MeshFilter>().sharedMesh.vertexCount + " bounds " + sampleCall.GetComponent<MeshRenderer>().bounds);
		}
		Log("  camera pos " + camera.transform.position + " near " + camera.nearClipPlane + " far " + camera.farClipPlane + " mask " + camera.cullingMask + " ortho " + camera.orthographic);
		// Other open scenes (e.g. a stale copy of this window) share the interface layer; hide their draw calls for the render.
		var hidden = new List<Renderer>();
		foreach (var drawCall in UIDrawCall.activeList)
		{
			var renderer = drawCall != null ? drawCall.GetComponent<Renderer>() : null;
			if (renderer != null && renderer.enabled && (drawCall.manager == null || !drawCall.manager.transform.IsChildOf(root.transform)))
			{
				renderer.enabled = false;
				hidden.Add(renderer);
			}
		}
		Log("  hid " + hidden.Count + " of " + UIDrawCall.activeList.size + " draw calls; managers: " + string.Join(", ", UIDrawCall.activeList.ToArray().Select(d => d == null ? "null" : (d.manager == null ? "<none>" : d.manager.name + "@" + d.manager.gameObject.scene.name)).Distinct().ToArray()));
		var texture = new RenderTexture(1365, 768, 24);
		var previous = camera.targetTexture;
		var mask = camera.cullingMask;
		camera.targetTexture = texture;
		camera.cullingMask = 1 << root.layer;
		camera.clearFlags = CameraClearFlags.SolidColor;
		camera.backgroundColor = new Color(0.2f, 0.25f, 0.3f);
		camera.Render();
		RenderTexture.active = texture;
		var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
		image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
		image.Apply();
		File.WriteAllBytes(path, image.EncodeToPNG());
		RenderTexture.active = null;
		camera.targetTexture = previous;
		camera.cullingMask = mask;
		UnityEngine.Object.DestroyImmediate(image);
		texture.Release();
		UnityEngine.Object.DestroyImmediate(texture);
		foreach (var renderer in hidden)
		{
			renderer.enabled = true;
		}
		Log("preview " + path + " (camera " + camera.name + " ortho " + camera.orthographicSize + ")");
	}

	// ---------------------------------------------------------------- helpers

	private static void AddToBuildSettings()
	{
		var scenes = EditorBuildSettings.scenes.ToList();
		if (scenes.Any(s => s.path == ScenePath))
		{
			return;
		}
		scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
		EditorBuildSettings.scenes = scenes.ToArray();
		Log("added to build settings");
	}

	private static UIAtlas LoadAtlas(string path)
	{
		var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
		var atlas = go != null ? go.GetComponent<UIAtlas>() : null;
		if (atlas == null)
		{
			throw new Exception("atlas not found: " + path);
		}
		return atlas;
	}

	private static GameObject FindRoot(Scene scene, string name)
	{
		var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
		if (go == null)
		{
			throw new Exception(name + " not found in " + scene.path);
		}
		return go;
	}

	private static Transform FindPath(Scene scene, string path)
	{
		var parts = path.Split(new[] { '/' }, 2);
		var root = FindRoot(scene, parts[0]);
		var found = parts.Length > 1 ? root.transform.Find(parts[1]) : root.transform;
		if (found == null)
		{
			throw new Exception(path + " not found in " + scene.path);
		}
		return found;
	}

	// Copies an object from another open scene into parent, keeping its local transform. Layout drivers that point
	// back into the source scene are removed.
	private static Transform CopyFrom(Scene source, string path, Transform parent, string name)
	{
		var original = FindPath(source, path);
		var copy = UnityEngine.Object.Instantiate(original.gameObject);
		copy.name = name;
		SceneManager.MoveGameObjectToScene(copy, parent.gameObject.scene);
		copy.transform.SetParent(parent, false);
		copy.transform.localPosition = original.localPosition;
		copy.transform.localRotation = original.localRotation;
		copy.transform.localScale = original.localScale;
		foreach (var layout in copy.GetComponentsInChildren<LayoutControl>(true))
		{
			UnityEngine.Object.DestroyImmediate(layout);
		}
		return copy.transform;
	}

	private static Transform NewChild(Transform parent, string name, Vector3 localPosition)
	{
		var go = new GameObject(name);
		go.layer = parent.gameObject.layer;
		go.transform.SetParent(parent, false);
		go.transform.localPosition = localPosition;
		return go.transform;
	}

	private static UISprite AddSprite(GameObject parent, string name, UIAtlas atlas, string sprite, int width, int height, int depth, Vector3 position)
	{
		var go = NewChild(parent.transform, name, position).gameObject;
		var widget = go.AddComponent<UISprite>();
		SetSprite(widget, atlas, sprite, width, height);
		widget.depth = depth;
		return widget;
	}

	private static void SetSprite(UISprite widget, UIAtlas atlas, string sprite, int width, int height)
	{
		if (atlas.GetSprite(sprite) == null)
		{
			Log("WARNING sprite " + sprite + " missing from " + atlas.name);
		}
		widget.atlas = atlas;
		widget.spriteName = sprite;
		widget.width = width;
		widget.height = height;
	}

	private static UILabel CopyLabel(UILabel source, Transform parent, string name, string text, Vector3 position)
	{
		var go = UnityEngine.Object.Instantiate(source.gameObject);
		go.name = name;
		go.transform.SetParent(parent, false);
		go.transform.localPosition = position;
		go.transform.localScale = Vector3.one;
		var label = go.GetComponent<UILabel>();
		label.text = text;
		return label;
	}

	private static T Ref<T>(SerializedObject data, string property) where T : UnityEngine.Object
	{
		var value = data.FindProperty(property).objectReferenceValue as T;
		if (value == null)
		{
			Log("WARNING " + property + " is empty");
		}
		return value;
	}

	private static void Set(SerializedObject data, string property, UnityEngine.Object value)
	{
		var serialized = data.FindProperty(property);
		if (serialized == null)
		{
			Log("WARNING no property " + property);
			return;
		}
		if (value == null)
		{
			Log("WARNING " + property + " set to null");
		}
		serialized.objectReferenceValue = value;
	}

	private static void SetFloat(SerializedObject data, string property, float value)
	{
		var serialized = data.FindProperty(property);
		if (serialized == null)
		{
			Log("WARNING no property " + property);
			return;
		}
		serialized.floatValue = value;
	}

	private static string PathOf(Component component)
	{
		if (component == null)
		{
			return "<null>";
		}
		var path = component.name;
		var t = component.transform.parent;
		while (t != null)
		{
			path = t.name + "/" + path;
			t = t.parent;
		}
		return path;
	}

	private static void SetLayerRecursively(GameObject go, int layer)
	{
		go.layer = layer;
		foreach (Transform child in go.transform)
		{
			SetLayerRecursively(child.gameObject, layer);
		}
	}
}
