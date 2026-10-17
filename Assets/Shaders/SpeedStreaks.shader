// Anime speed lines: a few crisp, tapered wedges pointing in from the screen edges (centre stays clear).
// Each line flashes in for a moment at a random angle and length, then is re-dealt. For a full-screen
// UI Image; the Image's alpha is the strength (0 = invisible).
Shader "ShutInVamp/UI/SpeedStreaks"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Line Colour", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.5
        _Slots ("Line Slots Around", Float) = 32
        _Coverage ("Share Of Slots With A Line", Range(0, 1)) = 0.28
        _Width ("Half Width At The Edge (screen half-heights)", Range(0, 0.1)) = 0.018
        _ReachMin ("Tip Start Min (fraction of the way to the edge)", Range(0, 1)) = 0.55
        _ReachMax ("Tip Start Max", Range(0, 1)) = 0.85
        _Rate ("Re-deal Rate", Float) = 9
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
            float _Opacity, _Slots, _Coverage, _Width, _ReachMin, _ReachMax, _Rate;

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
                float2 p = (i.uv - 0.5) * float2(aspect, 1) * 2;     // centre 0, top/bottom edge 1, corners ~2
                float r = length(p);
                float ang = atan2(p.y, p.x) / 6.2831853 + 0.5;     // 0..1 around

                float s = ang * _Slots;
                float aa = 1.5 * 2.0 / _ScreenParams.y;           // ~1.5 px soft edge, in the same units as p
                float id = floor(s);
                float h = hash(id + 0.17);
                // each slot is re-dealt at its own moments (not all lines at once)
                float epoch = floor(_Time.y * _Rate + h * 11.0);
                float roll = hash(id * 1.31 + epoch * 7.77);
                float shown = step(1.0 - _Coverage, roll);

                float centre = 0.5 + (hash(id * 2.13 + epoch * 3.1) - 0.5) * 0.6;   // not evenly spaced
                // how far the screen edge is in this direction, so lines look the same all round the frame
                float2 dir = p / max(r, 0.0001);
                float rEdge = min(aspect / max(abs(dir.x), 0.0001), 1.0 / max(abs(dir.y), 0.0001));
                float reach = rEdge * lerp(_ReachMin, _ReachMax, hash(id * 5.37 + epoch * 1.9));

                // wedge: a sharp tip at 'reach', widening to _Width at the screen edge
                float t = saturate((r - reach) / max(rEdge - reach, 0.0001));
                float halfWidth = _Width * t;
                float dist = abs(frac(s) - centre) * 6.2831853 / _Slots * r;   // sideways distance from the line's centre
                float wedge = (1.0 - smoothstep(halfWidth - aa, halfWidth + aa, dist)) * step(reach, r);

                return fixed4(_Color.rgb, wedge * shown * _Opacity * strength);
            }
            ENDCG
        }
    }
}
