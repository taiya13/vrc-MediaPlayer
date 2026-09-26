#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System;
using SmartMediaPlatform.Video.EditorTools;
using SmartMediaPlatform.World.Udon;
using SmartMediaPlatform.World.Udon.UI;
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
        //
        // Phase7-3 から、実体は <see cref="UdonMediaTheme"/> にあります。
        // ここに残っているのは<b>今までの名前で呼べるようにするため</b>の別名です。
        // 新しく書くときは Theme のほうを直接使ってください。

        public static readonly Color Backplate = UdonMediaTheme.Base;
        public static readonly Color Section = UdonMediaTheme.Surface;
        public static readonly Color ButtonFace = UdonMediaTheme.SurfaceRaised;
        public static readonly Color ButtonAccent = UdonMediaTheme.Accent;
        public static readonly Color RowFace = UdonMediaTheme.SurfaceRaised;

        // Phase7-3 で「1 行おきに濃さを変える」のをやめました。
        // 縞模様は、行の中身より先に縞のほうが目に入ります。
        // 余白で区切るほうが、目が縦に流れて速く読めます。
        public static readonly Color RowFaceAlt = UdonMediaTheme.SurfaceRaised;

        /// <summary>いま鳴っている行に敷く色。強調色をうっすら。</summary>
        public static readonly Color RowHighlight = UdonMediaTheme.AccentWash;

        // 押した直後だけ一瞬出る。「使う」で押したときの手応えになる。
        public static readonly Color RowPressed = UdonMediaTheme.SurfacePressed;

        public static readonly Color TrackBack = new Color(0f, 0f, 0f, 0.12f);
        public static readonly Color TrackFill = UdonMediaTheme.Accent;

        public static readonly Color TextPrimary = UdonMediaTheme.TextPrimary;
        public static readonly Color TextSecondary = UdonMediaTheme.TextSecondary;

        /// <summary>いま鳴っている行の見出し。強調色そのものにする。</summary>
        public static readonly Color TextNowPlaying = UdonMediaTheme.Accent;

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

        /// <summary>
        /// <b>角の丸い板。</b>Phase7-3。
        ///
        /// 角丸は 9 分割の絵で描きます(<see cref="UdonMediaTheme.RoundedSprite"/>)。
        /// 絵が用意できなかったときは<b>四角のまま出します</b> —
        /// 見た目が少し硬くなるだけで、動きは変わりません。
        /// </summary>
        public static Image RoundedPlate(
            Transform parent, string name, float x, float y, float width, float height,
            Color color, int radius)
        {
            var image = Plate(parent, name, x, y, width, height, color);
            ApplyRadius(image, radius);
            return image;
        }

        /// <summary>
        /// <b>凍った硝子のカード。</b>Frost の基本部品(Phase7-7)。
        ///
        /// 3 枚を重ねて 1 枚の硝子に見せます。
        /// <list type="number">
        /// <item><b>影</b> …… 少し下にずらして敷く。「浮いている」の下半分</item>
        /// <item><b>面</b> …… 半透明の白。<b>後ろのワールドが透ける</b></item>
        /// <item><b>縁の光</b> …… 上端に 2 px の白い線。「浮いている」の上半分</item>
        /// </list>
        ///
        /// <b>影と縁の光は必ず対で置きます。</b>片方だけだと、
        /// 硝子ではなく<b>「ただの薄い色の板」</b>にしか見えません。
        /// </summary>
        /// <returns>面(中身はこの子として置く)。</returns>
        public static Image GlassCard(
            Transform parent, string name, float x, float y, float width, float height,
            Color face, int radius)
        {
            RoundedPlate(
                parent, name + "Shadow", x, y + GlassShadowDrop, width, height,
                UdonMediaTheme.Shadow, radius).raycastTarget = false;

            Image card = RoundedPlate(parent, name, x, y, width, height, face, radius);

            AddGlassEdge(card, width, radius);
            return card;
        }

        /// <summary>影が要らないときの硝子(入れ子の中など)。</summary>
        public static Image GlassPlate(
            Transform parent, string name, float x, float y, float width, float height,
            Color face, int radius)
        {
            Image card = RoundedPlate(parent, name, x, y, width, height, face, radius);
            AddGlassEdge(card, width, radius);
            return card;
        }

        /// <summary>硝子の上端に光る線を 1 本引く。</summary>
        public static void AddGlassEdge(Image card, float width, int radius)
        {
            if (card == null) return;

            // 角の丸みに掛からないよう、左右を少し内側から始める。
            float inset = radius * 0.7f;
            float edgeWidth = width - inset * 2f;
            if (edgeWidth <= 4f) return;

            Image edge = Plate(
                card.transform, "Edge", inset, 0f, edgeWidth, 2f, UdonMediaTheme.GlassEdge);
            edge.raycastTarget = false;
        }

        /// <summary>影を落とす量(px)。大きくすると浮きすぎて安っぽくなる。</summary>
        public const float GlassShadowDrop = 4f;

        /// <summary>板を角丸にする。すでに置いてあるものにも使えます。</summary>
        public static void ApplyRadius(Image image, int radius)
        {
            if (image == null || radius <= 0) return;

            Sprite sprite = UdonMediaTheme.RoundedSprite(radius);
            if (sprite == null) return;

            image.sprite = sprite;
            image.type = Image.Type.Sliced;

            // 小さい部品では角丸が枠より大きくなることがある。
            // これを入れておくと、はみ出さずに詰めて描いてくれる。
            image.pixelsPerUnitMultiplier = 1f;
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

        /// <summary>
        /// <b>枠に収まるまで小さくなる文字。</b>Phase7-4。
        ///
        /// <b>なぜ要るのか</b><br/>
        /// 曲名の長さはこちらで決められません。
        /// 「SEKAI NO OWARI「Habit」」のような見出しを固定の大きさで出すと、
        /// <b>途中で切れて「SEKAI NO」しか読めません</b>。
        /// 切れた名前は、短い名前より役に立ちません —— どれがどれか分からないためです。
        ///
        /// <b>短い名前は大きいまま</b>です。小さくなるのは入り切らないときだけなので、
        /// ふだんの見た目は変わりません。
        /// </summary>
        /// <param name="size">入り切るときの大きさ(これより大きくはならない)。</param>
        /// <param name="minimum">これより小さくはしない。読めなくなるため。</param>
        public static Text FittedLabel(
            Transform parent, string name, float x, float y, float width, float height,
            int size, int minimum, TextAnchor anchor, Color color)
        {
            Text text = Label(parent, name, x, y, width, height, size, anchor, color);

            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = size;
            text.resizeTextMinSize = minimum;

            // 縮めて収めるので、折り返しは要らない。
            // 折り返すと、1 行に収まる名前まで 2 行になって余白が崩れます。
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            return text;
        }

        /// <summary>
        /// <b>角の丸い押せるボタン。</b>Phase7-3。
        /// 文字の色は面の色から決めます(強調色の上は暗い文字、灰色の上は明るい文字)。
        /// </summary>
        public static Button RoundedButton(
            Transform parent, string name, float x, float y, float width, float height,
            string caption, int fontSize, Color face, int radius, out Text label)
        {
            Button button = PushButton(
                parent, name, x, y, width, height, caption, fontSize, face, out label);

            ApplyRadius(button.targetGraphic as Image, radius);

            // ── 硝子の縁(Frost / Phase7-7)。
            //    ボタンも硝子の一部なので、上端に同じ光を入れます。
            //    <b>これが無いボタンだけ「別の素材」に見えます</b> —— 統一感は
            //    形や色より、こういう細部の一致から生まれます。
            AddGlassEdge(button.targetGraphic as Image, width, radius);

            if (label != null) label.color = OnFace(face);
            return button;
        }

        /// <summary>その面の上で読める文字色。明るい面には暗い文字を置く。</summary>
        public static Color OnFace(Color face)
        {
            // 人の目が感じる明るさ。緑がいちばん効くので重みを変えてある。
            //
            // Phase7-6 で配色が明るくなり、<b>向きが逆になりました</b>。
            // 白い面には濃い文字、色の付いた面(強調色)には白い文字です。
            float luminance = face.r * 0.2126f + face.g * 0.7152f + face.b * 0.0722f;
            return luminance > 0.5f ? UdonMediaTheme.TextPrimary : UdonMediaTheme.OnAccent;
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

            // ── 触れたときにはっきり明るくする。
            //    ワールドではカーソルが見えないことがあるので、
            //    「いまどこを指しているか」は面の明るさでしか分かりません。
            //    画面用の常識より強めに変えるのが正解です。
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            // 1.55 は「暗い灰色ではっきり分かり、強調色では白飛びしない」ぎりぎり。
            // ColorTint は掛け算なので、明るい面ほど効きが弱く見えます。
            colors.highlightedColor = new Color(1.55f, 1.55f, 1.55f, 1f);
            colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
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
            ApplyRadius(back, Mathf.RoundToInt(height * 0.5f));

            var fill = Plate(back.transform, "Fill", 0f, 0f, width, height, TrackFill);
            fill.raycastTarget = false;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            return fill;
        }

        /// <summary>
        /// <b>つかんで動かせるバー。</b>Phase7-3(シークバー・音量)。
        ///
        /// <b>触れる高さと、見えている高さを分けます。</b>
        /// 見た目が 10 px でも、レーザーで 10 px を狙うのは無理です。
        /// 当たり判定だけ <paramref name="touchHeight"/> まで広げ、
        /// バーそのものはその中で細く描きます。
        ///
        /// <b>つまみは大きめの丸</b>にします。つかむ場所が見えていないと、
        /// 「動かせる」ことに気付いてもらえません。
        /// </summary>
        /// <param name="fill">伸び縮みする側(位置を映すのに使う)。</param>
        public static Slider DragBar(
            Transform parent, string name, float x, float y, float width,
            float touchHeight, float barHeight, out Image fill)
        {
            RectTransform root = Place(parent, name, x, y, width, touchHeight);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;

            float barY = (touchHeight - barHeight) * 0.5f;
            int barRadius = Mathf.RoundToInt(barHeight * 0.5f);

            // 触れる面。透明でよいが raycastTarget は入れておく
            // (これが無いと、線の外側をつかんでも反応しない)。
            var hit = Plate(root, "Hit", 0f, 0f, width, touchHeight, new Color(0f, 0f, 0f, 0f));
            hit.raycastTarget = true;

            var back = Plate(root, "Track", 0f, barY, width, barHeight, TrackBack);
            back.raycastTarget = false;
            ApplyRadius(back, barRadius);

            // Fill Area / Fill …… Slider が伸ばすのはこの中身。
            //
            // <b>ここは Track と<b>まったく同じ場所</b>に置きます。</b>
            // Phase7-4 まで、置いたあとに anchor を上端(anchorMin.y = 1)へ
            // 書き換えていました。そのせいで色の付いた側だけが
            // <b>触れる面の上端</b>へ寄り、灰色の床から浮いて見えていました
            // (「バーの緑がずれている」の正体です)。
            // Slider が書き換えるのは<b>この中の Fill だけ</b>なので、
            // 入れ物のほうは Place の位置のままにしておくのが正解です。
            RectTransform fillArea = Place(root, "FillArea", 0f, barY, width, barHeight);

            var fillImage = Plate(fillArea, "Fill", 0f, 0f, width, barHeight, TrackFill);
            fillImage.raycastTarget = false;
            ApplyRadius(fillImage, barRadius);

            var fillRect = fillImage.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            // ── つまみ。<b>小さな丸</b>にする。
            //
            //    Unity の Slider は、つまみを<b>置き場所の高さいっぱいに引き伸ばします</b>
            //    (毎フレーム anchorMin.y = 0 / anchorMax.y = 1 を書き込む)。
            //    だから置き場所を触れる面と同じ高さにすると、
            //    <b>縦に長い棒</b>になります(Phase7-3 の最初の版がそれでした)。
            //    置き場所のほうを<b>丸の直径ちょうど</b>に細めれば、丸のままです。
            float knob = Mathf.Min(touchHeight * 0.55f, 26f);

            RectTransform handleArea = Place(root, "HandleArea", 0f, 0f, width, knob);
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.pivot = new Vector2(0.5f, 0.5f);
            handleArea.offsetMin = new Vector2(knob * 0.5f, -knob * 0.5f);
            handleArea.offsetMax = new Vector2(-knob * 0.5f, knob * 0.5f);

            var handle = Plate(handleArea, "Handle", 0f, 0f, knob, knob, TextPrimary);
            handle.raycastTarget = true;
            ApplyRadius(handle, Mathf.RoundToInt(knob * 0.5f));

            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(0f, 1f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(knob, 0f);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.value = 0f;

            CheckTouchSize(root.gameObject, width, touchHeight);

            fill = fillImage;
            return slider;
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
        /// <summary>ホイールやドラッグで動いたことを受け取る。</summary>
        public static bool Bind(ScrollRect scroll, UdonSharpBehaviour target, string eventName)
        {
            return Bind(
                scroll != null ? scroll.onValueChanged : null, target, eventName, "ScrollRect");
        }

        /// <summary>つまみを動かしたことを受け取る。</summary>
        public static bool Bind(Scrollbar bar, UdonSharpBehaviour target, string eventName)
        {
            return Bind(bar != null ? bar.onValueChanged : null, target, eventName, "Scrollbar");
        }

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
        /// つかんで動かすバーを繋ぐ。
        ///
        /// <b>「使う」(Interact)は付けません。</b>
        /// 押した瞬間に 1 回だけ届く仕組みなので、
        /// <b>つまみを動かす操作とは相性が悪い</b>ためです。
        /// バーは uGUI のドラッグ(PC のマウス・VR のレーザー)で動かします。
        /// </summary>
        public static bool WireSlider(
            Slider slider, UdonSharpBehaviour target, string eventName, string caption)
        {
            return Bind(slider, target, eventName);
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

            // ── 文字を出したくないときは<b>空白 1 文字</b>を書き込みます(Phase7-6)。
            //
            //    空文字("")では消えませんでした。VRChat は空を
            //    「何も設定していない」と見なして<b>既定の「使う」に戻す</b>ためです。
            //    空白 1 文字なら「設定されている」と見なされ、中身は何もありません。
            //    <b>手のアイコンは VRChat が描くので消せません</b>(文字だけ消えます)。
            var text = serialized.FindProperty("interactText");
            if (text != null && caption != null)
            {
                text.stringValue = caption.Length == 0 ? " " : caption;
            }

            var proximity = serialized.FindProperty("proximity");
            if (proximity != null) proximity.floatValue = InteractDistance;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>「使う」が届く距離(m)。</summary>
        public const float InteractDistance = 5f;

        // ───────── 「使う」で動かすバー(Phase7-6)─────────

        /// <summary>
        /// <b>バーの上に「使う」で押せる区画を並べる。</b>Phase7-6。
        ///
        /// <b>実機で uGUI のポインターが届いていない</b>ことが分かったので、
        /// つまみを掴む操作を<b>「使う」で狙う</b>操作へ置き換えます。
        /// バーの見た目はそのままで、上に透明な当たり判定を並べるだけです。
        ///
        /// uGUI の <c>Slider</c> / <c>Scrollbar</c> は残してあります。
        /// 値はこの区画から書き込まれるので、<b>今までの表示・処理はそのまま</b>動きます。
        /// </summary>
        /// <param name="parent">バーと同じ親。</param>
        /// <param name="bar">動かすバー(<c>Scrollbar</c> のときは null)。</param>
        /// <param name="scrollBar">動かすスクロールバー(<c>Slider</c> のときは null)。</param>
        /// <param name="invert">上が 0 のスクロールバー用に値を裏返す。</param>
        /// <param name="target">値を書き込んだあとに知らせる相手(保険)。</param>
        public static UdonValueStrip ValueStrip(
            Transform parent, string name, float x, float y, float width, float height,
            int segments, Slider bar, Scrollbar scrollBar, UdonListScroller scroller,
            bool invert, UdonSharpBehaviour target, string eventName, string caption)
        {
            if (segments < 2) segments = 2;

            RectTransform root = Place(parent, name, x, y, width, height);

            // ── ほんの少し手前に出す(Phase7-6)。
            //
            //    一覧のスクロールは、行や「予定へ」と<b>同じ場所に重なります</b>。
            //    当たり判定がぴったり同じ高さにあると、「使う」がどちらを拾うか
            //    決まらず、<b>送ったつもりが曲が始まる</b>ことになります
            //    (スクロールだけ動かなかったのは、たぶんこれです)。
            //    1 枚ぶん手前に置いて、必ずこちらが先に当たるようにします。
            root.localPosition = new Vector3(
                root.localPosition.x, root.localPosition.y, -3f);

            bool needsCompile;
            var strip = UdonSharpSceneUtility.AddUdonSharpComponent(
                root.gameObject, typeof(UdonValueStrip), out needsCompile) as UdonValueStrip;

            if (needsCompile) InteractNeedsCompile = true;

            if (strip == null)
            {
                InteractFailures++;
                Debug.LogWarning(
                    "[UdonWorldUiKit] " + name + " に UdonValueStrip を付けられませんでした。"
                    + "このバーは「使う」では動かせません。", root.gameObject);
                return null;
            }

            strip.Bar = bar;
            strip.ScrollBar = scrollBar;
            strip.Scroller = scroller;
            strip.SegmentCount = segments;
            strip.Invert = invert;
            strip.Target = target;
            strip.EventName = eventName == null ? "" : eventName;

            bool vertical = height > width;
            float cellW = vertical ? width : width / segments;
            float cellH = vertical ? height / segments : height;

            for (int i = 0; i < segments; i++)
            {
                float cellX = vertical ? 0f : cellW * i;
                float cellY = vertical ? cellH * i : 0f;

                RectTransform cell = Place(
                    root, "Cell" + i, cellX, cellY, cellW, cellH);

                var box = cell.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(cellW, cellH, 4f);
                box.center = new Vector3(cellW * 0.5f, -cellH * 0.5f, 0f);
                box.isTrigger = true;

                bool cellNeedsCompile;
                var relay = UdonSharpSceneUtility.AddUdonSharpComponent(
                    cell.gameObject, typeof(UdonMediaControlButton), out cellNeedsCompile)
                    as UdonMediaControlButton;

                if (cellNeedsCompile) InteractNeedsCompile = true;

                if (relay == null)
                {
                    InteractFailures++;
                    continue;
                }

                relay.Target = strip;
                relay.EventName = "Pick";
                relay.IndexVariable = "PickedIndex";
                relay.Index = i;
                relay.Label = null;
                relay.LabelText = "";

                ApplyInteractSettings(relay, caption);
                InteractCount++;
            }

            return strip;
        }

        /// <summary>
        /// <b>「使う」だけで押せる場所を置く。</b>Phase7-6。
        ///
        /// uGUI の <c>Button</c> を持ちません。<b>見た目のない当たり判定</b>だけです。
        /// 検索欄のように、押させたい相手がすでに別の部品
        /// (<c>InputField</c> など)であるときに、その上へ重ねて使います。
        /// </summary>
        public static UdonMediaControlButton InteractArea(
            Transform parent, string name, float x, float y, float width, float height,
            UdonSharpBehaviour target, string eventName, string caption)
        {
            RectTransform rect = Place(parent, name, x, y, width, height);

            // 一段手前に出して、下にある部品との取り合いを避ける。
            rect.localPosition = new Vector3(
                rect.localPosition.x, rect.localPosition.y, -2f);

            var box = rect.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(width, height, 4f);
            box.center = new Vector3(width * 0.5f, -height * 0.5f, 0f);
            box.isTrigger = true;

            bool needsCompile;
            var relay = UdonSharpSceneUtility.AddUdonSharpComponent(
                rect.gameObject, typeof(UdonMediaControlButton), out needsCompile)
                as UdonMediaControlButton;

            if (needsCompile) InteractNeedsCompile = true;

            if (relay == null)
            {
                InteractFailures++;
                return null;
            }

            relay.Target = target;
            relay.EventName = eventName;
            relay.Label = null;
            relay.LabelText = "";

            ApplyInteractSettings(relay, caption);
            InteractCount++;
            return relay;
        }

        /// <summary>
        /// <b>「使う」で押せる文字キーを 1 つ作る。</b>Phase7-6。
        /// 押すと <paramref name="strip"/>(キーボード)へ何番目のキーかを伝えます。
        /// </summary>
        public static Button KeyboardKey(
            Transform parent, string name, float x, float y, float width, float height,
            string caption, int fontSize, Color face, int radius,
            UdonSharpBehaviour target, string eventName, string indexVariable, int index)
        {
            Text label;
            Button button = RoundedButton(
                parent, name, x, y, width, height, caption, fontSize, face, radius, out label);

            // uGUI 側(将来ポインターが通るようになったとき用)。
            Bind(button, target, eventName);

            GameObject host = button.gameObject;
            var rect = host.GetComponent<RectTransform>();
            if (rect == null) return button;

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
                return button;
            }

            relay.Target = target;
            relay.EventName = eventName;
            relay.IndexVariable = indexVariable == null ? "" : indexVariable;
            relay.Index = index;
            relay.Label = null;
            relay.LabelText = "";

            ApplyInteractSettings(relay, caption);
            InteractCount++;
            return button;
        }

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

            // ── Canvas に当たり判定を付ける。<b>これが無いと uGUI が一切効きません。</b>
            //
            //    VRChat のレーザーは、まず<b>物理の当たり判定</b>を探します。
            //    そこに何も無ければ、その先にある Canvas は見えていないのと同じで、
            //    <b>押す・つかむ・ホイールのどれも届きません</b>。
            //
            //    Phase7-2 まで、これが無いのに<b>ボタンだけは動いていました</b>。
            //    ボタンには別途「使う」(Interact)を付けてあるからです。
            //    そのせいで「uGUI は効いている」と誤解したまま、
            //    <b>つまみ・スクロール・音量が実機でまったく動かない</b>状態が続いていました
            //    (押せるものは押せるので、原因が見えにくい)。
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(width, height, 0.01f / metersPerPixel);
            collider.center = Vector3.zero;

            // ── VRC_UiShape。<b>これが無いと、World Space Canvas は
            //    VRChat のポインター(VR のレーザー・Desktop のマウス)で
            //    操作できないことがあります</b>。
            //
            //    EventSystem + GraphicRaycaster は Unity 標準の仕組みで、
            //    Editor や Desktop では動きます。ところが VRChat 実機では、
            //    <b>「このワールドの Canvas は触ってよい」という印を
            //    VRC_UiShape で明示しないと、ボタンだけは Interact(使う)で
            //    動いていても、Slider や InputField のドラッグ操作は
            //    ポインターがそもそも Canvas に届かず沈黙します</b>。
            //    「使う」で押せるボタンだけ動き、つまみ・音量・検索欄が
            //    まったく反応しない、という報告と符合します。
            //
            //    型は SDK の版で名前空間が変わることがあるので、
            //    名前で探します。見つからなければ、Editor 確認用として
            //    そのまま進めます(実機では要調査、とログに残す)。
            Type shapeType = FindTypeByName("VRC_UiShape") ?? FindTypeByName("VRCUiShape");
            if (shapeType != null)
            {
                go.AddComponent(shapeType);
            }
            else
            {
                Debug.LogWarning(
                    "[UdonWorldUiKit] VRC_UiShape が見つかりませんでした。"
                    + "実機で uGUI(つまみ・音量・検索欄)が反応しない場合は、"
                    + "使っている VRChat SDK にこのコンポーネントがあるか確認してください。");
            }

            // ここから作るボタンの「実寸」を測れるようにしておく
            CurrentMetersPerPixel = metersPerPixel;
            return rect;
        }

        /// <summary>名前だけで型を探す(SDK の名前空間が版で変わっても壊れないように)。</summary>
        private static Type FindTypeByName(string simpleName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }

                if (types == null) continue;

                foreach (var type in types)
                {
                    if (type != null && type.Name == simpleName) return type;
                }
            }
            return null;
        }

        /// <summary>
        /// <b>シーンに EventSystem を用意する。</b>Phase7-3。
        ///
        /// <b>これが無いと uGUI は一切動きません。</b>
        /// 押す・つかむ・ホイール —— どれも EventSystem が配っている仕組みなので、
        /// 1 つも無いシーンでは<b>uGUI 側は完全に沈黙します</b>。
        ///
        /// <b>それでもボタンだけは動いていました。</b>ボタンには別途
        /// Collider +「使う」(Interact)を付けてあるからです。
        /// そのせいで「uGUI は効いている」と誤解したまま、
        /// <b>つまみ・スクロール・音量が実機でまったく動かない</b>状態が続いていました。
        /// 押せるものは押せるので、いちばん原因が見えにくい壊れ方です。
        ///
        /// <b>シーンに 1 つだけ</b>置きます。2 つあると、どちらが配るかが
        /// 決まらず、かえって動かなくなります。
        /// </summary>
        /// <returns>新しく作ったら true。すでにあれば false。</returns>
        public static bool EnsureEventSystem()
        {
            var existing = UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            if (existing != null) return false;

            var go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();

            // VRChat は実行時に自前の入力モジュールへ差し替えますが、
            // 何も付いていないと差し替え先が見つからないことがあるので置いておきます。
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Smart Media Platform: EventSystem");
            return true;
        }

        /// <summary>シーンにある EventSystem の数(診断用)。</summary>
        public static int CountEventSystems()
        {
            var found = UnityEngine.Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>();
            return found == null ? 0 : found.Length;
        }

        /// <summary>
        /// すでに置いてある Canvas に VRC_UiShape を足す。
        /// Phase7-2 以前に作った Prefab には無いので、置き直さずに直せるようにします。
        /// </summary>
        /// <returns>足した数。</returns>
        public static int AddUiShapeToExistingCanvases(GameObject root)
        {
            if (root == null) return 0;

            Type shapeType = FindTypeByName("VRC_UiShape") ?? FindTypeByName("VRCUiShape");
            if (shapeType == null) return 0;

            var canvases = root.GetComponentsInChildren<Canvas>(true);
            int added = 0;

            foreach (var canvas in canvases)
            {
                if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) continue;
                if (canvas.GetComponent(shapeType) != null) continue;

                canvas.gameObject.AddComponent(shapeType);
                added++;
            }

            return added;
        }
    }
}
#endif
