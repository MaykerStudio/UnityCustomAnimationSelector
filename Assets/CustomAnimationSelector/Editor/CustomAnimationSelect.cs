using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UNITY_2021_1_OR_NEWER
using PrefabStageUtility = UnityEditor.SceneManagement.PrefabStageUtility;
#else
using UnityEditor.Experimental.SceneManagement;
#endif

public class CustomAnimationSelect : EditorWindow
{
    #region Constants

    private const float MinWindowWidth = 220f;
    private const float HeaderHeight = 22f;

    private static readonly Color AccentBlue = new Color(0.3f, 0.6f, 1f);
    private static readonly Color AccentYellow = new Color(1f, 0.85f, 0.2f);
    private static readonly Color RowEven = new Color(0.22f, 0.22f, 0.22f);
    private static readonly Color RowOdd = new Color(0.19f, 0.19f, 0.19f);
    private static readonly Color RowHover = new Color(0.28f, 0.42f, 0.62f);
    private static readonly Color RowSelected = new Color(0.24f, 0.48f, 0.9f);
    private static readonly Color DividerColor = new Color(0.12f, 0.12f, 0.12f);

    #endregion

    #region Static / Instance

    public static CustomAnimationSelect Instance
    {
        get
        {
            if (!_instance)
                ShowWindow();
            return _instance;
        }
    }

    private static CustomAnimationSelect _instance;
    private static readonly List<AnimatorController> sortedControllers =
        new List<AnimatorController>();

    #endregion

    #region Private Fields

    private GenericMenu dropdownMenu;
    private AnimatorController selectedController;

    private Vector2 scrollPosition;
    private float contentWidth;

    private string searchQuery = string.Empty;
    private int hoveredIndex = -1;

    private GUIStyle rowStyle;
    private GUIStyle headerStyle;
    private GUIStyle searchStyle;
    private GUIStyle countBadgeStyle;
    private bool stylesInitialized;

    private bool isPlaying;
    private double lastEditorTime;

    #endregion

    #region Unity Editor Lifecycle

    private void OnEnable()
    {
        selectedController = null;
        dropdownMenu = null;
        searchQuery = string.Empty;
        stylesInitialized = false;
        wantsMouseMove = true;
        EditorApplication.update += EditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
        isPlaying = false;
    }

    [MenuItem("Tools/Custom animation select")]
    public static void ShowWindow()
    {
        _instance = GetWindow<CustomAnimationSelect>(title: "Anim Search");
        _instance.minSize = new Vector2(MinWindowWidth, 200f);
    }

    #endregion

    #region OnGUI

    private void OnGUI()
    {
        InitStyles();

        if (!selectedController)
            DrawControllerList();
        else
            DrawClipList();

        if (contentWidth > 0f)
        {
            float w = Mathf.Max(contentWidth + 24f, MinWindowWidth);
            minSize = new Vector2(w, 200f);
        }
    }

    #endregion

    #region Controller List View

