#if UNITY_EDITOR
using System.Collections.Generic;
using SmartMediaPlatform.Catalog;
using SmartMediaPlatform.Catalog.Udon;
using SmartMediaPlatform.Video.Data;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace SmartMediaPlatform.Catalog.UdonEditor
{
    /// <summary>
    /// <b>URL を入力する場所。</b><see cref="UdonMediaCatalog"/> の Inspector。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// <see cref="UdonMediaCatalog"/> は Udon の制約で
    /// <b>並列配列</b>(Ids / Titles / Urls / TagOffsets …)としてデータを持っています。
    /// 素の Inspector ではこれが 11 本の配列としてバラバラに並ぶので、
    /// 「3 番目の動画の URL を変えたい」が事実上できません。
    ///
    /// ここでは<b>1 行 = 1 本の動画</b>として見せて、
    /// タイトルと URL を直接書き換えられるようにします。
    ///
    /// <b>VRCUrl は実行時に作れません。</b>だからこの編集は<b>編集時にしかできず</b>、
    /// ここが URL を入れる唯一の場所になります(Phase1-2 からの前提)。
    ///
    /// 書き戻しは <see cref="UdonCatalogBaker.Bake"/> に任せます。
    /// タグや関連の CSR(平坦化配列 + オフセット)を組み直すのはあちらの仕事なので、
    /// <b>この Inspector は整合性の面倒を見ません</b>。
    /// </summary>
    [CustomEditor(typeof(UdonMediaCatalog))]
    public sealed class UdonMediaCatalogEditor : Editor
    {
        private sealed class Row
        {
            public string Id;
            public string Title;
            public string Artist;
            public string Genre;
            public string Url;
            public MediaType Type;
            public int DurationSeconds;
            public string[] Tags;
            public string[] RelatedIds;
        }

        private List<Row> _rows;
        private Vector2 _scroll;
        private bool _showRaw;
        private bool _dirty;

        private UdonMediaCatalog Target => (UdonMediaCatalog)target;

        public override void OnInspectorGUI()
        {
            // UdonSharp の見出し(Program Asset / 同期設定)を消さないために先に描く。
            // 版によって API 名が変わるのでリフレクション経由(このプロジェクトの方針)。
            if (UdonSharpEditorBridge.DrawDefaultHeader(target)) return;

            if (_rows == null) Load();

            EditorGUILayout.Space();
            DrawSummary();
            EditorGUILayout.Space();

            DrawRows();
            EditorGUILayout.Space();
            DrawButtons();

            EditorGUILayout.Space();
            _showRaw = EditorGUILayout.Foldout(_showRaw, "素のデータ(並列配列)を見る");
            if (_showRaw)
            {
                EditorGUI.indentLevel++;
                DrawDefaultInspector();
                EditorGUI.indentLevel--;
            }
        }

        // ───────── 見出し ─────────

        private void DrawSummary()
        {
            int missing = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(_rows[i].Url)) missing++;
            }

            EditorGUILayout.LabelField($"メディア {_rows.Count} 件", EditorStyles.boldLabel);

            if (_rows.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "空です。「サンプルを読み込む」で同梱カタログを入れるか、"
                    + "「+ 追加」で 1 件ずつ作ってください。",
                    MessageType.Info);
            }
            else if (missing > 0)
            {
                EditorGUILayout.HelpBox(
                    $"URL が空の項目が {missing} 件あります。そのままでは再生できません。",
                    MessageType.Warning);
            }

            if (_dirty)
            {
                EditorGUILayout.HelpBox(
                    "編集中です。「反映する」を押すまで Udon 側には渡りません。",
                    MessageType.Warning);
            }
        }

        // ───────── 一覧 ─────────

        private void DrawRows()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(420f));

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i + 1}.", GUILayout.Width(24f));
                row.Title = EditorGUILayout.TextField(row.Title);

                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(24f))) { Swap(i, i - 1); break; }
                GUI.enabled = i < _rows.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(24f))) { Swap(i, i + 1); break; }
                GUI.enabled = true;

                if (GUILayout.Button("×", GUILayout.Width(24f)))
                {
                    _rows.RemoveAt(i);
                    _dirty = true;
                    break;
                }
                EditorGUILayout.EndHorizontal();

                // ★ ここが URL の入力欄
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("URL", GUILayout.Width(34f));
                string url = EditorGUILayout.TextField(row.Url);
                if (url != row.Url) { row.Url = url; _dirty = true; }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("作者", GUILayout.Width(34f));
                row.Artist = EditorGUILayout.TextField(row.Artist);
                row.Type = (MediaType)EditorGUILayout.EnumPopup(row.Type, GUILayout.Width(80f));
                EditorGUILayout.LabelField("秒", GUILayout.Width(20f));
                row.DurationSeconds = EditorGUILayout.IntField(
                    row.DurationSeconds, GUILayout.Width(50f));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            if (GUI.changed) _dirty = true;
        }

        private void Swap(int a, int b)
        {
            var tmp = _rows[a];
            _rows[a] = _rows[b];
            _rows[b] = tmp;
            _dirty = true;
        }

        // ───────── ボタン ─────────

        private void DrawButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("+ 追加"))
            {
                _rows.Add(new Row
                {
                    Id = NextId(),
                    Title = "新しいメディア",
                    Artist = "",
                    Genre = "",
                    Url = "",
                    Type = MediaType.Video,
                    DurationSeconds = 0,
                    Tags = new string[0],
                    RelatedIds = new string[0],
                });
                _dirty = true;
            }

            if (GUILayout.Button("サンプルを読み込む")
                && EditorUtility.DisplayDialog(
                    "Smart Media Platform",
                    "同梱サンプル(動画 10 本)で置き換えます。いまの内容は失われます。",
                    "置き換える", "やめる"))
            {
                var sample = new MediaCatalog(new VideoCatalogSource());
                _rows = ToRows(sample.GetAll());
                _dirty = true;
            }

            if (GUILayout.Button("読み直す"))
            {
                Load();
                _dirty = false;
            }

            EditorGUILayout.EndHorizontal();

            GUI.enabled = _dirty;
            GUI.backgroundColor = _dirty ? new Color(0.6f, 1f, 0.6f) : Color.white;
            if (GUILayout.Button("反映する(VRCUrl へ焼き込む)", GUILayout.Height(28f)))
            {
                Apply();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            EditorGUILayout.HelpBox(
                "VRCUrl は実行時に作れないので、ここで焼き込んだ URL だけが再生できます。",
                MessageType.None);
        }

        // ───────── 読み書き ─────────

        private void Load()
        {
            _rows = new List<Row>();
            var t = Target;
            if (t == null || t.Ids == null) return;

            for (int i = 0; i < t.Ids.Length; i++)
            {
                _rows.Add(new Row
                {
                    Id = t.Ids[i],
                    Title = Get(t.Titles, i),
                    Artist = Get(t.Artists, i),
                    Genre = Get(t.Genres, i),
                    Url = UrlAt(t, i),
                    Type = TypeAt(t, i),
                    DurationSeconds = t.Durations != null && i < t.Durations.Length
                        ? t.Durations[i] : 0,
                    Tags = SliceTags(t, i),
                    RelatedIds = SliceRelated(t, i),
                });
            }
        }

        private void Apply()
        {
            var items = new List<MediaItem>();
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];

                string id = string.IsNullOrWhiteSpace(row.Id) ? NextId() : row.Id;
                string title = string.IsNullOrWhiteSpace(row.Title) ? id : row.Title;

                items.Add(new MediaItem(
                    id, title, row.Artist, row.Type, row.Genre,
                    row.Tags, row.Url, row.DurationSeconds, row.RelatedIds));
            }

            UdonCatalogBaker.Bake(Target, items);
            _dirty = false;
            Load();

            Debug.Log($"[UdonMediaCatalog] {items.Count} 件を焼き込みました。", Target);
        }

        // ───────── 小道具 ─────────

        private static List<Row> ToRows(IReadOnlyList<MediaItem> items)
        {
            var rows = new List<Row>();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                rows.Add(new Row
                {
                    Id = item.Id,
                    Title = item.Title,
                    Artist = item.Artist,
                    Genre = item.Genre,
                    Url = item.Url,
                    Type = item.Type,
                    DurationSeconds = item.DurationSeconds,
                    Tags = ToArray(item.Tags),
                    RelatedIds = ToArray(item.RelatedIds),
                });
            }
            return rows;
        }

        private static string[] ToArray(IReadOnlyList<string> source)
        {
            if (source == null) return new string[0];

            var result = new string[source.Count];
            for (int i = 0; i < source.Count; i++) result[i] = source[i];
            return result;
        }

        private string NextId()
        {
            int n = _rows != null ? _rows.Count + 1 : 1;
            for (int guard = 0; guard < 1000; guard++)
            {
                string candidate = $"media-{n:000}";
                if (!HasId(candidate)) return candidate;
                n++;
            }
            return System.Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private bool HasId(string id)
        {
            if (_rows == null) return false;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Id == id) return true;
            }
            return false;
        }

        private static string Get(string[] source, int index)
        {
            return source != null && index < source.Length ? source[index] : "";
        }

        private static string UrlAt(UdonMediaCatalog t, int index)
        {
            if (t.Urls == null || index >= t.Urls.Length) return "";

            VRCUrl url = t.Urls[index];
            return url != null ? url.Get() : "";
        }

        private static MediaType TypeAt(UdonMediaCatalog t, int index)
        {
            if (t.Types == null || index >= t.Types.Length) return MediaType.Video;
            return (MediaType)t.Types[index];
        }

        private static string[] SliceTags(UdonMediaCatalog t, int index)
        {
            return Slice(t.TagValues, t.TagOffsets, index);
        }

        private static string[] SliceRelated(UdonMediaCatalog t, int index)
        {
            return Slice(t.RelatedIds, t.RelatedOffsets, index);
        }

        /// <summary>CSR(平坦化配列 + オフセット)から 1 件ぶんを取り出す。</summary>
        private static string[] Slice(string[] values, int[] offsets, int index)
        {
            if (values == null || offsets == null || index + 1 >= offsets.Length)
            {
                return new string[0];
            }

            int start = offsets[index];
            int end = offsets[index + 1];
            if (end <= start || end > values.Length) return new string[0];

            var result = new string[end - start];
            for (int i = 0; i < result.Length; i++) result[i] = values[start + i];
            return result;
        }
    }
}
#endif
