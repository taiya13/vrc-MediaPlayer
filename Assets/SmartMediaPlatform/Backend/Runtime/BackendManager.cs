using System;
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Queue;

namespace SmartMediaPlatform.Backend
{
    /// <summary>
    /// Queue と Backend をつなぐ調整役。
    ///
    /// 役割は 3 つだけ:
    ///  1. メディア種別に応じて適切なバックエンドを選ぶ(<see cref="IMediaBackend.CanPlay"/> で判定)
    ///  2. Queue の先頭を Backend に読み込ませる
    ///  3. Backend の通知を受けて、必要なら Queue を進める
    ///
    /// 依存は Catalog / Queue / Backend のみ。
    /// 再生手段(AudioSource など)も UI もネットワークも知らない。
    ///
    /// 複数バックエンドを登録できるので、Phase2 で
    /// 「Music は AudioSource 実装、Video は VRCVideoPlayer 実装」と分けても
    /// この クラスと上位コードは変更不要。
    /// </summary>
    public sealed class BackendManager : IBackendObserver
    {
        private readonly List<IMediaBackend> _backends = new List<IMediaBackend>();
        private readonly IQueue _queue;
        private readonly IBackendLogger _logger;

        private IMediaBackend _active;

        /// <summary>
        /// 自然終了(<see cref="BackendEventType.Ended"/>)を受けたときに自動で次へ進むか。
        /// 既定は false。Phase1-5 では自動再生を実装しないため、
        /// 有効化は利用側の明示的な選択に委ねる。
        /// </summary>
        public bool AutoAdvanceOnEnded { get; set; }

        public BackendManager(IQueue queue, IBackendLogger logger = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _logger = logger ?? NullBackendLogger.Instance;
        }

        public IQueue Queue => _queue;

        /// <summary>現在メディアを保持しているバックエンド。未選択なら null。</summary>
        public IMediaBackend ActiveBackend => _active;

        public IReadOnlyList<IMediaBackend> Backends => _backends;

        // --- 登録 ---

        /// <summary>
        /// バックエンドを登録する。登録順に <see cref="IMediaBackend.CanPlay"/> を
        /// 問い合わせ、最初に「扱える」と答えたものを使う。
        /// </summary>
        public void RegisterBackend(IMediaBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            if (_backends.Contains(backend)) return;

            _backends.Add(backend);
            backend.AddObserver(this);
        }

        public bool UnregisterBackend(IMediaBackend backend)
        {
            if (backend == null || !_backends.Remove(backend)) return false;

            backend.RemoveObserver(this);
            if (ReferenceEquals(_active, backend)) _active = null;
            return true;
        }

        /// <summary>そのメディアを扱えるバックエンドを返す。無ければ null。</summary>
        public IMediaBackend SelectBackendFor(MediaItem item)
        {
            if (item == null) return null;

            for (int i = 0; i < _backends.Count; i++)
            {
                if (_backends[i].CanPlay(item)) return _backends[i];
            }
            return null;
        }

        // --- Queue 連携 ---

        /// <summary>
        /// Queue の先頭(Now)を、対応するバックエンドに読み込ませる。
        /// キューが空、または扱えるバックエンドが無ければ false。
        /// </summary>
        public bool LoadCurrent()
        {
            var head = _queue.Peek();
            if (head == null)
            {
                Log("LoadCurrent failed: queue is empty");
                return false;
            }
            return LoadItem(head.Item);
        }

        /// <summary>指定メディアを、対応するバックエンドに読み込ませる。</summary>
        public bool LoadItem(MediaItem item)
        {
            var backend = SelectBackendFor(item);
            if (backend == null)
            {
                string id = item != null ? item.Id : "(null)";
                Log($"LoadItem failed: no backend can play {id}");
                return false;
            }

            // 別のバックエンドが動いていれば止めてから切り替える。
            // すでに停止済み / 未ロードのときに Stop を呼ぶと拒否されて
            // 無用な Error 通知が飛ぶため、止める必要がある状態のときだけ呼ぶ。
            if (_active != null && !ReferenceEquals(_active, backend) && NeedsStop(_active.GetState()))
            {
                _active.Stop();
            }

            _active = backend;
            return backend.Load(item);
        }

        private static bool NeedsStop(BackendState state)
        {
            return state == BackendState.Playing
                || state == BackendState.Paused
                || state == BackendState.Ready
                || state == BackendState.Ended;
        }

        // --- 再生操作(アクティブなバックエンドへ委譲)---

        public bool Play() => _active != null && _active.Play();

        public bool Pause() => _active != null && _active.Pause();

        public bool Resume() => _active != null && _active.Resume();

        public bool Stop() => _active != null && _active.Stop();

        /// <summary>
        /// 現在のメディアを中断し、Queue を次へ進めて新しい先頭を読み込む。
        ///
        /// 再生していた場合は続けて再生を開始する
        /// (「スキップしたら次が始まる」という利用者の期待に沿うため)。
        /// キューが尽きた場合は読み込みを行わず false。
        /// </summary>
        public bool Skip()
        {
            bool wasPlaying = _active != null && _active.GetState() == BackendState.Playing;

            if (_active != null) _active.Skip();

            _queue.Skip();

            var head = _queue.Peek();
            if (head == null)
            {
                Log("Skip: queue is now empty");
                return false;
            }

            if (!LoadItem(head.Item)) return false;
            return wasPlaying ? Play() : true;
        }

        // --- 問い合わせ ---

        /// <summary>アクティブなバックエンドが保持しているメディア。無ければ null。</summary>
        public MediaItem GetCurrent()
        {
            return _active != null ? _active.GetCurrent() : null;
        }

        /// <summary>アクティブなバックエンドの状態。未選択なら <see cref="BackendState.Idle"/>。</summary>
        public BackendState GetState()
        {
            return _active != null ? _active.GetState() : BackendState.Idle;
        }

        /// <summary>いずれかのバックエンドがそのメディアを扱えるか。</summary>
        public bool CanPlay(MediaItem item)
        {
            return SelectBackendFor(item) != null;
        }

        // --- Backend からの通知 ---

        public void OnBackendEvent(BackendEvent backendEvent)
        {
            if (backendEvent.Type != BackendEventType.Ended) return;
            if (!AutoAdvanceOnEnded) return;

            // 自然終了 → 次へ。実機バックエンドでも同じ配線がそのまま使える。
            Skip();
        }

        private void Log(string message)
        {
            _logger.Log($"[BackendManager] {message}");
        }
    }
}
