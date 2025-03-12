using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SLZ.SLZEditorTools
{
    //[CreateAssetMenu(fileName = "TestSettings.asset",menuName = "TEST/ProjectShaderSymbols", order = 0)]
    public class ProjectShaderSymbols : ScriptableObject
    {
        public List<string> globalSymbols = new List<string>();
        public List<string> mobileOnlySymbols = new List<string>();
        public List<string> pcOnlySymbols = new List<string>() { "SLZ_LM_BICUBIC" };

        public string GenerateShaderInclude()
        {
            if (globalSymbols == null) globalSymbols = new List<string>();
            if (mobileOnlySymbols == null) mobileOnlySymbols = new List<string>();
            if (pcOnlySymbols == null) pcOnlySymbols = new List<string>();

            globalSymbols.Sort();
            mobileOnlySymbols.Sort(); 
            pcOnlySymbols.Sort();

            StringBuilder sb = new StringBuilder(
                "#ifndef SLZ_PROJECT_SHADER_DEFINES\n" +
                "#define SLZ_PROJECT_SHADER_DEFINES\n"
                );
            
            foreach (string define in globalSymbols)
            {
                if (!string.IsNullOrEmpty(define))
                {
                    sb.Append("#define " + define + "\n");
                }
            }

            sb.Append("#if defined(SHADER_API_MOBILE)" + "\n");
            foreach (string define in mobileOnlySymbols)
            {
                if (!string.IsNullOrEmpty(define))
                {
                    sb.Append("    #define " + define + "\n");
                }
            }
            sb.Append("#else\n");
            foreach (string define in pcOnlySymbols)
            {
                if (!string.IsNullOrEmpty(define))
                {
                    sb.Append("    #define " + define + "\n");
                }
            }
            sb.Append("#endif // SHADER_API_MOBILE\n");

            sb.Append("#endif // SLZ_PROJECT_SHADER_DEFINES\n");

            return sb.ToString();
        }
    }
}
