#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System.Reflection;
using System.Text;
using SmartMediaPlatform.Catalog.Udon;
using SmartMediaPlatform.World.Udon;
using UnityEditor;
using UnityEngine;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>動画が映らないときに、どこで切れているかを 1 段ずつ調べる。</b>Phase7-2。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// 「映らない」には原因が何通りもあり、<b>どれも同じ見た目(真っ黒)</b>になります。
    /// <list type="number">
    /// <item>URL が見本のまま(<c>example.com</c>)…… 押しても何も起きない</item>
    /// <item>マテリアルがライト付き …… 動画は流れているのに面が真っ黒</item>
    /// <item>AVPro の Screen が付いていない …… 音だけ鳴る</item>
    /// <item>板が裏を向いている …… 透明</item>
    /// <item>プレイヤーとバックエンドが別の GameObject …… イベントが届かない</item>
    /// </list>
    /// 全部を Inspector で目視するのは大変なので、<b>順に並べて出します</b>。
    ///
    /// <b>直しません。</b>直すべき場所を指すところまでが仕事です。
    /// </summary>
    public static class UdonVideoDoctor
    {
        private const string MenuPath = "Tools/Smart Media Platform/動画が映らないときに調べる";

        /// <summary>見本の URL。これが入っていると、押しても何も起きません。</summary>
        private const string PlaceholderHost = "example.com";

        [MenuItem(MenuPath, false, -58)]
        public static void DiagnoseMenuItem()
        {
            Debug.Log(Diagnose());
        }

        public static string Diagnose()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[UdonVideoDoctor] 動画の道すじを上から順に調べました。");
            sb.AppendLine();

            UdonSmartMediaPlayer[] players = FindPlayers();

            if (players.Length == 0)
            {
                sb.AppendLine("  ✗ シーンに SmartMediaPlayer がありません。");
                sb.AppendLine("    Tools > Smart Media Platform > SmartMediaPlayer を作る");
                sb.AppendLine("    で作って、Hierarchy へ置いてください。");
                return sb.ToString();
            }

            for (int i = 0; i < players.Length; i++)
            {
                InspectPlayer(players[i], sb);
            }

            return sb.ToString();
        }

        private static void InspectPlayer(UdonSmartMediaPlayer player, StringBuilder sb)
        {
            sb.AppendLine("── " + Path(player.gameObject));
            sb.AppendLine();

            bool ok = true;

            ok &= InspectCatalog(player, sb);
            ok &= InspectScreen(player, sb);
            ok &= InspectPlayerComponent(player, sb);
            ok &= InspectBackend(player, sb);

            sb.AppendLine();
            sb.AppendLine(ok
                ? "  ✓ 配線はそろっています。これで映らないなら、"
                  + "板の向き(裏からは透明)と、その URL が VRChat で再生できるかを見てください。"
                : "  ✗ 上の「✗」を直してください。");
            sb.AppendLine();
        }

        // ───────── ① カタログ(URL がそもそも本物か)─────────

        private static bool InspectCatalog(UdonSmartMediaPlayer player, StringBuilder sb)
        {
            sb.AppendLine("  ① 曲の URL");

            UdonCatalogStore store = player.Store;
            UdonMediaCatalog catalog = store != null ? store.Catalog : null;

            if (catalog == null)
            {
                sb.AppendLine("     ✗ Catalog がありません(Store の Catalog が未設定)。");
                return false;
            }

            int count = catalog.Count;
            sb.AppendLine("     曲数: " + count + " 件");

            if (count == 0)
            {
                sb.AppendLine("     ✗ 1 曲も焼かれていません。");
                sb.AppendLine("       Catalog Builder の「③ VRCUrl へ焼く」を押してください。");
                return false;
            }

            // URL が見本のままかどうか。ここが最初の関門。
            int placeholders = 0;
            int empty = 0;

            for (int i = 0; i < count; i++)
            {
                string url = catalog.GetUrl(i) != null ? catalog.GetUrl(i).Get() : "";

                if (string.IsNullOrEmpty(url)) empty++;
                else if (url.Contains(PlaceholderHost)) placeholders++;
            }

            if (placeholders > 0)
            {
                sb.AppendLine("     ✗ " + placeholders + " 件が見本の URL(" + PlaceholderHost
                              + ")のままです。");
                sb.AppendLine("       これは実在しないので、押しても<b>永久に映りません</b>。");
                sb.AppendLine("       Catalog Builder で自分のカタログを選び、");
                sb.AppendLine("       「③ VRCUrl へ焼く」を押してください。");
                return false;
            }

            if (empty > 0)
            {
                sb.AppendLine("     ✗ " + empty + " 件の URL が空です。焼き直してください。");
                return false;
            }

            sb.AppendLine("     ✓ 本物の URL が入っています(1 件目: "
                          + Shorten(catalog.GetUrl(0).Get()) + ")");
            return true;
        }

        // ───────── ② 映す面 ─────────

        private static bool InspectScreen(UdonSmartMediaPlayer player, StringBuilder sb)
        {
            sb.AppendLine("  ② 映す面(Screen / Surface)");

            UdonMediaScreen screen = player.Screen;
            if (screen == null)
            {
                sb.AppendLine("     ✗ Screen が未設定です。");
                return false;
            }

            if (screen.Surface == null)
            {
                sb.AppendLine("     ✗ Surface(Renderer)が未設定です。");
                return false;
            }

            Renderer renderer = screen.Surface;
            sb.AppendLine("     面: " + Path(renderer.gameObject));

            if (!renderer.enabled)
            {
                sb.AppendLine("     ✗ Renderer が切られています。");
                return false;
            }

            Material material = renderer.sharedMaterial;
            if (material == null)
            {
                sb.AppendLine("     ✗ マテリアルがありません。");
                return false;
            }

            string shaderName = material.shader != null ? material.shader.name : "(不明)";
            bool unlit = shaderName.Contains("Unlit") || shaderName.Contains("unlit");

            sb.AppendLine("     材質: " + material.name + " / " + shaderName);

            if (!unlit)
            {
                sb.AppendLine("     ✗ ライトが要るシェーダーです。");
                sb.AppendLine("       ライトの当たっていない場所では、");
                sb.AppendLine("       動画が流れていても<b>面が真っ黒</b>になります。");
                sb.AppendLine("       Prefab を作り直すか、マテリアルを Unlit/Texture にしてください。");
                return false;
            }

            if (!material.HasProperty("_MainTex"))
            {
                sb.AppendLine("     ✗ このシェーダーに _MainTex がありません。");
                sb.AppendLine("       動画プレイヤーはそこへ絵を書き込むので、映りません。");
                return false;
            }

            // Unlit/Texture は _MainTex が空だと「真っ白」になる。
            // 「真っ黒」ではないので、ライトの問題と見分けがつく。
            if (material.mainTexture == null)
            {
                sb.AppendLine("     ! _MainTex が空です(この状態の面は<b>真っ白</b>に見えます)。");
                sb.AppendLine("       再生前ならこれで正常です。再生中も白いなら、");
                sb.AppendLine("       ③ の出力先の設定を見てください。");
            }

            sb.AppendLine("     ✓ Unlit で _MainTex があります");
            return true;
        }

        // ───────── ③ 動画プレイヤー ─────────

        private static bool InspectPlayerComponent(UdonSmartMediaPlayer player, StringBuilder sb)
        {
            sb.AppendLine("  ③ 動画プレイヤー");

            UdonVideoBackend backend = player.Backend;
            if (backend == null)
            {
                sb.AppendLine("     ✗ Backend が未設定です。");
                return false;
            }

            if (backend.Player == null)
            {
                sb.AppendLine("     ✗ Backend の Player が未設定です(動画プレイヤーが無い)。");
                return false;
            }

            var component = backend.Player as Component;
            string kind = component != null ? component.GetType().Name : "(不明)";

            sb.AppendLine("     種類: " + kind);
            sb.AppendLine("     場所: " + (component != null ? Path(component.gameObject) : "(不明)"));

            // AVPro は「面の側」にコンポーネントを付けて、そこからプレイヤーを指す。
            // これが無いと音だけ鳴って絵が出ない。
            if (kind.Contains("AVPro"))
            {
                return InspectAVProScreen(player, component, sb);
            }

            // Unity 版はプレイヤー自身が出力先を持つ。
            return InspectUnityTarget(player, component, sb);
        }

        private static bool InspectAVProScreen(
            UdonSmartMediaPlayer player, Component videoPlayer, StringBuilder sb)
        {
            Renderer renderer = player.Screen != null ? player.Screen.Surface : null;
            if (renderer == null) return false;

            Component screenComponent = FindComponentByName(
                renderer.gameObject, "VRCAVProVideoScreen");

            if (screenComponent == null)
            {
                sb.AppendLine("     ✗ 面に VRCAVProVideoScreen がありません。");
                sb.AppendLine("       AVPro は「面の側」にこれを付けないと絵が出ません(音は鳴ります)。");
                return false;
            }

            object wired = ReadMember(screenComponent, "videoPlayer", "VideoPlayer");
            if (wired == null)
            {
                sb.AppendLine("     ✗ VRCAVProVideoScreen の Video Player が空です。");
                sb.AppendLine("       Inspector で " + Path(videoPlayer.gameObject) + " を挿してください。");
                return false;
            }

            sb.AppendLine("     ✓ VRCAVProVideoScreen が面に付いて、プレイヤーを指しています");

            // AVPro はエディタでは映らない。ここで言っておかないと
            // 「エディタで確かめられない = 壊れている」と誤解される。
            sb.AppendLine("     ※ AVPro は Unity エディタ(ClientSim)では映りません。");
            sb.AppendLine("       確認は Build & Test で行ってください。");
            return true;
        }

        private static bool InspectUnityTarget(
            UdonSmartMediaPlayer player, Component videoPlayer, StringBuilder sb)
        {
            // 出力先を持っているのは VRCUnityVideoPlayer ではなく、
            // その下にいる UnityEngine.Video.VideoPlayer のほう。
            var unityPlayer = videoPlayer.GetComponent<UnityEngine.Video.VideoPlayer>();

            if (unityPlayer == null)
            {
                sb.AppendLine("     ✗ UnityEngine.Video.VideoPlayer がありません。");
                return false;
            }

            sb.AppendLine("     Render Mode: " + unityPlayer.renderMode);

            // ここが Phase7-2 で踏んだ本命。
            // Material Override 以外だと、出力先を挿しても絵はマテリアルへ行かない。
            // 音は AudioSource 経由なので鳴り、「音は出るのに画面が真っ白」になる。
            if (unityPlayer.renderMode != UnityEngine.Video.VideoRenderMode.MaterialOverride)
            {
                sb.AppendLine("     ✗ Render Mode が Material Override ではありません。");
                sb.AppendLine("       この設定だと、出力先を挿しても絵はマテリアルへ行きません。");
                sb.AppendLine("       (音は鳴るので「音は出るのに画面が真っ白」になります)");
                sb.AppendLine("       Prefab を作り直すか、Inspector で Material Override にしてください。");
                return false;
            }

            if (unityPlayer.targetMaterialRenderer == null)
            {
                sb.AppendLine("     ✗ Target Material Renderer が空です。");
                sb.AppendLine("       Inspector で Screen/Surface を挿してください。");
                return false;
            }

            string property = unityPlayer.targetMaterialProperty;
            sb.AppendLine("     書き込み先 : "
                          + unityPlayer.targetMaterialRenderer.name + " の "
                          + (string.IsNullOrEmpty(property) ? "(空)" : property));

            if (string.IsNullOrEmpty(property))
            {
                sb.AppendLine("     ✗ Target Material Property が空です。_MainTex にしてください。");
                return false;
            }

            sb.AppendLine("     ✓ Material Override で出力先が挿さっています");
            return true;
        }

        // ───────── ④ バックエンド ─────────

        private static bool InspectBackend(UdonSmartMediaPlayer player, StringBuilder sb)
        {
            sb.AppendLine("  ④ 再生の受け渡し");

            UdonVideoBackend backend = player.Backend;
            if (backend == null) return false;

            var component = backend.Player as Component;

            // VRChat の動画イベント(OnVideoStart など)は
            // 「同じ GameObject の UdonBehaviour」にしか届かない。
            if (component != null && component.gameObject != backend.gameObject)
            {
                sb.AppendLine("     ✗ 動画プレイヤーと Backend が別の GameObject にあります。");
                sb.AppendLine("       プレイヤー: " + Path(component.gameObject));
                sb.AppendLine("       Backend   : " + Path(backend.gameObject));
                sb.AppendLine("       VRChat の動画イベントは同じ GameObject にしか届きません。");
                return false;
            }

            if (backend.Screen == null)
            {
                sb.AppendLine("     ✗ Backend の Screen が未設定です(音量が効きません)。");
                return false;
            }

            if (player.Session == null || player.Session.Backend == null)
            {
                sb.AppendLine("     ✗ Session の Backend が未設定です(押しても再生が始まりません)。");
                return false;
            }

            sb.AppendLine("     ✓ プレイヤー・Backend・Session がつながっています");
            return true;
        }

        // ───────── 道具 ─────────

        private static Component FindComponentByName(GameObject target, string typeName)
        {
            Component[] all = target.GetComponents<Component>();

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (all[i].GetType().Name == typeName) return all[i];
            }
            return null;
        }

        private static object ReadMember(object target, params string[] names)
        {
            if (target == null) return null;

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    FieldInfo field = type.GetField(names[i], flags);
                    if (field != null) return field.GetValue(target);

                    PropertyInfo property = type.GetProperty(names[i], flags);
                    if (property != null && property.CanRead) return property.GetValue(target, null);
                }
            }
            return null;
        }

        private static string Shorten(string url)
        {
            if (string.IsNullOrEmpty(url)) return "(空)";
            return url.Length <= 60 ? url : url.Substring(0, 57) + "…";
        }

        private static UdonSmartMediaPlayer[] FindPlayers()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<UdonSmartMediaPlayer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<UdonSmartMediaPlayer>(true);
#endif
        }

        private static string Path(GameObject go)
        {
            string path = go.name;

            for (Transform parent = go.transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }
            return path;
        }
    }
}
#endif
