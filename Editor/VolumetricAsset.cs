using System.IO;
using UnityEngine;
using UnityEditor.AssetImporters;
using UnityEditor;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;
using System;
using System.Runtime.InteropServices;
using System.IO.Compression;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using static UnityEngine.Rendering.Universal.Internal.CopyColorPass;

namespace SLZ.SLZEditorTools
{
	[ScriptedImporter(1, new string[] { "vol3d" }, new string[] { "" }, AllowCaching = true)]
	public class Vol3dImporter : ScriptedImporter
	{
		public AndroidCompression androidCompression;
		public PCCompression pcCompression;
		public TextureWrapMode wrapMode = TextureWrapMode.Clamp;
		public FilterMode filterMode = FilterMode.Bilinear;
		public TextureCompressionQuality CompressionQuality = TextureCompressionQuality.Best;

		const TextureFormat defaultAndroidCompression = TextureFormat.ASTC_HDR_4x4;
		const TextureFormat defaultPCCompression = TextureFormat.BC6H;


		public enum AndroidCompression : int
		{
			Default = 0,
			ASTC_HDR_4x4   = TextureFormat.ASTC_HDR_4x4,
			ASTC_HDR_5x5   = TextureFormat.ASTC_HDR_5x5,
			ASTC_HDR_6x6   = TextureFormat.ASTC_HDR_6x6,
			ASTC_HDR_8x8   = TextureFormat.ASTC_HDR_8x8,
			ASTC_HDR_10x10 = TextureFormat.ASTC_HDR_10x10,
			ASTC_HDR_12x12 = TextureFormat.ASTC_HDR_12x12,
			ASTC_4x4       = TextureFormat.ASTC_4x4,
		}

		public static Dictionary<TextureFormat, int> BlockSize = new Dictionary<TextureFormat, int>
		{
			{ TextureFormat.DXT1, 4},
			{ TextureFormat.DXT5, 4},
			{ TextureFormat.BC6H, 4},
			{ TextureFormat.BC7, 4},
			{ TextureFormat.ASTC_HDR_4x4, 4 },
			{ TextureFormat.ASTC_HDR_5x5, 5 },
			{ TextureFormat.ASTC_HDR_6x6, 6 },
			{ TextureFormat.ASTC_HDR_8x8, 8 },
			{ TextureFormat.ASTC_HDR_10x10, 10 },
			{ TextureFormat.ASTC_HDR_12x12, 12 },
			{ TextureFormat.ASTC_4x4, 4 },
		};

		public static Dictionary<TextureFormat, int> BytesPerBlock = new Dictionary<TextureFormat, int>
		{
			{ TextureFormat.RGB24, 3},
			{ TextureFormat.RGBA32, 4},
			{ TextureFormat.RGBA64, 8},
			{ TextureFormat.RGBAHalf, 8},
			{ TextureFormat.RGBAFloat, 16},
			{ TextureFormat.DXT1, 8},
			{ TextureFormat.DXT5, 16},
			{ TextureFormat.BC6H, 16},
			{ TextureFormat.BC7, 16},
			{ TextureFormat.ASTC_HDR_4x4, 16 },
			{ TextureFormat.ASTC_HDR_5x5, 16 },
			{ TextureFormat.ASTC_HDR_6x6, 16 },
			{ TextureFormat.ASTC_HDR_8x8, 16 },
			{ TextureFormat.ASTC_HDR_10x10, 16 },
			{ TextureFormat.ASTC_HDR_12x12, 16 },
			{ TextureFormat.ASTC_4x4, 16 },
		};

		public enum PCCompression : int
		{
			Default = 0,
			BC6H = TextureFormat.BC6H,
			NonHdrBC7 = TextureFormat.BC7
		}

