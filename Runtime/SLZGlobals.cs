using System.Collections;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Linq;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.Rendering.Universal
{
	public class SLZGlobals
	{
		static SLZGlobals s_Instance;
		// Blue Noise
		private ComputeBuffer BlueNoiseCB;
		private ComputeBuffer HiZDimBuffer;
		private float[] BlueNoiseDim = new float[8]; // width, height, depth, current slice index 
		private bool hasSetBNTextures;
#if UNITY_EDITOR
		private static long framecount = 0;
		private static double timeSinceStartup = 0.0;
#endif
		//private int HiZDimBufferID = Shader.PropertyToID("HiZDimBuffer");
		public static readonly int HiZMipNumID = Shader.PropertyToID("_HiZHighestMip");
		public static readonly int HiZDimID = Shader.PropertyToID("_HiZDim");
		public static readonly int SSRConstantsID = Shader.PropertyToID("SSRConstants");
		public static readonly int CameraOpaqueTextureID = Shader.PropertyToID("_CameraOpaqueTexture");
		public static readonly int PrevHiZ0TextureID = Shader.PropertyToID("_PrevHiZ0Texture");
		// float4 containing the camera opaque texture's 
		public static readonly int OpaqueTextureDimID = Shader.PropertyToID("_CameraOpaqueTexture_Dim");

		// Mips of the camera opaque texture only go down to 8x8 (2^3) so truncate the number of mips by this amount
		public const int opaqueMipTruncation = 3;
		public int opaqueTexID { get { return CameraOpaqueTextureID; } }
		public int prevHiZTexID { get { return PrevHiZ0TextureID; } }

		public GlobalKeyword HiZEnabledKW { get; private set; }
		public GlobalKeyword HiZMinMaxKW { get; private set; }
		public GlobalKeyword SSREnabledKW { get; private set; }

		public SLZPerCameraRTStorage PerCameraOpaque;
		public SLZPerCameraRTStorage PerCameraPrevHiZ;
		public SLZPerCameraBufferStorage PerCameraSSRGlobals;
		private uint PerCameraPrevHiZIter = 0;
		private uint PerCameraOpaqueIter = 0;

		private ComputeBuffer SSRGlobalCB;

		private double extraSmoothedDT = 0.01111;

		[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
		struct SSRBufferData
		{
			public float _SSRHitRadius;
			public float _SSRTemporalWeight;
			public float _SSRSteps;
			public int _SSRMinMip;
			
			public float _SSRDistScale;
			public float empty1;
			public float empty2;
			public float empty3;
		}
		private SLZGlobals()
		{
			BlueNoiseCB = new ComputeBuffer(8, sizeof(float), ComputeBufferType.Constant);
			BlueNoiseDim = new float[8];
			hasSetBNTextures = false;
			SSRGlobalCB = new ComputeBuffer(8, sizeof(float), ComputeBufferType.Constant);
			HiZDimBuffer = new ComputeBuffer(15, Marshal.SizeOf<Vector4>());
			SSREnabledKW = GlobalKeyword.Create("_SLZ_SSR_ENABLED");
			HiZEnabledKW = GlobalKeyword.Create("_HIZ_ENABLED");
			HiZMinMaxKW = GlobalKeyword.Create("_HIZ_MIN_MAX_ENABLED");
			PerCameraOpaque = new SLZPerCameraRTStorage();
			PerCameraPrevHiZ = new SLZPerCameraRTStorage();
			//PerCameraSSRGlobals = new SLZPerCameraBufferStorage(8, sizeof(float), ComputeBufferMode.Dynamic, ComputeBufferType.Constant);
		}
		public static SLZGlobals instance
		{
			get
			{
				if (s_Instance == null)
				{
					s_Instance = new SLZGlobals();
				}
				return s_Instance;
			}

		}

		public void SetHiZSSRKeyWords(bool enableSSR, bool requireHiZ, bool requireMinMax)
		{
			Shader.SetKeyword(SSREnabledKW, enableSSR);
			Shader.SetKeyword(HiZEnabledKW, requireHiZ);
			Shader.SetKeyword(HiZMinMaxKW, requireMinMax);
		}

		public void SetHiZGlobal(int numMips, Vector4 dim)
		{
			//HiZDimBuffer.SetData(data);
			//Shader.SetGlobalBuffer(HiZDimBufferID, HiZDimBuffer);
			Shader.SetGlobalInt(HiZMipNumID, numMips);
			Shader.SetGlobalVector(HiZDimID, dim);
			//Shader.SetKeyword(HiZMinMaxKW, minmax);
		}

		private SSRBufferData SetSSRGlobalsBase(int maxSteps, int minMip, float hitRadius, float temporalWeight, float fov, int screenHeight)
		{
			/*
			 * 0 float _SSRHitRadius;
			 * 1 float _SSREdgeFade;
			 * 2 int _SSRSteps;
			 * 3 none
			 */
			SSRBufferData SSRGlobalArray = new SSRBufferData();
			//SSRGlobalArray[0] = 1.0f / (1.0f + hitRadius);//hitRadius;
			//SSRGlobalArray[1] = -cameraNear / (cameraFar - cameraNear) * (hitRadius * SSRGlobalArray[0]);
			SSRGlobalArray._SSRHitRadius = hitRadius;
			extraSmoothedDT = 0.95 * extraSmoothedDT + 0.05 * Time.smoothDeltaTime;
			float framerateConst = 1.0f / (float)extraSmoothedDT * (1.0f / 90.0f);
			float expConst = math.exp(-framerateConst);
			float FRTemporal = (math.exp(-framerateConst * temporalWeight) - expConst) * (1.0f / (1.0f - expConst));
			//Debug.Log(FRTemporal);
			SSRGlobalArray._SSRTemporalWeight = math.clamp(1.0f - temporalWeight, 0.0078f, 1.0f); //Mathf.Clamp(FRTemporal, 0.0078f, 1.0f); 
			SSRGlobalArray._SSRSteps = maxSteps;
			float SSRRes = 1024;
			int dynamicMinMip = (int)math.round(math.log2(((float)screenHeight) / SSRRes));
			SSRGlobalArray._SSRMinMip = math.max(dynamicMinMip + minMip, 0);
			float halfTan = math.tan(Mathf.Deg2Rad * (fov * 0.5f));
			SSRGlobalArray._SSRDistScale = halfTan / (0.5f * (float)screenHeight); // rcp(0.5*_ScreenParams.y * UNITY_MATRIX_P._m11)
			//Debug.Log("SSR scale: " + SSRGlobalArray[4]);
			return SSRGlobalArray;
		   
		}
		public void SetSSRGlobals_(int maxSteps, int minMip, float hitRadius, float temporalWeight, float fov, int screenHeight)
		{
			Span<SSRBufferData> buffer = stackalloc SSRBufferData[1] { SetSSRGlobalsBase(maxSteps, minMip, hitRadius, temporalWeight, fov, screenHeight) };
			
			SSRGlobalCB.SetData<SSRBufferData>(buffer);
			Shader.SetGlobalConstantBuffer(SSRConstantsID, SSRGlobalCB, 0, SSRGlobalCB.count * SSRGlobalCB.stride);
		}

		public void SetSSRGlobalsCmd(ref CommandBuffer cmd, int maxSteps, int minMip, float hitRadius, float temporalWeight, float fov, int screenHeight)
		{
			Span<SSRBufferData> buffer = stackalloc SSRBufferData[1] { SetSSRGlobalsBase(maxSteps, minMip, hitRadius, temporalWeight, fov, screenHeight) };
			cmd.SetBufferData<SSRBufferData>(SSRGlobalCB, buffer );
			cmd.SetGlobalConstantBuffer(SSRGlobalCB, SSRConstantsID, 0, SSRGlobalCB.count * SSRGlobalCB.stride);
		}


		public void SetBlueNoiseGlobals(Texture2DArray BlueNoiseRGBA, Texture2DArray BlueNoiseR)
		{
	
			if (BlueNoiseRGBA != null)
			{
				BlueNoiseDim[0] = BlueNoiseRGBA.width;
				BlueNoiseDim[1] = BlueNoiseRGBA.height;
				BlueNoiseDim[2] = BlueNoiseRGBA.depth;
				BlueNoiseDim[3] = (float) Random.Range(0, BlueNoiseRGBA.width);
				BlueNoiseDim[4] = (float) Random.Range(0, BlueNoiseRGBA.height);
#if UNITY_EDITOR
				if (!EditorApplication.isPlaying)
				{
					if (timeSinceStartup != EditorApplication.timeSinceStartup)
					{
						timeSinceStartup = EditorApplication.timeSinceStartup;
						framecount++;
					}
					BlueNoiseDim[3] = (int)(framecount % BlueNoiseRGBA.depth);
					//Debug.Log(BlueNoiseDim[3]);
				}
				else
#endif
				{
					BlueNoiseDim[3] = math.abs((int)(Time.renderedFrameCount % BlueNoiseRGBA.depth));
				}
				if (BlueNoiseCB != null)
				{
					BlueNoiseCB.SetData(BlueNoiseDim);
					Shader.SetGlobalConstantBuffer("BlueNoiseDim", BlueNoiseCB, 0, BlueNoiseCB.count * BlueNoiseCB.stride);
				}
				if (!hasSetBNTextures)
				{
					Shader.SetGlobalTexture("_BlueNoiseRGBA", BlueNoiseRGBA);
					Shader.SetGlobalTexture("_BlueNoiseR", BlueNoiseR);
					hasSetBNTextures = true;
				}
			}
		}

		public void UpdateBlueNoiseFrame()
		{
			if (BlueNoiseCB != null)
			{
				long depth = (long)BlueNoiseDim[2];
#if UNITY_EDITOR
				if (!EditorApplication.isPlaying)
				{
					BlueNoiseDim[3] = (int)((Screen.currentResolution.refreshRate * EditorApplication.timeSinceStartup) % depth);
				}
				else
#endif
				{
					BlueNoiseDim[3] = (int)((Time.timeSinceLevelLoadAsDouble * Screen.currentResolution.refreshRate) % depth);
				}
				BlueNoiseCB.SetData(BlueNoiseDim);
				Shader.SetGlobalConstantBuffer("BlueNoiseDim", BlueNoiseCB, 0, BlueNoiseCB.count * BlueNoiseCB.stride);
			}
		}


		int purgeCounter = 0;
		const int maxCount = 360; 
		public void RemoveTempRTStupid()
		{
			purgeCounter++;
			if (purgeCounter > maxCount)
			{
				PerCameraOpaque.RemoveAllNull();
				PerCameraPrevHiZ.RemoveAllNull();
				purgeCounter = 0;
			}
		}

		public static void Dispose()
		{
			if (s_Instance != null)
			{
				if (s_Instance.SSRGlobalCB != null)
				{
					s_Instance.SSRGlobalCB.Dispose();
					s_Instance.SSRGlobalCB = null;
				}
				if (s_Instance.BlueNoiseCB != null)
				{
					s_Instance.BlueNoiseCB.Dispose();
					s_Instance.BlueNoiseCB = null;
				}
				if (s_Instance.HiZDimBuffer != null)
				{
					s_Instance.HiZDimBuffer.Dispose();
					s_Instance.HiZDimBuffer = null;
				}
			   
				if (s_Instance.PerCameraOpaque != null)
				{
					s_Instance.PerCameraOpaque.Dispose();
				}
				if (s_Instance.PerCameraPrevHiZ != null)
				{
					s_Instance.PerCameraPrevHiZ.Dispose();
				}
			   
			}
			s_Instance = null;
		}


		public static int CalculateOpaqueTexMipLevels(int width, int height)
		{
			return (int)math.floor(math.max(math.log2(width), math.log2(height))) + 1 - opaqueMipTruncation;
		}
	}


	public class SLZGlobalsSetPass : ScriptableRenderPass
	{
		private bool enableSSR;
		private bool requireHiZ;
		private bool requireMinMax;

		private float ssrHitRadius;
		private int ssrMaxSteps;
		private int ssrMinMip;
		private int opaqueMipLevels;
		private float cameraNear;
		private float cameraFar;
		int opaqueTexSizeFrac = 1;
		private Camera camera;
		private RTPermanentHandle prevOpaque;
		private RTPermanentHandle prevHiZ;

		SLZGlobalsData passData;
		public SLZGlobalsSetPass(RenderPassEvent evt)
		{
			renderPassEvent = evt;
			passData = new SLZGlobalsData();
		}
		public void Setup(CameraData camData)
		{

			// Hack to tell unity to store previous frame object to world matrices...
			// Not used by SRP to enable motion vectors or depth but somehow still necessary :(
			if (camData.enableSSR)
			{
				camData.camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
			}
			Downsampling downsamplingMethod = UniversalRenderPipeline.asset.opaqueDownsampling;
			if (downsamplingMethod == Downsampling._2xBilinear)
			{
				opaqueTexSizeFrac = 2;
			}
			else if (downsamplingMethod == Downsampling._4xBox || downsamplingMethod == Downsampling._4xBilinear)
			{
				opaqueTexSizeFrac = 4;
			}
			else
			{
				opaqueTexSizeFrac = 1;
			}
			
			//ConfigureTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.None), new RenderTargetIdentifier(BuiltinRenderTextureType.None));
			//Debug.Log("Setup for " + camData.camera.name);
		}


		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			CameraData camData = renderingData.cameraData;
			ref RenderTextureDescriptor targetDesc = ref camData.cameraTargetDescriptor;
			prevOpaque = SLZGlobals.instance.PerCameraOpaque.GetHandle(camData.camera);
			prevHiZ = SLZGlobals.instance.PerCameraPrevHiZ.GetHandle(camData.camera);
			passData.cmd = renderingData.commandBuffer;
			passData.cam = camData.camera;
			passData.enableSSR = camData.enableSSR;
			passData.requireHiZ = camData.requiresDepthPyramid;
			passData.requireMinMax = camData.requiresMinMaxDepthPyr;
			passData.opaqueTex = prevOpaque.handle;
			passData.hiZTex = prevHiZ.handle;
			passData.opaqueID = SLZGlobals.CameraOpaqueTextureID;
			passData.hiZID = SLZGlobals.PrevHiZ0TextureID;
			passData.ssrEnabledKW = SLZGlobals.instance.SSREnabledKW;
			passData.hiZEnabledKW = SLZGlobals.instance.HiZEnabledKW;
			passData.hiZMinMaxKW = SLZGlobals.instance.HiZMinMaxKW;
			passData.ssrMinMip = camData.SSRMinMip;
			passData.ssrMaxSteps = camData.maxSSRSteps;
			passData.ssrHitRadius = camData.SSRHitRadius;
			passData.temporalWeight = camData.SSRTemporalWeight;
			passData.fov = camData.camera.fieldOfView;
			passData.screenWidth = targetDesc.width;
			passData.screenHeight = targetDesc.height;
			passData.opaqueTexSizeFrac = opaqueTexSizeFrac;
			if (camData.requiresColorPyramid)
				passData.opaqueMipLevels = SLZGlobals.CalculateOpaqueTexMipLevels(targetDesc.width / opaqueTexSizeFrac, targetDesc.height / opaqueTexSizeFrac);
			else
				passData.opaqueMipLevels = 1;
			ExecutePass(passData, ref passData.cmd);
		}

		internal static void ExecutePass(SLZGlobalsData data, ref CommandBuffer cmd)
		{
			
			Camera cam = data.cam;
			bool enableSSR = data.enableSSR;
			bool requireHiZ = data.requireHiZ;
			bool requireMinMax = data.requireMinMax;
			RTHandle opaqueTex = data.opaqueTex;
			RTHandle hiZTex = data.hiZTex;
			int opaqueID = data.opaqueID;
			int hiZID = data.hiZID;
			GlobalKeyword ssrEnabledKW = data.ssrEnabledKW;
			GlobalKeyword hiZEnabledKW = data.hiZEnabledKW;
			GlobalKeyword hiZMinMaxKW = data.hiZMinMaxKW;
		   
			using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.SetSLZGlobals)))
			{

				if (enableSSR)
				{
					cmd.SetGlobalTexture(opaqueID, opaqueTex);
					cmd.SetGlobalTexture(hiZID, hiZTex);
				}

				SLZGlobals.instance.SetSSRGlobalsCmd(ref cmd, data.ssrMaxSteps, data.ssrMinMip, data.ssrHitRadius, data.temporalWeight, data.fov, data.screenHeight);
				cmd.SetKeyword(ssrEnabledKW, enableSSR);
				cmd.SetKeyword(hiZEnabledKW, requireHiZ);
				cmd.SetKeyword(hiZMinMaxKW, requireMinMax);
				cmd.SetGlobalVector(SLZGlobals.OpaqueTextureDimID, 
					new Vector4(data.screenWidth / data.opaqueTexSizeFrac, data.screenWidth / data.opaqueTexSizeFrac, data.opaqueMipLevels - 1, data.opaqueMipLevels + SLZGlobals.opaqueMipTruncation));
			}
		}

		/// <summary>
		/// Rendergraph stuff
		/// </summary>

		internal class SLZGlobalsData
		{
			public CommandBuffer cmd;
			public Camera cam;
			public bool enableSSR;
			public bool requireHiZ;
			public bool requireMinMax;
			public RTHandle opaqueTex;
			public RTHandle hiZTex;
			public int opaqueID;
			public int hiZID;
			public GlobalKeyword ssrEnabledKW;
			public GlobalKeyword hiZEnabledKW;
			public GlobalKeyword hiZMinMaxKW;
			public int ssrMinMip;
			public int ssrMaxSteps;
			public float ssrHitRadius;
			public float temporalWeight;
			public float fov;
			public int screenWidth;
			public int screenHeight;
			public int opaqueMipLevels;
			public int opaqueTexSizeFrac;
		}

		internal void Render(RenderGraph renderGraph, ref RenderingData renderingData)
		{
			CameraData camData = renderingData.cameraData;
			prevOpaque = SLZGlobals.instance.PerCameraOpaque.GetHandle(camData.camera);
			prevHiZ = SLZGlobals.instance.PerCameraPrevHiZ.GetHandle(camData.camera);
			// Hack to tell unity to store previous frame object to world vectors...
			// Not used by SRP to enable motion vectors or depth but somehow still necessary :(
			if (camData.enableSSR)
			{
				camData.camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
			}
			using (var builder = renderGraph.AddRenderPass<SLZGlobalsData>("Set SLZ Globals", out var passData, base.profilingSampler))
			{
				passData.cmd = renderingData.commandBuffer;
				passData.cam = camData.camera;
				passData.enableSSR = camData.enableSSR;
				passData.requireHiZ = camData.requiresDepthPyramid;
				passData.requireMinMax = camData.requiresMinMaxDepthPyr;
				TextureHandle prevOpaqueHandle = renderGraph.ImportTexture(RTHandles.Alloc(prevOpaque.renderTexture));
				//builder.ReadTexture(prevOpaqueHandle);
				passData.opaqueTex = prevOpaqueHandle;
				TextureHandle hiZHandle = renderGraph.ImportTexture(prevHiZ.handle);
				//builder.ReadTexture(hiZHandle);
				passData.hiZTex = hiZHandle;
				passData.opaqueID = SLZGlobals.instance.opaqueTexID;
				passData.hiZID = SLZGlobals.instance.prevHiZTexID;
				passData.ssrEnabledKW = SLZGlobals.instance.SSREnabledKW;
				passData.hiZEnabledKW = SLZGlobals.instance.HiZEnabledKW;
				passData.hiZMinMaxKW = SLZGlobals.instance.HiZMinMaxKW;
				builder.AllowPassCulling(false);
				builder.SetRenderFunc((SLZGlobalsData data, RenderGraphContext context) =>
				{
					ExecutePass(data, ref data.cmd);
				});
			}
		}
	}
}
