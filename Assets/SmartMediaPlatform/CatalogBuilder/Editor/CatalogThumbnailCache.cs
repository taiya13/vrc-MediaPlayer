#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
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
    /// <b>Assets の下には 1 枚も置きません。</b>置き場所は
    /// <c>Library/SmartMediaPlatform/Thumbnails/</c> です(Phase6-5)。
    /// <list type="bullet">
    /// <item><b>ワールドには入りません。</b><c>Library</c> は Unity が作る作業場で、
    ///       <c>Assets</c> の外なので<b>ビルドにも git にも入りません</b></item>
    /// <item><b>Unity を閉じても消えません。</b>200 枚を毎回取り直すと
    ///       起動のたびに数十秒かかり、YouTube にも無駄に当たります</item>
    /// <item>要らなくなったら「絵を捨てる」で丸ごと消せます</item>
    /// </list>
    ///
    /// <b>2 段構えです。</b>まずメモリ、次にディスク、最後に通信。
    /// 2 度目からは<b>通信しません</b>。
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

        /// <summary>
        /// 取れた絵。まだ無ければ <c>null</c>(裏で取りに行きます)。
        ///
        /// 探す順は<b>メモリ → ディスク → 通信</b>。
        /// 前に一度でも見た絵なら、Unity を再起動しても<b>通信しません</b>。
        /// </summary>
        public static Texture2D Get(string url)
        {
            if (!IsSupported(url)) return null;

            string key = url.Trim();

            Texture2D found;
            if (_done.TryGetValue(key, out found)) return found;

            if (_failed.Contains(key)) return null;

            Texture2D fromDisk = LoadFromDisk(key);
            if (fromDisk != null)
            {
                _done[key] = fromDisk;
                return fromDisk;
            }

            Request(key);
            return null;
        }

        // ───────── ディスク(Library の下)─────────

        /// <summary>絵の置き場所。<c>Assets</c> の外なのでビルドにも git にも入らない。</summary>
        public static string CacheDirectory
        {
            get
            {
                // Application.dataPath は <project>/Assets。その隣が Library。
                string project = Directory.GetParent(Application.dataPath).FullName;
                return Path.Combine(project, "Library/SmartMediaPlatform/Thumbnails")
                           .Replace('\\', '/');
            }
        }

        /// <summary>ディスクに残っている枚数。</summary>
        public static int DiskCount
        {
            get
            {
                try
                {
                    if (!Directory.Exists(CacheDirectory)) return 0;
                    return Directory.GetFiles(CacheDirectory, "*.png").Length;
                }
                catch (System.Exception)
                {
                    return 0;
                }
            }
        }

        /// <summary>ディスクのぶんも含めて全部捨てる。</summary>
        public static void ClearDisk()
        {
            try
            {
                if (Directory.Exists(CacheDirectory)) Directory.Delete(CacheDirectory, true);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[CatalogThumbnailCache] 絵を消せませんでした: " + e.Message);
            }

            Clear();
        }

        private static Texture2D LoadFromDisk(string url)
        {
            try
            {
                string path = PathFor(url);
                if (!File.Exists(path)) return null;

                var texture = new Texture2D(2, 2);
                if (texture.LoadImage(File.ReadAllBytes(path))) return texture;

                Object.DestroyImmediate(texture);
                return null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static void SaveToDisk(string url, Texture2D texture)
        {
            if (texture == null) return;

            try
            {
                Directory.CreateDirectory(CacheDirectory);
                File.WriteAllBytes(PathFor(url), texture.EncodeToPNG());
            }
            catch (System.Exception)
            {
                // 書けなくても表示はできる。次の起動でまた取りに行くだけ。
            }
        }

        /// <summary>
        /// URL からファイル名を作る。
        /// URL をそのまま使えないので<b>中身から決まる名前</b>にします
        /// (同じ URL なら必ず同じ名前 = 何度実行しても同じ絵に当たる)。
        /// </summary>
        private static string PathFor(string url)
        {
            return Path.Combine(CacheDirectory, HashOf(url) + ".png").Replace('\\', '/');
        }

        private static string HashOf(string value)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
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
                if (texture != null)
                {
                    _done[job.Url] = texture;

                    // 次の起動で取り直さないよう、その場で書き残す。
                    SaveToDisk(job.Url, texture);
                }
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
