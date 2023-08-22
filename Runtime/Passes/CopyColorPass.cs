using System;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

// SLZ MODIFIED
using System.Collections.Generic;
using Unity.Mathematics;
// END SLZ MODIFIED

namespace UnityEngine.Rendering.Universal.Internal
{
	/// <summary>
	/// Copy the given color buffer to the given destination color buffer.
	///
	/// You can use this pass to copy a color buffer to the destination,
	/// so you can use it later in rendering. For example, you can copy
	/// the opaque texture to use it for distortion effects.
	/// </summary>
	public class CopyColorPass : ScriptableRenderPass
	{
		int m_SampleOffsetShaderHandle;
		Material m_SamplingMaterial;
		Downsampling m_DownsamplingMethod;
		Material m_CopyColorMaterial;

		private RTHandle source { get; set; }

		private RTHandle destination { get; set; }

		// TODO: Remove when Obsolete Setup is removed
		private int destinationID { get; set; }
		private PassData m_PassData;

		// SLZ MODIFIED
		const int mipTruncation = 3;
		readonly static int s_SizeID = Shader.PropertyToID("_Size");
		readonly static int s_SourceID = Shader.PropertyToID("_Source");
		readonly static int s_DestID = Shader.PropertyToID("_Destination");
		readonly static int s_OpaqueTextureDimID = Shader.PropertyToID("_CameraOpaqueTexture_Dim");
		readonly static int s_TempBufferID = Shader.PropertyToID("_TempBuffer");
		static GlobalKeyword _RECONSTRUCT_VRS_TILES;

		ComputeShader m_ColorPyramidCompute;
		public bool m_RequiresMips;
		private int m_MipLevels;
		private MipSize m_Size;
		private static int[] m_SizeArray = new int[4];
		private int m_DownsampleKernelID;
		private int m_GaussianKernelID;
		private bool m_ReconstructTiles = false;
		private PersistentRT m_PermanentDest { get; set; }
		private RTHandle m_PermHandle;
		private bool m_UseRT;
		private RTHandle m_TempBuffer { get; set; }
		private RenderTextureDescriptor m_TempDescriptor;
		// END SLZ MODIFIED

		public struct MipSize
		{
			public int width;
			public int height;
		}


		/// <summary>
		/// Creates a new <c>CopyColorPass</c> instance.
		/// </summary>
		/// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
		/// <param name="samplingMaterial">The <c>Material</c> to use for downsampling quarter-resolution image with box filtering.</param>
		/// <param name="copyColorMaterial">The <c>Material</c> to use for other downsampling options.</param>
		/// <seealso cref="RenderPassEvent"/>
		/// <seealso cref="Downsampling"/>
		// SLZ MODIFIED
		public CopyColorPass(RenderPassEvent evt, Material samplingMaterial, ComputeShader colorPyramid, Material copyColorMaterial = null, bool reconstructTiles = false)
		// END SLZ MODIFIED
		{
			base.profilingSampler = new ProfilingSampler(nameof(CopyColorPass));
			m_PassData = new PassData();

			m_SamplingMaterial = samplingMaterial;
			m_CopyColorMaterial = copyColorMaterial;
			m_SampleOffsetShaderHandle = Shader.PropertyToID("_SampleOffset");
			renderPassEvent = evt;
			m_DownsamplingMethod = Downsampling.None;
			base.useNativeRenderPass = false;

			m_ColorPyramidCompute = colorPyramid;
			m_DownsampleKernelID = m_ColorPyramidCompute.FindKernel("KColorDownsample");
			m_GaussianKernelID = m_ColorPyramidCompute.FindKernel("KColorGaussian");
			m_MipLevels = 1;
			m_ReconstructTiles = reconstructTiles;
			if (reconstructTiles)
			{
				//Debug.Log(m_CopyColorMaterial.shader.name + " 0");
				_RECONSTRUCT_VRS_TILES = GlobalKeyword.Create("_RECONSTRUCT_VRS_TILES");
			}

			// END SLZ MODIFIED
		}