		public override void OnImportAsset(AssetImportContext ctx)
		{
			var buildTarget = ctx.selectedBuildTarget;
			string path = Path.GetFullPath(ctx.assetPath);
			bool dispFileBytes = false;
			NativeArray<byte> fileBytes = Vol3d.ReadVol3DToNative(path, out Vol3d.ImageInfo info);
			try
			{
				TextureFormat compressedFmt = GetPlatformFormat(buildTarget);
				TextureFormat uncompFmt = GraphicsFormatUtility.GetTextureFormat(info.graphicsFormat);
				//Debug.Log("Uncompressed Format: " + uncompFmt.ToString() + " " + (GraphicsFormat)info.graphicsFormat);
				int rawSliceSize = fileBytes.Length / info.depth;
				bool needsPadding = compressedFmt == TextureFormat.BC6H;

				
				int mipLevels = math.max(1, info.mipLevels);
				//Debug.Log("Mip Levels: " + mipLevels);
				int blockSize = BlockSize[compressedFmt];
				int bytesPerBlock = BytesPerBlock[compressedFmt];
				int bytesPerRawPixel = BytesPerBlock[uncompFmt];
				int2[] mipBlocks = new int2[mipLevels];
				
				int3[] mipDim = new int3[mipLevels];

				int[] cmpSliceLen = new int[mipLevels];
				int[] uncmpSliceLen = new int[mipLevels];

				int3 imageDim = new int3(info.width, info.height, info.depth);
				for (int mip = 0; mip < mipLevels; mip++)
				{
					mipDim[mip] = math.max(imageDim >> mip, 1);

					// closest multiple of the block size
					mipBlocks[mip] = (mipDim[mip].xy + (blockSize - 1)) / blockSize;
				}
				if (needsPadding)
				{
					for (int mip = 1; mip < mipLevels; mip++)
					{
						mipDim[mip] = new int3(mipBlocks[mip] * blockSize, mipDim[mip].z);
					}
				}

				int[] compressedMipPtr = new int[mipLevels + 1];
				int[] uncompressedMipPtr = new int[mipLevels + 1];
				compressedMipPtr[0] = 0;
				uncompressedMipPtr[0] = 0;
				for (int mip = 0; mip < mipLevels; mip++)
				{
					cmpSliceLen[mip] = bytesPerBlock * ( mipBlocks[mip].x * mipBlocks[mip].y);
					compressedMipPtr[mip + 1] = compressedMipPtr[mip] + mipDim[mip].z * cmpSliceLen[mip];

					uncmpSliceLen[mip] = bytesPerRawPixel * mipDim[mip].x * mipDim[mip].y;
					uncompressedMipPtr[mip + 1] = uncompressedMipPtr[mip] + mipDim[mip].z * uncmpSliceLen[mip];
				}

				bool compress = true;

				Texture3D volumeCompressed = new Texture3D(info.width, info.height, info.depth, 
					compress ? compressedFmt : uncompFmt, info.mipLevels > 1);
				volumeCompressed.wrapMode = wrapMode;
				volumeCompressed.filterMode = filterMode;

				if (needsPadding)
				{
					CopyAndCompressTextureBC6H (fileBytes, volumeCompressed, info, uncompFmt, compressedFmt,
						mipLevels, ref mipDim, ref uncompressedMipPtr, ref compressedMipPtr, ref uncmpSliceLen, ref cmpSliceLen, bytesPerRawPixel);
				}
				else
				{
					CopyAndCompressTexture<byte, byte>(fileBytes, volumeCompressed, info, uncompFmt, compressedFmt,
						mipLevels, ref mipDim, ref uncompressedMipPtr, ref compressedMipPtr, ref uncmpSliceLen, ref cmpSliceLen, true);
				}
				/*
				for (int slice = 0; slice < info.depth; slice++)
				{
					Texture2D tex;
					tex = new Texture2D(info.width, info.height, uncompFmt, true, true);
					for (int mip = 0; mip < mipLevels; mip++)
					{
						if (slice < mipDim[mip].z)
						{
							Debug.Log("Mip: " + mip + " Slice Pointer: " + uncompressedMipPtr[mip] + slice * uncmpSliceBytes[mip]);
							NativeArray<byte> texBacking = tex.GetPixelData<byte>(mip);
							//if (uncmpSliceBytes[mip] != texBacking.Length) Debug.LogError("Expected size: " + uncmpSliceBytes[mip] + " got: " + texBacking.Length);
							NativeArray<byte>.Copy(fileBytes, uncompressedMipPtr[mip] + slice * uncmpSliceBytes[mip], texBacking, 0, uncmpSliceBytes[mip]);
						}
					}
					//rawPtr += texBacking.Length;
					//texBacking.Dispose();
					if (compress)
					{
						EditorUtility.CompressTexture(tex, compressedFmt, (int)CompressionQuality);
					}
					if (slice == 0)
					{
						AssetDatabase.CreateAsset(tex, "Assets/TestTexture.asset");
					}
					//if (d == 0) Debug.Log("Dimensions: " + tex.width + tex.height);
					for (int mip = 0; mip < mipLevels; mip++)
					{
						int mipDepth = mipDim[mip].z;
						if (slice < mipDepth)
						{
							NativeArray<byte> texBacking = tex.GetPixelData<byte>(mip);
							if (cmpSliceBytes[mip] != texBacking.Length) Debug.LogError("Cmp Expected size: " + cmpSliceBytes[mip] + " got: " + texBacking.Length);
							NativeArray<byte> vcData = volumeCompressed.GetPixelData<byte>(mip);
							if (vcData.Length / mipDepth != texBacking.Length) Debug.LogError("VC Expected size: " + vcData.Length + " got: " + texBacking.Length);
							NativeArray<byte>.Copy(texBacking, 0, vcData,
								compress ? slice * cmpSliceBytes[mip] : slice * uncmpSliceBytes[mip],
								compress ? cmpSliceBytes[mip] : uncmpSliceBytes[mip]);
						}
					}

					DestroyImmediate(tex);
				}
				*/
				///GraphicsFormat graphicsFormat = GraphicsFormatUtility.GetGraphicsFormat(compressedFmt, false);
			
				
				SerializedObject volSerial = new SerializedObject(volumeCompressed);
				SerializedProperty isReadable = volSerial.FindProperty("m_IsReadable");
				isReadable.boolValue = false;
				//SerializedProperty isSRGB = volSerial.FindProperty("m_IsReadable");
				volSerial.ApplyModifiedProperties();
				ctx.AddObjectToAsset("MainTex", volumeCompressed);
				ctx.SetMainObject(volumeCompressed);
			}
			finally
			{
				fileBytes.Dispose();
			}
		}

