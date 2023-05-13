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
using UnityEngine.Rendering.Universal;

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
			ASTC_HDR_4x4 = TextureFormat.ASTC_HDR_4x4,
			ASTC_HDR_5x5 = TextureFormat.ASTC_HDR_5x5,
			ASTC_HDR_6x6 = TextureFormat.ASTC_HDR_6x6,
			ASTC_HDR_8x8 = TextureFormat.ASTC_HDR_8x8,
			ASTC_HDR_10x10 = TextureFormat.ASTC_HDR_10x10,
			ASTC_HDR_12x12 = TextureFormat.ASTC_HDR_12x12,
			ASTC_4x4 = TextureFormat.ASTC_4x4,
		}

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
				Debug.Log("Uncompressed Format: " + uncompFmt.ToString() + " " + (GraphicsFormat)info.graphicsFormat);
				int rawSliceSize = fileBytes.Length / info.depth;
				int rawPtr = 0;
				int compressedPtr = 0;
				for (int d = 0; d < info.depth; d++)
				{
					Texture2D tex;
					tex = new Texture2D(info.width, info.height, uncompFmt, false, true);
					NativeArray<byte> texBacking = tex.GetPixelData<byte>(0);
					NativeArray<byte>.Copy(fileBytes, rawPtr, texBacking, 0, texBacking.Length);
					rawPtr += texBacking.Length;
					//texBacking.Dispose();
					EditorUtility.CompressTexture(tex, compressedFmt, (int)CompressionQuality);
					if (d == 0) Debug.Log("Dimensions: " + tex.width + tex.height);
					NativeArray<byte> texBacking2 = tex.GetPixelData<byte>(0);
					NativeArray<byte>.Copy(texBacking2, 0, fileBytes, compressedPtr, texBacking2.Length);
					compressedPtr += texBacking2.Length;
					texBacking2.Dispose();
					DestroyImmediate(tex);
				}
				Debug.Log("Slice Size: " + compressedPtr / info.depth);
				///GraphicsFormat graphicsFormat = GraphicsFormatUtility.GetGraphicsFormat(compressedFmt, false);
				Texture3D volumeCompressed = new Texture3D(info.width, info.height, info.depth, compressedFmt, false);
				volumeCompressed.wrapMode = wrapMode;
				volumeCompressed.filterMode = filterMode;
				NativeArray<byte> vcData = volumeCompressed.GetPixelData<byte>(0);
				NativeArray<byte>.Copy(fileBytes, 0, vcData, 0, vcData.Length);
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
			public Int32 graphicsFormat;
			public UInt16 width;
			public UInt16 height;
			public UInt16 depth;
			public UInt16 compressionFormat;
			public UInt64 compressedLength;
			public UInt64 uncompressedLength;

			public Header(Int32 graphicsFormat, UInt16 width, UInt16 height, UInt16 depth, UInt64 compressedLength, UInt64 uncompressedLength)
			{
				this.name = ((uint)'V') | ((uint)'O' << 8) | ((uint)'L' << 16) | ((uint)'3' << 24);
				this.graphicsFormat = graphicsFormat;
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
			NativeArray<byte> data = tex.GetPixelData<byte>(0);
			int graphicsFormat = (int)tex.graphicsFormat;
			ulong length = (ulong)data.Length;
			if (length == 0)
			{
				throw new Exception("Vol3d Saver: Failed to create vol3d. Texture3D has no data!");
			}
			Header header = new Header(graphicsFormat, (ushort)tex.width, (ushort)tex.height, (ushort)tex.depth, length, length);
			bool needsDirty = File.Exists(path);
			using FileStream outStream = File.Create(path);
			//byte[] fileOut = new byte[headerSize + data.Length];
			byte[] fileUncompressed = new byte[data.Length];
			NativeArray<byte>.Copy(data, 0, fileUncompressed, 0, data.Length);
			if (true)
			{

				header.compressionFormat = (UInt16)CompressionFormat.DEFLATE;
				byte[] headerBytes = HeaderToBytes(header);
				outStream.Write(headerBytes, 0, headerBytes.Length);
				using MemoryStream rawStream = new MemoryStream(fileUncompressed);
				using DeflateStream compress = new DeflateStream(outStream, CompressionMode.Compress);
				rawStream.CopyTo(compress);
			}
			else
			{
				byte[] headerBytes = HeaderToBytes(header);
				outStream.Write(headerBytes, 0, headerBytes.Length);
				outStream.Write(fileUncompressed, 0, fileUncompressed.Length);
			}
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
				Debug.Log("UncompressedData");
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