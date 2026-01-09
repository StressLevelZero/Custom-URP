/* 
Utility to allow setting the default size and position of new editor windows, 
without clamping the window's size or overriding the saved position.

* This is free and unencumbered software released into the public domain.
* 
* Anyone is free to copy, modify, publish, use, compile, sell, or
* distribute this software, either in source code form or as a compiled
* binary, for any purpose, commercial or non-commercial, and by any
* means.
* 
* In jurisdictions that recognize copyright laws, the author or authors
* of this software dedicate any and all copyright interest in the
* software to the public domain. We make this dedication for the benefit
* of the public at large and to the detriment of our heirs and
* successors. We intend this dedication to be an overt act of
* relinquishment in perpetuity of all present and future rights to this
* software under copyright law.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
* EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
* MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
* IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR
* OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
* ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
* OTHER DEALINGS IN THE SOFTWARE.
* 
* For more information, please refer to <http://unlicense.org/>
*/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class EditorWindowUtils
{
    /// <summary>
    /// Set the default size of new Editor windows of the given type in pixel coordinates. 
    /// This will not change the window's size if there is a size saved for the type, 
    /// unless <paramref name="overrideSavedLayout"/> is set.
    /// </summary>
    /// <typeparam name="T">Type extending EditorWindow to set the default size of</typeparam>
    /// <param name="width">default width, in pixels</param>
    /// <param name="height">default height, in pixels</param>
    /// <param name="overrideSavedLayout">Always set the size to the given value, ignoring any saved value</param>
    public static void SetDefaultSize<T>(float width, float height, bool overrideSavedLayout = false) where T : EditorWindow
    {
        string typename = typeof(T).ToString();
        string widthKey = typename + "w";
        string heightKey = typename + "h";
        if (overrideSavedLayout || !EditorPrefs.HasKey(widthKey))
        {
            EditorPrefs.SetFloat(widthKey, width);
            EditorPrefs.SetFloat(heightKey, height);
        }
    }

    /// <summary>
    /// Set the default position of new Editor windows of the given type in pixel coordinates. 
    /// This will not change the window's position if there is already a saved position in the 
    /// layout, unless <paramref name="overrideSavedLayout"/> is set.
    /// </summary>
    /// <typeparam name="T">Type extending EditorWindow to set the default position of</typeparam>
    /// <param name="x">default x coordinate, in pixels</param>
    /// <param name="y">default y coordinate, in pixels</param>
    /// <param name="maximized">default maximized state</param>
    /// <param name="overrideSavedLayout">Always set the position to the given value, ignoring any saved value</param>
    public static void SetDefaultPosition<T>(float x, float y, bool maximized = false, bool overrideSavedLayout = false) where T : EditorWindow
    {
        string typename = typeof(T).ToString();
        string posXKey = typename + "x";
        string posYKey = typename + "y";
        string maximizedKey = typename + "z";
        if (overrideSavedLayout || !EditorPrefs.HasKey(posXKey))
        {
            EditorPrefs.SetFloat(posXKey, x);
            EditorPrefs.SetFloat(posYKey, y);
            EditorPrefs.SetBool(maximizedKey, maximized);
        }
    }

    /// <summary>
    /// Delete the editor window's saved position and/or size 
    /// </summary>
    /// <typeparam name="T">Type extending EditorWindow to delete the layout for</typeparam>
    /// <param name="size">If true, delete the saved width and height of the window</param>
    /// <param name="position">If true, delete the saved screen x/y coordinates of the window</param>
    public static void DeleteSavedLayout<T>(bool size = true, bool position = true) where T : EditorWindow
    {
        string typename = typeof(T).ToString();

        if (position)
        {
            string posXKey = typename + "x";
            string posYKey = typename + "y";
            string maximizedKey = typename + "z";
            EditorPrefs.DeleteKey(posXKey);
            EditorPrefs.DeleteKey(posYKey);
            EditorPrefs.DeleteKey(maximizedKey);
        }

        if (size)
        {
            string widthKey = typename + "w";
            string heightKey = typename + "h";

            EditorPrefs.DeleteKey(widthKey);
            EditorPrefs.DeleteKey(heightKey);
        }
    }
}
