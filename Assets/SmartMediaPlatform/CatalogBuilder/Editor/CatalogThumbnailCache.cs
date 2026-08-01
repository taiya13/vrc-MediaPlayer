#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace SmartMediaPlatform.CatalogBuilder.EditorTools
{
    /// <summary>
    /// <b>サムネイルの絵を取ってきて覚えておく。</b>Phase6-4。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// 200 件の一覧が文字だけだと、見分けるのに 1 件ずつ読むことになります。
    /// 絵が出ていれば<b>一目で分かります</b>。
    ///
    /// <b>プロジェクトには 1 枚も保存しません。</b>覚えているのは
    /// エディタが動いている間のメモリだけです。
    /// <list type="bullet">
    /// <item>ワールドの容量が増えない(VRChat のアップロード上限に効いてくる)</item>
    /// <item>取り込み直しても古い絵が残らない</item>
    /// <item>要らなくなったら Unity を閉じるだけで消える</item>
    /// </list>
    /// 絵をアセットとして持ちたい場合は Phase6-5 以降で足します。
    ///
    /// <b>待ちません。</b><see cref="Get"/> はまだ無ければ <c>null</c> を返し、
    /// 裏で取りに行きます。窓は<b>絵が無い前提</b>で描いてください
    /// (取れたら <see cref="EditorWindow.Repaint"/> が呼ばれて描き直されます)。
    ///
    /// <b>同時に取りに行く数を絞っています</b>(<see cref="MaxConcurrent"/>)。
    /// 200 件を一度に投げると Unity が固まり、YouTube 側にも一気に当たるためです。
    /// </summary>
    public static class CatalogThumbnailCache
    {
        /// <summary>同時に取りに行く上限。</summary>
        public const int MaxConcurrent = 4;

        /// <summary>1 枚あたりの待ち時間(秒)。</summary>
        public const int TimeoutSeconds = 10;

        private static readonly Dictionary<string, Texture2D> _done =
            new Dictionary<string, Texture2D>();

        private static readonly List<string> _failed = new List<string>();
        private static readonly List<string> _queue = new List<string>();
        private static readonly List<Job> _running = new List<Job>();

        private static bool _pumping;

        /// <summary>取れた絵。まだ無ければ <c>null</c>(裏で取りに行きます)。</summary>
        public static Texture2D Get(string url)
        {
            if (!IsSupported(url)) return null;

            string key = url.Trim();

            Texture2D found;
            if (_done.TryGetValue(key, out found)) return found;

            if (_failed.Contains(key)) return null;

            Request(key);
            return null;
        }

        /// <summary>取りに行ける形か。<b>http(s) だけ</b>を相手にします。</summary>
        public static bool IsSupported(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            string trimmed = url.Trim();
            return trimmed.StartsWith("http://") || trimmed.StartsWith("https://");
        }

        /// <summary>取りに行っている / 順番待ちの枚数。窓の「読み込み中 N 枚」に使います。</summary>
        public static int Pending
        {
            get { return _queue.Count + _running.Count; }
        }

        public static int LoadedCount { get { return _done.Count; } }

        /// <summary>全部捨てる。取り込み直したときに呼びます。</summary>
        public static void Clear()
        {
            foreach (var pair in _done)
            {
                if (pair.Value != null) Object.DestroyImmediate(pair.Value);
            }

            _done.Clear();
            _failed.Clear();
            _queue.Clear();

            for (int i = 0; i < _running.Count; i++) _running[i].Cancel();
            _running.Clear();

            StopPump();
        }

        // ───────── 内部 ─────────

        private static void Request(string url)
        {
            if (_queue.Contains(url)) return;

            for (int i = 0; i < _running.Count; i++)
            {
                if (_running[i].Url == url) return;
            }

            _queue.Add(url);
            StartPump();
        }

        private static void StartPump()
        {
            if (_pumping) return;

            _pumping = true;
            EditorApplication.update += Pump;
        }

        private static void StopPump()
        {
            if (!_pumping) return;

            _pumping = false;
            EditorApplication.update -= Pump;
        }

        /// <summary>
        /// エディタのフレームごとに 1 回呼ばれる。
        /// <b>ここで待ちません。</b>終わったものを取り込んで、空きぶんだけ次を始めるだけです。
        /// </summary>
        private static void Pump()
        {
            bool changed = false;

            for (int i = _running.Count - 1; i >= 0; i--)
            {
                Job job = _running[i];
                if (!job.IsDone) continue;

                _running.RemoveAt(i);
                changed = true;

                Texture2D texture = job.TakeTexture();
                if (texture != null) _done[job.Url] = texture;
                else _failed.Add(job.Url);

                job.Dispose();
            }

            while (_running.Count < MaxConcurrent && _queue.Count > 0)
            {
                string next = _queue[0];
                _queue.RemoveAt(0);

                _running.Add(Job.Start(next));
            }

            if (changed) RepaintWindows();

            if (_running.Count == 0 && _queue.Count == 0) StopPump();
        }

        /// <summary>絵が届いたら描き直す。窓の側に「読み込み待ち」の仕組みを持たせないため。</summary>
        private static void RepaintWindows()
        {
            var windows = Resources.FindObjectsOfTypeAll<CatalogBuilderWindow>();
            if (windows == null) return;

            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null) windows[i].Repaint();
            }
        }

        /// <summary>1 枚ぶんの取りに行き。</summary>
        private sealed class Job
        {
            public string Url;
            private UnityWebRequest _request;

            public static Job Start(string url)
            {
                var job = new Job();
                job.Url = url;

                job._request = UnityWebRequestTexture.GetTexture(url);
                job._request.timeout = TimeoutSeconds;
                job._request.SendWebRequest();

                return job;
            }

            public bool IsDone
            {
                get { return _request == null || _request.isDone; }
            }

            /// <summary>取れた絵。失敗なら null。</summary>
            public Texture2D TakeTexture()
            {
                if (_request == null) return null;
                if (_request.result != UnityWebRequest.Result.Success) return null;

                return DownloadHandlerTexture.GetContent(_request);
            }

            public void Cancel()
            {
                if (_request == null) return;

                _request.Abort();
                Dispose();
            }

            public void Dispose()
            {
                if (_request == null) return;

                _request.Dispose();
                _request = null;
            }
        }
    }
}
#endif
