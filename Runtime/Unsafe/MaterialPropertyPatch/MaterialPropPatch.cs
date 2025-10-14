using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public unsafe static class MaterialPropPatch 
{
    delegate Material GetMaterialDelegate(Renderer r);
    delegate Material[] GetMaterialArrayDelegate(Renderer r);
    public static Material materialDetour(Renderer r)
    {
        throw new InvalidOperationException("Tried to call Renderer.material. You probably meant Renderer.sharedMaterial. If you actually want to generate a material instance, use the extension method 'Renderer.CreateMaterialInstanceExt' or the built-in renderer.GetMaterials method");
    }

    public static Material[] materialsDetour(Renderer r)
    {
        throw new InvalidOperationException("Tried to call Renderer.materials. You probably meant Renderer.sharedMaterials. If you actually want to generate material instances, use the extension method 'Renderer.CreateMaterialsInstanceExt' or the built-in renderer.GetMaterials method");
    }

    #region apdk_detour
    // Copied from apkd's static batching sorting fix: https://github.com/apkd/UnityStaticBatchingSortingPatch
    /*
        MIT License

        Copyright (c) 2021 Mateusz Major
        
        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:
        
        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.
        
        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE. 
     */


    // this is based on an interesting technique from the RimWorld ComunityCoreLibrary project, originally credited to RawCode:
    // https://github.com/RimWorldCCLTeam/CommunityCoreLibrary/blob/master/DLL_Project/Classes/Static/Detours.cs
    private static unsafe void TryDetourFromTo(MethodInfo src, MethodInfo dst)
    {
        try
        {
            if (IntPtr.Size == sizeof(Int64))
            {
                // 64-bit systems use 64-bit absolute address and jumps
                // 12 byte destructive

                // Get function pointers
                long Source_Base = src.MethodHandle.GetFunctionPointer().ToInt64();
                long Destination_Base = dst.MethodHandle.GetFunctionPointer().ToInt64();

                // Native source address
                byte* Pointer_Raw_Source = (byte*)Source_Base;

                // Pointer to insert jump address into native code
                long* Pointer_Raw_Address = (long*)(Pointer_Raw_Source + 0x02);

                // Insert 64-bit absolute jump into native code (address in rax)
                // mov rax, immediate64
                // jmp [rax]
                *(Pointer_Raw_Source + 0x00) = 0x48;
                *(Pointer_Raw_Source + 0x01) = 0xB8;
                *Pointer_Raw_Address = Destination_Base; // ( Pointer_Raw_Source + 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 )
                *(Pointer_Raw_Source + 0x0A) = 0xFF;
                *(Pointer_Raw_Source + 0x0B) = 0xE0;
            }
            else
            {
                // 32-bit systems use 32-bit relative offset and jump
                // 5 byte destructive

                // Get function pointers
                int Source_Base = src.MethodHandle.GetFunctionPointer().ToInt32();
                int Destination_Base = dst.MethodHandle.GetFunctionPointer().ToInt32();

                // Native source address
                byte* Pointer_Raw_Source = (byte*)Source_Base;

                // Pointer to insert jump address into native code
                int* Pointer_Raw_Address = (int*)(Pointer_Raw_Source + 1);

                // Jump offset (less instruction size)
                int offset = (Destination_Base - Source_Base) - 5;

                // Insert 32-bit relative jump into native code
                *Pointer_Raw_Source = 0xE9;
                *Pointer_Raw_Address = offset;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unable to detour: {src?.Name ?? "UnknownSrc"} -> {dst?.Name ?? "UnknownDst"}\n{ex}");
            throw;
        }
    }
    #endregion // detour

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    static void DetourObsoleteMethods()
    {
        {
            PropertyInfo materialPropInfo = typeof(Renderer).GetProperty("material", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo materialGetterInfo = materialPropInfo.GetGetMethod();

            MethodInfo newMaterialGetterInfo = ((GetMaterialDelegate)materialDetour).Method;
            TryDetourFromTo(materialGetterInfo, newMaterialGetterInfo);
        }


        {
            PropertyInfo materialsPropInfo = typeof(Renderer).GetProperty("materials", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo materialsGetterInfo = materialsPropInfo.GetGetMethod();

            MethodInfo newMaterialsGetterInfo = ((GetMaterialArrayDelegate)materialsDetour).Method;
            TryDetourFromTo(materialsGetterInfo, newMaterialsGetterInfo);
        }
    }
#endif
}
