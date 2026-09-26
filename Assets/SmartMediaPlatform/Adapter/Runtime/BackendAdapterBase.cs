using System;
using System.Collections.Generic;
using SmartMediaPlatform.Backend;
using SmartMediaPlatform.Catalog;

namespace SmartMediaPlatform.Adapter
{
    /// <summary>
    /// アダプタ共通の土台。内側のバックエンドへ委譲しつつ、
    /// 足りない能力(シーク・SkipNext/Previous・エラー通知)を補う。
    ///
    /// 通知の流れ:
    ///   内側の Backend --(BackendEvent)--> このアダプタ --(名前を差し替えて)--> 上位の観測者
    /// 上位から見ると「アダプタが 1 つのバックエンド」に見えるので、
    /// BackendManager / MediaPlayer / PlayerSession は変更不要。
    /// </summary>
    public abstract class BackendAdapterBase : IBackendAdapter, IBackendObserver
    {
        private readonly List<IBackendObserver> _observers = new List<IBackendObserver>();
        private readonly MediaType[] _supportedTypes;
        private readonly IReadOnlyList<MediaType> _readOnlyTypes;

        private IBackendLogger _logger;

        /// <summary>包んでいるバックエンド。</summary>
        protected IMediaBackend Inner { get; }

        /// <summary>内側がシークに対応していればそれ、していなければ null。</summary>
        protected ISeekableBackend InnerSeekable { get; }

        protected BackendAdapterBase(
            string name,
            IMediaBackend inner,
            IBackendLogger logger,
            params MediaType[] supportedTypes)
        {
            Name = string.IsNullOrEmpty(name) ? GetType().Name : name;
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            InnerSeekable = inner as ISeekableBackend;
            _logger = logger ?? NullBackendLogger.Instance;

            _supportedTypes = supportedTypes != null && supportedTypes.Length > 0
                ? (MediaType[])supportedTypes.Clone()
                : new[] { MediaType.Unknown };
            _readOnlyTypes = Array.AsReadOnly(_supportedTypes);

            // 内側の出来事を受け取って、上位へ中継する
            Inner.AddObserver(this);
        }

        public string Name { get; }

        public IReadOnlyList<MediaType> SupportedTypes => _readOnlyTypes;

        public float RewindThresholdSeconds { get; set; } = 3f;

        public string LastError { get; private set; }

        public bool HasError => LastError != null;

        public void SetLogger(IBackendLogger logger)
        {
            _logger = logger ?? NullBackendLogger.Instance;
        }

        // ───────── 問い合わせ ─────────

        public virtual bool CanPlay(MediaItem item)
        {
            if (item == null) return false;

            for (int i = 0; i < _supportedTypes.Length; i++)
            {
                if (_supportedTypes[i] == item.Type) return CanPlayCore(item);
            }
            return false;
        }

        /// <summary>
        /// 種別が合っていたうえで、さらに再生できるかを判定する。
        /// 音源やURLが用意できているか、といったバックエンド固有の条件をここで見る。
        /// </summary>
        protected virtual bool CanPlayCore(MediaItem item) => Inner.CanPlay(item);

        public MediaItem GetCurrent() => Inner.GetCurrent();

        public MediaItem GetCurrentMedia() => Inner.GetCurrent();

        public BackendState GetState() => Inner.GetState();

        // ───────── 操作 ─────────

        public virtual bool Load(MediaItem item)
        {
            ClearError();

            if (item == null)
            {
                ReportError("Load 失敗: メディアが null です");
                return false;
            }

            if (!CanPlay(item))
            {
                ReportError($"Load 失敗: {Name} は {item.Id} ({item.Type}) を扱えません");
                return false;
            }

            bool ok = Inner.Load(item);
            if (!ok) ReportError($"Load 失敗: {item.Id}");
            else OnLoaded(item);

            return ok;
        }

        /// <summary>読み込みが成功したときに呼ばれる(派生クラスの追加処理用)。</summary>
        protected virtual void OnLoaded(MediaItem item) { }

        public virtual bool Play() => Inner.Play();

        public virtual bool Pause() => Inner.Pause();

        public virtual bool Resume() => Inner.Resume();

