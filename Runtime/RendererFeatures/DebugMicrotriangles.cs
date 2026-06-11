using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SLZ.SLZEditorTools
{
    public class DebugMircotriangles : ScriptableRendererFeature
    {
        public static GlobalKeyword _FIXED_SCREEN_DISTANCE;
        public static GlobalKeyword _MICRO_TRI_DISPLAY_AS_WIREFRAME;

        public const string shaderName = "SLZ/Debug/Show Micro Triangles";

        static readonly int ID_MicroTriVisParams = Shader.PropertyToID("MicroTriVisParams");
        static readonly int ID_Conservative = Shader.PropertyToID("_Conservative");
        static readonly int ID_Gradient = Shader.PropertyToID("_Gradient");
        static ComputeBuffer m_MicroTriVisParamsBuffer;
        static NativeArray<MicroTriVisParams> m_MicroTriVisParams;

        static Material s_MicroTriMat;
        public static Material MicroTriMat
        {
            get
            {
                if (s_MicroTriMat == null)
                {
                    Shader s = Shader.Find(shaderName);
                    s_MicroTriMat = new Material(s);
                }
                return s_MicroTriMat;
            }
        }


        [Reload("Shaders/Debug/DebugMicroTriangles.shader")]
        public Shader MicrotriangleShader;
        [Reload("Textures/HeatmapGradient0.png")]
        public Texture2D m_Gradient;
        
#if UNITY_EDITOR
        const string EDITOR_defaultHeatmapGUID = "aac25ef0107c1c34aa67a2235978803c";
#endif

        public static Texture2D s_Gradient;

        public static Texture2D Gradient { 
            get { return s_Gradient; } 
            set {s_Gradient = value; if (s_Gradient) MicroTriMat.SetTexture(ID_Gradient, s_Gradient); }
        }

        DrawDebugPass m_ScriptablePass;

        public static bool active = false;

        LocalKeyword m_FallbackGradientKw;
        LocalKeyword FallbackGradientKw
        {
            get
            {
                if (!m_FallbackGradientKw.isValid)
                {
                    m_FallbackGradientKw = new LocalKeyword(MicroTriMat.shader, "_MICROTRI_USE_BUILTIN_GRADIENT");
                }
                return m_FallbackGradientKw;
            }
        }

        const int MicroTriVisParamsSize = 32;
        [StructLayout(LayoutKind.Explicit, Size = MicroTriVisParamsSize)]
        public struct MicroTriVisParams
        {
            [FieldOffset(0)]
            public Vector4 _MTParams1;
            [FieldOffset(16)]
            public Vector4 _MTParams2;

            [FieldOffset(0)]
            public float _MeshDistance;
            [FieldOffset(4)]
            public float _Resolution;
            [FieldOffset(8)]
            public float _FovDegrees;
            [FieldOffset(12)]
            public float _MaxThreshold;
            [FieldOffset(16)]
            public float _MinThreshold;
        }

        /// <inheritdoc/>
        public override void Create()
        {
            _FIXED_SCREEN_DISTANCE = GlobalKeyword.Create("_FIXED_SCREEN_DISTANCE");
            _MICRO_TRI_DISPLAY_AS_WIREFRAME = GlobalKeyword.Create("_MICRO_TRI_DISPLAY_AS_WIREFRAME");


            m_ScriptablePass = new DrawDebugPass("Debug Microtriangles", false, RenderPassEvent.AfterRenderingTransparents);
            m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

            #if UNITY_EDITOR
            if (m_Gradient == null)
            {
                string gradientPath = AssetDatabase.GUIDToAssetPath(EDITOR_defaultHeatmapGUID);
                if (string.IsNullOrEmpty(gradientPath))
                {
                    Debug.LogError($"DebugMicrotriangles: Cannot find default heatmap gradient by guid {EDITOR_defaultHeatmapGUID}");
                }
                else
                {
                    m_Gradient = AssetDatabase.LoadAssetAtPath<Texture2D>(gradientPath);
                    if (m_Gradient == null)
                    {
                        Debug.LogError($"DebugMicrotriangles: default heatmap gradient guid does not belong to a texture! GUID: {EDITOR_defaultHeatmapGUID}");
                    }
                    else
                    {
                        EditorUtility.SetDirty(this);
                        AssetDatabase.SaveAssetIfDirty(this);
                    }
                }
            }

            if (s_Gradient == null && m_Gradient != null)
            {
                s_Gradient = m_Gradient;
            }
            #endif
        }

        

        protected override void Dispose(bool disposing)
        {
            if (m_MicroTriVisParamsBuffer != null) m_MicroTriVisParamsBuffer.Dispose();
            if (m_MicroTriVisParams.IsCreated) m_MicroTriVisParams.Dispose();
        }

        static void EnsureBuffersExist()
        {
            bool needsRefresh = false;
            if (s_MicroTriMat == null)
            {
                Shader s = Shader.Find("SLZ/Debug/Show Micro Triangles");
                s_MicroTriMat = new Material(s);
                needsRefresh = true;
            }
            if (m_MicroTriVisParamsBuffer == null || !m_MicroTriVisParamsBuffer.IsValid())
            {
                m_MicroTriVisParamsBuffer = new ComputeBuffer(1, Marshal.SizeOf<MicroTriVisParams>(), ComputeBufferType.Constant);
                needsRefresh = true;
            }
            if (!m_MicroTriVisParams.IsCreated)
            {
                m_MicroTriVisParams = new NativeArray<MicroTriVisParams>(1, Allocator.Persistent);
                m_MicroTriVisParams[0] = GetDefaultParameters();
                needsRefresh = true;
            }
            if (needsRefresh)
            {
                m_MicroTriVisParamsBuffer.SetData(m_MicroTriVisParams);
                Shader.SetGlobalConstantBuffer(ID_MicroTriVisParams, m_MicroTriVisParamsBuffer, 0, MicroTriVisParamsSize);
            }
        }

        public static MicroTriVisParams GetParameters()
        {
            EnsureBuffersExist();
            return m_MicroTriVisParams[0];
        }

        public static void SetAllParameters(MicroTriVisParams p)
        {
            EnsureBuffersExist();
            m_MicroTriVisParams[0] = p;
            m_MicroTriVisParamsBuffer.SetData(m_MicroTriVisParams);
            Shader.SetGlobalConstantBuffer(ID_MicroTriVisParams, m_MicroTriVisParamsBuffer, 0, MicroTriVisParamsSize);
        }


        public static void SetAllParametersNoCheck(MicroTriVisParams p)
        {
            m_MicroTriVisParams[0] = p;
            m_MicroTriVisParamsBuffer.SetData(m_MicroTriVisParams);
            Shader.SetGlobalConstantBuffer(ID_MicroTriVisParams, m_MicroTriVisParamsBuffer, 0, MicroTriVisParamsSize);
        }

        public static void SetMeshDistance(float dist)
        {
            EnsureBuffersExist();
            MicroTriVisParams p = m_MicroTriVisParams[0];
            p._MeshDistance = dist;
            SetAllParametersNoCheck(p);
        }

        public static void SetResolution(float res)
        {
            EnsureBuffersExist();
            MicroTriVisParams p = m_MicroTriVisParams[0];
            p._Resolution = res;
            SetAllParametersNoCheck(p);
        }

        public static void SetFovDegrees(float fov)
        {
            EnsureBuffersExist();
            MicroTriVisParams p = m_MicroTriVisParams[0];
            p._FovDegrees = fov;
            SetAllParametersNoCheck(p);
        }

        public static void SetMaxThreshold(float maxT)
        {
            EnsureBuffersExist();
            MicroTriVisParams p = m_MicroTriVisParams[0];
            p._MaxThreshold = maxT;
            SetAllParametersNoCheck(p);
        }

        public static void SetMinThreshold(float minT)
        {
            EnsureBuffersExist();
            MicroTriVisParams p = m_MicroTriVisParams[0];
            p._MinThreshold = minT;
            SetAllParametersNoCheck(p);
        }

        public static void SetConservativeRaster(bool val)
        {
            MicroTriMat.SetFloat(ID_Conservative, val ? 1.0f : 0.0f);
        }

        public static MicroTriVisParams GetDefaultParameters()
        {
            return new MicroTriVisParams()
            {
                _MeshDistance = 10,
                _Resolution = 1760,
                _FovDegrees = 96,
                _MaxThreshold = 16,
                _MinThreshold = 4,
            };
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (active && MicrotriangleShader)
            {
                EnsureBuffersExist();
                MicroTriMat.SetTexture(ID_Gradient, s_Gradient);
                MicroTriMat.SetKeyword(FallbackGradientKw, s_Gradient == null);
                renderer.EnqueuePass(m_ScriptablePass);
            }
        }
    }
}