		TextureFormat GetPlatformFormat(BuildTarget target)
		{
			if (target == BuildTarget.Android)
			{
				if (androidCompression == AndroidCompression.Default)
				{
					return defaultAndroidCompression;
				}
				else
				{
					return (TextureFormat)androidCompression;
				}
			}
			else
			{
				if (pcCompression == PCCompression.Default)
				{
					return defaultPCCompression;
				}
				else
				{
					return (TextureFormat)pcCompression;
				}
			}
		}

		void CopyAndCompressTexture<T,T2>(NativeArray<T> fileBytes, Texture3D tex3D,
			Vol3d.ImageInfo info, TextureFormat uncompFmt, TextureFormat compressedFmt,
			int mipLevels, ref int3[] mipDim, ref int[] uncompressedMipPtr, ref int[] compressedMipPtr,
			ref int[] uncmpSliceLen, ref int[] cmpSliceLen, bool compress
			) where T : struct
			 where T2 : struct
		{
			for (int slice = 0; slice < info.depth; slice++)
			{
				Texture2D tex;
				tex = new Texture2D(info.width, info.height, uncompFmt, true, true);
				for (int mip = 0; mip < mipLevels; mip++)
				{
					if (slice < mipDim[mip].z)
					{
						//Debug.Log("Mip: " + mip + " Slice Pointer: " + uncompressedMipPtr[mip] + slice * uncmpSliceLen[mip]);
						NativeArray<T> texBacking = tex.GetPixelData<T>(mip);
						//if (uncmpSliceBytes[mip] != texBacking.Length) Debug.LogError("Expected size: " + uncmpSliceBytes[mip] + " got: " + texBacking.Length);
						NativeArray<T>.Copy(fileBytes, uncompressedMipPtr[mip] + slice * uncmpSliceLen[mip], texBacking, 0, uncmpSliceLen[mip]);
					}
				}
				//rawPtr += texBacking.Length;
				//texBacking.Dispose();
				//if (slice == 0)
				//{
				//	AssetDatabase.CreateAsset(tex, "Assets/TestTexture.asset");
				//}
				if (compress)
				{
					EditorUtility.CompressTexture(tex, compressedFmt, (int)CompressionQuality);
				}

				//if (d == 0) Debug.Log("Dimensions: " + tex.width + tex.height);
				for (int mip = 0; mip < mipLevels; mip++)
				{
					int mipDepth = mipDim[mip].z;
					if (slice < mipDepth)
					{
						NativeArray<T2> texBacking = tex.GetPixelData<T2>(mip);
						//if (cmpSliceLen[mip] != texBacking.Length) Debug.LogError("Cmp Expected size: " + cmpSliceLen[mip] + " got: " + texBacking.Length);
						NativeArray<T2> vcData = tex3D.GetPixelData<T2>(mip);
						//if (vcData.Length / mipDepth != texBacking.Length) Debug.LogError("VC Expected size: " + vcData.Length + " got: " + texBacking.Length);
						NativeArray<T2>.Copy(texBacking, 0, vcData,
							compress ? slice * cmpSliceLen[mip] : slice * uncmpSliceLen[mip],
							compress ? cmpSliceLen[mip] : uncmpSliceLen[mip]);
					}
				}

				DestroyImmediate(tex);
			}
		}

