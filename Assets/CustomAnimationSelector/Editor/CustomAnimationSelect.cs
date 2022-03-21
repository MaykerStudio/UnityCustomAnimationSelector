#if UNITY_2019_1_OR_NEWER
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class CustomAnimationSelect : EditorWindow
{
    public const float DROPDOWNHEIGHT = 300f;

    public static CustomAnimationSelect Instance
    {
        get
        {
            if (!_instance)
            {
                ShowWindow();
            }

            return _instance;
        }
    }

    private static CustomAnimationSelect _instance;

    private static GameObject selected;

    private bool isLocked;

    [MenuItem("Tools/Custom animation select")]
    public static void ShowWindow()
    {
        _instance = GetWindow<CustomAnimationSelect>(utility: true, title: "Search clip");
        _instance.minSize = new Vector2(100, 50);
        _instance.maxSize = new Vector2(100, 50);
    }

    private void OnGUI()
    {
        if(!isLocked)
            selected = Selection.activeGameObject;

        if (GUILayout.Button("Clips"))
        {
            if (selected)
            {
                var animator = selected.GetComponent<Animator>();
                AnimatorController controller = null;

                if (animator)
                    controller = (AnimatorController)animator.runtimeAnimatorController;
                else
                    Debug.LogError($"{selected.name} doesn't contains an animator controller");

                if (controller)
                {
                    AdvancedGenericMenu menu = new AdvancedGenericMenu();

                    for (int i = 0; i < controller.animationClips.Length; i++)
                    {
                        AnimationClip c = controller.animationClips[i];
                        menu.AddItem(controller.animationClips[i].name, false, () => 
                        {
                            var animWindow = GetWindow<AnimationWindow>();
                            animWindow.Focus();
                            animWindow.animationClip = c;
                        });
                    }

                    Rect rect = new Rect();

                    menu.DropDown(rect);

                    var window = focusedWindow;

                    if (window == null)
                    {
                        Debug.LogWarning("EditorWindow.focusedWindow was null.");
                        return;
                    }

                    if (!string.Equals(window.GetType().Namespace, typeof(AdvancedDropdown).Namespace))
                    {
                        Debug.LogWarning("EditorWindow.focusedWindow " + focusedWindow.GetType().FullName + " was not in expected namespace.");
                        return;
                    }

                    var position = window.position;
                    if (position.height <= DROPDOWNHEIGHT)
                    {
                        return;
                    }

                    position.height = DROPDOWNHEIGHT;
                    window.minSize = position.size;
                    window.maxSize = position.size;
                    window.position = position;
                    window.ShowAsDropDown(GUIUtility.GUIToScreenRect(rect), position.size);
                }
            }
        }

        if (!isLocked)
        {
            if (selected)
            {
                if (GUILayout.Button(new GUIContent("Lock", "Lock the current selected object")))
                    isLocked = true;
            }
        }
        else
        {
            GUI.contentColor = Color.red;
            if (GUILayout.Button(new GUIContent("Unlock", "Unlock the current selected object")))
            {
                isLocked = false;
            }
        }

        if (Application.isPlaying)
            isLocked = false;
    }
}
#endif