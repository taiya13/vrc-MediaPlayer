#if UNITY_EDITOR && VRC_SDK_VRCSDK3
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
        public static readonly Color RowHighlight = new Color(0.15f, 0.42f, 0.66f, 0.55f);
        public static readonly Color TrackBack = new Color(0.25f, 0.27f, 0.32f, 1f);
        public static readonly Color TrackFill = new Color(0.35f, 0.72f, 0.95f, 1f);

        public static readonly Color TextPrimary = new Color(0.95f, 0.96f, 0.98f, 1f);
        public static readonly Color TextSecondary = new Color(0.66f, 0.70f, 0.78f, 1f);

        /// <summary><see cref="Bind(Button, UdonSharpBehaviour, string)"/> が失敗した回数。</summary>
        public static int BindFailures { get; private set; }

        public static void ResetBindFailures()
        {
            BindFailures = 0;
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

            return button;
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
            if (button == null) return Fail("(Button が null)", eventName);
            return Bind(button.onClick, target, eventName, button.name);
        }

        /// <summary>
        /// <c>Slider.onValueChanged</c> から <paramref name="eventName"/> を呼べるようにする。
        /// <b>値は渡りません</b>(Udon のイベントは引数を取れないため)。
        /// 受け取る側がつまみを読みに行ってください。
        /// </summary>
        public static bool Bind(Slider slider, UdonSharpBehaviour target, string eventName)
        {
            if (slider == null) return Fail("(Slider が null)", eventName);
            return Bind(slider.onValueChanged, target, eventName, slider.name);
        }

        private static bool Bind(
            UnityEventBase unityEvent, UdonSharpBehaviour target, string eventName, string where)
        {
            if (unityEvent == null) return Fail(where, eventName);
            if (target == null) return Fail(where, eventName);
            if (string.IsNullOrEmpty(eventName)) return Fail(where, eventName);

            // uGUI が呼べるのは「裏の UdonBehaviour」の SendCustomEvent(string) だけ。
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
            if (udon == null) return Fail(where, eventName);

            UnityEventTools.AddStringPersistentListener(
                unityEvent, new UnityAction<string>(udon.SendCustomEvent), eventName);
            return true;
        }

        private static bool Fail(string where, string eventName)
        {
            BindFailures++;
            Debug.LogWarning(
                "[UdonWorldUiKit] " + where + " に " + eventName + " を繋げませんでした。\n"
                + "  → Inspector で Button の On Click に UdonBehaviour と\n"
                + "    SendCustomEvent(\"" + eventName + "\") を手で設定してください。");
            return false;
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
            return rect;
        }
    }
}
#endif
