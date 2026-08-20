using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using UnityEditor;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Mathematics;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering.Universal
{
    public static class FoveationManager
    {
        public static float injectedFoveationLevel = 0.25f;

        public static bool enableFoveationInjection = false;
        public static bool useEyePositions = false;
        public static float3 eyeLeftDir = new float3(0.0f,0.0f,-1f); 
        public static float3 eyeRightDir = new float3(0.0f,0.0f,-1f); 
        public static NativeArray<FoveationDataOld> foveationData;
        public static RenderTexture FDMImageManual;




        public static void Configure(CommandBuffer cmd, ref CameraData cameraData, bool yFlip)
        {
            if (cameraData.xr.supportsFoveatedRendering)
            {
                if (false)//(cameraData.xr.foveatedRenderingInfo != System.IntPtr.Zero)
                {
                    
                    FoveationDataOld data = Marshal.PtrToStructure<FoveationDataOld>(cameraData.xr.foveatedRenderingInfo);
                    //Debug.Log($"Foveation struct: {data}");
                    //data.eyeLeftCenter  += new Vector2(0, 96f);
                    //data.eyeRightCenter += new Vector2(0, 96f);
                    if (!foveationData.IsCreated)
                    {
                        foveationData = new NativeArray<FoveationDataOld>(1, Allocator.Temp);
                    }

                    foveationData[0] = data;
                    cmd.ConfigureFoveatedRendering(NativeArrayIntPtr.GetIntPtr<FoveationDataOld>(foveationData));
                }
                else
                {
                    //Debug.Log($"Foveation struct pointer: {cameraData.xr.foveatedRenderingInfo}");
                     cmd.ConfigureFoveatedRendering(cameraData.xr.foveatedRenderingInfo);
                }
                if (XRSystem.foveatedRenderingCaps.HasFlag(FoveatedRenderingCaps.NonUniformRaster))
                    cmd.EnableShaderKeyword(ShaderKeywordStrings.FoveatedRenderingNonUniformRaster);
            }
            else if (enableFoveationInjection && cameraData.xr.enabled)
            {
                //Debug.Log($"cameraData.xr.supportsFoveatedRendering: {cameraData.xr.supportsFoveatedRendering}");
                //Debug.Log("Trying to set foveation...");
                
                if (!foveationData.IsCreated)
                {
                    foveationData = new NativeArray<FoveationDataOld>(1, Allocator.Temp);
                }
                uint rtWidth = (uint)cameraData.cameraTargetDescriptor.width;
                uint rtHeight = (uint)cameraData.cameraTargetDescriptor.height;
                float2 rtSize = new float2(rtWidth, rtHeight);
                
                    
                float4 eyeCenters = CalculateEyeUVCoordinatesDir(cameraData, new Vector3(0f,0f,-1f), new Vector3(0f,0f,-1f), yFlip);
                if (true)
                {
                    // Unity's internal compute shader for generating the foveation image makes a mistake where
                    // it calculates the distance between the pixel and the foveation center as length(pixel + center)
                    // rather than length(pixel - center). This means the foveation position is mirrored about the origin, 
                    // which in this case is the texture center due to the (-1,1) coordinate system they use. 
                    // The Y coordinate gets double flipped into being correct since unity tries to conform to the OpenGL 
                    // coordinate convention where bottom left is 0,0 which is the opposite of every other API. If rendering
                    // to the system backbuffer it can't do this and must render right side up. Shaders have to handle this 
                    // flip when calculating coordinates, but it seems the internal compute shader doesn't do this correctly.
                    // The result is that only X is flipped if rendering to the backbuffer. 
                    eyeCenters.xz = 1.0f - eyeCenters.xz;
                }

                foveationData[0] = new FoveationDataOld()
                {
                    size = 56,
                    typeName = FoveationDataOld.shadingRateType,
                    version = 0x00020002,
                    width = rtWidth,
                    height = rtHeight,
                    level = 1f,
                    eyeLeftCenter = (eyeCenters.xy) * rtSize,
                    eyeRightCenter = eyeCenters.zw * rtSize,
                    handle = 0UL
                };

                    /* Does not work, unity does not look at the foveation struct for FDM in 2022
                    Int32 width  = cameraData.cameraTargetDescriptor.width / 32;
                    Int32 height = cameraData.cameraTargetDescriptor.height / 32;
                    Int32 depth  = 2;//cameraData.cameraTargetDescriptor.volumeDepth;
                    
                    if (FDMImageManual != null && 
                        (
                        FDMImageManual.width  != width ||  
                        FDMImageManual.height != height ||  
                        FDMImageManual.depth  != depth
                        )
                    )
                    {
                        CoreUtils.Destroy(FDMImageManual);
                        FDMImageManual = null;
                    }

                    IntPtr FDMPtr = (FDMImageManual && FDMImageManual.IsCreated()) ? FDMImageManual.GetNativeTexturePtr() : IntPtr.Zero;

                    if (FDMImageManual == null)
                    {
                        
                        
                        RenderTextureDescriptor FDMDesc = new RenderTextureDescriptor()
                        {
                            width       = (int)width,
                            height      = (int)height,
                            volumeDepth = (int)depth,
                            graphicsFormat = GraphicsFormat.R8G8_UNorm,
                            depthStencilFormat = GraphicsFormat.None,
                            msaaSamples = 1,
                            mipCount = 1,
                            enableRandomWrite = true,
                            dimension = depth > 1 ? TextureDimension.Tex2DArray : TextureDimension.Tex2D
                        };

                        SLZQuestNative.HookImageCreationToFDM((uint)width, (uint)height, (uint)depth);
                        FDMImageManual = new RenderTexture(FDMDesc);
                        FDMImageManual.name = "FDM Image Manual";
                        bool isCreated = FDMImageManual.Create();
                        FDMPtr = FDMImageManual.GetNativeTexturePtr();
                        if (SLZQuestNative.ImageCreationHookStatus() != 0)
                        {
                            Debug.LogError($"FDM image hook failed, isCreated = {isCreated}, FDMPtr = 0x{FDMPtr:x8}");
                            SLZQuestNative.CancelImageCreationHook();
                            CoreUtils.Destroy(FDMImageManual);
                            FDMImageManual = null;
                        }
                        else
                        {
                            ComputeShader fdmPopulate = Resources.Load("FDMMap") as ComputeShader;
                            
                            cmd.SetComputeVectorParam(fdmPopulate, "eyePos", eyeCenters);
                            cmd.SetComputeTextureParam(fdmPopulate, 0, "Result", new RenderTargetIdentifier(FDMImageManual, 0, CubemapFace.Unknown, -1));
                            cmd.DispatchCompute(fdmPopulate, 0, (width + 7) / 8, (height + 7) / 8, 2);
                            cmd.IssuePluginEventAndData(SLZQuestNative.GetRenderEventWithDataFunc(), 0, FDMImageManual.GetNativeTexturePtr());
                           
                        }
                    }
                    if (FDMImageManual != null)
                    {
                        FoveationDataOld copy = foveationData[0];
                        copy.handle = (ulong)FDMPtr;
                        foveationData[0] = copy;
                    }
                    */

                //Debug.Log($"Eye Centers: left: {eyeCenters.xy}, right: {eyeCenters.zw}");


                cmd.ConfigureFoveatedRendering(NativeArrayIntPtr.GetIntPtr<FoveationDataOld>(foveationData));
            }
        }

        /// <summary>
        /// Call after <see cref="Configure"/> to dispose of nativearray containing foveation data
        /// </summary>
        public static void CleanupConfigure()
        {
            if (foveationData.IsCreated) foveationData.Dispose();
        }

        /// <summary>
        /// Calculates the normalized UV coordinates of the given pair of eye directions.
        /// </summary>
        /// <param name="eyeDirLeft" >Left eye direction vector in view space</param>
        /// <param name="eyeDirRight">Right eye direction vector in view space</param>
        /// <param name="projLeft" >Left eye projection matrix</param>
        /// <param name="projRight">Right eye projection matrix</param>
        /// <returns>Vector4 containing (left eye uv, right eye uv) coordinates of the left and right eye directions</returns>
        public static float4 CalculateEyeUVCoordinatesDir( float3 eyeDirLeft, float3 eyeDirRight, float4x4 projLeft, float4x4 projRight)
        {
        
            float4 eyeCenterProj_left  = math.mul(projLeft,  math.float4(eyeDirLeft, 1.0f));
            float4 eyeCenterProj_right = math.mul(projRight, math.float4(eyeDirRight, 1.0f));

            // Perspective correction
            float2 eyeCenterUV_left  = eyeCenterProj_left.xy;// / eyeCenterProj_left.w;
            float2 eyeCenterUV_right = eyeCenterProj_right.xy;// / eyeCenterProj_right.w;

            // remap from -1,1 to 0,1.
            eyeCenterUV_left  = 0.5f * eyeCenterUV_left  + 0.5f; 
            eyeCenterUV_right = 0.5f * eyeCenterUV_right + 0.5f; 

            // fix for unity's broken compute shader. Internal compute shader calculates the distance of each pixel to the eye center as
            // the length of the sum of the pixel coordinate and the eye coordinate instead of their difference. 
            // My guess is they got confused because the coordinate space is right side up with Y down when rendering to the backbuffer 
            // instead of openGL's y up. Quest's projection matrix is symmetric horizontally, so this works out on that platform.

            return new float4(eyeCenterUV_left, eyeCenterUV_right);
        }

        /// <summary>
        /// Calculates the normalized UV coordinates of the given pair of eye directions.
        /// </summary>
        /// <param name="cameraData">Camera data</param>
        /// <param name="eyeDirLeft" >Left eye direction vector in view space</param>
        /// <param name="eyeDirRight">Right eye direction vector in view space</param>
        /// <param name="isTargetFlipped">Is the rendertarget flipped? True if not rendering directly to the backbuffer. Unity tries to normalize its uv coordinate system to OpenGL style with 0,0 at bottom left, which is upside-down for D3D and Vulkan. If rendering directly to the backbuffer it is forced to use the API defined UV coordinate system.</param>
        /// <returns>Vector4 containing (left eye uv, right eye uv) coordinates of the left and right eye directions</returns>
        public static float4 CalculateEyeUVCoordinatesDir(CameraData cameraData, Vector3 eyeCenterLeft, Vector3 eyeCenterRight, bool isTargetFlipped)
        {
            int rightEyeIndex = cameraData.xr.singlePassEnabled ? 1 : 0;

            // Unity's internal compute shader that generates the shading 
            Matrix4x4 projLeft  = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrixNoJitter(0), isTargetFlipped);
            Matrix4x4 projRight = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrixNoJitter(rightEyeIndex), isTargetFlipped);
            float4 eyeCoords = CalculateEyeUVCoordinatesDir(eyeCenterLeft, eyeCenterRight, projLeft, projRight);
            return eyeCoords;
        }
    }
}
