#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using SmartMediaPlatform.Video.EditorTools;
using SmartMediaPlatform.World.Udon;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SmartMediaPlatform.World.EditorTools
{
    /// <summary>
    /// <b>ワールド内 uGUI を組むための小道具。</b>Phase5-3。
    ///
    /// <b>なぜ要るのか</b><br/>
    /// ワールドに置く Canvas は<b>コードで組む</b>ことにしています(Phase5-1 からの方針)。
    /// UdonSharp のプログラム(.asset)は SDK のバージョンごとに中身が変わるため、
    /// 出来合いの Prefab を同梱すると「SDK を更新したら program asset が null」に
    /// なるからです。そのぶん組み立てコードが長くなるので、
    /// <b>座標の決め方とイベントの繋ぎ方をここへ集約</b>しています。
    ///
    /// <b>座標の決め方</b><br/>
    /// すべて<b>左上から数えたピクセル</b>です(<c>anchor = pivot = 左上</c>)。
    /// <c>y</c> は下向きが正。入れ子にしても親の左上が原点になるので、
    /// 「上から順に積む」だけでレイアウトが決まります。
    ///
    /// <b>イベントの繋ぎ方</b><br/>
    /// uGUI の <c>Button</c> は <c>UdonSharpBehaviour</c> を直接は呼べません。
    /// 実際に呼べるのはその裏にいる <c>UdonBehaviour</c> の
    /// <c>SendCustomEvent(string)</c> なので、<see cref="Bind(Button, UdonSharpBehaviour, string)"/>
    /// が<b>裏の UdonBehaviour を引き当てて</b>永続リスナーとして登録します。
    /// </summary>
    public static class UdonWorldUiKit
    {
        // ───────── 色 ─────────

        public static readonly Color Backplate = new Color(0.07f, 0.08f, 0.10f, 0.96f);
        public static readonly Color Section = new Color(0.13f, 0.14f, 0.17f, 1f);
        public static readonly Color ButtonFace = new Color(0.20f, 0.22f, 0.27f, 1f);
        public static readonly Color ButtonAccent = new Color(0.15f, 0.42f, 0.66f, 1f);
        public static readonly Color RowFace = new Color(0.16f, 0.17f, 0.21f, 1f);

        // 1 行おきに少しだけ濃さを変える。目が横に滑らないようにするためで、
        // 色そのものには意味を持たせない。
        public static readonly Color RowFaceAlt = new Color(0.19f, 0.20f, 0.25f, 1f);

        public static readonly Color RowHighlight = new Color(0.15f, 0.42f, 0.66f, 0.45f);

        // 押した直後だけ一瞬出る。「使う」で押したときの手応えになる。
        public static readonly Color RowPressed = new Color(1f, 1f, 1f, 0.22f);
        public static readonly Color TrackBack = new Color(0.25f, 0.27f, 0.32f, 1f);
        public static readonly Color TrackFill = new Color(0.35f, 0.72f, 0.95f, 1f);

        public static readonly Color TextPrimary = new Color(0.95f, 0.96f, 0.98f, 1f);
        public static readonly Color TextSecondary = new Color(0.66f, 0.70f, 0.78f, 1f);

        /// <summary>いま鳴っている行の見出し。ふだんより明るくする。</summary>
        public static readonly Color TextNowPlaying = new Color(1f, 1f, 1f, 1f);

        /// <summary><see cref="Bind(Button, UdonSharpBehaviour, string)"/> が失敗した回数。</summary>
        public static int BindFailures { get; private set; }

        /// <summary>読み戻しまで確かめて繋がったボタンの数。</summary>
        public static int BindCount { get; private set; }

        /// <summary><see cref="SyncProxies"/> で UdonBehaviour へ写した数。</summary>
        public static int SyncedCount { get; private set; }

        /// <summary>Collider +「使う」で押せるようにできたボタンの数。</summary>
        public static int InteractCount { get; private set; }

        /// <summary>「使う」を足せなかったボタンの数。</summary>
        public static int InteractFailures { get; private set; }

        /// <summary>U# のコンパイル待ちで「使う」を足せなかったか。</summary>
        public static bool InteractNeedsCompile { get; private set; }

        // ───────── VR で押しやすい大きさ ─────────

        /// <summary>
        /// <b>VR で気持ちよく押せる最小の辺(m)。</b>
        ///
        /// レーザーで狙うぶんには 2 cm 角でも当たりますが、腕が伸びた姿勢や
        /// 動きながらだと外します。<b>4.5 cm</b> を下回るボタンは
        /// 組み立て時に警告を出すようにしています。
        /// </summary>
        public const float ComfortableTouchMeters = 0.045f;

        /// <summary>いま組んでいる Canvas の 1 px が何 m か。</summary>
        public static float CurrentMetersPerPixel { get; private set; }

        /// <summary>小さすぎたボタンの数(組み立て後の報告用)。</summary>
        public static int SmallTouchTargets { get; private set; }

        public static void ResetCounters()
        {
            BindFailures = 0;
            BindCount = 0;
            SyncedCount = 0;
            InteractCount = 0;
            InteractFailures = 0;
            InteractNeedsCompile = false;
            SmallTouchTargets = 0;
        }

        /// <summary>押せる大きさか確かめる。小さければ警告して数える。</summary>
        private static void CheckTouchSize(GameObject target, float width, float height)
        {
            if (CurrentMetersPerPixel <= 0f) return;

            float shortest = (width < height ? width : height) * CurrentMetersPerPixel;
            if (shortest >= ComfortableTouchMeters) return;

            SmallTouchTargets++;
            Debug.LogWarning(
                "[UdonWorldUiKit] " + target.name + " は "
                + Mathf.RoundToInt(shortest * 1000f) + " mm しかありません。VR では狙いにくいので "
                + Mathf.RoundToInt(ComfortableTouchMeters * 1000f) + " mm 以上を目安にしてください。",
                target);
        }

        // ───────── 置く ─────────

        /// <summary>左上から数えた位置に、大きさだけ持つ入れ物を置く。</summary>
        public static RectTransform Place(
            Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>べた塗りの板。</summary>
        public static Image Plate(
            Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var rect = Place(parent, name, x, y, width, height);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>文字。<c>raycastTarget</c> は切ってある(下のボタンが押せなくなるため)。</summary>
        public static Text Label(
            Transform parent, string name, float x, float y, float width, float height,
            int fontSize, TextAnchor anchor, Color color)
        {
            var rect = Place(parent, name, x, y, width, height);

            var text = rect.gameObject.AddComponent<Text>();
            text.font = BuiltinFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            // 文字が当たり判定を食うと、行そのものが押せなくなる。
            text.raycastTarget = false;
            return text;
        }

        /// <summary>押せるボタン。見出しは子の <see cref="Text"/> に入る。</summary>
        public static Button PushButton(
            Transform parent, string name, float x, float y, float width, float height,
            string caption, int fontSize, Color face, out Text label)
        {
            var image = Plate(parent, name, x, y, width, height, face);

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.05f;
            button.colors = colors;

            label = Label(
                image.transform, "Label", 0f, 0f, width, height,
                fontSize, TextAnchor.MiddleCenter, TextPrimary);

            if (caption != null) label.text = caption;

            CheckTouchSize(button.gameObject, width, height);
            return button;
        }

        /// <summary>見出しの要らないボタン(行そのものを押させるときなど)。</summary>
        public static Button HitArea(
            Transform parent, string name, float x, float y, float width, float height, Color face)
        {
            var image = Plate(parent, name, x, y, width, height, face);

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.05f;
            button.colors = colors;

            CheckTouchSize(button.gameObject, width, height);
            return button;
        }

        /// <summary>区切り線。節どうしの境目をはっきりさせる。</summary>
        public static Image Divider(
            Transform parent, string name, float x, float y, float width, Color color)
        {
            var line = Plate(parent, name, x, y, width, 2f, color);
            line.raycastTarget = false;
            return line;
        }

        /// <summary>横に伸びる進捗バー。返すのは伸び縮みする側。</summary>
        public static Image ProgressBar(
            Transform parent, string name, float x, float y, float width, float height)
        {
            var back = Plate(parent, name, x, y, width, height, TrackBack);
            back.raycastTarget = false;

            var fill = Plate(back.transform, "Fill", 0f, 0f, width, height, TrackFill);
            fill.raycastTarget = false;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            return fill;
        }

        // ───────── 繋ぐ ─────────

        /// <summary>
        /// <c>Button.onClick</c> から <paramref name="target"/> の
        /// <paramref name="eventName"/> を呼べるようにする。
        /// </summary>
        public static bool Bind(Button button, UdonSharpBehaviour target, string eventName)
        {
            if (button == null) return Fail("(Button が null)", eventName, "Button が null");
            return Bind(button.onClick, target, eventName, button.name);
        }

        /// <summary>
        /// <c>Slider.onValueChanged</c> から <paramref name="eventName"/> を呼べるようにする。
        /// <b>値は渡りません</b>(Udon のイベントは引数を取れないため)。
        /// 受け取る側がつまみを読みに行ってください。
        /// </summary>
        public static bool Bind(Slider slider, UdonSharpBehaviour target, string eventName)
        {
            if (slider == null) return Fail("(Slider が null)", eventName, "Slider が null");
            return Bind(slider.onValueChanged, target, eventName, slider.name);
        }

        /// <summary>
        /// <c>InputField.onValueChanged</c> から <paramref name="eventName"/> を呼べるようにする。
        /// Phase7-2。<b>打った文字は渡りません</b>(Udon のイベントは引数を取れないため)。
        /// 受け取る側が <c>InputField.text</c> を読みに行ってください。
        /// </summary>
        public static bool Bind(InputField field, UdonSharpBehaviour target, string eventName)
        {
            if (field == null) return Fail("(InputField が null)", eventName, "InputField が null");
            return Bind(field.onValueChanged, target, eventName, field.name);
        }

        private static bool Bind(
            UnityEventBase unityEvent, UdonSharpBehaviour target, string eventName, string where)
        {
            if (unityEvent == null) return Fail(where, eventName, "イベントが null");
            if (target == null) return Fail(where, eventName, "送り先が null");
            if (string.IsNullOrEmpty(eventName)) return Fail(where, eventName, "イベント名が空");

            // uGUI が呼べるのは「裏の UdonBehaviour」の SendCustomEvent(string) だけ。
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
            if (udon == null)
            {
                return Fail(where, eventName, "裏の UdonBehaviour が見つからない");
            }

            int before = unityEvent.GetPersistentEventCount();

            UnityEventTools.AddStringPersistentListener(
                unityEvent, new UnityAction<string>(udon.SendCustomEvent), eventName);

            // ── 登録できたことにせず、必ず読み戻して確かめる。
            // Phase5-3 の最初の版はここを省いて「すべて繋がりました」と報告したのに、
            // Inspector の On Click は No Function のままでした。
            // 書いたつもりで書けていないのが一番たちが悪いので、戻り読みを挟みます。
            int after = unityEvent.GetPersistentEventCount();
            if (after <= before)
            {
                return Fail(where, eventName, "永続リスナーが増えなかった");
            }

            int index = after - 1;
            if (unityEvent.GetPersistentTarget(index) == null)
            {
                return Fail(where, eventName, "送り先が保存されなかった");
            }
            if (unityEvent.GetPersistentMethodName(index) != "SendCustomEvent")
            {
                return Fail(where, eventName, "呼ぶ関数が保存されなかった(No Function)");
            }

            BindCount++;
            return true;
        }

        private static bool Fail(string where, string eventName, string reason)
        {
            BindFailures++;
            Debug.LogWarning(
                "[UdonWorldUiKit] " + where + " に " + eventName + " を繋げませんでした("
                + reason + ")。\n"
                + "  → Inspector で Button の On Click に UdonBehaviour と\n"
                + "    SendCustomEvent(\"" + eventName + "\") を手で設定してください。");
            return false;
        }

        // ───────── Collider + Interact(実機で確実に押せる経路)─────────

        /// <summary>
        /// <b>ボタンを 2 通りの方法で押せるようにする。</b>
        ///
        /// <list type="number">
        /// <item>uGUI の <c>Button.onClick</c>(<see cref="Bind(Button, UdonSharpBehaviour, string)"/>)</item>
        /// <item>Collider + VRChat の「使う」(<see cref="AddInteract"/>)</item>
        /// </list>
        ///
        /// <b>なぜ 2 通りなのか</b><br/>
        /// Phase5-3 の最初の版は uGUI だけにしましたが、
        /// <b>実機でボタンが 1 つも押せませんでした</b>
        /// (配線は 39 個すべて正しく入っていたので、VRChat 側が
        ///  ワールド内 uGUI のレイキャストを拾っていない)。
        /// Phase5-2 の <c>UdonMediaControlButton</c> のコメントに
        /// 「VRChat でいちばん確実に押せるのは Collider + Interact」と
        /// 書いてあったとおりでした。
        ///
        /// <b>二重発火</b>は受け取る側(<c>UdonTransportView</c> /
        /// <c>UdonMediaListView</c>)が短い時間で同じ操作を捨てることで防ぎます。
        /// どちらの経路も同じイベント名に着地するので、そこ 1 か所で足ります。
        /// </summary>
        public static bool Wire(
            Button button, UdonSharpBehaviour target, string eventName, string caption)
        {
            bool bound = Bind(button, target, eventName);
            bool interact = AddInteract(button, target, eventName, caption);

            return bound || interact;
        }

        /// <summary>
        /// Collider と <c>UdonMediaControlButton</c> を足して、
        /// VRChat の「使う」でも押せるようにする。
        /// </summary>
        public static bool AddInteract(
            Button button, UdonSharpBehaviour target, string eventName, string caption)
        {
            if (button == null || target == null) return false;

            GameObject host = button.gameObject;
            var rect = host.GetComponent<RectTransform>();
            if (rect == null) return false;

            // 当たり判定。RectTransform の pivot は左上、BoxCollider は中心基準なので
            // 半分ぶんずらす。奥行きはレーザーが拾える程度に薄く。
            Vector2 size = rect.sizeDelta;
            var box = host.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, 4f);
            box.center = new Vector3(size.x * 0.5f, -size.y * 0.5f, 0f);
            box.isTrigger = true;

            bool needsCompile;
            var relay = UdonSharpSceneUtility.AddUdonSharpComponent(
                host, typeof(UdonMediaControlButton), out needsCompile) as UdonMediaControlButton;

            if (needsCompile) InteractNeedsCompile = true;

            if (relay == null)
            {
                InteractFailures++;
                Debug.LogWarning(
                    "[UdonWorldUiKit] " + host.name
                    + " に UdonMediaControlButton を付けられませんでした。"
                    + "このボタンは「使う」では押せません。", host);
                return false;
            }

            relay.Target = target;
            relay.EventName = eventName;

            // Label は触らせない。触ると Start でボタンの見出しを上書きしてしまう。
            relay.Label = null;
            relay.LabelText = "";

            ApplyInteractSettings(relay, caption);

            InteractCount++;
            return true;
        }

        /// <summary>
        /// 「使う」のときに出る文字と、届く距離を設定する。
        ///
        /// <c>UdonBehaviour</c> の既定は <c>Use</c> / 2 m です。
        /// 壁パネルは少し離れて操作するので広げておきます。
        /// フィールド名は SDK のバージョンで変わりうるので、
        /// 見つからなければ黙って既定のままにします。
        /// </summary>
        private static void ApplyInteractSettings(UdonSharpBehaviour relay, string caption)
        {
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(relay);
            if (udon == null) return;

            var serialized = new UnityEditor.SerializedObject(udon);

            var text = serialized.FindProperty("interactText");
            if (text != null && !string.IsNullOrEmpty(caption)) text.stringValue = caption;

            var proximity = serialized.FindProperty("proximity");
            if (proximity != null) proximity.floatValue = InteractDistance;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>「使う」が届く距離(m)。</summary>
        public const float InteractDistance = 5f;

        // ───────── proxy を UdonBehaviour へ書き戻す ─────────

        /// <summary>
        /// <b>これを呼ばないと、コードで入れた値が実機に届きません。</b>
        ///
        /// <c>UdonSharpBehaviour</c> のコンポーネントは<b>Inspector 用の見せかけ(proxy)</b>で、
        /// 実際に動くのは裏の <c>UdonBehaviour</c> が持つシリアライズ済みデータのほうです。
        /// Inspector で値を変えたときは UdonSharp のエディタが写してくれますが、
        /// <b>エディタスクリプトから代入したぶんは自分で写す必要があります</b>。
        ///
        /// Phase5-3 の最初の版はこれを呼んでおらず、
        /// <c>Rows</c> / <c>Lists</c> / <c>Core</c> などの参照が実機に届かないまま
        /// Prefab が保存されていました(Console の
        /// <c>OdinSerializer … ArgumentNullException: unityObject</c> がその副作用)。
        /// </summary>
        /// <returns>写せなかった数(0 なら全部通った)。</returns>
        public static int SyncProxies(GameObject root)
        {
            if (root == null) return 0;

            int failed = 0;
            var behaviours = root.GetComponentsInChildren<UdonSharpBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                UdonSharpBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;

                // 裏がいない proxy を写そうとすると Odin が
                // ArgumentNullException を投げてそこで止まる。先に弾く。
                if (UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour) == null)
                {
                    failed++;
                    Debug.LogWarning(
                        "[UdonWorldUiKit] " + behaviour.name + " の "
                        + behaviour.GetType().Name + " に裏の UdonBehaviour がありません。"
                        + "この部品の設定は実機に届きません。", behaviour);
                    continue;
                }

                UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
                SyncedCount++;
            }

            return failed;
        }

        // ───────── 共通 ─────────

        /// <summary>
        /// 既定のフォント。名前が Unity のバージョンで変わっているので分けます
        /// (存在しない名前を渡すと Console にエラーが出るため、条件コンパイルで避ける)。
        /// </summary>
        public static Font BuiltinFont()
        {
#if UNITY_2022_2_OR_NEWER
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        }

        /// <summary>ワールドに置く Canvas を作る。</summary>
        public static RectTransform WorldCanvas(
            GameObject parent, string name, float width, float height, float metersPerPixel)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;   // ワールドで文字がぼやけないように

            go.AddComponent<GraphicRaycaster>();

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = new Vector3(metersPerPixel, metersPerPixel, metersPerPixel);

            // ここから作るボタンの「実寸」を測れるようにしておく
            CurrentMetersPerPixel = metersPerPixel;
            return rect;
        }
    }
}
#endif
