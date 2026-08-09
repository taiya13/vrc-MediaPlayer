// 画面 1 枚で 2 つの動画を混ぜるためのシェーダー。Phase7-5。
//
// ■ なぜ要るのか
//
// クロスフェードには動画プレイヤーが 2 つ要りますが、
// 画面(モニター)は 1 枚のままにしたい。
// AVPro の VRCAVProVideoScreen は「どの Renderer の、どのテクスチャ欄へ書くか」を
// 指定できるので、
//
//   プレイヤー A → この Renderer の _MainTex
//   プレイヤー B → この Renderer の _SecondTex
//
// と<b>同じ 1 枚の Renderer に別々の欄で</b>書かせ、ここで混ぜます。
// Renderer が 1 つなので、板も描画も 1 枚のままです。
//
// ■ 負荷
//
// 混ざっていないとき(_Blend = 0 または 1)は、片方のテクスチャしか要りません。
// が、分岐を書くと GPU では両方の枝を通ることが多く、かえって遅くなります。
// テクスチャ 2 枚を読んで lerp するだけのほうが軽く、予測もしやすいので、
// 常に 2 枚読む形にしてあります(全画面 1 枚ぶんの追加コストで、
// Quest でも問題になる量ではありません)。
//
// ■ ライトを受けません
//
// 映像は自分で光っているものなので、Unlit です。
// Standard にすると、ライトを置いていないワールドで真っ黒になります
// (Phase7-2 で実際に踏んだ問題)。

Shader "SmartMediaPlatform/Crossfade"
{
    Properties
    {
        // 名前を _MainTex にしてあるのは、
        // クロスフェードを使わない構成(プレイヤー 1 つ)でも
        // そのまま Unlit/Texture と同じように動かすためです。
        _MainTex ("いま鳴っている映像 (A)", 2D) = "black" {}
        _SecondTex ("次の映像 (B)", 2D) = "black" {}

        // 0 = A だけ / 1 = B だけ。Udon が毎フレーム書き換えます。
        _Blend ("混ざり具合", Range(0, 1)) = 0
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
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _SecondTex;
            float4 _SecondTex_ST;
            float _Blend;

            v2f vert (appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                fixed4 a = tex2D(_MainTex, i.uv);
                fixed4 b = tex2D(_SecondTex, i.uv);

                // 絵は素直に重ねる。音と違って、真ん中で薄くなることはない。
                return lerp(a, b, _Blend);
            }
            ENDCG
        }
    }

    // 何かの理由でこのシェーダーが使えないときは、
    // ふつうの Unlit として出す(絵は A だけになるが、真っ白にはならない)。
    FallBack "Unlit/Texture"
}
