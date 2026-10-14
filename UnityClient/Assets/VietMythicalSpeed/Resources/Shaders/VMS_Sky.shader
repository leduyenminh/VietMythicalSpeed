// Gradient sky with procedural twinkling stars (and optional aurora curtains), drawn on a sphere that follows the camera.
Shader "VMS/Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.02, 0.03, 0.12, 1)
        _HorizonColor ("Horizon", Color) = (0.35, 0.12, 0.45, 1)
        _BottomColor ("Bottom", Color) = (0.01, 0.01, 0.04, 1)
        _StarColor ("Stars", Color) = (1, 0.95, 0.85, 1)
        _StarDensity ("Star density", Range(0, 1)) = 0.5
        _Aurora ("Aurora", Range(0, 2)) = 0
        _AuroraA ("Aurora low", Color) = (0.36, 1, 0.69, 1)
        _AuroraB ("Aurora high", Color) = (0.71, 0.48, 1, 1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Front
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "VmsCommon.cginc"

            fixed4 _TopColor;
            fixed4 _HorizonColor;
            fixed4 _BottomColor;
            fixed4 _StarColor;
            float _StarDensity;
            float _Aurora;
            fixed4 _AuroraA;
            fixed4 _AuroraB;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;
                float3 c = y > 0.0
                    ? lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(y), 0.55))
                    : lerp(_HorizonColor.rgb, _BottomColor.rgb, pow(saturate(-y), 0.45));
                c += _HorizonColor.rgb * exp(-abs(y) * 16.0) * 0.35 * (1.0 + 0.6 * _VmsPulse);

                float3 q = floor(d * 230.0);
                float h = frac(sin(dot(q, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                float star = step(1.0 - 0.006 * _StarDensity, h) * saturate(y * 4.0);
                float twinkle = 0.55 + 0.45 * sin(_Time.y * 2.7 + h * 61.0);
                c += _StarColor.rgb * star * twinkle;

                if (_Aurora > 0.0)
                {
                    // Curtains: a wavy horizontal band, broken into vertical rays, drifting slowly.
                    float az = atan2(d.z, d.x);
                    float centre = 0.3 + sin(az * 3.0 + _Time.y * 0.12) * 0.05 + sin(az * 7.3 - _Time.y * 0.07) * 0.025;
                    float k = (y - centre) / 0.1;
                    float band = exp(-k * k);
                    float rays = 0.55 + 0.45 * sin(az * 90.0 + sin(az * 11.0 + _Time.y * 0.3) * 3.0);
                    float3 tint = lerp(_AuroraA.rgb, _AuroraB.rgb, saturate(k * 0.5 + 0.5));
                    c += tint * band * rays * saturate(y * 6.0) * _Aurora * 0.55 * (1.0 + 0.3 * _VmsPulse);
                }
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
