using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Off-screen studio far below the map where the real in-game models are rendered into the UI:
    /// the HUD helmet portrait, the HUD weapon render and the inventory's full-body operative.
    /// A copy of the Marshal (same FBX, same material, same Animator controller) idles here holding a copy of
    /// the equipped weapon; each view is a camera on that stage rendering into its own RenderTexture.
    /// </summary>
    public class PreviewStage : MonoBehaviour
    {
        public static PreviewStage I { get; private set; }

        static readonly Vector3 Origin = new Vector3(0f, -400f, 0f);
        static readonly Vector3 WeaponSpot = new Vector3(6f, -400f, 0f);

        public RenderTexture Portrait { get; private set; }
        public RenderTexture WeaponTex { get; private set; }
        public RenderTexture Body { get; private set; }

        Transform marine, marineWeaponSocket, weaponRoot, turntable;
        Camera portraitCam, weaponCam, bodyCam;
        WeaponKind? shownBodyWeapon, shownWeapon;
        float bodyYaw = 200f, weaponSpin;
        public bool BodyViewActive { get; set; }
        /// <summary>The spinning 3D weapon render; off while the HUD shows the painted weapon art instead.</summary>
        public bool WeaponViewActive { get; set; } = true;

        public static PreviewStage Ensure()
        {
            if (I) return I;
            var go = new GameObject("PreviewStage");
            I = go.AddComponent<PreviewStage>();
            I.Build();
            return I;
        }

        static RenderTexture RT(int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "Preview", antiAliasing = 4 };
            rt.Create();
            return rt;
        }

        Camera MakeCam(string name, RenderTexture rt, float fov)
        {
            var c = new GameObject(name).AddComponent<Camera>();
            c.transform.SetParent(transform, false);
            c.targetTexture = rt;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0f, 0f, 0f, 0f);
            c.fieldOfView = fov;
            c.nearClipPlane = 0.05f;
            c.farClipPlane = 30f;
            c.allowHDR = false;
            c.allowMSAA = true;
            c.depth = -50;
            return c;
        }

        void Light(string name, Vector3 pos, Color color, float intensity, float range)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.position = pos;
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        void Build()
        {
            Portrait = RT(768, 768);
            WeaponTex = RT(768, 320);
            Body = RT(768, 1152);

            turntable = new GameObject("Turntable").transform;
            turntable.SetParent(transform, false);
            turntable.position = Origin;
            var model = PlayerFactory.SpawnMarshalModel(turntable, out marineWeaponSocket);
            marine = model ? model.transform : null;
            if (marine)
                foreach (var r in marine.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // studio lighting: warm key, cool rim, cyan kicker (matches the HUD accent)
            Light("Key", Origin + new Vector3(1.6f, 2.4f, 2.2f), new Color(1f, 0.95f, 0.88f), 7f, 8f);
            Light("Rim", Origin + new Vector3(-1.8f, 2.2f, -1.8f), new Color(0.55f, 0.75f, 1f), 9f, 8f);
            Light("Kick", Origin + new Vector3(-1.4f, 1.0f, 1.4f), new Color(0.3f, 0.9f, 1f), 3f, 6f);
            Light("WKey", WeaponSpot + new Vector3(0.6f, 0.8f, 1.2f), new Color(1f, 0.96f, 0.9f), 5f, 5f);
            Light("WRim", WeaponSpot + new Vector3(-0.8f, 0.6f, -1.0f), new Color(0.5f, 0.8f, 1f), 6f, 5f);

            portraitCam = MakeCam("PortraitCam", Portrait, 22f);
            portraitCam.enabled = false;      // no HUD shows the helmet portrait any more; kept for future use
            bodyCam = MakeCam("BodyCam", Body, 28f);
            weaponCam = MakeCam("WeaponCam", WeaponTex, 20f);
            weaponCam.orthographic = true;

            weaponRoot = new GameObject("WeaponRoot").transform;
            weaponRoot.SetParent(transform, false);
            weaponRoot.position = WeaponSpot;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var rt in new[] { Portrait, WeaponTex, Body }) if (rt) rt.Release();
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }

        static Bounds RendererBounds(Transform t)
        {
            var b = new Bounds(t.position, Vector3.zero);
            bool first = true;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        public void RotateBody(float deltaDeg) => bodyYaw += deltaDeg;

        void LateUpdate()
        {
            var player = PlayerController.I;
            WeaponKind? kind = player && player.gun && player.gun.Current != null ? player.gun.Current.kind : (WeaponKind?)null;

            // the stage marine holds the same weapon as the player
            if (marineWeaponSocket && kind != shownBodyWeapon)
            {
                shownBodyWeapon = kind;
                Clear(marineWeaponSocket);
                if (kind.HasValue) WeaponModels.Build(kind.Value, marineWeaponSocket);
                foreach (var r in marineWeaponSocket.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // weapon profile render
            if (kind != shownWeapon)
            {
                shownWeapon = kind;
                Clear(weaponRoot);
                if (kind.HasValue)
                {
                    var holder = new GameObject("Holder").transform;
                    holder.SetParent(weaponRoot, false);
                    WeaponModels.Build(kind.Value, holder);
                    holder.localRotation = Quaternion.Euler(0f, -90f, 0f);    // muzzle points to screen-right
                    var b = RendererBounds(holder);
                    holder.position += WeaponSpot - b.center;
                    weaponCam.orthographicSize = Mathf.Max(b.extents.y * 1.25f, b.extents.x / (WeaponTex.width / (float)WeaponTex.height) * 1.12f);
                }
            }
            weaponSpin += Time.unscaledDeltaTime;
            weaponRoot.rotation = Quaternion.Euler(Mathf.Sin(weaponSpin * 0.7f) * 4f, 12f + Mathf.Sin(weaponSpin * 0.45f) * 14f, 0f);
            weaponCam.transform.position = WeaponSpot + new Vector3(0f, 0.05f, 3f);
            weaponCam.transform.rotation = Quaternion.LookRotation(Vector3.back);
            weaponCam.enabled = WeaponViewActive;

            if (!marine) return;
            if (!BodyViewActive) bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, 200f, Time.unscaledDeltaTime * 30f);
            turntable.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
            bodyCam.enabled = BodyViewActive;

            var head = CharacterModels.Find(marine, "Head");
            Vector3 h = head ? head.position + Vector3.up * 0.08f : Origin + Vector3.up * 1.72f;
            // portrait: helmet 3/4 from the front-right, independent of the turntable yaw
            Vector3 face = Quaternion.Euler(0f, bodyYaw, 0f) * Vector3.forward;
            Vector3 camDir = Quaternion.Euler(0f, -28f, 0f) * face;
            // bust shot (helmet + shoulders): far enough that the helmet reads as a shape, not as a wall of facets
            portraitCam.transform.position = h + camDir * 2.5f + Vector3.up * 0.12f;
            portraitCam.transform.LookAt(h - Vector3.up * 0.2f);
            // full body
            bodyCam.transform.position = Origin + new Vector3(0f, 1.1f, 0f) + Quaternion.Euler(0f, 200f, 0f) * Vector3.forward * 4.5f;
            bodyCam.transform.LookAt(Origin + new Vector3(0f, 0.97f, 0f));
        }
    }
}
