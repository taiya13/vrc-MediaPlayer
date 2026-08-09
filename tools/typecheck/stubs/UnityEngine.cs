// Minimal UnityEngine stubs — API shape only, no behaviour.
// Purpose: let mcs type-check the project's C# without Unity installed.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public int GetInstanceID() { return 0; }
        public static void Destroy(Object o) { }
        public static void Destroy(Object o, float t) { }
        public static void DestroyImmediate(Object o) { }
        public static void DestroyImmediate(Object o, bool allow) { }
        public static void DontDestroyOnLoad(Object o) { }
        public static T Instantiate<T>(T original) where T : Object { return default(T); }
        public static Object Instantiate(Object original) { return null; }
        public static T FindObjectOfType<T>() where T : Object { return default(T); }
        public static T FindObjectOfType<T>(bool includeInactive) where T : Object { return default(T); }
        public static Object FindObjectOfType(Type t) { return null; }
        public static T[] FindObjectsOfType<T>() where T : Object { return new T[0]; }
        public static T[] FindObjectsOfType<T>(bool includeInactive) where T : Object { return new T[0]; }
        public static Object[] FindObjectsOfType(Type t) { return new Object[0]; }
        public static Object[] FindObjectsOfType(Type t, bool includeInactive) { return new Object[0]; }
        public override string ToString() { return base.ToString(); }
        public static implicit operator bool(Object exists) { return false; }
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !ReferenceEquals(a, b); }
        public override bool Equals(object other) { return base.Equals(other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }

    [Flags]
    public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }

    public class Component : Object
    {
        public Transform transform { get; set; }
        public GameObject gameObject { get; set; }
        public string tag { get; set; }
        public T GetComponent<T>() { return default(T); }
        public Component GetComponent(Type t) { return null; }
        public Component GetComponent(string t) { return null; }
        public bool TryGetComponent<T>(out T c) { c = default(T); return false; }
        public T GetComponentInChildren<T>() { return default(T); }
        public T GetComponentInChildren<T>(bool includeInactive) { return default(T); }
        public Component GetComponentInChildren(Type t, bool includeInactive) { return null; }
        public T GetComponentInParent<T>() { return default(T); }
        public T GetComponentInParent<T>(bool includeInactive) { return default(T); }
        public T[] GetComponents<T>() { return new T[0]; }
        public Component[] GetComponents(Type t) { return new Component[0]; }
        public T[] GetComponentsInChildren<T>() { return new T[0]; }
        public T[] GetComponentsInChildren<T>(bool includeInactive) { return new T[0]; }
        public T[] GetComponentsInParent<T>() { return new T[0]; }
        public T[] GetComponentsInParent<T>(bool includeInactive) { return new T[0]; }
        public void SendMessage(string method) { }
        public void SendMessage(string method, object value) { }
    }

    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }
        public Transform transform { get; set; }
        public string tag { get; set; }
        public int layer { get; set; }
        public bool activeSelf { get { return true; } }
        public bool activeInHierarchy { get { return true; } }
        public Scene scene { get; set; }
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component { return default(T); }
        public Component AddComponent(Type t) { return null; }
        public T GetComponent<T>() { return default(T); }
        public Component GetComponent(Type t) { return null; }
        public bool TryGetComponent<T>(out T c) { c = default(T); return false; }
        public T GetComponentInChildren<T>() { return default(T); }
        public T GetComponentInChildren<T>(bool includeInactive) { return default(T); }
        public T GetComponentInParent<T>() { return default(T); }
        public T GetComponentInParent<T>(bool includeInactive) { return default(T); }
        public T[] GetComponents<T>() { return new T[0]; }
        public Component[] GetComponents(Type t) { return new Component[0]; }
        public T[] GetComponentsInChildren<T>() { return new T[0]; }
        public T[] GetComponentsInChildren<T>(bool includeInactive) { return new T[0]; }
        public T[] GetComponentsInParent<T>() { return new T[0]; }
        public T[] GetComponentsInParent<T>(bool includeInactive) { return new T[0]; }
        public static GameObject Find(string name) { return null; }
        public static GameObject FindWithTag(string tag) { return null; }
        public static GameObject CreatePrimitive(PrimitiveType type) { return null; }
    }

    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }

    public struct Scene { public string name { get; set; } public bool IsValid() { return false; } public string path { get { return null; } } }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 localEulerAngles { get; set; }
        public Vector3 eulerAngles { get; set; }
        public Vector3 forward { get; set; }
        public Vector3 up { get; set; }
        public Vector3 right { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Transform parent { get; set; }
        public void SetAsFirstSibling() { }
        public void SetAsLastSibling() { }
        public Transform root { get { return null; } }
        public int childCount { get { return 0; } }
        public Transform GetChild(int index) { return null; }
        public Transform Find(string name) { return null; }
        public void SetParent(Transform p) { }
        public void SetParent(Transform p, bool worldPositionStays) { }
        public void SetSiblingIndex(int index) { }
        public int GetSiblingIndex() { return 0; }
        public void LookAt(Transform target) { }
        public void LookAt(Vector3 target) { }
        public IEnumerator GetEnumerator() { yield break; }
    }

    public class Behaviour : Component { public bool enabled { get; set; } public bool isActiveAndEnabled { get { return true; } } }

    public class MonoBehaviour : Behaviour
    {
        public bool useGUILayout { get; set; }
        public void Invoke(string method, float time) { }
        public void InvokeRepeating(string method, float time, float rate) { }
        public void CancelInvoke() { }
        public void CancelInvoke(string method) { }
        public bool IsInvoking(string method) { return false; }
        public Coroutine StartCoroutine(IEnumerator routine) { return null; }
        public Coroutine StartCoroutine(string method) { return null; }
        public void StopCoroutine(IEnumerator routine) { }
        public void StopCoroutine(Coroutine routine) { }
        public void StopAllCoroutines() { }
        public static void print(object message) { }
    }

    public sealed class Coroutine : Object { }
    public sealed class WaitForSeconds : YieldInstruction { public WaitForSeconds(float s) { } }
    public class YieldInstruction { }

    public class ScriptableObject : Object
    {
        public static ScriptableObject CreateInstance(Type type) { return null; }
        public static T CreateInstance<T>() where T : ScriptableObject { return default(T); }
    }

    public class Renderer : Component
    {
        public Material material { get; set; }
        public Material sharedMaterial { get; set; }
        public Material[] materials { get; set; }
        public Material[] sharedMaterials { get; set; }
        public bool enabled { get; set; }
        public Bounds bounds { get; set; }
    }

    public class MeshRenderer : Renderer { }
    public class SkinnedMeshRenderer : Renderer { }
    public class MeshFilter : Component { public Mesh mesh { get; set; } public Mesh sharedMesh { get; set; } }
    public class Mesh : Object { }
    public class Material : Object
    {
        public Material(Shader shader) { }
        public Material(Material source) { }
        public Shader shader { get; set; }
        public Color color { get; set; }
        public Texture mainTexture { get; set; }
        public void SetTexture(string name, Texture value) { }
        public void SetTexture(int id, Texture value) { }
        public Texture GetTexture(string name) { return null; }
        public void SetColor(string name, Color value) { }
        public void SetFloat(string name, float value) { }
        public float GetFloat(string name) { return 0f; }
        public bool HasProperty(string name) { return false; }
    }
    public class Shader : Object { public static Shader Find(string name) { return null; } }
    public class Texture : Object { public int width { get; set; } public int height { get; set; } }
    public class Texture2D : Texture
    {
        public Texture2D(int w, int h) { }
        public Texture2D(int w, int h, TextureFormat format, bool mipChain) { }
        public Color GetPixel(int x, int y) { return default(Color); }
        public void SetPixel(int x, int y, Color c) { }
        public void SetPixels32(Color32[] colors) { }
        public void SetPixels32(int x, int y, int w, int h, Color32[] colors) { }
        public Color32[] GetPixels32() { return new Color32[0]; }
        public bool LoadImage(byte[] data) { return false; }
        public bool LoadImage(byte[] data, bool markNonReadable) { return false; }
        public byte[] EncodeToPNG() { return new byte[0]; }
        public byte[] EncodeToJPG() { return new byte[0]; }
        public void Apply() { }

        public static Texture2D blackTexture { get { return null; } }
        public static Texture2D whiteTexture { get { return null; } }
        public static Texture2D grayTexture { get { return null; } }
    }
    public class RenderTexture : Texture { public RenderTexture(int w, int h, int depth) { } }
    public class Collider : Component { public bool enabled { get; set; } public bool isTrigger { get; set; } }
    public class BoxCollider : Collider
    {
        public Vector3 size { get; set; }
        public Vector3 center { get; set; }
    }
    public class MeshCollider : Collider { }

    public class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public bool playOnAwake { get; set; }
        public bool loop { get; set; }
        public bool mute { get; set; }
        public bool isPlaying { get { return false; } }
        public float volume { get; set; }
        public float pitch { get; set; }
        public float time { get; set; }
        public float spatialBlend { get; set; }
        public float minDistance { get; set; }
        public float maxDistance { get; set; }
        public float dopplerLevel { get; set; }
        public float spread { get; set; }
        public AudioRolloffMode rolloffMode { get; set; }
        public AudioMixerGroupStub outputAudioMixerGroup { get; set; }
        public void Play() { }
        public void Stop() { }
        public void Pause() { }
        public void UnPause() { }
        public void PlayOneShot(AudioClip clip) { }
    }
    public class AudioMixerGroupStub : Object { }
    public enum AudioRolloffMode { Logarithmic, Linear, Custom }
    public class AudioClip : Object
    {
        public float length { get { return 0f; } }
        public int samples { get { return 0; } }
        public int channels { get { return 0; } }
        public int frequency { get { return 0; } }
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) { return null; }
        public bool SetData(float[] data, int offsetSamples) { return false; }
        public bool GetData(float[] data, int offsetSamples) { return false; }
    }
    public class AudioListener : Behaviour { }

    public class Camera : Behaviour
    {
        public static Camera main { get { return null; } }
        public RenderTexture targetTexture { get; set; }
    }

    public class Light : Behaviour { }
    public class Canvas : Behaviour { public RenderMode renderMode { get; set; } }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void Log(object message, Object context) { }
        public static void LogWarning(object message) { }
        public static void LogWarning(object message, Object context) { }
        public static void LogError(object message) { }
        public static void LogError(object message, Object context) { }
        public static void LogException(Exception e) { }
        public static void LogException(Exception e, Object context) { }
        public static void Assert(bool condition) { }
        public static void DrawLine(Vector3 a, Vector3 b) { }
        public static bool isDebugBuild { get { return false; } }
    }

    public static class Application
    {
        public static bool isPlaying { get { return false; } }
        public static bool isEditor { get { return false; } }
        public static string dataPath { get { return ""; } }
        public static string persistentDataPath { get { return ""; } }
        public static string version { get { return ""; } }
        public static RuntimePlatform platform { get { return RuntimePlatform.WindowsPlayer; } }
        public static void OpenURL(string url) { }
    }
    public enum RuntimePlatform { WindowsPlayer, OSXPlayer, LinuxPlayer, Android, WindowsEditor, OSXEditor, LinuxEditor }

    public static class Time
    {
        public static float time { get { return 0f; } }
        public static float deltaTime { get { return 0f; } }
        public static float unscaledTime { get { return 0f; } }
        public static float unscaledDeltaTime { get { return 0f; } }
        public static float fixedDeltaTime { get { return 0f; } }
        public static float realtimeSinceStartup { get { return 0f; } }
        public static float timeScale { get; set; }
        public static int frameCount { get { return 0; } }
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public static float Log(float f) { return (float)System.Math.Log(f); }
        public static float Log10(float f) { return (float)System.Math.Log10(f); }
        public const float Infinity = float.PositiveInfinity;
        public const float Epsilon = 1.401298E-45f;
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public static float Abs(float f) { return 0f; }
        public static int Abs(int f) { return 0; }
        public static float Clamp(float v, float min, float max) { return 0f; }
        public static int Clamp(int v, int min, int max) { return 0; }
        public static float Clamp01(float v) { return 0f; }
        public static float Min(float a, float b) { return 0f; }
        public static int Min(int a, int b) { return 0; }
        public static float Max(float a, float b) { return 0f; }
        public static int Max(int a, int b) { return 0; }
        public static float Lerp(float a, float b, float t) { return 0f; }
        public static float MoveTowards(float a, float b, float d) { return 0f; }
        public static int RoundToInt(float f) { return 0; }
        public static int FloorToInt(float f) { return 0; }
        public static int CeilToInt(float f) { return 0; }
        public static float Round(float f) { return 0f; }
        public static float Floor(float f) { return 0f; }
        public static float Ceil(float f) { return 0f; }
        public static float Sqrt(float f) { return 0f; }
        public static float Pow(float f, float p) { return 0f; }
        public static float Sin(float f) { return 0f; }
        public static float Cos(float f) { return 0f; }
        public static float Repeat(float t, float length) { return 0f; }
        public static bool Approximately(float a, float b) { return false; }
    }

    public static class Random
    {
        public static float value { get { return 0f; } }
        public static int Range(int min, int max) { return 0; }
        public static float Range(float min, float max) { return 0f; }
        public static void InitState(int seed) { }
    }

    public static class Screen
    {
        public static int width { get { return 0; } }
        public static int height { get { return 0; } }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return default(Vector2); } }
        public static Vector2 one { get { return default(Vector2); } }
        public static Vector2 up { get { return default(Vector2); } }
        public float magnitude { get { return 0f; } }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return default(Vector2); }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return default(Vector2); }
        public static Vector2 operator *(Vector2 a, float b) { return default(Vector2); }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y) { this.x = x; this.y = y; this.z = 0f; }
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return default(Vector3); } }
        public static Vector3 one { get { return default(Vector3); } }
        public static Vector3 up { get { return default(Vector3); } }
        public static Vector3 down { get { return default(Vector3); } }
        public static Vector3 forward { get { return default(Vector3); } }
        public static Vector3 back { get { return default(Vector3); } }
        public static Vector3 left { get { return default(Vector3); } }
        public static Vector3 right { get { return default(Vector3); } }
        public float magnitude { get { return 0f; } }
        public float sqrMagnitude { get { return 0f; } }
        public Vector3 normalized { get { return default(Vector3); } }
        public static float Distance(Vector3 a, Vector3 b) { return 0f; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return default(Vector3); }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return default(Vector3); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return default(Vector3); }
        public static Vector3 operator *(Vector3 a, float b) { return default(Vector3); }
        public static Vector3 operator *(float a, Vector3 b) { return default(Vector3); }
        public static Vector3 operator /(Vector3 a, float b) { return default(Vector3); }
    }

    public struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) : this() { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity { get { return default(Quaternion); } }
        public static Quaternion Euler(float x, float y, float z) { return default(Quaternion); }
        public static Quaternion Euler(Vector3 e) { return default(Quaternion); }
        public Vector3 eulerAngles { get; set; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return default(Color); } }
        public static Color black { get { return default(Color); } }
        public static Color gray { get { return default(Color); } }
        public static Color red { get { return default(Color); } }
        public static Color green { get { return default(Color); } }
        public static Color blue { get { return default(Color); } }
        public static Color yellow { get { return default(Color); } }
        public static Color cyan { get { return default(Color); } }
        public static bool operator ==(Color a, Color b) { return false; }
        public static bool operator !=(Color a, Color b) { return true; }
        public override bool Equals(object other) { return false; }
        public override int GetHashCode() { return 0; }
        public static Color Lerp(Color a, Color b, float t) { return default(Color); }
        public static Color clear { get { return default(Color); } }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }

        // Unity と同じく Color と行き来できる。
        public static implicit operator Color32(Color c) { return new Color32(0, 0, 0, 0); }
        public static implicit operator Color(Color32 c) { return default(Color); }
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) : this() { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public float xMin { get { return 0f; } }
        public float yMin { get { return 0f; } }
        public float xMax { get { return 0f; } }
        public float yMax { get { return 0f; } }
        public Vector2 position { get; set; }
        public Vector2 size { get; set; }
        public Vector2 center { get; set; }
        public bool Contains(Vector2 p) { return false; }
    }

    public struct Bounds { public Vector3 center; public Vector3 size; public Vector3 extents { get { return default(Vector3); } } }

    // ───────── Attributes ─────────
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)] public sealed class SerializeReference : Attribute { }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)] public sealed class SerializableAttribute2 : Attribute { }
    public class PropertyAttribute : Attribute { }
    public sealed class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string header) { } }
    public sealed class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string tooltip) { } }
    public sealed class SpaceAttribute : PropertyAttribute { public SpaceAttribute() { } public SpaceAttribute(float height) { } }
    public sealed class RangeAttribute : PropertyAttribute { public RangeAttribute(float min, float max) { } }
    public sealed class MinAttribute : PropertyAttribute { public MinAttribute(float min) { } }
    public sealed class TextAreaAttribute : PropertyAttribute { public TextAreaAttribute() { } public TextAreaAttribute(int minLines, int maxLines) { } }
    public sealed class MultilineAttribute : PropertyAttribute { public MultilineAttribute() { } public MultilineAttribute(int lines) { } }
    public sealed class HideInInspector : Attribute { }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public sealed class RequireComponent : Attribute { public RequireComponent(Type a) { } public RequireComponent(Type a, Type b) { } public RequireComponent(Type a, Type b, Type c) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string menuName) { } public AddComponentMenu(string menuName, int order) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class ExecuteInEditMode : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class ExecuteAlways : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string fileName { get; set; } public string menuName { get; set; } public int order { get; set; } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class HelpURLAttribute : Attribute { public HelpURLAttribute(string url) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class SelectionBaseAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class ContextMenu : Attribute { public ContextMenu(string name) { } public ContextMenu(string name, bool isValidateFunction) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate)] public sealed class PreserveAttribute : Attribute { }

    // ───────── IMGUI ─────────
    public class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public bool richText { get; set; }
        public bool wordWrap { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public GUIStyleState normal { get; set; }
        public GUIStyleState hover { get; set; }
        public GUIStyleState active { get; set; }
        public RectOffset padding { get; set; }
        public RectOffset margin { get; set; }
        public float fixedWidth { get; set; }
        public float fixedHeight { get; set; }
        public Vector2 CalcSize(GUIContent content) { return default(Vector2); }
    }
    public class GUIStyleState { public Color textColor { get; set; } public Texture2D background { get; set; } }
    public class RectOffset { public RectOffset() { } public RectOffset(int l, int r, int t, int b) { } public int left { get; set; } public int right { get; set; } public int top { get; set; } public int bottom { get; set; } }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }

    public class GUIContent
    {
        public GUIContent() { }
        public GUIContent(string text) { }
        public GUIContent(string text, string tooltip) { }
        public GUIContent(Texture image) { }
        public string text { get; set; }
        public string tooltip { get; set; }
        public static GUIContent none { get { return null; } }
    }

    public class GUISkin : ScriptableObject
    {
        public GUIStyle box { get; set; }
        public GUIStyle label { get; set; }
        public GUIStyle button { get; set; }
        public GUIStyle toggle { get; set; }
        public GUIStyle textField { get; set; }
        public GUIStyle textArea { get; set; }
        public GUIStyle window { get; set; }
        public GUIStyle scrollView { get; set; }
        public GUIStyle horizontalSlider { get; set; }
        public GUIStyle GetStyle(string name) { return null; }
    }

    public class GUILayoutOption { }

    public static class GUILayout
    {
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static void Label(GUIContent content, params GUILayoutOption[] options) { }
        public static void Label(GUIContent content, GUIStyle style, params GUILayoutOption[] options) { }
        public static void Box(string text, params GUILayoutOption[] options) { }
        public static bool Button(string text, params GUILayoutOption[] options) { return false; }
        public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) { return false; }
        public static bool Button(GUIContent content, params GUILayoutOption[] options) { return false; }
        public static bool RepeatButton(string text, params GUILayoutOption[] options) { return false; }
        public static bool Toggle(bool value, string text, params GUILayoutOption[] options) { return false; }
        public static bool Toggle(bool value, string text, GUIStyle style, params GUILayoutOption[] options) { return false; }
        public static bool Toggle(bool value, GUIContent content, GUIStyle style, params GUILayoutOption[] options) { return false; }
        public static string TextField(string text, params GUILayoutOption[] options) { return null; }
        public static string TextArea(string text, params GUILayoutOption[] options) { return null; }
        public static float HorizontalSlider(float value, float min, float max, params GUILayoutOption[] options) { return 0f; }
        public static int SelectionGrid(int selected, string[] texts, int xCount, params GUILayoutOption[] options) { return 0; }
        public static int Toolbar(int selected, string[] texts, params GUILayoutOption[] options) { return 0; }
        public static void Space(float pixels) { }
        public static void FlexibleSpace() { }
        public static void BeginHorizontal(params GUILayoutOption[] options) { }
        public static void BeginHorizontal(GUIStyle style, params GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] options) { }
        public static void BeginVertical(GUIStyle style, params GUILayoutOption[] options) { }
        public static void EndVertical() { }
        public static void BeginArea(Rect screenRect) { }
        public static void BeginArea(Rect screenRect, GUIStyle style) { }
        public static void EndArea() { }
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) { return default(Vector2); }
        public static Vector2 BeginScrollView(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] options) { return default(Vector2); }
        public static void EndScrollView() { }
        public static GUILayoutOption Width(float width) { return null; }
        public static GUILayoutOption MinWidth(float width) { return null; }
        public static GUILayoutOption MaxWidth(float width) { return null; }
        public static GUILayoutOption Height(float height) { return null; }
        public static GUILayoutOption MinHeight(float height) { return null; }
        public static GUILayoutOption MaxHeight(float height) { return null; }
        public static GUILayoutOption ExpandWidth(bool expand) { return null; }
        public static GUILayoutOption ExpandHeight(bool expand) { return null; }
    }

    public static class GUILayoutUtility
    {
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight) { return default(Rect); }
        public static Rect GetRect(float width, float height, params GUILayoutOption[] options) { return default(Rect); }
        public static Rect GetRect(GUIContent content, GUIStyle style, params GUILayoutOption[] options) { return default(Rect); }
        public static Rect GetLastRect() { return default(Rect); }
    }

    public static class GUI
    {
        public static void FocusControl(string name) { }
        public static string GetNameOfFocusedControl() { return ""; }

        public static GUISkin skin { get; set; }
        public static bool enabled { get; set; }
        public static bool changed { get; set; }
        public static Color color { get; set; }
        public static Color backgroundColor { get; set; }
        public static Color contentColor { get; set; }
        public static int depth { get; set; }
        public static void Label(Rect r, string text) { }
        public static void Label(Rect r, string text, GUIStyle style) { }
        public static void Box(Rect r, string text) { }
        public static void Box(Rect r, GUIContent content) { }
        public static void Box(Rect r, GUIContent content, GUIStyle style) { }
        public static void Label(Rect r, GUIContent content) { }
        public static bool Button(Rect r, string text) { return false; }
        public static bool Button(Rect r, GUIContent content) { return false; }
        public static bool Button(Rect r, GUIContent content, GUIStyle style) { return false; }
        public static bool Button(Rect r, string text, GUIStyle style) { return false; }
        public static void DrawTexture(Rect r, Texture image) { }
        public static void DrawTexture(Rect r, Texture image, ScaleMode scaleMode) { }
        public static void DrawTexture(Rect r, Texture image, ScaleMode scaleMode, bool alphaBlend) { }
    }

    public enum ScaleMode { StretchToFill, ScaleAndCrop, ScaleToFit }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp, Mirror, MirrorOnce }
    public enum TextureFormat { Alpha8, RGB24, RGBA32, ARGB32, DXT1, DXT5 }
    public enum SpriteAlignment { Center, TopLeft, TopCenter, TopRight, LeftCenter, RightCenter, BottomLeft, BottomCenter, BottomRight, Custom }
}

