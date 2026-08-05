// Minimal UnityEditor stubs — API shape only.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static void AddObjectToAsset(UnityEngine.Object asset, UnityEngine.Object parent) { }
        public static void SaveAssets() { }
        public static void SaveAssetIfDirty(UnityEngine.Object asset) { }
        public static void Refresh() { }
        public static void ImportAsset(string path) { }
        public static void ImportAsset(string path, ImportAssetOptions options) { }
        public static UnityEngine.Object[] LoadAllAssetsAtPath(string path) { return new UnityEngine.Object[0]; }
        public static string GetAssetPath(UnityEngine.Object asset) { return ""; }
        public static string AssetPathToGUID(string path) { return ""; }
        public static string GUIDToAssetPath(string guid) { return ""; }
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object { return default(T); }
        public static UnityEngine.Object LoadAssetAtPath(string path, Type type) { return null; }
        public static UnityEngine.Object LoadMainAssetAtPath(string path) { return null; }
        public static string[] FindAssets(string filter) { return new string[0]; }
        public static string[] FindAssets(string filter, string[] searchInFolders) { return new string[0]; }
        public static bool DeleteAsset(string path) { return false; }
        public static string MoveAsset(string oldPath, string newPath) { return ""; }
        public static string CreateFolder(string parent, string newFolderName) { return ""; }
        public static bool IsValidFolder(string path) { return false; }
        public static string GenerateUniqueAssetPath(string path) { return path; }
        public static void StartAssetEditing() { }
        public static void StopAssetEditing() { }
    }

    public static class EditorUtility
    {
        public static void SetDirty(UnityEngine.Object target) { }
        public static bool DisplayDialog(string title, string message, string ok) { return false; }
        public static bool DisplayDialog(string title, string message, string ok, string cancel) { return false; }
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static void ClearProgressBar() { }
        public static string SaveFilePanelInProject(string title, string defaultName, string extension, string message) { return ""; }
        public static string SaveFilePanelInProject(string title, string defaultName, string extension, string message, string path) { return ""; }
        public static string OpenFilePanel(string title, string directory, string extension) { return ""; }
    }

    public static class EditorApplication
    {
        public static bool isPlaying { get { return false; } }
        public static bool isPlayingOrWillChangePlaymode { get { return false; } }
        public static bool isCompiling { get { return false; } }
        public static void ExecuteMenuItem(string menuItemPath) { }
        public static Action update;
        public static Action delayCall;
        public static double timeSinceStartup { get { return 0.0; } }
    }

    public static class EditorGUIUtility
    {
        public static void PingObject(UnityEngine.Object obj) { }
        public static void PingObject(int instanceID) { }
        public static float singleLineHeight { get { return 18f; } }
    }

    public static class Selection
    {
        public static UnityEngine.Object activeObject { get; set; }
        public static GameObject activeGameObject { get; set; }
        public static Transform activeTransform { get; set; }
        public static GameObject[] gameObjects { get { return new GameObject[0]; } }
        public static UnityEngine.Object[] objects { get; set; }
    }

    public static class Undo
    {
        public static void RecordObject(UnityEngine.Object o, string name) { }
        public static void RegisterCreatedObjectUndo(UnityEngine.Object o, string name) { }
        public static T AddComponent<T>(GameObject go) where T : Component { return default(T); }
        public static Component AddComponent(GameObject go, Type type) { return null; }
        public static void DestroyObjectImmediate(UnityEngine.Object o) { }
    }

    public static class PrefabUtility
    {
        public static GameObject SaveAsPrefabAsset(GameObject root, string path) { return null; }
        public static GameObject SaveAsPrefabAsset(GameObject root, string path, out bool success) { success = true; return null; }
        public static GameObject SaveAsPrefabAssetAndConnect(GameObject root, string path, InteractionMode mode) { return null; }
        public static GameObject InstantiatePrefab(UnityEngine.Object asset) { return null; }
        public static bool IsPartOfPrefabAsset(UnityEngine.Object o) { return false; }
    }
    public enum InteractionMode { AutomatedAction, UserAction }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
        public MenuItem(string itemName, bool isValidateFunction) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }
        public string menuItem;
        public bool validate;
        public int priority;
    }

    public static class TypeCache
    {
        public class TypeCollection : IEnumerable<Type>
        {
            public int Count { get { return 0; } }
            public Type this[int index] { get { return null; } }
            public IEnumerator<Type> GetEnumerator() { yield break; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { yield break; }
        }
        public class MethodCollection : IEnumerable<System.Reflection.MethodInfo>
        {
            public int Count { get { return 0; } }
            public IEnumerator<System.Reflection.MethodInfo> GetEnumerator() { yield break; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { yield break; }
        }
        public static TypeCollection GetTypesDerivedFrom<T>() { return new TypeCollection(); }
        public static TypeCollection GetTypesDerivedFrom(Type type) { return new TypeCollection(); }
        public static TypeCollection GetTypesWithAttribute<T>() where T : Attribute { return new TypeCollection(); }
        public static MethodCollection GetMethodsWithAttribute<T>() where T : Attribute { return new MethodCollection(); }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CustomEditor : Attribute
    {
        public CustomEditor(Type inspectedType) { }
        public CustomEditor(Type inspectedType, bool editorForChildClasses) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InitializeOnLoadMethodAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CustomPropertyDrawer : Attribute { public CustomPropertyDrawer(Type type) { } }

    public class MonoScript : UnityEngine.TextAsset
    {
        public System.Type GetClass() { return null; }
        public static MonoScript FromMonoBehaviour(UnityEngine.MonoBehaviour behaviour) { return null; }
        public static MonoScript FromScriptableObject(UnityEngine.ScriptableObject obj) { return null; }
    }

    public class Editor : ScriptableObject
    {
        public UnityEngine.Object target { get; set; }
        public UnityEngine.Object[] targets { get; set; }
        public SerializedObject serializedObject { get; set; }
        public virtual void OnInspectorGUI() { }
        public void DrawDefaultInspector() { }
        public static Editor CreateEditor(UnityEngine.Object obj) { return null; }
    }

    public class EditorWindow : ScriptableObject
    {
        public string title { get; set; }
        public GUIContent titleContent { get; set; }
        public Rect position { get; set; }
        public Vector2 minSize { get; set; }
        public Vector2 maxSize { get; set; }
        public void Show() { }
        public void Close() { }
        public void Repaint() { }
        public static T GetWindow<T>() where T : EditorWindow { return default(T); }
        public static T GetWindow<T>(string title) where T : EditorWindow { return default(T); }
    }

    public class SerializedObject
    {
        public SerializedObject(UnityEngine.Object obj) { }
        public SerializedProperty FindProperty(string path) { return null; }
        public void Update() { }
        public bool ApplyModifiedProperties() { return false; }
        public bool ApplyModifiedPropertiesWithoutUndo() { return false; }
    }

    public class SerializedProperty
    {
        public string stringValue { get; set; }
        public int intValue { get; set; }
        public float floatValue { get; set; }
        public bool boolValue { get; set; }
        public UnityEngine.Object objectReferenceValue { get; set; }
        public int arraySize { get; set; }
        public SerializedProperty GetArrayElementAtIndex(int index) { return null; }
        public void InsertArrayElementAtIndex(int index) { }
        public void DeleteArrayElementAtIndex(int index) { }
    }

    public static class EditorGUILayout
    {
        public static void LabelField(string label, params GUILayoutOption[] options) { }
        public static void LabelField(string label, GUIStyle style, params GUILayoutOption[] options) { }
        public static void LabelField(string label, string value, params GUILayoutOption[] options) { }
        public static void LabelField(string label, string value, GUIStyle style, params GUILayoutOption[] options) { }
        public static void HelpBox(string message, MessageType type) { }
        public static void HelpBox(string message, MessageType type, bool wide) { }
        public static void PropertyField(SerializedProperty property) { }
        public static void PropertyField(SerializedProperty property, bool includeChildren) { }
        public static void Space() { }
        public static void Space(float width) { }
        public static bool Toggle(bool value, params GUILayoutOption[] options) { return false; }
        public static bool Toggle(string label, bool value, params GUILayoutOption[] options) { return false; }
        public static bool ToggleLeft(string label, bool value, params GUILayoutOption[] options) { return false; }
        public static bool ToggleLeft(string label, bool value, GUIStyle style, params GUILayoutOption[] options) { return false; }
        public static string TextField(string text, GUIStyle style, params GUILayoutOption[] options) { return text; }
        public static int Popup(int selected, string[] displayed, GUIStyle style, params GUILayoutOption[] options) { return selected; }
        public static bool Foldout(bool foldout, string content) { return false; }
        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick) { return false; }
        public static int IntField(int value, params GUILayoutOption[] options) { return 0; }
        public static int IntField(string label, int value, params GUILayoutOption[] options) { return 0; }
        public static float FloatField(float value, params GUILayoutOption[] options) { return 0f; }
        public static float FloatField(string label, float value, params GUILayoutOption[] options) { return 0f; }
        public static string TextField(string value, params GUILayoutOption[] options) { return value; }
        public static string TextField(string label, string value, params GUILayoutOption[] options) { return value; }
        public static string TextArea(string value, params GUILayoutOption[] options) { return value; }
        public static Enum EnumPopup(Enum selected, params GUILayoutOption[] options) { return selected; }
        public static Enum EnumPopup(string label, Enum selected, params GUILayoutOption[] options) { return selected; }
        public static int Popup(int index, string[] displayed, params GUILayoutOption[] options) { return index; }
        public static string PasswordField(string value, params GUILayoutOption[] options) { return value; }
        public static string PasswordField(string label, string value, params GUILayoutOption[] options) { return value; }
        public static int IntSlider(int value, int left, int right, params GUILayoutOption[] options) { return value; }
        public static int IntSlider(string label, int value, int left, int right, params GUILayoutOption[] options) { return value; }
        public static float Slider(string label, float value, float left, float right, params GUILayoutOption[] options) { return value; }
        public static void SelectableLabel(string text, params GUILayoutOption[] options) { }
        public static void SelectableLabel(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static int Popup(string label, int index, string[] displayed, params GUILayoutOption[] options) { return index; }
        public static UnityEngine.Object ObjectField(UnityEngine.Object obj, Type type, bool allowSceneObjects, params GUILayoutOption[] options) { return null; }
        public static UnityEngine.Object ObjectField(string label, UnityEngine.Object obj, Type type, bool allowSceneObjects, params GUILayoutOption[] options) { return null; }
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) { return scrollPosition; }
        public static void EndScrollView() { }
        public static void BeginHorizontal(params GUILayoutOption[] options) { }
        public static void BeginHorizontal(GUIStyle style, params GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] options) { }
        public static void BeginVertical(GUIStyle style, params GUILayoutOption[] options) { }
        public static void EndVertical() { }
        public static void Separator() { }
    }

    public static class EditorStyles
    {
        public static GUIStyle boldLabel { get { return null; } }
        public static GUIStyle label { get { return null; } }
        public static GUIStyle miniLabel { get { return null; } }
        public static GUIStyle wordWrappedLabel { get { return null; } }
        public static GUIStyle helpBox { get { return null; } }
        public static GUIStyle textField { get { return null; } }
        public static GUIStyle foldout { get { return null; } }
        public static GUIStyle miniButton { get { return null; } }
        public static GUIStyle toolbarButton { get { return null; } }
        public static GUIStyle toolbar { get { return null; } }
        public static GUIStyle miniBoldLabel { get { return null; } }
        public static GUIStyle wordWrappedMiniLabel { get { return null; } }
        public static GUIStyle boldFont { get { return null; } }
        public static GUIStyle largeLabel { get { return null; } }
        public static GUIStyle toolbarTextField { get { return null; } }
        public static GUIStyle toolbarPopup { get { return null; } }
        public static GUIStyle toolbarDropDown { get { return null; } }
        public static GUIStyle centeredGreyMiniLabel { get { return null; } }
        public static GUIStyle miniButtonLeft { get { return null; } }
        public static GUIStyle miniButtonMid { get { return null; } }
        public static GUIStyle miniButtonRight { get { return null; } }
    }

    public enum ImportAssetOptions { Default, ForceUpdate, ForceSynchronousImport, ImportRecursive }
    public enum TextureImporterType { Default, Sprite, NormalMap, GUI }
    public enum SpriteImportMode { None, Single, Multiple, Polygon }
    public enum TextureImporterCompression { Uncompressed, Compressed, CompressedHQ, CompressedLQ }

    public struct SpriteMetaData
    {
        public string name;
        public UnityEngine.Rect rect;
        public int alignment;
        public UnityEngine.Vector2 pivot;
        public UnityEngine.Vector4 border;
    }

    public class AssetImporter : UnityEngine.Object
    {
        public string assetPath { get; set; }
        public void SaveAndReimport() { }
        public static AssetImporter GetAtPath(string path) { return null; }
    }

    public class TextureImporter : AssetImporter
    {
        public TextureImporterType textureType { get; set; }
        public SpriteImportMode spriteImportMode { get; set; }
        public SpriteMetaData[] spritesheet { get; set; }
        public bool mipmapEnabled { get; set; }
        public bool alphaIsTransparency { get; set; }
        public bool isReadable { get; set; }
        public UnityEngine.FilterMode filterMode { get; set; }
        public UnityEngine.TextureWrapMode wrapMode { get; set; }
        public int maxTextureSize { get; set; }
        public TextureImporterCompression textureCompression { get; set; }
        public UnityEngine.Vector4 spriteBorder { get; set; }
        public float spritePixelsPerUnit { get; set; }
        public SpriteMeshType spriteMeshType { get; set; }
        public TextureImporterSettings ReadTextureSettings(TextureImporterSettings dest) { return dest; }
        public void SetTextureSettings(TextureImporterSettings settings) { }
    }

    public class TextureImporterSettings
    {
        public UnityEngine.Vector4 spriteBorder { get; set; }
        public SpriteMeshType spriteMeshType { get; set; }
    }

    public enum SpriteMeshType { FullRect, Tight }

    public enum MessageType { None, Info, Warning, Error }

    public sealed class EditorGUIDisabledScope : System.IDisposable
    {
        public EditorGUIDisabledScope(bool disabled) { }
        public void Dispose() { }
    }

    public static class EditorGUI
    {
        public sealed class DisabledScope : System.IDisposable
        {
            public DisabledScope(bool disabled) { }
            public void Dispose() { }
        }

        public static int indentLevel { get; set; }
        public static void BeginChangeCheck() { }
        public static bool EndChangeCheck() { return false; }
        public static void BeginDisabledGroup(bool disabled) { }
        public static void EndDisabledGroup() { }
        public static void DrawRect(UnityEngine.Rect rect, UnityEngine.Color color) { }
    }

    public static class EditorPrefs
    {
        public static bool GetBool(string key, bool defaultValue) { return defaultValue; }
        public static void SetBool(string key, bool value) { }
        public static string GetString(string key, string defaultValue) { return defaultValue; }
        public static void SetString(string key, string value) { }
    }
}

namespace UnityEditor.Events
{
    public static class UnityEventTools
    {
        public static void AddPersistentListener(UnityEngine.Events.UnityEventBase e) { }
        public static void AddVoidPersistentListener(UnityEngine.Events.UnityEventBase e, UnityEngine.Events.UnityAction call) { }
        public static void AddStringPersistentListener(UnityEngine.Events.UnityEventBase e, UnityEngine.Events.UnityAction<string> call, string argument) { }
        public static void AddIntPersistentListener(UnityEngine.Events.UnityEventBase e, UnityEngine.Events.UnityAction<int> call, int argument) { }
        public static void AddFloatPersistentListener(UnityEngine.Events.UnityEventBase e, UnityEngine.Events.UnityAction<float> call, float argument) { }
        public static void AddBoolPersistentListener(UnityEngine.Events.UnityEventBase e, UnityEngine.Events.UnityAction<bool> call, bool argument) { }
        public static void RemovePersistentListener(UnityEngine.Events.UnityEventBase e, int index) { }
    }
}

namespace UnityEditor.SceneManagement
{
    public static class EditorSceneManager
    {
        public static bool MarkSceneDirty(UnityEngine.Scene scene) { return false; }
        public static bool MarkAllScenesDirty() { return false; }
        public static UnityEngine.Scene GetActiveScene() { return default(UnityEngine.Scene); }
        public static bool SaveOpenScenes() { return false; }
        public static bool SaveScene(UnityEngine.Scene scene) { return false; }
        public static bool SaveScene(UnityEngine.Scene scene, string dstScenePath) { return false; }
        public static bool SaveScene(UnityEngine.Scene scene, string dstScenePath, bool saveAsCopy) { return false; }
        public static UnityEngine.Scene OpenScene(string path) { return default(UnityEngine.Scene); }
        public static UnityEngine.Scene OpenScene(string path, OpenSceneMode mode) { return default(UnityEngine.Scene); }
        public static UnityEngine.Scene NewScene(NewSceneSetup setup) { return default(UnityEngine.Scene); }
        public static UnityEngine.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) { return default(UnityEngine.Scene); }
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() { return true; }
        public static bool CloseScene(UnityEngine.Scene scene, bool removeScene) { return false; }
    }

    public enum OpenSceneMode { Single, Additive, AdditiveWithoutLoading }
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }
}

namespace UnityEditor.Compilation
{
    public static class CompilationPipeline
    {
        public static void RequestScriptCompilation() { }
    }
}

namespace UnityEditor.Callbacks
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class DidReloadScripts : System.Attribute { public DidReloadScripts() { } public DidReloadScripts(int order) { } }
}