		/// <summary>
		/// Get a descriptor and filter mode for the required texture for this pass
		/// </summary>
		/// <param name="downsamplingMethod"></param>
		/// <param name="descriptor"></param>
		/// <param name="filterMode"></param>
		/// <seealso cref="Downsampling"/>
		/// <seealso cref="RenderTextureDescriptor"/>
		/// <seealso cref="FilterMode"/>
		public static void ConfigureDescriptor(Downsampling downsamplingMethod, ref RenderTextureDescriptor descriptor, bool requiresMips, out FilterMode filterMode, out int mipLevels, out MipSize size)
		{
			descriptor.msaaSamples = 1;
			descriptor.depthBufferBits = 0;
			if (downsamplingMethod == Downsampling._2xBilinear)
			{
				descriptor.width /= 2;
				descriptor.height /= 2;
			}
			else if (downsamplingMethod == Downsampling._4xBox || downsamplingMethod == Downsampling._4xBilinear)
			{
				descriptor.width /= 4;
				descriptor.height /= 4;
			}

			filterMode = downsamplingMethod == Downsampling.None ? FilterMode.Point : FilterMode.Bilinear;

			descriptor.autoGenerateMips = false;
			descriptor.useMipMap = requiresMips;
			if (requiresMips)
			{
				descriptor.enableRandomWrite = true;
				// mips with smallest dimension of 1, 2, and 4 useless, and compute shader works on 8x8 blocks, so subtract 3 (mipTruncation) from the mip count
				mipLevels = Mathf.FloorToInt(
					Mathf.Max(Mathf.Log(descriptor.width, 2), Mathf.Log(descriptor.height, 2))
					) + 1 - mipTruncation;
				descriptor.mipCount = mipLevels;
			}
			else
			{
				mipLevels = 1;
			}
			size = new MipSize { width = descriptor.width, height = descriptor.height};
		}

		/// <summary>
		/// Configure the pass with the source and destination to execute on.
		/// </summary>
		/// <param name="source">Source Render Target</param>
		/// <param name="destination">Destination Render Target</param>
		[Obsolete("Use RTHandles for source and destination.")]
		public void Setup(RenderTargetIdentifier source, RenderTargetHandle destination, Downsampling downsampling, bool RequiresMips)
		{
			this.source = RTHandles.Alloc(source);
			this.destination = RTHandles.Alloc(destination.Identifier());
			this.destinationID = destination.id;
			m_DownsamplingMethod = downsampling;
			m_RequiresMips = RequiresMips;
			m_UseRT = false;
		}

		[Obsolete("Use RTHandles for source and destination.")]
		public void Setup(RenderTargetIdentifier source, PersistentRT destination, Downsampling downsampling, bool RequiresMips, bool reconstructTiles = false)
		{
			this.source = RTHandles.Alloc(source);
			this.m_PermanentDest = destination;
			m_DownsamplingMethod = downsampling;
			m_RequiresMips = RequiresMips;
			m_UseRT = true;
			m_ReconstructTiles = reconstructTiles;
		}

		/// <summary>
		/// Configure the pass with the source and destination to execute on.
		/// </summary>
		/// <param name="source">Source Render Target</param>
		/// <param name="destination">Destination Render Target</param>
		public void Setup(RTHandle source, RTHandle destination, Downsampling downsampling, bool RequiresMips, bool reconstructTiles = false)
		{
			this.source = source;
			this.destination = destination;
			m_DownsamplingMethod = downsampling;
			m_RequiresMips = RequiresMips;
			m_UseRT = false;
			m_ReconstructTiles = reconstructTiles;
		}

		public void Setup(RTHandle source, PersistentRT destination, Downsampling downsampling, bool RequiresMips, bool reconstructTiles = false)
		{
			this.source = source;
			this.m_PermanentDest = destination;
			m_DownsamplingMethod = downsampling;
			m_RequiresMips = RequiresMips;
			m_UseRT = true;
			m_ReconstructTiles = reconstructTiles;
		}

