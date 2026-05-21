using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

namespace SLZ.SLZEditorTools
{
    public static class AdvancedMaterialProps
    {
        public const string advVisSessionStateKey = "SlzAdvancedShaderGUI";
        public static Action<bool> stateChangeCallback;

        #if MARROW_INTERNAL
        [MenuItem("CONTEXT/Material/Toggle Advanced Properties")]
        private static void ContextOption(MenuCommand command)
        {    
            ToggleVisibility();
        }
        #endif

        public static void SetVisibility(bool value)
        {
#if MARROW_INTERNAL
            SessionState.SetBool(advVisSessionStateKey, value);
            stateChangeCallback?.Invoke(value);
#endif
        }

        public static bool GetVisibility()
        {
#if MARROW_INTERNAL
            return SessionState.GetBool(advVisSessionStateKey, false);
#else
            return true;
#endif
        }

        public static void ToggleVisibility()
        {
#if MARROW_INTERNAL
            bool currentValue = GetVisibility();
            SetVisibility(!currentValue);
#endif
        }
    }
}