		void CopyAndCompressTextureBC6H(NativeArray<byte> fileBytes, Texture3D tex3D,
			Vol3d.ImageInfo info, TextureFormat uncompFmt, TextureFormat compressedFmt,
			int mipLevels, ref int3[] mipDim, ref int[] uncompressedMipPtr, ref int[] compressedMipPtr,
			ref int[] uncmpSliceLen, ref int[] cmpSliceLen, int pixelSize
			) 
		{
			NativeArray<byte> paddedBytes = GetPaddedTexture<byte>(fileBytes, mipDim[0], mipLevels, pixelSize);
			//File.WriteAllBytes("C:/temp/TestFile.bin", paddedBytes.ToArray());
			try
			{
				for (int mip = 0; mip < mipLevels; mip++)
				{
					for (int slice = 0; slice < mipDim[mip].z; slice++)
					{
						Texture2D tex;
						tex = new Texture2D(mipDim[mip].x, mipDim[mip].y, uncompFmt, false, true);

						//Debug.Log("Mip: " + mip + " Slice Pointer: " + uncompressedMipPtr[mip] + slice * uncmpSliceLen[mip]);
						NativeArray<byte> texBacking = tex.GetPixelData<byte>(0);
						//if (uncmpSliceBytes[mip] != texBacking.Length) Debug.LogError("Expected size: " + uncmpSliceBytes[mip] + " got: " + texBacking.Length);
						NativeArray<byte>.Copy(paddedBytes, uncompressedMipPtr[mip] + slice * uncmpSliceLen[mip], texBacking, 0, uncmpSliceLen[mip]);

						//rawPtr += texBacking.Length;
						//texBacking.Dispose();

						EditorUtility.CompressTexture(tex, compressedFmt, (int)CompressionQuality);

						//if (d == 0) Debug.Log("Dimensions: " + tex.width + tex.height);

						int mipDepth = mipDim[mip].z;

						NativeArray<byte> texBacking2 = tex.GetPixelData<byte>(0);
						//if (cmpSliceLen[mip] != texBacking.Length) Debug.LogError("Cmp Expected size: " + cmpSliceLen[mip] + " got: " + texBacking.Length);
						NativeArray<byte> vcData = tex3D.GetPixelData<byte>(mip);
						//if (vcData.Length / mipDepth != texBacking.Length) Debug.LogError("VC Expected size: " + vcData.Length + " got: " + texBacking.Length);
						NativeArray<byte>.Copy(texBacking2, 0, vcData, slice * cmpSliceLen[mip], cmpSliceLen[mip]);
						DestroyImmediate(tex);
					}
				}
			}
			finally
			{
				paddedBytes.Dispose();
			}
		}

