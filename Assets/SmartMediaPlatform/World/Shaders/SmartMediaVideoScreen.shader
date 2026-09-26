// 動画を映す画面のシェーダー。Phase8-3。
//
// ■ なぜ要るのか
//
// ミラーに映った画面は左右が逆になり、動画の中の字幕や歌詞が裏返って読めません。
// そこで「ミラーが描いているときだけ」映像を左右反転します。
// ミラーの中で反転した画面をもう一度反転するので、鏡越しでも文字が正しく読めます。
// 画面を直接見ているときは何も変わりません。
//
// ■ ミラーかどうかの見分け方
//
// VRChat は全シェーダーに _VRChatMirrorMode を配っています。
//   0 = ふつうの描画 / 1 = VR のミラー / 2 = デスクトップのミラー
// Unity のエディタではこの値が配られない(0 のまま)ので、エディタでは反転しません。
//
// ■ 見え方は Unity の Unlit/Texture と同じです
//
// これまでの画面は Unlit/Texture でした。色の出し方・霧の掛かり方を変えないよう、
// 中身を Unlit/Texture と同じにして、UV の左右反転を 1 か所足しただけにしてあります。
// 反転は「タイリング / オフセット」を掛ける前の UV に対して行うので、
// Inspector で拡大・位置合わせをしていても、ずれません。

Shader "SmartMediaPlatform/VideoScreen"
{
    Properties
    {
        // 動画プレイヤーが書き込む欄。名前は Unlit/Texture と同じにしてあります。
        _MainTex ("映像", 2D) = "black" {}

        // 1 = ミラーの中では左右を反転する(字幕が読めるように) / 0 = しない
        [ToggleUI] _MirrorFlip ("ミラーの中では左右を反転する", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _MirrorFlip;

            // VRChat が配る値。宣言しておくだけで、VRChat の中では自動で入ります。
            float _VRChatMirrorMode;

            v2f vert (appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);

                // ミラーが描いていて、反転が有効なときだけ 1。
                // 分岐を書かずに掛け算で選ぶ(どの GPU でも同じ動きになるように)。
                float flip = step(0.5, _MirrorFlip) * step(0.5, _VRChatMirrorMode);

                float2 uv = v.uv;
                uv.x = lerp(uv.x, 1.0 - uv.x, flip);

                o.uv = TRANSFORM_TEX(uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                fixed4 col = tex2D(_MainTex, i.uv);
                UNITY_APPLY_FOG(i.fogCoord, col);

                // Unlit/Texture と同じく、不透明として出す(動画の透明度に引きずられない)。
                UNITY_OPAQUE_ALPHA(col.a);
                return col;
            }
            ENDCG
        }
    }

    // 何かの理由でこのシェーダーが使えないときは、これまでと同じ Unlit/Texture で出す
    // (ミラーの中の反転だけが効かなくなる)。
    FallBack "Unlit/Texture"
}
