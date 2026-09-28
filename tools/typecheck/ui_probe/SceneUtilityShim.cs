using System;
using UnityEngine;
namespace SmartMediaPlatform.Video.EditorTools
{
    // 本物は UdonSharp のプログラムアセットを作る。ここでは部品を足すだけ。
    public static class UdonSharpSceneUtility
    {
        public static Component AddUdonSharpComponent(GameObject target, Type behaviourType, out bool needsCompile)
        {
            needsCompile = false;
            return target.AddComponent(behaviourType);
        }
        public static GameObject EnsureSceneDescriptor(string objectName = "VRCWorld") { return new GameObject(objectName); }
        public static string ManualSetupInstruction(GameObject target, Type behaviourType) { return ""; }
        public static string RecompileInstruction(string menuPath) { return ""; }
    }
}
namespace SmartMediaPlatform.Video.EditorTools
{
    public static partial class UdonSharpSceneUtilityExtra { }
}
