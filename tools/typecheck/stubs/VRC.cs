// Minimal VRChat SDK3 + UdonSharp stubs — API shape only.
// Mirrors the members this project touches; NOT the full SDK.
using System;
using UnityEngine;

namespace VRC.SDKBase
{
    [Serializable]
    public class VRCUrl
    {
        public VRCUrl(string url) { }
        public static VRCUrl Empty { get { return null; } }
        public string Get() { return null; }
    }

    public static class Networking
    {
        public static VRCPlayerApi LocalPlayer { get { return null; } }
        public static bool IsOwner(GameObject obj) { return false; }
        public static bool IsOwner(VRCPlayerApi player, GameObject obj) { return false; }
        public static void SetOwner(VRCPlayerApi player, GameObject obj) { }
        public static bool IsMaster { get { return false; } }
        public static double GetServerTimeInSeconds() { return 0d; }
    }

    public class VRCPlayerApi
    {
        public int playerId;
        public string displayName;
        public bool isLocal;
        public bool isMaster;
        public bool IsValid() { return false; }
        public static VRCPlayerApi GetPlayerById(int id) { return null; }
    }

    public class VRC_SceneDescriptor : MonoBehaviour { }

    public enum VRC_EventHandler_VrcBroadcastType { Always, Master, Owner, Local }
}

namespace VRC.SDK3.Components
{
    public class VRCSceneDescriptor : VRC.SDKBase.VRC_SceneDescriptor { }
    public class VRCObjectSync : MonoBehaviour { }
    public class VRCPickup : MonoBehaviour { }
}

namespace VRC.SDK3.Components.Video
{
    public enum VideoError
    {
        Unknown = 0,
        InvalidURL = 1,
        AccessDenied = 2,
        PlayerError = 3,
        RateLimited = 4,
    }
}

namespace VRC.SDK3.Video.Components.Base
{
    public abstract class BaseVRCVideoPlayer : MonoBehaviour
    {
        public abstract bool IsPlaying { get; }
        public abstract bool IsReady { get; }
        public abstract bool Loop { get; set; }
        public abstract void LoadURL(VRC.SDKBase.VRCUrl url);
        public abstract void PlayURL(VRC.SDKBase.VRCUrl url);
        public abstract void Play();
        public abstract void Pause();
        public abstract void Stop();
        public abstract void SetTime(float value);
        public abstract float GetTime();
        public abstract float GetDuration();
        public bool EnableAutomaticResync { get; set; }
    }
}

namespace VRC.SDK3.Video.Components
{
    public class VRCUnityVideoPlayer : VRC.SDK3.Video.Components.Base.BaseVRCVideoPlayer
    {
        public override bool IsPlaying { get { return false; } }
        public override bool IsReady { get { return false; } }
        public override bool Loop { get; set; }
        public override void LoadURL(VRC.SDKBase.VRCUrl url) { }
        public override void PlayURL(VRC.SDKBase.VRCUrl url) { }
        public override void Play() { }
        public override void Pause() { }
        public override void Stop() { }
        public override void SetTime(float value) { }
        public override float GetTime() { return 0f; }
        public override float GetDuration() { return 0f; }
    }
}

namespace VRC.SDK3.Video.Components.AVPro
{
    public class VRCAVProVideoPlayer : VRC.SDK3.Video.Components.Base.BaseVRCVideoPlayer
    {
        public override bool IsPlaying { get { return false; } }
        public override bool IsReady { get { return false; } }
        public override bool Loop { get; set; }
        public override void LoadURL(VRC.SDKBase.VRCUrl url) { }
        public override void PlayURL(VRC.SDKBase.VRCUrl url) { }
        public override void Play() { }
        public override void Pause() { }
        public override void Stop() { }
        public override void SetTime(float value) { }
        public override float GetTime() { return 0f; }
        public override float GetDuration() { return 0f; }
        public bool UseLowLatency { get; set; }
        public int MaximumResolution { get; set; }
    }

    public class VRCAVProVideoScreen : MonoBehaviour { public VRCAVProVideoPlayer VideoPlayer; public bool UseSharedMaterial; public int MaterialIndex; public string TextureProperty; }
    public class VRCAVProVideoSpeaker : MonoBehaviour { public VRCAVProVideoPlayer VideoPlayer; public enum ChannelMode { StereoLeft, StereoRight, Mono } public ChannelMode Mode; }
}

namespace VRC.SDK3.Data
{
    public enum TokenType { Null, Boolean, SByte, Byte, Short, UShort, Int, UInt, Long, ULong, Float, Double, String, Reference, DataList, DataDictionary, Error }

