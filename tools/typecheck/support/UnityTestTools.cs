// Unity 側のテスト用 API。NUnit の代役とは別アセンブリに置く
// (テストコードが参照するのは形だけで、判定は NUnitAsserting.cs が行う)。

namespace UnityEngine.TestTools
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class UnityTestAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class UnitySetUpAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class UnityTearDownAttribute : System.Attribute { }

    public static class LogAssert
    {
        public static bool ignoreFailingMessages { get; set; }
        public static void Expect(UnityEngine.LogType type, string message) { }
        public static void NoUnexpectedReceived() { }
    }
}

namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
}