    private void DrawControllerList()
    {
        DrawToolbar(isControllerView: true);
        DrawSearchBar();
        DrawDivider();

        var filtered = GetFilteredControllers();

        if (sortedControllers.Count == 0)
        {
            DrawEmptyState("Click Scan to find Animator Controllers.");
            return;
        }

        if (filtered.Count == 0)
        {
            DrawEmptyState("No controllers match the search.");
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(
            scrollPosition,
            GUIStyle.none,
            GUI.skin.verticalScrollbar
        );

        for (int i = 0; i < filtered.Count; i++)
        {
            var controller = filtered[i];
            int clipCount = CountAnimationsInController(controller);
            DrawControllerRow(controller, clipCount, i);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawControllerRow(AnimatorController controller, int clipCount, int index)
    {
        Rect rowRect = GUILayoutUtility.GetRect(0f, HeaderHeight, GUILayout.ExpandWidth(true));

        bool isHovered = rowRect.Contains(Event.current.mousePosition);
        Color rowColor = isHovered ? RowHover : (index % 2 == 0 ? RowEven : RowOdd);

        EditorGUI.DrawRect(rowRect, rowColor);

        if (isHovered)
            Repaint();

        string badge = clipCount.ToString();
        float badgeWidth = countBadgeStyle.CalcSize(new GUIContent(badge)).x + 10f;
        Rect badgeRect = new Rect(
            rowRect.xMax - badgeWidth - 6f,
            rowRect.y + 3f,
            badgeWidth,
            rowRect.height - 6f
        );
        Rect labelRect = new Rect(
            rowRect.x + 8f,
            rowRect.y,
            rowRect.width - badgeWidth - 20f,
            rowRect.height
        );

        GUI.Label(labelRect, controller.name, rowStyle);
        GUI.Label(badgeRect, badge, countBadgeStyle);

        if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
        {
            SelectController(controller);
        }

        DrawDivider(rowRect.yMax);
    }

    private void SelectController(AnimatorController controller)
    {
        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            StageUtility.GoToMainStage();

        selectedController = controller;
        dropdownMenu = null;
        searchQuery = string.Empty;
        scrollPosition = Vector2.zero;
        CalculateContentWidth();
        FocusObjectWithController(controller);
    }

    /// <summary>
    /// Selects an Animator that uses the given controller, first checking scene instances,
    /// then opening a Prefab if needed. Also frames the object in the Scene view.
    /// </summary>
    private void FocusObjectWithController(AnimatorController controller)
    {
        foreach (
            var animator in FindObjectsByType<Animator>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            if (animator.runtimeAnimatorController == controller)
            {
                Selection.SetActiveObjectWithContext(animator.gameObject, animator.gameObject);
                SceneView.lastActiveSceneView?.FrameSelected();
                return;
            }
        }

        string prefabPath = FindPrefabPathWithController(controller);
        if (!string.IsNullOrEmpty(prefabPath))
        {
#if UNITY_2021_1_OR_NEWER
            PrefabStageUtility.OpenPrefab(prefabPath);
#else
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            AssetDatabase.OpenAsset(prefab);
#endif
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                var animators = stage.prefabContentsRoot.GetComponentsInChildren<Animator>(true);
                foreach (var anim in animators)
                {
                    if (anim.runtimeAnimatorController == controller)
                    {
                        Selection.activeObject = anim.gameObject;
                        SceneView.lastActiveSceneView?.FrameSelected();
                        return;
                    }
                }
                Selection.activeObject = stage.prefabContentsRoot;
            }
            return;
        }

        Selection.SetActiveObjectWithContext(controller, controller);
    }

    #endregion

    #region Clip List View

    private void DrawClipList()
    {
        DrawToolbar(isControllerView: false);
        DrawSearchBar();
        DrawDivider();

        if (!selectedController)
            return;

        if (selectedController.animationClips.Length == 0)
        {
            DrawEmptyState("No animation clips in this controller.");
            return;
        }

        if (dropdownMenu == null)
            BuildDropdownMenu();

        var clips = GetFilteredClips();

        if (clips.Count == 0)
        {
            DrawEmptyState("No clips match the search.");
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(
            scrollPosition,
            GUIStyle.none,
            GUI.skin.verticalScrollbar
        );

        for (int i = 0; i < clips.Count; i++)
        {
            DrawClipRow(clips[i], i);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawClipRow(AnimationClip clip, int index)
    {
        Rect rowRect = GUILayoutUtility.GetRect(0f, HeaderHeight, GUILayout.ExpandWidth(true));

        bool isHovered = rowRect.Contains(Event.current.mousePosition);
        Color rowColor = isHovered ? RowHover : (index % 2 == 0 ? RowEven : RowOdd);

        EditorGUI.DrawRect(rowRect, rowColor);
        if (isHovered)
            Repaint();

        Rect labelRect = new Rect(rowRect.x + 8f, rowRect.y, rowRect.width - 8f, rowRect.height);
        GUI.Label(labelRect, clip.name, rowStyle);

        if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
        {
            OpenClipInAnimationWindow(clip);
        }

        DrawDivider(rowRect.yMax);
    }

    private void OpenClipInAnimationWindow(AnimationClip clip)
    {
        EnsureAnimatorSelected(selectedController);

        var animWindow = GetWindow<AnimationWindow>();
        animWindow.animationClip = clip;
        animWindow.Focus();
    }

    /// <summary>
    /// Makes sure a GameObject with the given controller is selected,
    /// without moving the Scene view camera. Opens a Prefab if necessary
    /// but does not call FrameSelected.
    /// </summary>
    private void EnsureAnimatorSelected(AnimatorController controller)
    {
        var activeGO = Selection.activeGameObject;
        if (activeGO != null)
        {
            var anim = activeGO.GetComponentInParent<Animator>();
            if (anim != null && anim.runtimeAnimatorController == controller)
                return;
        }

        foreach (
            var animator in FindObjectsByType<Animator>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            if (animator.runtimeAnimatorController == controller)
            {
                Selection.activeGameObject = animator.gameObject;
                return;
            }
        }

        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null)
        {
            var animators = stage.prefabContentsRoot.GetComponentsInChildren<Animator>(true);
            foreach (var anim in animators)
            {
                if (anim.runtimeAnimatorController == controller)
                {
                    Selection.activeGameObject = anim.gameObject;
                    return;
                }
            }
        }

        string prefabPath = FindPrefabPathWithController(controller);
        if (!string.IsNullOrEmpty(prefabPath))
        {
#if UNITY_2021_1_OR_NEWER
            PrefabStageUtility.OpenPrefab(prefabPath);
#else
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            AssetDatabase.OpenAsset(prefab);
#endif
            stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                var animators = stage.prefabContentsRoot.GetComponentsInChildren<Animator>(true);
                foreach (var anim in animators)
                {
                    if (anim.runtimeAnimatorController == controller)
                    {
                        Selection.activeGameObject = anim.gameObject;
                        return;
                    }
                }
                Selection.activeGameObject = stage.prefabContentsRoot;
            }
            return;
        }

        Selection.activeObject = controller;
    }

    private void BuildDropdownMenu()
    {
        dropdownMenu = new GenericMenu();

        for (int i = 0; i < selectedController.animationClips.Length; i++)
        {
            AnimationClip c = selectedController.animationClips[i];
            dropdownMenu.AddItem(new GUIContent(c.name), false, () => OpenClipInAnimationWindow(c));
        }
    }

    #endregion

    #region Shared UI Elements

    private void DrawToolbar(bool isControllerView)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (isControllerView)
        {
            GUI.backgroundColor = AccentBlue;
            if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                ScanForAnimatorControllers();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.FlexibleSpace();

            if (sortedControllers.Count > 0)
            {
                GUILayout.Label($"{sortedControllers.Count} controllers", EditorStyles.miniLabel);
            }
        }
        else
        {
            GUI.backgroundColor = AccentYellow;
            if (GUILayout.Button("◀  Back", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                GoBack();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUILayout.FlexibleSpace();
            GUILayout.Label(selectedController.name, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (
                GUILayout.Button(
                    isPlaying ? "■ Stop" : "▶ Play",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(60f)
                )
            )
            {
                PlayCurrentAnimation();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSearchBar()
    {
        EditorGUILayout.BeginHorizontal(GUILayout.Height(22f));
        GUILayout.Space(6f);

        GUI.SetNextControlName("SearchField");
        string newQuery = EditorGUILayout.TextField(
            searchQuery,
            searchStyle ?? EditorStyles.toolbarSearchField
        );

        if (newQuery != searchQuery)
        {
            searchQuery = newQuery;
            scrollPosition = Vector2.zero;
        }

        if (!string.IsNullOrEmpty(searchQuery))
        {
            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20f)))
            {
                searchQuery = string.Empty;
                GUI.FocusControl(null);
            }
        }

        GUILayout.Space(6f);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDivider(float y = -1f)
    {
        if (y < 0f)
        {
            Rect r = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, DividerColor);
        }
        else
        {
            EditorGUI.DrawRect(new Rect(0f, y, position.width, 1f), DividerColor);
        }
    }

    private void DrawEmptyState(string message)
    {
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label(message, EditorStyles.centeredGreyMiniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    private void GoBack()
    {
        selectedController = null;
        dropdownMenu = null;
        searchQuery = string.Empty;
        scrollPosition = Vector2.zero;
    }

    /// <summary>
    /// Toggles playback. When playing, EditorUpdate advances animWindow.time
    /// each tick using EditorApplication.timeSinceStartup as the clock,
    /// looping when the clip ends.
    /// </summary>
    private void PlayCurrentAnimation()
    {
        var animWindow = GetWindow<AnimationWindow>();
        if (animWindow == null || animWindow.animationClip == null)
            return;

        isPlaying = !isPlaying;

        if (isPlaying)
        {
            animWindow.previewing = true;
            lastEditorTime = EditorApplication.timeSinceStartup;
            animWindow.Focus();
        }
        else
        {
            animWindow.previewing = false;
        }

        Repaint();
    }

    private void EditorUpdate()
    {
        if (!isPlaying)
            return;

        var animWindow = Resources.FindObjectsOfTypeAll<AnimationWindow>();
        if (animWindow.Length == 0 || animWindow[0].animationClip == null)
        {
            isPlaying = false;
            Repaint();
            return;
        }

        var window = animWindow[0];
        double now = EditorApplication.timeSinceStartup;
        float delta = (float)(now - lastEditorTime);
        lastEditorTime = now;

        float duration = window.animationClip.length;
        float next = window.time + delta;

        window.time = duration > 0f ? next % duration : 0f;
    }

    #endregion

    #region Filtering

    private List<AnimatorController> GetFilteredControllers()
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
            return sortedControllers;

        var result = new List<AnimatorController>();
        string lower = searchQuery.ToLowerInvariant();

        foreach (var c in sortedControllers)
        {
            if (c.name.ToLowerInvariant().Contains(lower))
                result.Add(c);
        }

        return result;
    }

    private List<AnimationClip> GetFilteredClips()
    {
        var clips = selectedController.animationClips;
        var result = new List<AnimationClip>(clips.Length);
        string lower = searchQuery.ToLowerInvariant();

        foreach (var clip in clips)
        {
            if (
                string.IsNullOrWhiteSpace(searchQuery)
                || clip.name.ToLowerInvariant().Contains(lower)
            )
                result.Add(clip);
        }

        return result;
    }

    #endregion

    #region Scanning

    private void ScanForAnimatorControllers()
    {
        sortedControllers.Clear();
        contentWidth = 0f;

        string[] guids = AssetDatabase.FindAssets("t:AnimatorController");

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

            if (controller == null)
                continue;

            sortedControllers.Add(controller);

            float w = GUI.skin.button.CalcSize(new GUIContent(controller.name)).x;
            if (w > contentWidth)
                contentWidth = w;
        }

        sortedControllers.Sort(
            (a, b) => CountAnimationsInController(b).CompareTo(CountAnimationsInController(a))
        );
    }

    #endregion

    #region Helpers

    private int CountAnimationsInController(AnimatorController controller)
    {
        int count = 0;
        foreach (var layer in controller.layers)
            count += CountAnimationsInStateMachine(layer.stateMachine);
        return count;
    }

    private int CountAnimationsInStateMachine(AnimatorStateMachine stateMachine)
    {
        int count = stateMachine.states.Length;
        foreach (var sub in stateMachine.stateMachines)
            count += CountAnimationsInStateMachine(sub.stateMachine);
        return count;
    }

    private void CalculateContentWidth()
    {
        if (!selectedController)
            return;

        foreach (var clip in selectedController.animationClips)
        {
            float w = GUI.skin.button.CalcSize(new GUIContent(clip.name)).x;
            if (w > contentWidth)
                contentWidth = w;
        }
    }

    /// <summary>
    /// Searches all Prefab assets for one containing an Animator with the given controller.
    /// Returns the asset path of the first match, or null.
    /// </summary>
    private static string FindPrefabPathWithController(AnimatorController controller)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            Animator[] animators = prefab.GetComponentsInChildren<Animator>(true);
            foreach (var anim in animators)
            {
                if (anim.runtimeAnimatorController == controller)
                    return path;
            }
        }

        return null;
    }

    #endregion

    #region Style Initialization

    private void InitStyles()
    {
        if (stylesInitialized)
            return;

        rowStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            normal = { textColor = new Color(0.88f, 0.88f, 0.88f) },
        };

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
        };

        searchStyle = new GUIStyle(EditorStyles.toolbarSearchField) { fixedHeight = 18f };

        countBadgeStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            normal = { textColor = new Color(0.55f, 0.85f, 0.55f) },
        };

        stylesInitialized = true;
    }

    #endregion
}
