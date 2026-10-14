// Dark glossy road. uv.x = 0..1 across the road, uv.y = metres along it,
// uv2.x = beat count at that point of the track, uv2.y = song intensity there.
Shader "VMS/Road"
{
    Properties
    {
        _BaseColor ("Base", Color) = (0.03, 0.04, 0.09, 1)
        _GridColor ("Grid", Color) = (0.2, 0.5, 1, 1)
        _BeatColor ("Beat", Color) = (1, 0.6, 0.9, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "VmsCommon.cginc"

            fixed4 _BaseColor;
            fixed4 _GridColor;
            fixed4 _BeatColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 beat : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.beat = v.uv2;
                o.fog = VmsFog(mul(unity_ObjectToWorld, v.vertex).xyz);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float g = frac(i.uv.y / 6.0);
                float grid = smoothstep(0.035, 0.0, min(g, 1.0 - g));
                float b = frac(i.beat.x);
                float beatLine = saturate(1.0 - min(b, 1.0 - b) * 20.0);
                float edge = abs(i.uv.x - 0.5) * 2.0;

                float3 c = _BaseColor.rgb;
                c += _GridColor.rgb * grid * 0.22;
                c += _BeatColor.rgb * beatLine * (0.3 + 0.7 * i.beat.y) * (0.65 + 0.35 * _VmsPulse);
                c += _GridColor.rgb * 0.05 * (1.0 - edge) * (0.5 + _VmsEnergy);
                c *= lerp(1.0, 0.55, edge * edge);
                c = lerp(c, _VmsFogColor.rgb, i.fog);
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
