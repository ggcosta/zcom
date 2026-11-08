using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>A named area. Shows a discovery banner the first time you enter; the HUD shows where you are.</summary>
    public class MapZone : MonoBehaviour
    {
        public static readonly List<MapZone> All = new List<MapZone>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); }

        public string zoneName = "ZONE";
        [TextArea] public string subtitle = "";
        public float radius = 10f;
        public Color color = new Color(0.35f, 0.9f, 1f);
        public bool Discovered { get; private set; }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>The zone the player is standing in (closest centre wins), or null.</summary>
        public static MapZone At(Vector3 p)
        {
            MapZone best = null;
            float bd = float.MaxValue;
            foreach (var z in All)
            {
                Vector3 d = p - z.transform.position; d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < z.radius * z.radius && sq < bd) { bd = sq; best = z; }
            }
            return best;
        }

        void Update()
        {
            if (Discovered) return;
            var p = PlayerController.I;
            if (!p || !p.CanAct || At(p.transform.position) != this) return;
            Discovered = true;
            if (Hud.I && Time.timeSinceLevelLoad > 1f) Hud.I.Banner(zoneName, subtitle);
            if (Time.timeSinceLevelLoad > 1f) ProcAudio.Play("clear", 0.35f, 0f, 1.1f);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