		NativeArray<T> GetPaddedTexture<T>(NativeArray<T> rawImage, int3 dimensions, int numMips, int pixelSize, int padSize = 4) where T : struct
		{
			int3 padAdd = new int3(padSize - 1, padSize - 1, 0);
			int3 padMul = new int3(padSize, padSize, 1);
			Span<int3> mipDim = stackalloc int3[numMips];
			Span<int3> padMipDim = stackalloc int3[numMips];
			Span<int> mipPtr = stackalloc int[numMips];
			Span<int> padMipPtr = stackalloc int[numMips];

			mipDim[0] = padMipDim[0] = dimensions; // mip 0 guaranteed to be multiple of 4 on x and y
			mipPtr[0] = padMipPtr[0] = 0;
			int totalPadTSize = dimensions.x * dimensions.y * dimensions.z * pixelSize;
			for (int mip = 1; mip < numMips; mip++)
			{
				int prevMip = mip - 1;
				mipDim[mip] = math.max(dimensions >> mip, 1);
				padMipDim[mip] = ((mipDim[mip] + padAdd) / padMul) * padMul;
				mipPtr[mip] = mipPtr[prevMip] + mipDim[prevMip].x * mipDim[prevMip].y * mipDim[prevMip].z * pixelSize;
				padMipPtr[mip] = padMipPtr[prevMip] + padMipDim[prevMip].x * padMipDim[prevMip].y * padMipDim[prevMip].z * pixelSize;
				totalPadTSize += padMipPtr[mip];
			}

			NativeArray<T> padTex = new NativeArray<T>(totalPadTSize, Allocator.Temp);
			for (int mip = 0; mip < numMips; mip++)
			{
				int rowPad = (padMipDim[mip].x - mipDim[mip].x) * pixelSize;
				int columnPad = padMipDim[mip].x * (padMipDim[mip].y - mipDim[mip].z) * pixelSize;
				bool xLargerThanBlock = mipDim[mip].x >= padSize;
				bool yLargerThanBlock = mipDim[mip].y >= padSize;
				for (int z = 0; z < mipDim[mip].z; z++)
				{
					int slicePtr = mipPtr[mip] + z * mipDim[mip].x * mipDim[mip].y * pixelSize;
					int padSlicePtr = padMipPtr[mip] + z * padMipDim[mip].x * padMipDim[mip].y * pixelSize;
					//Debug.Log(padMipDim[mip].x);
					for (int y = 0; y < mipDim[mip].y; y++)
					{
						int rowPtr = slicePtr + y * mipDim[mip].x * pixelSize;
						int padRowPtr = padSlicePtr + y * padMipDim[mip].x * pixelSize;
						int rowCount = mipDim[mip].x * pixelSize;
						NativeArray<T>.Copy(rawImage, rowPtr, padTex, padRowPtr, rowCount);
						if (rowPad > 0)
						{
							if (xLargerThanBlock)
							{
								NativeArray<T>.Copy(rawImage, rowPtr + rowCount - rowPad, padTex, padRowPtr + rowCount, rowPad);
							}
							else
							{
								for (int p = 0; p < rowPad; p++)
								{
									NativeArray<T>.Copy(rawImage, rowPtr + rowCount - 1, padTex, padRowPtr + rowCount + p, 1);
								}
							}
						}
					}

					if (columnPad > 0)
					{
						int padColumnPtr = padMipPtr[mip] + (z + 1) * padMipDim[mip].x * padMipDim[mip].y * pixelSize;
						if (yLargerThanBlock)
						{
							NativeArray<T>.Copy(padTex, padColumnPtr - columnPad, padTex, padColumnPtr, columnPad);
						}
						else
						{
							int rowSize = padMipDim[mip].x * pixelSize;
							for (int p = 0; p < rowPad; p++)
							{
								NativeArray<T>.Copy(padTex, padColumnPtr - 1, padTex, padColumnPtr + p * rowSize, columnPad);
							}
						}
					}
				}

			}
			return padTex;
		}

		/// <summary>
		/// Manually pad mips of the texture to work around a bug in Unity's BC6H compressor not padding mip levels before compressing
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="rawImage">NativeArray containing the entire uncompressed image</param>
		/// <param name="dimensions">width, height, and depth of the image's lowest mip</param>
		/// <param name="mips">Number of mip levels</param>
		/// <param name="pixelSize">number of elements in the nativeArray that represent a single pixel</param>
		/// <param name="padSize">number of pixels in a block that we need to pad to be a multiple of, 4 for BC6H</param>
		/// <returns></returns>
		NativeArray<T> GetPaddedTextureJobs<T>(NativeArray<T> rawImage, int3 dimensions, int numMips, int pixelSize, int padSize = 4) where T : struct
		{
			int3 padAdd = new int3(padSize - 1, padSize - 1, 0);
			int3 padMul = new int3(padSize, padSize, 1);

			//int tSize = Marshal.SizeOf<T>();
			int rawPtr = dimensions.x * dimensions.y * dimensions.z * pixelSize;
			int paddedPtr = rawPtr;
			NativeArray<rowPadInfo> rowInfos = new NativeArray<rowPadInfo>(numMips - 1, Allocator.Temp);
			NativeArray<int> mipRowOffsets = new NativeArray<int>(numMips - 1, Allocator.Temp);
			int3 mipDim = new int3(0,0,0);
			int3 paddedDim = new int3(0,0,0);
			int rowOffset = 0;
			int padRowCount = 0;
			for (int level = 0; level < numMips - 1; level++)
			{
				rowOffset += mipDim.y * mipDim.z;
				mipDim = math.max(dimensions >> level, 1);
				paddedDim = ((mipDim + padAdd) / padMul) * padMul;
				padRowCount += mipDim.y * mipDim.z;
				mipRowOffsets[level] = rowOffset;
				rowInfos[level] = new rowPadInfo
				(
					rawPtr,
					paddedPtr,
					paddedDim.x * paddedDim.y * pixelSize,
					(ushort)mipDim.y,
					(ushort)(mipDim.x * pixelSize),
					(ushort)(paddedDim.x * pixelSize),
					(ushort)((paddedDim.x - mipDim.x) * pixelSize)
				);
			
				rawPtr += mipDim.x * mipDim.y * mipDim.z * pixelSize;
				paddedPtr += paddedDim.x * paddedDim.y * paddedDim.z * pixelSize;
			}

		
			NativeArray<T> outImage = new NativeArray<T>(paddedPtr, Allocator.TempJob);
			for (int mip = 0; mip < numMips - 1; mip++)
			{

			}

			return outImage;
		}