		/// <inheritdoc />
		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
			//if (destination == null || destination.rt == null || m_UseRT)
			{
				RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
				descriptor.msaaSamples = 1;
				descriptor.depthBufferBits = 0;
				if (m_DownsamplingMethod == Downsampling._2xBilinear)
				{
					descriptor.width /= 2;
					descriptor.height /= 2;
				}
				else if (m_DownsamplingMethod == Downsampling._4xBox || m_DownsamplingMethod == Downsampling._4xBilinear)
				{
					descriptor.width /= 4;
					descriptor.height /= 4;
				}

				// SLZ MODIFIED
				descriptor.autoGenerateMips = false;
				descriptor.useMipMap = m_RequiresMips;
				if (m_RequiresMips)
				{
					descriptor.enableRandomWrite = true;
					// mips with smallest dimension of 1, 2, and 4 useless, and compute shader works on 8x8 blocks, so subtract 3 (mipTruncation) from the mip count
					m_MipLevels = Mathf.FloorToInt(
						Mathf.Max(Mathf.Log(descriptor.width, 2), Mathf.Log(descriptor.height, 2))
						) + 1 - mipTruncation;
					descriptor.mipCount = m_MipLevels;
				}
				
				m_Size = new MipSize { width = descriptor.width, height = descriptor.height };

				if (m_UseRT)
				{
					destination = m_PermanentDest.GetRTHandle(descriptor, renderingData.cameraData.camera.name, "Opaque");
				}
				else if (destination == null || destination.rt == null)
				{
					cmd.GetTemporaryRT(destinationID, descriptor, m_DownsamplingMethod == Downsampling.None ? FilterMode.Point : FilterMode.Bilinear);
				}

				if (m_RequiresMips)
				{
					m_TempDescriptor = descriptor;
					m_TempDescriptor.width /= 2;
					m_TempDescriptor.height /= 2;
					m_TempDescriptor.useMipMap = false;
					m_TempDescriptor.enableRandomWrite = true;
				}
				cmd.SetGlobalTexture("_CameraOpaqueTexture", destination.nameID);
				// END SLZ MODIFIED

			}
			//else
			//{
			//    cmd.SetGlobalTexture(destination.name, destination.nameID);
			//}

		}

		/// <inheritdoc/>
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			m_PassData.samplingMaterial = m_SamplingMaterial;
			m_PassData.copyColorMaterial = m_CopyColorMaterial;
			m_PassData.downsamplingMethod = m_DownsamplingMethod;
			m_PassData.clearFlag = clearFlag;
			m_PassData.clearColor = clearColor;
			m_PassData.sampleOffsetShaderHandle = m_SampleOffsetShaderHandle;

			m_PassData.colorPyramidCompute = m_ColorPyramidCompute;
			m_PassData.requiresMips = m_RequiresMips;
			m_PassData.mipLevels = m_MipLevels;
			m_PassData.size = m_Size;
			m_PassData.downsampleKernelID = m_DownsampleKernelID;
			m_PassData.gaussianKernelID = m_GaussianKernelID;
			m_PassData.reconstructTiles = m_ReconstructTiles;
			m_PassData.useRT = m_UseRT;
			m_PassData.xrEnabled = renderingData.cameraData.xr.enabled;
			m_PassData.tempDesc = m_TempDescriptor;
			var cmd = renderingData.commandBuffer;

			// TODO RENDERGRAPH: Do we need a similar check in the RenderGraph path?
			//It is possible that the given color target is now the frontbuffer
			if (source == renderingData.cameraData.renderer.GetCameraColorFrontBuffer(cmd))
			{
				source = renderingData.cameraData.renderer.cameraColorTargetHandle;
			}

