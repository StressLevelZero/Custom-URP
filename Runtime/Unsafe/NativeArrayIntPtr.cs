using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
	public static unsafe class NativeArrayIntPtr
	{
		public static unsafe IntPtr GetIntPtr<T>(NativeArray<T> array)
			where T : unmanaged
		{
			void* dataPointer = array.GetUnsafePtr();
			return (IntPtr)dataPointer;
		}
	}
}