		readonly struct rowPadInfo
		{
			public readonly int rawPointer; // Start of the mip in the unpadded array
			public readonly int paddedPointer; // Start of the mip in the padded array
			public readonly int paddedSliceSize; // Size of a single padded Z slice, including the padding on both the rows and columns
			public readonly ushort rowsPerSlice; // number of rows per Z slice in the unpadded array (ie the height)
			public readonly ushort rowLength; // number of elements in the array of a single unpadded row (pixels * bytes per pixel / bytes per array element)
			public readonly ushort paddedRowLength; // number of elements in the array of a single padded row
			public readonly ushort padLength; // paddedRowLength - rowLength

			public rowPadInfo(

				int rawPointer,
				int paddedPointer,
				int paddedSliceSize,
				ushort rowsPerSlice,
				ushort rowLength,
				ushort paddedRowLength,
				ushort padLength)
			{
				this.rawPointer = rawPointer;
				this.paddedPointer = paddedPointer;
				this.paddedSliceSize = paddedSliceSize;
				this.rowsPerSlice = rowsPerSlice;
				this.rowLength = rowLength;
				this.paddedRowLength = paddedRowLength;
				this.padLength = padLength;
			}

		}

		[BurstCompile]
		struct PadRow<T> : IJobParallelFor where T : struct
		{
			[NativeDisableContainerSafetyRestriction]
			public NativeArray<T> paddedData;
			[ReadOnly]
			public NativeArray<T> rawData;
			[ReadOnly]
			public NativeArray<int> mipRowOffset;
			[ReadOnly]
			public NativeArray<rowPadInfo> mipInfos;

			public void Execute(int i)
			{
				int mip = GetMipLevel(mipRowOffset.Length, i);
				rowPadInfo m = mipInfos[mip];
				int mipRow = i - mipRowOffset[mip];
				int rowSliceIdx = mipRow % m.rowsPerSlice; // index of the row in the slice
				int sliceOffset = (mipRow / m.rowsPerSlice) * m.paddedSliceSize + m.paddedPointer;
				int slicePtr = rowSliceIdx * m.paddedRowLength + sliceOffset;
				NativeArray<T>.Copy(rawData, mipRow * m.rowLength + m.rawPointer, 
					paddedData, slicePtr, m.rowLength);
				NativeArray<T>.Copy(rawData, (mipRow + 1) * m.rowLength - m.padLength,
					paddedData, slicePtr + m.rowLength, m.padLength);
			}

			int GetMipLevel([AssumeRange(0,12)] int numMips, int threadIdx)
			{
				int mip = 0;
				for (; mip < numMips; mip++)
				{
					if (mipRowOffset[mip] > threadIdx) break;
				}
				mip = math.max(0, mip - 1);
				return mip;
			}

		}