        public virtual bool Stop() => Inner.Stop();

        public virtual bool Skip() => Inner.Skip();

        public virtual bool SkipNext()
        {
            if (GetCurrent() == null)
            {
                Log("SkipNext 無視: 何も読み込んでいません");
                return false;
            }

            Log($"SkipNext: {GetCurrent().Id} を打ち切ります(次の選択は上位の仕事)");
            return Inner.Skip();
        }

        public virtual bool SkipPrevious()
        {
            var current = GetCurrent();
            if (current == null)
            {
                Log("SkipPrevious 無視: 何も読み込んでいません");
                return false;
            }

            // 十分に再生が進んでいれば頭出しする(よくある音楽プレイヤーの挙動)
            if (CanSeek && GetCurrentTime() > RewindThresholdSeconds)
            {
                Log($"SkipPrevious: {current.Id} を頭出しします");
                return Seek(0f);
            }

            Log($"SkipPrevious: {current.Id} を打ち切ります(前の選択は上位の仕事)");
            return Inner.Skip();
        }

        // ───────── シーク(能力の吸収) ─────────

        /// <summary>
        /// 既定では内側の能力に従う。
        /// 内側がシークを持たない場合は、派生クラスが再生位置を肩代わりできる。
        /// </summary>
        public virtual bool CanSeek => InnerSeekable != null && InnerSeekable.CanSeek;

        public virtual float GetCurrentTime()
        {
            return InnerSeekable != null ? InnerSeekable.GetCurrentTime() : 0f;
        }

        public virtual float GetDuration()
        {
            if (InnerSeekable != null)
            {
                float duration = InnerSeekable.GetDuration();
                if (duration > 0f) return duration;
            }

            // 分からなければ Catalog のメタデータで補う
            var current = GetCurrent();
            return current != null ? current.DurationSeconds : 0f;
        }

        public virtual float GetProgress()
        {
            float duration = GetDuration();
            if (duration <= 0f) return 0f;

            float progress = GetCurrentTime() / duration;
            return progress < 0f ? 0f : progress > 1f ? 1f : progress;
        }

        public virtual bool Seek(float normalizedPosition)
        {
            if (!CanSeek)
            {
                Log("Seek 無視: このバックエンドはシークに対応していません");
                return false;
            }

            float clamped = normalizedPosition < 0f ? 0f
                : normalizedPosition > 1f ? 1f : normalizedPosition;
            return InnerSeekable.Seek(clamped);
        }

        // ───────── エラー ─────────

        public void ReportError(string message)
        {
            LastError = message ?? "unknown error";
            Log($"エラー: {LastError}");

            Notify(new BackendEvent(
                BackendEventType.Error, GetCurrent(), GetState(), GetState(), Name, LastError));
        }

        public void ClearError()
        {
            LastError = null;
        }

        // ───────── 通知 ─────────

        public void AddObserver(IBackendObserver observer)
        {
            if (observer == null || _observers.Contains(observer)) return;
            _observers.Add(observer);
        }

        public void RemoveObserver(IBackendObserver observer)
        {
            if (observer == null) return;
            _observers.Remove(observer);
        }

        /// <summary>内側のバックエンドからの通知を、アダプタの名前に付け替えて中継する。</summary>
        public void OnBackendEvent(BackendEvent backendEvent)
        {
            if (backendEvent.Type == BackendEventType.Error)
            {
                LastError = backendEvent.Message;
            }

            Notify(new BackendEvent(
                backendEvent.Type,
                backendEvent.Item,
                backendEvent.PreviousState,
                backendEvent.NewState,
                Name,
                backendEvent.Message));
        }

        protected void Notify(BackendEvent backendEvent)
        {
            var snapshot = _observers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnBackendEvent(backendEvent);
            }
        }

        protected void Log(string message)
        {
            _logger.Log($"[{Name}] {message}");
        }

        public override string ToString()
        {
            // CanSeek は「いま読み込んでいるメディア」によって変わる状態なので、
            // 一覧表示には含めない(読み込み前は常に false になって誤解を招くため)。
            return $"{Name} (types: {string.Join("/", Array.ConvertAll(_supportedTypes, t => t.ToString()))})";
        }
    }
}
