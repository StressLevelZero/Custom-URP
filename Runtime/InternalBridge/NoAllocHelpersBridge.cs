using System.Runtime.CompilerServices;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
   public class NoAllocHelpersBridge
   {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         public static T[] ExtractArrayFromListT<T>(List<T> list)
         {
            return NoAllocHelpers.ExtractArrayFromListT(list);
         }
   }
}
