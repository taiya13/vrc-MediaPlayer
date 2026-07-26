using System.Collections.Generic;

namespace SmartMediaPlatform.Backend
{
    /// <summary>
    /// Backend のログ出力先を抽象化する。
    ///
    /// Backend 層は UnityEngine を参照しない(asmdef が noEngineReferences)ため、
    /// UnityEngine.Debug.Log を直接呼べない。出力先を注入する形にすることで、
    /// 「純粋 C# のままログを出す」という要件を満たしつつ、
    /// Unity では Debug.Log、テストでは記録用リストへ、と差し替えられる。
    /// </summary>
    public interface IBackendLogger
    {
        void Log(string message);
    }

    /// <summary>何もしないロガー(既定)。</summary>
    public sealed class NullBackendLogger : IBackendLogger
    {
        public static readonly NullBackendLogger Instance = new NullBackendLogger();

        private NullBackendLogger() { }

        public void Log(string message) { }
    }

    /// <summary>
    /// ログを溜め込むロガー。EditMode テストと Console デモの両方で使う
    /// (デモは溜めたものをまとめて 1 回で出力する)。
    /// </summary>
    public sealed class ListBackendLogger : IBackendLogger
    {
        private readonly List<string> _lines = new List<string>();

        public IReadOnlyList<string> Lines => _lines;

        public void Log(string message)
        {
            _lines.Add(message ?? "");
        }

        public void Clear()
        {
            _lines.Clear();
        }

        public override string ToString()
        {
            return string.Join("\n", _lines);
        }
    }
}
