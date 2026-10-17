// Soft wind streaks flowing outward near the screen edges (speed feel), for a full-screen UI Image.
// The Image's alpha is the strength (0 = invisible).
Shader "ShutInVamp/UI/SpeedStreaks"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Streak Colour", Color) = (0.92, 0.96, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.6
        _Density ("Streaks Around", Float) = 150
        _Coverage ("Share Of Streaks Shown", Range(0, 1)) = 0.4
        _Length ("Streak Spacing (along)", Float) = 2.2
        _Speed ("Flow Speed", Float) = 2.5
        _Inner ("Clear Centre Radius", Range(0, 2)) = 0.55
        _Outer ("Full Strength Radius", Range(0, 2)) = 1.1
        _Flicker ("Re-roll Rate", Float) = 4
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

            fixed4 _Color;
            float _Opacity, _Density, _Coverage, _Length, _Speed, _Inner, _Outer, _Flicker;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float hash (float n) { return frac(sin(n * 127.1) * 43758.5453); }

            fixed4 frag (v2f i) : SV_Target
            {
                float strength = i.color.a;
                if (strength <= 0.001) return 0;

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 p = (i.uv - 0.5) * float2(aspect, 1) * 2;          // centre = 0, top/bottom edge = 1
                float r = length(p);
                float ang = atan2(p.y, p.x) / 6.2831853 + 0.5;          // 0..1 around the centre

                // one possible streak per angular slice; a random share of them is shown, re-rolled over time
                float slice = ang * _Density;
                float id = floor(slice);
                float h = hash(id);
                float epoch = floor(_Time.y * _Flicker + h * 13.0);
                float shown = step(1.0 - _Coverage, hash(id * 3.7 + epoch));

                // thin across, soft at the sides
                float across = 1.0 - abs(frac(slice) * 2.0 - 1.0);
                float thin = pow(across, 2.0);

                // dashes flowing outward along the radius
                float flow = frac(r * _Length - _Time.y * _Speed * (0.7 + h * 0.6) + h * 7.0);
                float dash = smoothstep(0.0, 0.2, flow) * smoothstep(0.75, 0.35, flow);

                // only near the edges: the centre stays clear
                float edge = smoothstep(_Inner, _Outer, r);

                float a = thin * shown * dash * edge * _Opacity * strength;
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
}
