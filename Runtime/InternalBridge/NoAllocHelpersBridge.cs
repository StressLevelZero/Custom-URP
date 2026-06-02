using System.Runtime.CompilerServices;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoAllocHelpersBridge
{
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static T[] ExtractArrayFromListT<T>(List<T> list)
      {
         return NoAllocHelpers.ExtractArrayFromListT(list);
      }
}