namespace UnityEngine.Events
{
    public class UnityEventBase
    {
        public int GetPersistentEventCount() { return 0; }
        public string GetPersistentMethodName(int index) { return null; }
        public UnityEngine.Object GetPersistentTarget(int index) { return null; }
    }
    public class UnityEvent : UnityEventBase
    {
        public void Invoke() { }
        public void AddListener(UnityEngine.Events.UnityAction call) { }
        public void RemoveListener(UnityEngine.Events.UnityAction call) { }
    }
    public class UnityEvent<T0> : UnityEventBase
    {
        public void Invoke(T0 arg0) { }
        public void AddListener(UnityAction<T0> call) { }
        public void RemoveListener(UnityAction<T0> call) { }
    }
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);
}

namespace UnityEngine.SceneManagement
{
    public static class SceneManager
    {
        public static UnityEngine.Scene GetActiveScene() { return default(UnityEngine.Scene); }
        public static int sceneCount { get { return 0; } }
        public static UnityEngine.Scene GetSceneAt(int index) { return default(UnityEngine.Scene); }
    }
}

namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Behaviour
    {
        public UnityEngine.Color color { get; set; }
        public bool raycastTarget { get; set; }
        public UnityEngine.RectTransform rectTransform { get { return null; } }
    }
    public class Text : Graphic
    {
        public string text { get; set; }
        public int fontSize { get; set; }
        public UnityEngine.Font font { get; set; }
        public UnityEngine.FontStyle fontStyle { get; set; }
        public UnityEngine.TextAnchor alignment { get; set; }
        public float lineSpacing { get; set; }
        public bool supportRichText { get; set; }
        public bool resizeTextForBestFit { get; set; }
        public int resizeTextMinSize { get; set; }
        public int resizeTextMaxSize { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
    }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }
    public class CanvasScaler : UnityEngine.Behaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public ScaleMode uiScaleMode { get; set; }
        public float dynamicPixelsPerUnit { get; set; }
        public float referencePixelsPerUnit { get; set; }
    }
    public class GraphicRaycaster : UnityEngine.Behaviour { public bool ignoreReversedGraphics { get; set; } }
    public class LayoutElement : UnityEngine.Behaviour { public float preferredHeight { get; set; } }
    public class Image : Graphic
    {
        public enum Type { Simple, Sliced, Tiled, Filled }
        public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
        public UnityEngine.Sprite sprite { get; set; }
        public Type type { get; set; }
        public FillMethod fillMethod { get; set; }
        public int fillOrigin { get; set; }
        public float fillAmount { get; set; }
        public bool preserveAspect { get; set; }
        public float pixelsPerUnitMultiplier { get; set; }
    }
    public struct ColorBlock
    {
        public UnityEngine.Color normalColor { get; set; }
        public UnityEngine.Color highlightedColor { get; set; }
        public UnityEngine.Color pressedColor { get; set; }
        public UnityEngine.Color selectedColor { get; set; }
        public UnityEngine.Color disabledColor { get; set; }
        public float colorMultiplier { get; set; }
        public float fadeDuration { get; set; }
        public static ColorBlock defaultColorBlock { get { return default(ColorBlock); } }
    }
    public class Selectable : UnityEngine.Behaviour
    {
        public enum Transition { None, ColorTint, SpriteSwap, Animation }
        public bool interactable { get; set; }
        public Transition transition { get; set; }
        public ColorBlock colors { get; set; }
        public Graphic targetGraphic { get; set; }
        public void Select() { }
    }
    public class Button : Selectable { public UnityEngine.Events.UnityEvent onClick { get; set; } }
    public class InputField : Selectable
    {
        public string text { get; set; }
        public Text textComponent { get; set; }
        public Text placeholder { get; set; }
        public int characterLimit { get; set; }
        public InputFieldSubmitEvent onEndEdit = new InputFieldSubmitEvent();
        public InputFieldChangeEvent onValueChanged = new InputFieldChangeEvent();
        public void ActivateInputField() { }
        public void DeactivateInputField() { }
    }

    public class InputFieldSubmitEvent : UnityEngine.Events.UnityEvent<string> { }
    public class InputFieldChangeEvent : UnityEngine.Events.UnityEvent<string> { }

    public class Slider : Selectable
    {
        public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
        public float value { get; set; }
        public float minValue { get; set; }
        public float maxValue { get; set; }
        public bool wholeNumbers { get; set; }
        public Direction direction { get; set; }
        public UnityEngine.RectTransform fillRect { get; set; }
        public UnityEngine.RectTransform handleRect { get; set; }
        public UnityEngine.Events.UnityEvent<float> onValueChanged { get; set; }
    }
    public class Toggle : Selectable { public bool isOn { get; set; } }
    public class Scrollbar : Selectable
    {
        public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
        public float value { get; set; }
        public float size { get; set; }
        public int numberOfSteps { get; set; }
        public Direction direction { get; set; }
        public UnityEngine.RectTransform handleRect { get; set; }
        public UnityEngine.Events.UnityEvent<float> onValueChanged { get; set; }
    }
    public class ScrollRect : UnityEngine.Behaviour
    {
        public enum MovementType { Unrestricted, Elastic, Clamped }
        public UnityEngine.Vector2 normalizedPosition { get; set; }
        public float verticalNormalizedPosition { get; set; }
        public float horizontalNormalizedPosition { get; set; }
        public UnityEngine.RectTransform content { get; set; }
        public UnityEngine.RectTransform viewport { get; set; }
        public bool horizontal { get; set; }
        public bool vertical { get; set; }
        public MovementType movementType { get; set; }
        public float elasticity { get; set; }
        public bool inertia { get; set; }
        public float decelerationRate { get; set; }
        public float scrollSensitivity { get; set; }
        public Scrollbar verticalScrollbar { get; set; }
        public Scrollbar horizontalScrollbar { get; set; }
        public UnityEngine.Vector2 velocity { get; set; }
        public UnityEngine.Events.UnityEvent<UnityEngine.Vector2> onValueChanged { get; set; }
    }
    public class Mask : UnityEngine.Behaviour { public bool showMaskGraphic { get; set; } }
    public class RectMask2D : UnityEngine.Behaviour { }
    public class RawImage : Graphic { public UnityEngine.Texture texture { get; set; } }
}

