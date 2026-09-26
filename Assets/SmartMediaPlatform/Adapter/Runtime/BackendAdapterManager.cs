using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Adapter
{
    /// <summary>
    /// アダプタの登録簿。どのメディアをどのアダプタが担当するかを決める。
    ///
    /// <b>上位への渡し方</b>
    /// <see cref="IBackendAdapter"/> は <see cref="IMediaBackend"/> でもあるため、
    /// ここで集めたアダプタはそのまま <see cref="BackendManager"/> や
    /// PlayerSession の登録口に渡せる。上位のコードは変更不要。
    ///
    /// <code>
    /// var adapters = new BackendAdapterManager(logger);
    /// adapters.Register(new AudioBackendAdapter(...));
    /// adapters.Register(new DummyVideoBackendAdapter(...));   // ← 増やすのはここだけ
    ///
    /// foreach (var adapter in adapters.Adapters) session.RegisterBackend(adapter);
    /// </code>
    ///
    /// この層は Session も MediaPlayer も知りません(依存は下向きのみ)。
    /// </summary>
    public sealed class BackendAdapterManager
    {
        private readonly List<IBackendAdapter> _adapters = new List<IBackendAdapter>();
        private readonly IReadOnlyList<IBackendAdapter> _readOnlyAdapters;

        private IBackendLogger _logger;

        public BackendAdapterManager(IBackendLogger logger = null)
        {
            _logger = logger ?? NullBackendLogger.Instance;
            _readOnlyAdapters = _adapters.AsReadOnly();
        }

        public int Count => _adapters.Count;

        /// <summary>登録順のアダプタ一覧(読み取り専用)。</summary>
        public IReadOnlyList<IBackendAdapter> Adapters => _readOnlyAdapters;

        public void SetLogger(IBackendLogger logger)
        {
            _logger = logger ?? NullBackendLogger.Instance;
        }

        // ───────── 登録 ─────────

        /// <summary>
        /// アダプタを登録する。<see cref="SelectFor"/> は登録順に問い合わせ、
        /// 最初に「扱える」と答えたものを使う。
        /// </summary>
        public bool Register(IBackendAdapter adapter)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            if (_adapters.Contains(adapter)) return false;

            _adapters.Add(adapter);
            Log($"アダプタを登録しました: {adapter}");
            return true;
        }

        public bool Unregister(IBackendAdapter adapter)
        {
            return adapter != null && _adapters.Remove(adapter);
        }

        public void Clear()
        {
            _adapters.Clear();
        }

        public IBackendAdapter Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            for (int i = 0; i < _adapters.Count; i++)
            {
                if (string.Equals(_adapters[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return _adapters[i];
            }
            return null;
        }

        // ───────── 選択 ─────────

        /// <summary>そのメディアを扱えるアダプタ。無ければ null。</summary>
        public IBackendAdapter SelectFor(MediaItem item)
        {
            if (item == null) return null;

            for (int i = 0; i < _adapters.Count; i++)
            {
                if (_adapters[i].CanPlay(item)) return _adapters[i];
            }
            return null;
        }

        /// <summary>いずれかのアダプタがそのメディアを扱えるか。</summary>
        public bool CanPlay(MediaItem item)
        {
            return SelectFor(item) != null;
        }

        /// <summary>その種別を扱えるアダプタ(種別だけで判定する)。</summary>
        public IBackendAdapter SelectForType(MediaType type)
        {
            for (int i = 0; i < _adapters.Count; i++)
            {
                var types = _adapters[i].SupportedTypes;
                for (int t = 0; t < types.Count; t++)
                {
                    if (types[t] == type) return _adapters[i];
                }
            }
            return null;
        }

        // ───────── 上位への受け渡し ─────────

        /// <summary>
        /// 登録済みのアダプタを、バックエンドを受け取る側へまとめて渡す。
        ///
        /// 渡し先(PlayerSession / MediaPlayer / BackendManager)は
        /// <see cref="IMediaBackend"/> しか知らないままでよい。
        /// </summary>
        /// <param name="register">登録処理(例: <c>session.RegisterBackend</c>)。</param>
        /// <returns>渡した件数。</returns>
        public int AttachAll(Action<IMediaBackend> register)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            for (int i = 0; i < _adapters.Count; i++)
            {
                register(_adapters[i]);
            }

            Log($"{_adapters.Count} 個のアダプタを上位へ渡しました");
            return _adapters.Count;
        }

        /// <summary>Console 表示用の一覧。</summary>
        public string Describe()
        {
            if (_adapters.Count == 0) return "(アダプタ未登録)";

            var lines = new string[_adapters.Count];
            for (int i = 0; i < _adapters.Count; i++)
            {
                lines[i] = $"  {i + 1}. {_adapters[i]}";
            }
            return string.Join("\n", lines);
        }

        private void Log(string message)
        {
            _logger.Log($"[BackendAdapterManager] {message}");
        }
    }
}