		#region SRGB_EXPERIMENT_NOT_USED
		/*
		static float3 Linear2sRGB(float3 val)
		{
			bool3 disc = val < 0.0031308f;
			float3 small = val * 12.92f;
			float3 big = (1.055f * math.pow(val, 1.0f / 2.4f)) - 0.055f;
			float3 final = new float3(disc.x ? small.x : big.x, disc.y ? small.y : big.y, disc.z ? small.z : big.z);
			return final;
		}

		interface IsRGB2Linear
		{
			public NativeArray<byte> FileBytes { get; set; }
			public JobHandle ScheduleI(int i, int i2);
		}

		[BurstCompile]
		struct RGB24Linear2SRGB : IJobParallelFor, IsRGB2Linear
		{
			[NativeDisableContainerSafetyRestriction]
			public NativeArray<byte> fileBytes;
			public NativeArray<byte> FileBytes { get => fileBytes; set => fileBytes = value; }
			const float fixedToFloat = (1.0f / 255.0f);
			public void Execute(int i)
			{
				int idx = 3 * i;
				float3 color = new float3(fileBytes[idx], fileBytes[idx] + 1, fileBytes[idx + 2]);
				color *= fixedToFloat;
				color = Linear2sRGB(color);
				color *= 255.0f;

				//ReadOnlySpan<RGB24> colorSpan = stackalloc RGB24[1] { new RGB24((byte)color.x, (byte)color.y, (byte)color.z) };
				//ReadOnlySpan<byte> castbyte = MemoryMarshal.AsBytes(colorSpan);
				//NativeArraySpanExt.Copy(castbyte, 0, fileBytes, idx, 3);
				fileBytes[idx] = (byte)color.x;
				fileBytes[idx + 1] = (byte)color.y;
				fileBytes[idx + 2] = (byte)color.z;
			}

			public JobHandle ScheduleI(int i, int i2)
			{
				return this.Schedule(i, i2);
			}
		}

		[BurstCompile]
		struct RGBHalf2SRGB : IJobParallelFor
		{
			[NativeDisableContainerSafetyRestriction]
			public NativeArray<byte> fileBytes;
			const float fixedToFloat = (1.0f / 255.0f);
			public void Execute(int i)
			{
				int idx = 6 * i;
				ReadOnlySpan<byte> rb = stackalloc byte[2] { fileBytes[idx], fileBytes[idx + 1] };
				ReadOnlySpan<byte> gb = stackalloc byte[2] { fileBytes[idx + 2], fileBytes[idx + 3] };
				ReadOnlySpan<byte> bb = stackalloc byte[2] { fileBytes[idx + 4], fileBytes[idx + 5] };
				ReadOnlySpan<half> ru = MemoryMarshal.Cast<byte, half>(rb);

				float3 color = new float3(fileBytes[idx], fileBytes[idx] + 1, fileBytes[idx + 2]);
				color *= fixedToFloat;
				color = Linear2sRGB(color);
				color *= 255.0f;

				//ReadOnlySpan<RGB24> colorSpan = stackalloc RGB24[1] { new RGB24((byte)color.x, (byte)color.y, (byte)color.z) };
				//ReadOnlySpan<byte> castbyte = MemoryMarshal.AsBytes(colorSpan);
				//NativeArraySpanExt.Copy(castbyte, 0, fileBytes, idx, 3);
				fileBytes[idx] = (byte)color.x;
				fileBytes[idx + 1] = (byte)color.y;
				fileBytes[idx + 2] = (byte)color.z;
			}
		}
		*/
		#endregion
	}


	public static class Vol3d
	{
		const int headerSize = 32;
		public const string fileExtension = ".vol3d";
		[StructLayout(LayoutKind.Sequential)]
		struct Header
		{
			public UInt32 name;
			public UInt16 graphicsFormat;
			public UInt16 mipLevels;
			public UInt16 width;
			public UInt16 height;
			public UInt16 depth;
			public UInt16 compressionFormat;
			public UInt64 compressedLength;
			public UInt64 uncompressedLength;

			public Header(UInt16 graphicsFormat, UInt16 mipLevels, UInt16 width, UInt16 height, UInt16 depth, UInt64 compressedLength, UInt64 uncompressedLength)
			{
				this.name = ((uint)'V') | ((uint)'O' << 8) | ((uint)'L' << 16) | ((uint)'3' << 24);
				this.graphicsFormat = graphicsFormat;
				this.mipLevels = mipLevels;
				this.width = width;
				this.height = height;
				this.depth = depth;
				this.compressionFormat = 0;
				this.compressedLength = compressedLength;
				this.uncompressedLength = uncompressedLength;
			}
		}

		public enum CompressionFormat : UInt16
		{
			NONE = 0,
			DEFLATE = 1,
		}

		public struct ImageInfo
		{
			public GraphicsFormat graphicsFormat;
			public int mipLevels;
			public int width;
			public int height;
			public int depth;
		}