			bool xrEnabled = renderingData.cameraData.xr.enabled;
			bool disableFoveatedRenderingForPass = xrEnabled && renderingData.cameraData.xr.supportsFoveatedRendering;
			ScriptableRenderer.SetRenderTarget(cmd, destination, k_CameraTarget, clearFlag, clearColor);
			ExecutePass(m_PassData, source, destination, ref renderingData.commandBuffer, xrEnabled, disableFoveatedRenderingForPass);
		}

		private static void ExecutePass(PassData passData, RTHandle source, RTHandle destination, ref CommandBuffer cmd, bool useDrawProceduralBlit, bool disableFoveatedRenderingForPass)
		{
			var samplingMaterial = passData.samplingMaterial;
			var copyColorMaterial = passData.copyColorMaterial;
			var downsamplingMethod = passData.downsamplingMethod;
			var clearFlag = passData.clearFlag;
			var clearColor = passData.clearColor;
			var sampleOffsetShaderHandle = passData.sampleOffsetShaderHandle;

			var colorPyramidCompute = passData.colorPyramidCompute;
			var requiresMips = passData.requiresMips;
			var mipLevels = passData.mipLevels;
			var downsampleKernelID = passData.downsampleKernelID;
			var gaussianKernelID = passData.gaussianKernelID;
			var reconstructTiles = passData.reconstructTiles;
			var useRT = passData.useRT;
			var tempDesc = passData.tempDesc;
			var xrEnabled = passData.xrEnabled;
			var tempHandle = passData.tempHandle;

#if ENABLE_VR && ENABLE_XR_MODULE
			if (disableFoveatedRenderingForPass)
				cmd.SetFoveatedRenderingMode(FoveatedRenderingMode.Disabled);
#endif

			if (samplingMaterial == null)
			{
				Debug.LogErrorFormat(
					"Missing {0}. Copy Color render pass will not execute. Check for missing reference in the renderer resources.",
					samplingMaterial);
				return;
			}

			// TODO RENDERGRAPH: cmd.Blit is not compatible with RG but RenderingUtils.Blits would still call into it in some cases
			using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.CopyColor)))
			{
				ScriptableRenderer.SetRenderTarget(cmd, destination, k_CameraTarget, clearFlag, clearColor);
				if (reconstructTiles)
				{
					cmd.EnableKeyword(_RECONSTRUCT_VRS_TILES);
				}
				switch (downsamplingMethod)
				{
					case Downsampling.None:
						Blitter.BlitCameraTexture(cmd, source, destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, copyColorMaterial, 0);
						break;
					case Downsampling._2xBilinear:
						Blitter.BlitCameraTexture(cmd, source, destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, copyColorMaterial, 0);//1
						break;
					case Downsampling._4xBox:
						samplingMaterial.SetFloat(sampleOffsetShaderHandle, 2);
						Blitter.BlitCameraTexture(cmd, source, destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, samplingMaterial, 0);
						break;
					case Downsampling._4xBilinear:
						Blitter.BlitCameraTexture(cmd, source, destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, copyColorMaterial, 0);//1
						break;
				}
                if (reconstructTiles)
                {
                    cmd.DisableKeyword(_RECONSTRUCT_VRS_TILES);
                }

            }

		   
			if (requiresMips && mipLevels > 1)
			{
				int slices = 1;
#if ENABLE_VR && ENABLE_XR_MODULE
				if (xrEnabled)
				{
					slices = 2;
				}
#endif

				m_SizeArray[0] = passData.size.width;
				m_SizeArray[1] = passData.size.height;
				m_SizeArray[2] = 0;
				m_SizeArray[3] = 0;


				RenderTargetIdentifier tempID = new RenderTargetIdentifier(s_TempBufferID);
				cmd.GetTemporaryRT(s_TempBufferID, tempDesc, FilterMode.Bilinear);
				
				cmd.SetComputeIntParams(colorPyramidCompute, s_SizeID, m_SizeArray);
		
				cmd.SetComputeTextureParam(colorPyramidCompute, downsampleKernelID, s_DestID, tempID, 0);
				cmd.SetComputeTextureParam(colorPyramidCompute, gaussianKernelID, s_SourceID, tempID, 0);
				
				for (int i = 1; i < mipLevels; i++)
				{
					
					cmd.SetComputeTextureParam(colorPyramidCompute, downsampleKernelID, s_SourceID, destination, i - 1);

					m_SizeArray[0] = math.max(m_SizeArray[0] >> 1, 1);
					m_SizeArray[1] = math.max(m_SizeArray[1] >> 1, 1);
					cmd.DispatchCompute(colorPyramidCompute, downsampleKernelID, (int)math.ceil((float)m_SizeArray[0] / 8.0f + 0.00001f),
					                                                             (int)math.ceil((float)m_SizeArray[1] / 8.0f + 0.00001f), slices);

					cmd.SetComputeIntParams(colorPyramidCompute, s_SizeID, m_SizeArray);
					
					cmd.SetComputeTextureParam(colorPyramidCompute, gaussianKernelID, s_DestID, destination, i);
					cmd.DispatchCompute(colorPyramidCompute, gaussianKernelID, (int)math.ceil((float)m_SizeArray[0] / 8.0f + 0.0001f),
					                                                           (int)math.ceil((float)m_SizeArray[1] / 8.0f + 0.0001f), slices);

				}
				
				cmd.ReleaseTemporaryRT(s_TempBufferID);
				
			}

			if (reconstructTiles)
			{
				cmd.DisableKeyword(_RECONSTRUCT_VRS_TILES);
			}
		}

		private class PassData
		{
			internal TextureHandle source;
			internal TextureHandle destination;
			// internal RenderingData renderingData;
			internal bool useProceduralBlit;
			internal bool disableFoveatedRenderingForPass;
			internal CommandBuffer cmd;
			internal Material samplingMaterial;
			internal Material copyColorMaterial;
			internal Downsampling downsamplingMethod;
			internal ClearFlag clearFlag;
			internal Color clearColor;
			internal int sampleOffsetShaderHandle;

			// SLZ MODIFIED

			public ComputeShader colorPyramidCompute;
			public bool requiresMips;
			public int mipLevels;
			public MipSize size;
			public int downsampleKernelID;
			public int gaussianKernelID;
			public bool reconstructTiles;
			public bool useRT;
			public RenderTextureDescriptor tempDesc;
			public TextureHandle tempHandle;
			public bool xrEnabled;
			// END SLZ MODIFIED
		}

		internal TextureHandle Render(RenderGraph renderGraph, out TextureHandle destination, in TextureHandle source, Downsampling downsampling, ref RenderingData renderingData)
		{
			m_DownsamplingMethod = downsampling;
			
			using (var builder = renderGraph.AddRenderPass<PassData>("Copy Color", out var passData, base.profilingSampler))
			{
				RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
				ConfigureDescriptor(downsampling, ref descriptor, m_RequiresMips, out var filterMode, out m_MipLevels, out m_Size);
				if (m_RequiresMips)
				{
					passData.tempDesc = descriptor;
					passData.tempDesc.width = descriptor.width / 2;
					passData.tempDesc.height = descriptor.height / 2;
					passData.tempDesc.useMipMap = false;
					passData.tempDesc.enableRandomWrite = true;
				}
				if (true)
				{
					destination = renderGraph.ImportTexture(m_PermanentDest.GetRTHandle(descriptor, renderingData.cameraData.camera.name, "Opaque"));
				}
				else
				{
					destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "_CameraOpaqueTexture", true, filterMode);
				}
				passData.destination = builder.UseColorBuffer(destination, 0);
				passData.source = builder.ReadTexture(source);
				passData.cmd = renderingData.commandBuffer;
				passData.useProceduralBlit = renderingData.cameraData.xr.enabled;
				passData.disableFoveatedRenderingForPass = renderingData.cameraData.xr.enabled && renderingData.cameraData.xr.supportsFoveatedRendering;
				passData.samplingMaterial = m_SamplingMaterial;
				passData.copyColorMaterial = m_CopyColorMaterial;
				passData.downsamplingMethod = m_DownsamplingMethod;
				passData.clearFlag = clearFlag;
				passData.clearColor = clearColor;
				passData.sampleOffsetShaderHandle = m_SampleOffsetShaderHandle;

				passData.colorPyramidCompute = m_ColorPyramidCompute;
				passData.requiresMips = m_RequiresMips;
				passData.mipLevels = m_MipLevels;
				passData.size = m_Size;
				passData.downsampleKernelID = m_DownsampleKernelID;
				passData.gaussianKernelID = m_GaussianKernelID;
				passData.reconstructTiles = m_ReconstructTiles;
				passData.useRT = m_UseRT;
				passData.xrEnabled = renderingData.cameraData.xr.enabled;
				// TODO RENDERGRAPH: culling? force culling off for testing
				builder.AllowPassCulling(false);

				builder.SetRenderFunc((PassData data, RenderGraphContext context) =>
				{
					ExecutePass(data, data.source, data.destination, ref data.cmd, data.useProceduralBlit, data.disableFoveatedRenderingForPass);
				});
			}

			using (var builder = renderGraph.AddRenderPass<PassData>("Set Global Copy Color", out var passData, base.profilingSampler))
			{
				//RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
				//ConfigureDescriptor(downsampling, ref descriptor, out var filterMode);

				passData.destination = builder.UseColorBuffer(destination, 0);
				passData.cmd = renderingData.commandBuffer;

				// TODO RENDERGRAPH: culling? force culling off for testing
				builder.AllowPassCulling(false);

				builder.SetRenderFunc((PassData data, RenderGraphContext context) =>
				{
					data.cmd.SetGlobalTexture("_CameraOpaqueTexture", data.destination);
				});
			}

			return destination;

		}

		/// <inheritdoc/>
		public override void OnCameraCleanup(CommandBuffer cmd)
		{
			if (cmd == null)
				throw new ArgumentNullException("cmd");

			if (destination.rt == null && destinationID != -1)
			{
				cmd.ReleaseTemporaryRT(destinationID);
				destination.Release();
				destination = null;
			}
		}
	}
}
