Shader "Hidden/Universal Render Pipeline/Blit"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100

        Pass
        {
            Name "Blit"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment Fragment
            #pragma multi_compile_fragment _ _LINEAR_TO_SRGB_CONVERSION
            #pragma multi_compile _ _USE_DRAW_PROCEDURAL
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_fragment _ _RECONSTRUCT_VRS_TILES
            #pragma multi_compile_fragment _ _VRS_TILE_MODE

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/Fullscreen.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingFullscreen.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
           

            TEXTURE2D_X(_SourceTex);
            SAMPLER(sampler_SourceTex);
            float4 _TileRadii;
#if defined(_RECONSTRUCT_VRS_TILES) && defined(_VRS_TILE_MODE)
            float4 _SourceTex_TexelSize;
            float4 _EyeCenterCoords;

            // Lookup tables for bilinear filtering from a checkerboarded image. Given integer pixel coordinates offset
            // back by 1/2 texel on each axis
            static const uint4x4 lutX00 = uint4x4(
                0, 0, 1, 0,
                1, 0, 0, -1,
                1, 0, 0, 0,
                0, -1, 1, 0
            );

            static const int4x4 lutY00 = uint4x4(
                0, 1, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 1,
                0, 0, 0, 0
                );

            static const int4x4 lutX10 = uint4x4(
                0, 1, 1, 1,
                1, 1, 0, 1,
                1, 1, 0, 1,
                0, 1, 1, 1
                );

            static const int4x4 lutY10 = uint4x4(
                0, 1, 0, 0,
                0, 0, 0, -1,
                0, 0, 0, 1,
                0, -1, 0, 0
                );
               
            static const int4x4 lutX11 = uint4x4(
                1, 1, 0, 1,
                1, 1, 0, 2,
                0, 1, 1, 1,
                0, 2, 1, 1
                );

            static const int4x4 lutY11 = uint4x4(
                1, 1, 1, 0,
                1, 1, 1, 1,
                1, 0, 1, 1,
                1, 1, 1, 1
                );

            static const int4x4 lutX01 = uint4x4(
                1, 0, 0, 1,
                1, 0, 0, 0,
                0, 0, 1, 0,
                0, 0, 1, 0
                );

            static const int4x4 lutY01 = uint4x4(
                1, 1, 1, 0,
                1, 1, 1, 2,
                1, 0, 1, 1,
                1, 2, 1, 1
                );

#endif

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv;
#if defined(_RECONSTRUCT_VRS_TILES) && defined(_VRS_TILE_MODE)
                //float xCenter = (UNITY_MATRIX_P._m03 - UNITY_MATRIX_P._m02) / (UNITY_MATRIX_P._m33 - UNITY_MATRIX_P._m32);
                //xCenter = 0.5;
                float2 screenUV = _SourceTex_TexelSize.zw * uv ;
                
                float2 gridUnit2 = 4 * floor(screenUV * 0.25);
                float2 center = unity_StereoEyeIndex == 0 ? _EyeCenterCoords.xy : _EyeCenterCoords.zw;
                float4 radii;
                radii.x = length(gridUnit2 + float2(0.0, 0.0) - center) * _SourceTex_TexelSize.y;
                radii.y = length(gridUnit2 + float2(4.0, 0.0) - center) * _SourceTex_TexelSize.y;
                radii.z = length(gridUnit2 + float2(4.0, 4.0) - center) * _SourceTex_TexelSize.y;
                radii.w = length(gridUnit2 + float2(0.0, 4.0) - center) * _SourceTex_TexelSize.y;
                float avgRadius = (radii.x + radii.y + radii.z + radii.w) * 0.25;
                bool inner = all(radii < _TileRadii.x);
                half4 col;

                bool middle = any(radii <= _TileRadii.y);
                bool outer = any(radii > _TileRadii.y);
                bool onEdge = middle && outer;
               
                //uint2 gridCoordOffset = floor(screenUV * 0.5 - 0.5);
                uint2 gridCoord = (uint2(screenUV - 0.5 ));
                bool grid1 = (gridCoord.x & 1) & (gridCoord.y & 1);
                bool grid2 = !((((gridCoord.x >> 1) + 1) & 1) ^ (((gridCoord.y >> 1) + 1) & 1));
                bool isCovered = inner && !middle;// || (grid1 && grid2);
                //bool grid2 = (gridUnit.x & 1) & (gridUnit.y & 1);
                
                //bool grid = grid1;
                //grid = insideOuter ? grid : grid2;
                half4 col00;
                half4 col10;
                half4 col11;
                half4 col01;
                uint2 uv00;
                uint2 uv10;
                uint2 uv11;
                uint2 uv01;
                uint2 uvInt = gridCoord;
                
                // if the closest 4 pixels are not empty, just use them
                if (isCovered || outer)
                {
                    uv00 = gridCoord;
                    uv10 = gridCoord + uint2(1, 0);
                    uv11 = gridCoord + uint2(1, 1);
                    uv01 = gridCoord + int2(0, 1);

                    if (outer) // for 1/4 shaded rate areas, offset the pixel coordinates into shaded regions 
                    {
                        uint4 uvCorners = uint4(uv00.x, uv11.x, uv00.y, uv11.y);
                        uint4 uvGrid0 = ((uvCorners >> 1) + 1) & 1;
                        uint4 uvGrid = (uvCorners + 1) >> 1;
                        uint4 uvGrid2 = (uvCorners + 3) >> 1;
                        int2 offset = ((uvGrid.xz & 1) & uvGrid0.xz) - ((uvGrid2.xz & 1) & uvGrid0.xz);
                        uv00 += offset;
                        offset = ((uvGrid.yz & 1) & uvGrid0.yz) - ((uvGrid2.yz & 1) & uvGrid0.yz);
                        uv10 += offset;
                        offset = ((uvGrid.yw & 1) & uvGrid0.yw) - ((uvGrid2.yw & 1) & uvGrid0.yw);
                        uv11 += offset;
                        offset = ((uvGrid.xw & 1) & uvGrid0.xw) - ((uvGrid2.xw & 1) & uvGrid0.xw);
                        uv01 += offset;
                    }
                }
                // if we're in one of the empty 2x2 groups inside the 2x checkerboard, we need to go to a lookup table
                // to determine what is the closest set of filled pixels to sample from. For 2x2 groups where some of
                // the pixels are empty, the lookup table provides duplicate coordinates. if all are empty, the lookup
                // provides a pinwheel pattern of pixels from the surrounding filled 2x2 blocks.
                else 
                {
                    uint2 uvMod = (gridCoord +1) & 3u; // modulo 4
                    uint2 offset00 = uint2(lutX00[uvMod.y][uvMod.x], lutY00[uvMod.y][uvMod.x]);
                    uv00 = gridCoord +offset00;

                    uint2 offset10 = uint2(lutX10[uvMod.y][uvMod.x], lutY10[uvMod.y][uvMod.x]);
                    uv10 = gridCoord + offset10;
                    uint2 offset11 = uint2(lutX11[uvMod.y][uvMod.x], lutY11[uvMod.y][uvMod.x]);
                    uv11 = gridCoord + offset11;
                    uint2 offset01 = uint2(lutX01[uvMod.y][uvMod.x], lutY10[uvMod.y][uvMod.x]);
                    uv01 = gridCoord + offset01;

                }
                
                col00 = LOAD_TEXTURE2D_X(_SourceTex, uv00);
                col10 = LOAD_TEXTURE2D_X(_SourceTex, uv10);
                col11 = LOAD_TEXTURE2D_X(_SourceTex, uv11);
                col01 = LOAD_TEXTURE2D_X(_SourceTex, uv01);
                //col00 = offset.x > 0 ? half4(1, 0, 1, 1) : col00;
                //col10 = LOAD_TEXTURE2D_X(_SourceTex, uv10);
                //col11 = LOAD_TEXTURE2D_X(_SourceTex, uv11);
                //col01 = LOAD_TEXTURE2D_X(_SourceTex, uv10);

                half4 colB1 = lerp(col00, col10, frac(screenUV.x-0.5) );
                half4 colB2 = lerp(col10, col11, frac(screenUV.x-0.5) );
                col = lerp(colB1, colB2, frac(screenUV.y - 0.5));
                        ///col = !isCovered ? half4(1, 0, 1, 1) : col;
                        //col = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_SourceTex, uv);
                    //}
                
#else
                half4 col = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_SourceTex, uv);
#endif
                #ifdef _LINEAR_TO_SRGB_CONVERSION
                col = LinearToSRGB(col);
                #endif

                #if defined(DEBUG_DISPLAY)
                half4 debugColor = 0;

                if(CanDebugOverrideOutputColor(col, uv, debugColor))
                {
                    return debugColor;
                }
                #endif

                return col;
            }
            ENDHLSL
        }
    }
}