		[MenuItem("Tools/Test Create Vol3d")]
		public static void TestSave3DTex()
		{
			UnityEngine.Object selection = Selection.activeObject;
			if (selection.GetType() == typeof(Texture3D))
			{
				Texture3D texture = (Texture3D)selection;
				string path = Path.GetFullPath(AssetDatabase.GetAssetPath(texture));
				path += ".vol3d";
				WriteTex3DToVol3D(texture, path);
			}
		}
		public static void WriteTex3DToVol3D(Texture3D tex, string path)
		{
			NativeArray<byte>[] data = new NativeArray<byte>[tex.mipmapCount];
			int dataLength = 0;
			for (int mip = 0; mip < tex.mipmapCount; mip++)
			{
				data[mip] = tex.GetPixelData<byte>(mip);
				dataLength += data[mip].Length;
			}
			int graphicsFormat = (int)tex.graphicsFormat;
			ulong length = (ulong)dataLength;
			if (length == 0)
			{
				throw new Exception("Vol3d Saver: Failed to create vol3d. Texture3D has no data!");
			}
			Header header = new Header((ushort)graphicsFormat, (ushort)tex.mipmapCount, (ushort)tex.width, (ushort)tex.height, (ushort)tex.depth, length, length);
			bool needsDirty = File.Exists(path);
			using FileStream outStream = File.Create(path);
			//byte[] fileOut = new byte[headerSize + data.Length];

			byte[] fileUncompressed = new byte[dataLength];
			int ptr = 0;
			for (int mip = 0; mip < tex.mipmapCount; mip++)
			{
				NativeArray<byte>.Copy(data[mip], 0, fileUncompressed, ptr, data[mip].Length); ;
				ptr += data[mip].Length;
			}
			if (true)
			{

				header.compressionFormat = (UInt16)CompressionFormat.DEFLATE;
				byte[] headerBytes = HeaderToBytes(header);
				outStream.Write(headerBytes, 0, headerBytes.Length);
				using MemoryStream rawStream = new MemoryStream(fileUncompressed);
				using DeflateStream compress = new DeflateStream(outStream, CompressionMode.Compress);
				rawStream.CopyTo(compress);
			}
			//else
			//{
			//	byte[] headerBytes = HeaderToBytes(header);
			//	outStream.Write(headerBytes, 0, headerBytes.Length);
			//	outStream.Write(fileUncompressed, 0, fileUncompressed.Length);
			//}
		}

		public static NativeArray<byte> ReadVol3DToNative(string path, out ImageInfo imageInfo)
		{
			path = Path.GetFullPath(path);
			using FileStream inStream = File.OpenRead(path);
			Span<byte> headerByte = stackalloc byte[headerSize];
			inStream.Read(headerByte);
			ReadOnlySpan<Header> header = MemoryMarshal.Cast<byte, Header>(headerByte);

			imageInfo = new ImageInfo();
			imageInfo.graphicsFormat = (GraphicsFormat)header[0].graphicsFormat;
			imageInfo.mipLevels = header[0].mipLevels;
			imageInfo.width = header[0].width;
			imageInfo.height = header[0].height;
			imageInfo.depth = header[0].depth;
			int cfilesize = (int)header[0].compressedLength;
			int dfilesize = (int)header[0].uncompressedLength;
			bool compressed = header[0].compressionFormat > 0;

			if (compressed)
			{
				using MemoryStream decompressedStream = new MemoryStream(dfilesize);
				using DeflateStream decompressor = new DeflateStream(inStream, CompressionMode.Decompress);
				decompressor.CopyTo(decompressedStream);
				byte[] decompressedBytes = decompressedStream.ToArray();
				NativeArray<byte> vol3d = new NativeArray<byte>(dfilesize, Allocator.Persistent);
				NativeArray<byte>.Copy(decompressedBytes, 0, vol3d, 0, dfilesize);
				return vol3d;
			}
			else
			{
				//Debug.Log("UncompressedData");
				NativeArray<byte> vol3d = new NativeArray<byte>(dfilesize, Allocator.Persistent);
				byte[] uncompressedBytes = new byte[dfilesize];
				inStream.Read(uncompressedBytes, 0, dfilesize);
				NativeArray<byte>.Copy(uncompressedBytes, 0, vol3d, 0, dfilesize);
				return vol3d;
			}
		}

		static byte[] HeaderToBytes(Header header)
		{
			IntPtr headerPtr = IntPtr.Zero;
			byte[] headerBytes = new byte[headerSize];
			try
			{
				headerPtr = Marshal.AllocHGlobal(headerSize);
				Marshal.StructureToPtr(header, headerPtr, false);
				Marshal.Copy(headerPtr, headerBytes, 0, headerBytes.Length);
			}
			finally
			{
				if (headerPtr != IntPtr.Zero)
				{
					Marshal.FreeHGlobal(headerPtr);
				}
			}
			return headerBytes;
		}
	}
}