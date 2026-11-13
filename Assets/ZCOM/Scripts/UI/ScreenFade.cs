using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>Full-screen black fade that survives scene loads (deploy to the raid, return to the ship).</summary>
    public class ScreenFade : MonoBehaviour
    {
        static ScreenFade inst;
        Image img;
        float alpha, target, speed = 1f;

        static ScreenFade Get()
        {
            if (inst) return inst;
            var go = new GameObject("ScreenFade");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<ScreenFade>();
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var ig = new GameObject("Black", typeof(RectTransform)).GetComponent<RectTransform>();
            ig.SetParent(go.transform, false);
            ig.anchorMin = Vector2.zero; ig.anchorMax = Vector2.one; ig.offsetMin = ig.offsetMax = Vector2.zero;
            inst.img = ig.gameObject.AddComponent<Image>();
            inst.img.color = new Color(0, 0, 0, 0);
            inst.img.raycastTarget = false;
            return inst;
        }

        /// <summary>Fade to black over the given seconds.</summary>
        public static void Out(float seconds) { var f = Get(); f.target = 1f; f.speed = 1f / Mathf.Max(0.01f, seconds); }

        /// <summary>Fade from black (starts black when nothing is showing yet, e.g. the very first scene).</summary>
        public static void In(float seconds)
        {
            bool fresh = !inst;
            var f = Get();
            if (fresh) f.alpha = 1f;
            f.target = 0f;
            f.speed = 1f / Mathf.Max(0.01f, seconds);
        }

        public static bool Black => inst && inst.alpha > 0.99f;

        void Update()
        {
            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime * speed);
            img.color = new Color(0f, 0.005f, 0.012f, alpha);
            img.enabled = alpha > 0.001f;
        }
    }
}