namespace UnityEngine
{
    public class Sprite : Object { }
    public class Font : Object { }
    public static class Resources
    {
        public static T Load<T>(string path) where T : Object { return default(T); }
        public static Object Load(string path) { return null; }
        public static T GetBuiltinResource<T>(string path) where T : Object { return default(T); }
        public static Object GetBuiltinResource(Type type, string path) { return null; }
        public static T[] FindObjectsOfTypeAll<T>() where T : Object { return new T[0]; }
        public static Object[] FindObjectsOfTypeAll(Type type) { return new Object[0]; }
    }
    public class TextAsset : Object { public string text { get { return null; } } }

    public static class JsonUtility
    {
        public static T FromJson<T>(string json) { return default(T); }
        public static object FromJson(string json, Type type) { return null; }
        public static void FromJsonOverwrite(string json, object target) { }
        public static string ToJson(object obj) { return ""; }
        public static string ToJson(object obj, bool prettyPrint) { return ""; }
    }
    public class RectTransform : Transform
    {
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
    }
}

namespace UnityEngine.Video
{
    public class VideoPlayer : UnityEngine.Behaviour
    {
        public string url { get; set; }
        public bool isPlaying { get { return false; } }
        public bool isPrepared { get { return false; } }
        public bool isLooping { get; set; }
        public double time { get; set; }
        public double length { get { return 0d; } }
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void Prepare() { }

