Shader "Hidden/Convert To MAS" {
    Properties {
        _MetallicGlossMap ("Base (RGB)", 2D) = "black" {}
		_OcclusionMap ("Second (RGB)", 2D) = "white" {}
    }
    SubShader {
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"
            
            uniform sampler2D _MetallicGlossMap;
			uniform sampler2D _OcclusionMap;

            fixed4 frag(v2f_img i) : SV_Target {
                
               fixed4 color;
               color.rb = tex2D(_MetallicGlossMap, i.uv).ra;
               color.g = tex2D(_OcclusionMap, i.uv).g;
               color.a = 1;
                
                return color;
            }
            ENDCG
        }
    }
}