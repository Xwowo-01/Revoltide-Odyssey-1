Shader "UI/EyeOpenEffect"
{
    Properties
    {
        [PerRendererData] _MainTex("Main Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _Progress("Progress", Range(0,1)) = 0
        _BlurStrength("Blur Strength", Range(0, 0.1)) = 0.05
        _Feather("Feather (Óð»¯¿í¶È)", Range(0, 0.2)) = 0.05
    }

        SubShader
        {
            Tags
            {
                "Queue" = "Transparent"
                "IgnoreProjector" = "True"
                "RenderType" = "Transparent"
                "PreviewType" = "Plane"
            }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            Lighting Off
            ZWrite Off

            Pass
            {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #include "UnityCG.cginc"

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

                sampler2D _MainTex;
                float4 _MainTex_ST;
                fixed4 _Color;
                float _Progress;
                float _BlurStrength;
                float _Feather;

                v2f vert(appdata v)
                {
                    v2f o;
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                    o.color = v.color * _Color;
                    return o;
                }

                fixed4 frag(v2f i) : SV_Target
                {
                    // È«ºÚ½×¶Î£¨½ø¶È¼«Ð¡£©
                    if (_Progress < 0.001)
                        return fixed4(0, 0, 0, 1);

                // ¶¯Ì¬ÍÖÔ²ÕÚÕÖ
                float2 center = float2(0.5, 0.5);
                float2 uvCentered = i.uv - center;

                // ºáÏò°ë¾¶£º³õÊ¼0.5£¨µÈÓÚÍ¼Æ¬°ë¿í£©£¬×îÖÕ0.8£¨³¬³öÆÁÄ»£©
                float a = 0.5 + _Progress * 0.3;
                // ×ÝÏò°ë¾¶£º´Ó0µ½1.0
                float b = _Progress * 1.0;
                b = max(b, 0.0001);

                float ellipse = (uvCentered.x * uvCentered.x) / (a * a) +
                                (uvCentered.y * uvCentered.y) / (b * b);

                // Óð»¯ÕÚÕÖ
                float alpha = 1 - smoothstep(1 - _Feather, 1 + _Feather, ellipse);

                // Ðé½¹£¨Ä£ºýËæ½ø¶È¼õÈõ£©
                float blurAmount = _BlurStrength * (1 - _Progress);
                int samples = 8;
                float2 offsets[8] = {
                    float2(1,0), float2(-1,0), float2(0,1), float2(0,-1),
                    float2(0.707,0.707), float2(-0.707,0.707),
                    float2(0.707,-0.707), float2(-0.707,-0.707)
                };
                fixed4 blurred = fixed4(0,0,0,0);
                for (int j = 0; j < samples; j++)
                {
                    float2 offset = offsets[j] * blurAmount;
                    blurred += tex2D(_MainTex, i.uv + offset);
                }
                blurred /= samples;
                blurred *= i.color;

                // ºÚÉ«±³¾°»ìºÏ
                float3 finalRGB = lerp(float3(0,0,0), blurred.rgb, alpha);
                return fixed4(finalRGB, 1);
            }
            ENDCG
        }
        }
}