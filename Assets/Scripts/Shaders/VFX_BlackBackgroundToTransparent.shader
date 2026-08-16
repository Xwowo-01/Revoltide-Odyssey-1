Shader "UI/VFX_LuminanceToAlpha"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
    }
        SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha

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
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

            // 计算亮度（人眼感知权重）
            float lum = dot(col.rgb, float3(0.299, 0.587, 0.114));

            // 直接以亮度作为透明度（纯黑→0，纯白→1，线性过渡）
            col.a = lum;

            // 预乘Alpha，消除边缘黑/白边
            col.rgb *= col.a;

            return col;
        }
        ENDCG
    }
    }
}