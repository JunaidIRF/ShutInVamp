// Short flickering flames along the bottom edge of the screen, for a full-screen UI Image.
// The Image's alpha drives the strength.
Shader "ShutInVamp/UI/ScreenFire"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Height ("Flame Height", Range(0, 1)) = 0.13
        _Speed ("Rise Speed", Float) = 0.9
        _Opacity ("Opacity", Range(0, 1)) = 0.6
        _Hot ("Hot", Color) = (1, 0.85, 0.4, 1)
        _Cool ("Cool", Color) = (1, 0.3, 0.05, 1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off Lighting Off ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            float _Height, _Speed, _Opacity;
            fixed4 _Hot, _Cool;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float hash (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise (float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 p = float2(i.uv.x * aspect * 6, i.uv.y * 4 - _Time.y * _Speed * 4);
                float n = noise(p) * 0.65 + noise(p * 2.1) * 0.35;

                float heat = 1 - i.uv.y / _Height;              // 1 at the bottom edge, 0 at the flame top
                float h = heat + (n - 0.5) * 0.8;
                float a = smoothstep(0.1, 0.6, h);
                fixed3 col = lerp(_Cool.rgb, _Hot.rgb, smoothstep(0.5, 1, h));
                return fixed4(col, a * _Opacity * i.color.a);
            }
            ENDCG
        }
    }
}