    public struct DataToken
    {
        public DataToken(string value) { }
        public DataToken(int value) { }
        public DataToken(float value) { }
        public DataToken(double value) { }
        public DataToken(bool value) { }
        public DataToken(object value) { }
        public TokenType TokenType { get { return TokenType.Null; } }
        public string String { get { return null; } }
        public int Int { get { return 0; } }
        public float Float { get { return 0f; } }
        public double Double { get { return 0d; } }
        public bool Boolean { get { return false; } }
        public object Reference { get { return null; } }
        public DataList DataList { get { return null; } }
        public DataDictionary DataDictionary { get { return null; } }
        public bool IsNull { get { return true; } }
        public static implicit operator DataToken(string value) { return default(DataToken); }
        public static implicit operator DataToken(int value) { return default(DataToken); }
        public static implicit operator DataToken(bool value) { return default(DataToken); }
        public static implicit operator DataToken(float value) { return default(DataToken); }
    }

    public class DataList
    {
        public DataList() { }
        public DataList(DataToken[] tokens) { }
        public int Count { get { return 0; } }
        public DataToken this[int index] { get { return default(DataToken); } set { } }
        public void Add(DataToken token) { }
        public void Clear() { }
        public bool Contains(DataToken token) { return false; }
        public int IndexOf(DataToken token) { return -1; }
        public void RemoveAt(int index) { }
        public bool TryGetValue(int index, out DataToken value) { value = default(DataToken); return false; }
    }

    public class DataDictionary
    {
        public DataDictionary() { }
        public int Count { get { return 0; } }
        public DataToken this[DataToken key] { get { return default(DataToken); } set { } }
        public void Add(DataToken key, DataToken value) { }
        public void Clear() { }
        public bool ContainsKey(DataToken key) { return false; }
        public bool Remove(DataToken key) { return false; }
        public bool TryGetValue(DataToken key, out DataToken value) { value = default(DataToken); return false; }
        public bool TryGetValue(DataToken key, TokenType type, out DataToken value) { value = default(DataToken); return false; }
        public bool SetValue(DataToken key, DataToken value) { return false; }
        public DataList GetKeys() { return null; }
        public DataList GetValues() { return null; }
    }
}

namespace VRC.Udon
{
    public class UdonBehaviour : MonoBehaviour
    {
        public object GetProgramVariable(string symbolName) { return null; }
        public void SetProgramVariable(string symbolName, object value) { }
        public void SendCustomEvent(string eventName) { }
        public void SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget target, string eventName) { }
    }
}

namespace VRC.Udon.Common.Interfaces
{
    public enum NetworkEventTarget { All, Owner }
}

namespace UdonSharp
{
    public enum BehaviourSyncMode { Any, Continuous, Manual, NoVariableSync, None }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class UdonBehaviourSyncModeAttribute : Attribute
    {
        public UdonBehaviourSyncModeAttribute(BehaviourSyncMode mode) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RecursiveMethodAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FieldChangeCallbackAttribute : Attribute { public FieldChangeCallbackAttribute(string targetPropertyName) { } }

    public abstract class UdonSharpBehaviour : MonoBehaviour
    {
        public virtual void Interact() { }
        public virtual void OnPlayerJoined(VRC.SDKBase.VRCPlayerApi player) { }
        public virtual void OnPlayerLeft(VRC.SDKBase.VRCPlayerApi player) { }
        public virtual void OnDeserialization() { }
        public virtual void OnPreSerialization() { }
        public virtual void OnVideoReady() { }
        public virtual void OnVideoStart() { }
        public virtual void OnVideoEnd() { }
        public virtual void OnVideoLoop() { }
        public virtual void OnVideoPause() { }
        public virtual void OnVideoPlay() { }
        public virtual void OnVideoError(VRC.SDK3.Components.Video.VideoError videoError) { }
        public void SendCustomEvent(string eventName) { }
        public void SendCustomEventDelayedSeconds(string eventName, float delaySeconds) { }
        public void SendCustomEventDelayedFrames(string eventName, int delayFrames) { }
        public void SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget target, string eventName) { }
        public void RequestSerialization() { }
        public void DisableInteractive() { }
    }
}

namespace UdonSharp.Compiler
{
    public static class UdonSharpCompilerV1 { public static void CompileSync() { } }
}

namespace UdonSharpEditor
{
    public class UdonSharpProgramAsset : ScriptableObject { }
    public static class UdonSharpEditorUtility
    {
        public static VRC.Udon.UdonBehaviour GetBackingUdonBehaviour(UdonSharp.UdonSharpBehaviour behaviour) { return null; }
        public static UdonSharp.UdonSharpBehaviour GetProxyBehaviour(VRC.Udon.UdonBehaviour behaviour) { return null; }
        public static void CopyProxyToUdon(UdonSharp.UdonSharpBehaviour proxy) { }
        public static void CopyUdonToProxy(UdonSharp.UdonSharpBehaviour proxy) { }
    }
    public static class UdonSharpUndo
    {
        public static T AddComponent<T>(GameObject gameObject) where T : UdonSharp.UdonSharpBehaviour { return default(T); }
        public static UdonSharp.UdonSharpBehaviour AddComponent(GameObject gameObject, Type type) { return null; }
    }
}
