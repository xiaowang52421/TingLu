Shader "Hidden/TingLu/StreetSurface"
{
    Properties
    {
        _BaseColor ("Surface", Color) = (0.35,0.38,0.38,1)
        _AccentColor ("Pattern", Color) = (0.55,0.58,0.56,1)
        _Pattern ("Pattern Type", Float) = 6
        _UvScale ("Pattern Scale", Float) = 2.4
        _Brightness ("Memory Brightness", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _BaseColor;
            fixed4 _AccentColor;
            float _Pattern;
            float _UvScale;
            float _Brightness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float hash21(float2 samplePoint)
            {
                return frac(sin(dot(samplePoint, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 tile = input.uv * _UvScale;
                float pattern = 0;
                if (_Pattern < 0.5)
                {
                    float aggregate = hash21(floor(tile * 5.0));
                    pattern = smoothstep(0.79, 0.94, aggregate) * 0.42;
                }
                else if (_Pattern < 1.5)
                {
                    float2 edge = min(frac(tile), 1.0 - frac(tile));
                    float seam = 1.0 - smoothstep(0.025, 0.055, min(edge.x, edge.y));
                    pattern = seam * 0.40;
                }
                else if (_Pattern < 2.5)
                {
                    float groove = abs(frac(tile.y) - 0.5);
                    pattern = (1.0 - smoothstep(0.34, 0.43, groove)) * 0.70;
                }
                else if (_Pattern < 3.5)
                {
                    float2 cell = frac(tile) - 0.5;
                    float distanceToDot = length(cell * 2.0);
                    pattern = (1.0 - smoothstep(0.22, 0.34, distanceToDot)) * 0.90;
                }
                else if (_Pattern < 4.5)
                {
                    pattern = step(0.48, frac(tile.y));
                }
                else if (_Pattern < 5.5)
                {
                    float diagonal = abs(frac((tile.x + tile.y) * 0.72) - 0.5);
                    pattern = (1.0 - smoothstep(0.035, 0.09, diagonal)) * 0.52;
                }

                fixed3 surfaceColor = lerp(_BaseColor.rgb, _AccentColor.rgb, pattern) * _Brightness;
                fixed4 result;
                result.rgb = surfaceColor * input.color.rgb;
                result.a = _BaseColor.a * input.color.a;
                return result;
            }
            ENDCG
        }
    }
}
