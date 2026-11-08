using UnityEngine;

namespace Zcom
{
    /// <summary>Hit-stop (brief time freeze) and camera shake entry points.</summary>
    public class GameFeel : MonoBehaviour
    {
        static GameFeel inst;
        /// <summary>Pause menu / raid summary freeze the simulation (hit-stop never overrides it).</summary>
        public static bool Paused;
        float stopUntil;
        float stopScale = 1f;

        static GameFeel I
        {
            get
            {
                if (inst == null) inst = new GameObject("GameFeel").AddComponent<GameFeel>();
                return inst;
            }
        }

        void Awake()
        {
            inst = this;
            Paused = false;
            Time.timeScale = 1f;
        }

        void OnDestroy()
        {
            if (inst == this) Time.timeScale = 1f;
        }

        /// <summary>Freeze time for a few frames. Longer/stronger calls win over weaker ones.</summary>
        public static void HitStop(float duration, float scale = 0.05f)
        {
            var g = I;
            float until = Time.unscaledTime + duration;
            if (until > g.stopUntil)
            {
                g.stopUntil = until;
                g.stopScale = scale;
            }
        }

        public static void Shake(float trauma)
        {
            if (CameraRig.I != null) CameraRig.I.AddTrauma(trauma);
        }

        public static void Kick(Vector3 worldDir, float amount)
        {
            if (CameraRig.I != null) CameraRig.I.Kick(worldDir, amount);
        }

        void Update()
        {
            Time.timeScale = Paused ? 0f : Time.unscaledTime < stopUntil ? stopScale : 1f;
        }
    }
}
