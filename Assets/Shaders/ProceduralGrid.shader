Shader "Custom/ProceduralGrid"
{
    Properties
    {
        _GridColor ("Màu đường lưới", Color) = (0, 1, 1, 1)
        _GridSize ("Mật độ lưới (Tiling)", Float) = 100.0
        _LineThickness ("Độ dày đường lưới", Range(0.001, 0.1)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
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
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            float4 _GridColor;
            float _GridSize;
            float _LineThickness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // Nhân UV với mật độ để tạo số ô lặp lại
                o.uv = v.uv * _GridSize; 
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 grid = frac(i.uv);
                
                float2 lineUV = step(grid, _LineThickness);
                
                float isLine = max(lineUV.x, lineUV.y);

                return float4(_GridColor.rgb, isLine * _GridColor.a);
            }
            ENDCG
        }
    }
}