        public VideoRenderMode renderMode { get; set; }
        public UnityEngine.Renderer targetMaterialRenderer { get; set; }
        public string targetMaterialProperty { get; set; }
        public VideoAudioOutputMode audioOutputMode { get; set; }
        public ushort audioTrackCount { get { return 1; } }
        public void SetTargetAudioSource(ushort track, UnityEngine.AudioSource source) { }
        public UnityEngine.AudioSource GetTargetAudioSource(ushort track) { return null; }
        public void EnableAudioTrack(ushort track, bool enabled) { }
    }

    public enum VideoRenderMode
    {
        CameraFarPlane, CameraNearPlane, RenderTexture, MaterialOverride, APIOnly
    }

    public enum VideoAudioOutputMode { None, AudioSource, Direct, APIOnly }
}

namespace UnityEngine.Serialization
{
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class FormerlySerializedAsAttribute : System.Attribute { public FormerlySerializedAsAttribute(string oldName) { } }
}

namespace UnityEngine.Networking
{
    public class DownloadHandler : System.IDisposable
    {
        public string text { get { return ""; } }
        public byte[] data { get { return new byte[0]; } }
        public void Dispose() { }
    }

    public class UnityWebRequestAsyncOperation
    {
        public bool isDone { get { return true; } }
        public float progress { get { return 1f; } }
    }

