using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ABH.Shared.BalancingData;
using ActionTreeEditor.Editor;
using ActionTreeEditor.Loca;
using ActionTreeEditor.Runtime;
using ActionTreeEditor.Runtime.StateManagers;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ActionTreePreviewer : EditorWindow
{
    private static ActionTree m_tree;

    private static ActionTree ActionTree
    {
        get => m_tree;
        set
        {
            m_tree = value;
            ActionTreeObjectId = GlobalObjectId.GetGlobalObjectIdSlow(m_tree);
        }
    }

    private static GlobalObjectId? ActionTreeObjectId { get; set; } = null;
    
    private bool LockedToTree { get; set; }
    
    private static bool Reloading { get; set; }
    
    private static bool Started { get; set; }
    
    public static bool ActiveStorySequence { get; private set; }

    private static bool IsPlayingTree => EditorApplication.isPlaying && 
                                  !Reloading &&
                                  ActionTree != null && 
                                  (Started || WaitingForBattleInit);

    private static Vector3? m_cachedDragControllerPos = null;

    public static ActionTreePreviewer Instance { get; private set; } = null;
    
    public event Action BeforeEnterPlaymode;

    public event Action PlaybackBegan;
    
    public event Action PlaybackEnded;
    
    private const bool HideLogs = true;
    
    // Battle preview fields
    private static bool IsBattlePreview => 
        ActionTree != null &&
        ActionTree.gameObject.scene.name.Contains("Battleground");
    
    private bool BattlePreviewValid => 
        IsBattlePreview && 
        m_birds.Count(b => b.Active) != 0;

    private static bool WaitingForBattleInit { get; set; } = false;
    
    internal static bool PlayBattleMusic
    {
        get => EditorPrefs.GetBool(PLAY_BATTLE_MUSIC_KEY, true);
        set => EditorPrefs.SetBool(PLAY_BATTLE_MUSIC_KEY, value);
    }
    
    private const string ACTIVE_BIRDS_LIST_KEY  = "ActionTreePV-ActiveBirdsList";
    internal static HashSet<string> ActiveBirds
    {
        get => EditorPrefs.GetString(ACTIVE_BIRDS_LIST_KEY, "").Split(";").ToHashSet();
        set => EditorPrefs.SetString(ACTIVE_BIRDS_LIST_KEY, string.Join(";", value));
    }
    
    // Configuration keys & menu paths
    
    private const string PLAY_BATTLE_MUSIC_KEY  = "ActionTreePV-PlayBattleMusic";
    private const string PLAY_BATTLE_MUSIC_PATH = "ActionTree/Previewer/Play battle music";
    
    [MenuItem(PLAY_BATTLE_MUSIC_PATH, true, 1)]
    private static bool ValidatePlayBattleMusic()
    {
        Menu.SetChecked(PLAY_BATTLE_MUSIC_PATH, PlayBattleMusic);
        return true;
    }

    [MenuItem(PLAY_BATTLE_MUSIC_PATH, priority = 1)]
    private static void TogglePlayBattleMusic()
    {
        PlayBattleMusic = !PlayBattleMusic;
    }

    // private const string BATTLE_NAME_ID_PREFS_KEY_PREFIX = "ActionTreePV-BattleNameId_";
    // private string m_battleNameId;
    // private string BattleNameId
    // {
    //     get
    //     {
    //         if (string.IsNullOrEmpty(m_battleNameId) && IsBattlePreview)
    //         {
    //             var sceneName = ActionTree.gameObject.scene.name;
    //             m_battleNameId = EditorPrefs.GetString(BATTLE_NAME_ID_PREFS_KEY_PREFIX + sceneName, null);
    //         }
    //         return m_battleNameId;
    //     }
    //     set
    //     {
    //         m_battleNameId = value;
    //
    //         if (IsBattlePreview)
    //         {
    //             var sceneName = ActionTree.gameObject.scene.name;
    //             EditorPrefs.SetString(BATTLE_NAME_ID_PREFS_KEY_PREFIX + sceneName, m_battleNameId);
    //         }
    //     }
    // }

    private IEnumerable<string> ActiveBirdNameIds => m_birds
        .Where(b => b.Active)
        .Select(b => b.NameId);
    
    private (string NameId, bool Active)[] m_birds = new[]
    {
        ("bird_red",    false),
        ("bird_yellow", false),
        ("bird_white",  false),
        ("bird_black",  false),
        ("bird_blue",   false) 
    };
    
    /// <summary>
    /// Launches the Previewer with an ActionTree selected, and locks it to that tree specifically.
    /// This means that selecting different trees will not update this window.
    /// </summary>
    public static ActionTreePreviewer LaunchAndLockToTree(ActionTree tree)
    {
        ActionTree = tree;
        Instance = GetWindow<ActionTreePreviewer>();
        Instance.LockedToTree = true;
        return Instance;
    }
    
    /// <summary>
    /// Launch the Previewer with no active ActionTree.
    /// The Previewer will play the currently selected ActionTree in the hierarchy.
    /// </summary>
    [MenuItem("ActionTree/Open Previewer")]
    public static void Launch()
    {
        Instance = GetWindow<ActionTreePreviewer>();
    }

    private void CreateGUI()
    {
        minSize = new Vector2(320f, 60f);
        maxSize = new Vector2(maxSize.x, 100f);
    }

    private void OnEnable()
    {
        if (Instance == null)
            Instance = this;
        
        Selection.selectionChanged += SelectionChanged;
        // AssemblyReloadEvents.beforeAssemblyReload += Close;

        var activeBirds = ActiveBirds; // get once
        for (var i = 0; i < m_birds.Length; i++)
        {
            ref var item = ref m_birds[i];
            item.Active = activeBirds.Contains(item.NameId);
        }
        
        SelectionChanged();
    }
    
    private void OnDisable()
    {
        Stop();

        ActiveBirds = ActiveBirdNameIds.ToHashSet();
        
        Selection.selectionChanged -= SelectionChanged;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RuntimeInit()
    {
        if (Instance == null || ActionTree == null || ActionTreeObjectId == null)
            return;
        
        // higher priorities finish first
        
        var contentOp = SceneManager.LoadSceneAsync("ContentLoader", LoadSceneMode.Additive);
        contentOp.priority = 1;
        contentOp.allowSceneActivation = true;
        
        var rootOp = SceneManager.LoadSceneAsync("Root", LoadSceneMode.Additive);
        rootOp.priority = 0;
        rootOp.allowSceneActivation = true;
        rootOp.completed += RootLoaded;

        RestoreActionTree();
        
        Debug.Log($"[Action Tree Preview] Previewing ActionTree '{ActionTree.name}'");
        
        if (HideLogs)
            Debug.unityLogger.logHandler = new ActionTreePreviewLogger(Debug.unityLogger.logHandler);
        
        PrepareScene();
        
        // manual reset of DIContainerInfrastructure's static fields
        ResetStaticClass<DIContainerInfrastructure>();
        ResetStaticClass<ClientInfo>();
        // set our PlayerPrefs handler to block saving
        InjectNullPlayerPrefsService();
        
        // load balancing
        DIContainerBalancing.Init(restart: true);
    }

    private static void InjectNullPlayerPrefsService()
    {
        var service = new NullPlayerPrefsService();
        
        var type = typeof(DIContainerInfrastructure);
        var storageServiceField = type.GetField("m_storageService", BindingFlags.Static | BindingFlags.NonPublic);
        
        storageServiceField?.SetValue(null, service);
    }

    private static void RestoreActionTree()
    {
        if (ActionTree != null || ActionTreeObjectId == null) 
            return;
        
        ActionTree = (ActionTree)GlobalObjectId.GlobalObjectIdentifierToObjectSlow(ActionTreeObjectId.Value);
    }
    
    private static void RootLoaded(AsyncOperation op)
    {
        var rootObj = CoreStateMgr.Instance.gameObject;
        var coreStateMgrFields = SnapshotClassFields(CoreStateMgr.Instance);
        rootObj.SetActive(false);
        DestroyImmediate(CoreStateMgr.Instance);
        
        var contentObj = ContentLoader.Instance.gameObject;
        contentObj.SetActive(false);
        
        ContentLoader.Instance.enabled = false;
        ContentLoader.Instance.AddComponentSafely(ref ContentLoader.Instance.m_AsynchStatusService);
        DestroyImmediate(ContentLoader.Instance);
        
        if (IsBattlePreview)
        {
            var mgr = InitPreviewStateManager<ActionTreeBattlePreviewMgr>(rootObj, coreStateMgrFields);
            
            // mgr.m_BattleBalancingNameId = Instance.BattleNameId;
            mgr.m_BirdBalancingNameIds = Instance.ActiveBirdNameIds.ToList();
            
            mgr.gameObject.SetActive(true);
            
            InitPreviewBattleMgr();
        }
        else
        {
            InitPreviewStateManager<ActionTreePreviewMgr>(rootObj, coreStateMgrFields);
            InitPreviewLocationManager();
        }
        
        rootObj.SetActive(true);
        // contentObj.SetActive(true);
    }
    
    private static void InitDebugger()
    {
        var dbgGameObject = new GameObject("ActionTreeDebugger");
        DontDestroyOnLoad(dbgGameObject);
        
        var debugger = dbgGameObject.AddComponent<ActionTreeDebugger>();
        debugger.InitTree(ActionTree);
        
        debugger.BreakpointHit -= Debugger_BreakpointHit;
        debugger.BreakpointHit += Debugger_BreakpointHit;
        
        debugger.TreeNodeChanged -= Debugger_TreeNodeChanged;
        debugger.TreeNodeChanged += Debugger_TreeNodeChanged;
        
        debugger.Stopped -= Debugger_Stopped;
        debugger.Stopped += Debugger_Stopped;
    }
    
    private static FieldInfo m_storySequenceVisibleField = typeof(ScreenElements).GetField("m_storySequenceVisible", BindingFlags.NonPublic | BindingFlags.Instance);

    private static void Debugger_TreeNodeChanged(int oldNode, int newNode)
    {
        if (ScreenElements.Instance == null || m_storySequenceVisibleField == null)
            return;
        
        ActiveStorySequence = (bool)m_storySequenceVisibleField.GetValue(ScreenElements.Instance);

        CheckForDragControllerUnlock();
    }

    private static void CheckForDragControllerUnlock()
    {
        var dragControllerActive = !ActiveStorySequence || ActionTreeDebugger.Instance.IsDebugBroken;

        if (DIContainerInfrastructure.CurrentDragController)
            DIContainerInfrastructure.CurrentDragController.SetActiveDepth(dragControllerActive, 0);
    }

    private static void Debugger_BreakpointHit(int node, bool singleStep)
    {
        if (!singleStep && DIContainerInfrastructure.CurrentDragController)
            m_cachedDragControllerPos = DIContainerInfrastructure.CurrentDragController.transform.position;
        
        CheckForDragControllerUnlock();
        
        Instance?.Repaint();
    }

    private void ContinueFromBreakpoint()
    {
        if (DIContainerInfrastructure.CurrentDragController && m_cachedDragControllerPos.HasValue)
            DIContainerInfrastructure.CurrentDragController.transform.position = m_cachedDragControllerPos.Value;
        
        ActionTreeDebugger.Instance.Continue();
    }
        
    private static void Debugger_Stopped()
    {
        Started = false;
        WaitingForBattleInit = false;
        Instance.PlaybackEnded?.Invoke();
    }

    private void EnterPlayModeNoDomainReload(Action callback = null)
    {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        
        EditorApplication.update += WaitThenRestore;
        EditorApplication.playModeStateChanged += HideLogsWhilePreviewing;
        
        EditorApplication.EnterPlaymode();
        return;
        
        void HideLogsWhilePreviewing(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                BeforeEnterPlaymode?.Invoke();
                return;
            }
            
            if (change != PlayModeStateChange.EnteredEditMode) 
                return;
            
            if (Debug.unityLogger.logHandler is ActionTreePreviewLogger logger)
                Debug.unityLogger.logHandler = logger.OriginalHandler;
                
            RestoreActionTree();
            EditorApplication.playModeStateChanged -= HideLogsWhilePreviewing;
        }

        void WaitThenRestore()
        {
            if (EditorApplication.isPlaying)
            {
                EditorSettings.enterPlayModeOptionsEnabled = false;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
                
                Repaint();
                
                EditorApplication.update -= WaitThenRestore;
                callback?.Invoke();
            }
        }
    }

    private static void PrepareScene()
    {
        // stop UITapHoldTriggers (their awake checks CoreStateMgr)
        
        var holdTriggers = FindObjectsOfType<UITapHoldTrigger>(true);
        foreach (var trigger in holdTriggers)
        {
            var actionInvoker = trigger.gameObject.GetComponent<ActionOverlayInvoker>();
            if (actionInvoker != null)
                DestroyImmediate(actionInvoker);
            
            var genericInvoker = trigger.gameObject.GetComponent<GenericOverlayInvoker>();
            if (genericInvoker != null)
                DestroyImmediate(genericInvoker);
            
            DestroyImmediate(trigger);
        }
        
        var condActionTreePlayers = FindObjectsOfType<WorldMapConditionalActionTreePlayer>();
        condActionTreePlayers.ForEach(DestroyImmediate);
        
        InitDebugger();
    }
    
    // runs AFTER Awake but BEFORE Start
    // Awake -> ContentLoader & Root load -> Init -> Start

    private static void InitPreviewLocationManager()
    {
        var locMgr = FindObjectOfType<BaseLocationStateManager>(true);
        var locMgrFields = SnapshotClassFields(locMgr);
        
        var previewLocMgr = locMgr.gameObject.AddComponent<ActionTreePreviewLocationMgr>();
        RestoreClassSnapshot(previewLocMgr, locMgrFields);
        
        DestroyImmediate(locMgr);
    }
    
    private static void InitPreviewBattleMgr()
    {
        var battleMgr = FindObjectOfType<BattleMgr>(true);
        var battleMgrFields = SnapshotClassFields(battleMgr);
        
        battleMgr.gameObject.SetActive(false);
        var previewBattleMgr = battleMgr.gameObject.AddComponent<ActionTreePreviewBattleMgr>();

        WaitingForBattleInit = true;
        previewBattleMgr.PlayBattleMusic = PlayBattleMusic;
        previewBattleMgr.BattleInitialized -= Instance.PlayFromBattle;
        previewBattleMgr.BattleInitialized += Instance.PlayFromBattle;
        
        RestoreClassSnapshot(previewBattleMgr, battleMgrFields);
        battleMgr.gameObject.SetActive(true);
        
        DestroyImmediate(battleMgr);
    }
    
    private static TPreview InitPreviewStateManager<TPreview>(GameObject rootObj, Dictionary<string, object> fields) 
        where TPreview : ActionTreePreviewMgr
    {
        var previewStateMgr = rootObj.AddComponent<TPreview>();
        previewStateMgr.SetActionTree(ActionTree);
        RestoreClassSnapshot(previewStateMgr, fields);
        
        // load all assetproviders
        var assetProviders = rootObj.GetComponentsInChildren<GenericAssetProvider>();
        foreach (var provider in assetProviders)
        {
            provider.Initialize();
            provider.enabled = false;
        }

        return previewStateMgr;
    }
    
    private void Stop(bool isRestart = false)
    {
        WaitingForBattleInit = false;
        Reloading = true;
        
        EditorApplication.ExitPlaymode();
        if (IsPlayingTree)
            ActionTree.StopExecution();

        EditorApplication.delayCall += delegate
        {
            Reloading = false;
            
            if (!isRestart)
                SelectionChanged();
        };
    }

    private void SelectionChanged()
    {
        if (Reloading || LockedToTree || IsPlayingTree)
            return;
        
        var selectedTree = Selection.activeGameObject?.GetComponent<ActionTree>();
        ActionTree = selectedTree;
        
        Repaint();
    }

    public void OnGUI()
    {
        if (!Reloading && LockedToTree && ActionTree == null)
        {
            Close();
            return;
        }

        if (ActionTree == null)
        {
            GUILayout.Label("Select an ActionTree to preview");
            
            GUI.enabled = false;
        }
        else
        {
            GUILayout.Label($"Previewing: {ActionTree.name}");
        }

        if (IsBattlePreview && !BattlePreviewValid)
        {
            GUI.enabled = false;
        }

        DrawBasePreviewUI();

        GUI.enabled = true;

        if (IsBattlePreview)
            DrawBattlePreviewUI();

        GUI.enabled = true;
    }

    private void DrawBattlePreviewUI()
    {
        EditorGUILayout.Space(8f);
        
        // BattleNameId = EditorGUILayout.TextField("Battle NameId", BattleNameId);
        //
        // EditorGUILayout.Space(3f);
        
        GUILayout.BeginHorizontal();
        
        for (var i = 0; i < m_birds.Length; i++)
        {
            ref var bird = ref m_birds[i];
            
            bird.Active = GUILayout.Toggle(bird.Active, bird.NameId, "Button");
        }

        GUILayout.EndHorizontal();
    }

    private void DrawBasePreviewUI()
    {
        GUILayout.BeginHorizontal();
        
        if (GUILayout.Button(!IsPlayingTree ? "Start" : "Stop"))
        {
            if (!IsPlayingTree)
            {
                EnterPlayModeNoDomainReload(Play);
            }
            else
            {
                Stop();
            }
        }

        var oldEnabled = GUI.enabled;
        GUI.enabled = IsPlayingTree && !WaitingForBattleInit;

        if (ActionTreeDebugger.Instance != null)
        {
            if (ActionTreeDebugger.Instance.IsDebugBroken)
                DrawDebugBrokenStateControls();
            else
                DrawDebugRunningStateControls();
        }
        else
        {
            DrawDefaultStateControls();
        }

        GUI.enabled = oldEnabled;
        
        GUILayout.EndHorizontal();
    }

    private void DrawDebugBrokenStateControls()
    {
        if (GUILayout.Button("Continue"))
        {
            ContinueFromBreakpoint();
        }

        if (GUILayout.Button(new GUIContent("Step", Tooltips.PreviewerSingleStepTooltip)))
        {
            ActionTreeDebugger.Instance?.SingleStep();
        }
    }

    private void DrawDebugRunningStateControls()
    {
        if (GUILayout.Button("Break"))
        {
            ActionTreeDebugger.Instance?.Break();
        }
            
        DrawRestartControl();
    }

    private void DrawDefaultStateControls()
    {
        if (GUILayout.Button(EditorApplication.isPaused ? "Play" : "Pause"))
        {
            if (IsPlayingTree)
                EditorApplication.isPaused = !EditorApplication.isPaused;
        }

        DrawRestartControl();
    }

    private void DrawRestartControl()
    {
        if (GUILayout.Button("Restart"))
        {
            Reloading = true;
            Stop(true);
            EditorApplication.delayCall += delegate
            {
                EnterPlayModeNoDomainReload(delegate
                {
                    Play();
                    Reloading = false;
                });
            };
        }
    }

    private ActionTreePreviewMgr GetActionTreePreviewMgr()
    {
        return DIContainerInfrastructure.GetCoreStateMgr() as ActionTreePreviewMgr;
    }

    private void Play()
    {
        Play(false);
    }
    
    private void PlayFromBattle()
    {
        Play(true);
    }

    private void Play(bool battleInitialized)
    {
        if (IsBattlePreview && !battleInitialized)
            return;

        if (IsBattlePreview)
            WaitingForBattleInit = false;
        
        Started = true;
        PlaybackBegan?.Invoke();
        ActionTreeDebugger.Playing = true;
        
        GetActionTreePreviewMgr().Play();
        
        Repaint();
    }
    
    private static void ResetStaticClass<T>()
    {
        var classToReset = typeof(T);
        var fields = classToReset.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var field in fields)
        {
            try
            {
                if (field.IsLiteral)
                    continue;

                // if (field.FieldType.BaseType != typeof(MonoBehaviour))
                //     continue;

                var defaultValue = field.FieldType.IsValueType
                    ? Activator.CreateInstance(field.FieldType)
                    : null;

                field.SetValue(null, defaultValue);
            }
            catch (Exception e)
            {
                // discard
            }
        }

        var staticCtor = classToReset.TypeInitializer;
        staticCtor?.Invoke(null, null);
    }
    
    private static Dictionary<string, object> SnapshotClassFields(object obj)
    {
        var dict = new Dictionary<string, object>();
        
        var fields = obj.GetType().GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var field in fields)
        {
            dict.Add(field.Name, field.GetValue(obj));
        }

        return dict;
    }
    
    private static void RestoreClassSnapshot<T>(T obj, Dictionary<string, object> fields)
    {
        foreach (var (fieldName, value) in fields)
        {
            try
            {
                var field = typeof(T).GetField(fieldName, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null)
                    continue;
                
                field.SetValue(obj, value);
            }
            catch (Exception e)
            {
                // Debug.LogException(e);
            }
        }
    }
}