// Derived from https://github.com/keijiro/Akvfx/blob/master/Packages/jp.keijiro.akvfx/Runtime/Internal/Extensions.cs

using UnityEngine;
using System;
using System.Reflection;

namespace UnityEngine.Rendering.Universal
{
	public static class ComputeBufferExtensions
	{
		// SetData with ReadOnlySpan
		public unsafe static void SetData<T>
		  (this ComputeBuffer buffer, ReadOnlySpan<T> data) where T : unmanaged
		{
			fixed (T* pData = &data.GetPinnableReference())
				buffer.SetData((IntPtr)pData, data.Length, sizeof(T));
		}

		// Directly load an unmanaged data array to a compute buffer via an
		// Intptr. This is not a public interface so will be broken one day.
		// DO NOT TRY AT HOME.
		public static void SetData
		  (this ComputeBuffer buffer, IntPtr pointer, int count, int stride)
		{
			_args5[0] = pointer;
			_args5[1] = 0;      // source offset
			_args5[2] = 0;      // buffer offset
			_args5[3] = count;
			_args5[4] = stride;

			SetNativeData.Invoke(buffer, _args5);
		}

		static MethodInfo _setNativeData;

		static MethodInfo SetNativeData
		  => _setNativeData ?? (_setNativeData = GetSetNativeDataMethod());

		static MethodInfo GetSetNativeDataMethod()
		  => typeof(ComputeBuffer).GetMethod("InternalSetNativeData",
											 BindingFlags.InvokeMethod |
											 BindingFlags.NonPublic |
											 BindingFlags.Instance);

		static object[] _args5 = new object[5];
	}

}