    public class UnityWebRequest : System.IDisposable
    {
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }

        public string url { get; set; }
        public string error { get { return ""; } }
        public long responseCode { get { return 200; } }
        public int timeout { get; set; }
        public Result result { get { return Result.Success; } }
        public DownloadHandler downloadHandler { get; set; }

        public bool isDone { get { return true; } }

        public UnityWebRequestAsyncOperation SendWebRequest() { return new UnityWebRequestAsyncOperation(); }
        public void Abort() { }
        public void Dispose() { }

        public static UnityWebRequest Get(string uri) { return new UnityWebRequest(); }
        public static UnityWebRequest Post(string uri, string body) { return new UnityWebRequest(); }
        public static string EscapeURL(string s) { return s; }
        public static string UnEscapeURL(string s) { return s; }
    }

    public class UnityWebRequestTexture
    {
        public static UnityWebRequest GetTexture(string uri) { return new UnityWebRequest(); }
    }

    public class DownloadHandlerTexture : DownloadHandler
    {
        public Texture2D texture { get { return null; } }
        public static Texture2D GetContent(UnityWebRequest request) { return null; }
    }
}

namespace UnityEngine.EventSystems
{
    public class EventSystem : UnityEngine.Behaviour { }
    public class StandaloneInputModule : UnityEngine.Behaviour { }
}
