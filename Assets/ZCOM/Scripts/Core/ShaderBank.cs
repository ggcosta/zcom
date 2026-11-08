using UnityEngine;

namespace Zcom
{
    /// <summary>Holds shader references in the scene so they are included in builds.</summary>
    public class ShaderBank : MonoBehaviour
    {
        public static ShaderBank I { get; private set; }
        public Shader lit;
        public Shader fxAdditive;
        public Shader fxAlpha;
        void Awake() { I = this; }
    }
}
