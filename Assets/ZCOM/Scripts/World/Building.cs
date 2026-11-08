using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Duckov-style cutaway: the roof disappears while the player is inside, or while the building
    /// stands between the camera and the player.
    /// </summary>
    public class Building : MonoBehaviour
    {
        [Tooltip("Interior footprint (local X/Z) used to detect the player inside.")]
        public Vector2 footprint = new Vector2(8f, 6f);
        public Renderer[] roof = new Renderer[0];
        public Light interiorLight;

        bool hidden;
        float checkT;

        void Update()
        {
            var p = PlayerController.I;
            if (!p) return;
            checkT -= Time.deltaTime;
            if (checkT > 0f) return;
            checkT = 0.05f;

            Vector3 local = transform.InverseTransformPoint(p.transform.position);
            bool inside = Mathf.Abs(local.x) < footprint.x * 0.5f + 0.2f && Mathf.Abs(local.z) < footprint.y * 0.5f + 0.2f;
            bool occluding = false;
            var cam = CameraRig.I ? CameraRig.I.Cam : null;
            if (!inside && cam && Physics.Linecast(cam.transform.position, p.Chest, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                occluding = hit.collider.transform.IsChildOf(transform);

            bool hide = inside || occluding;
            if (hide == hidden) return;
            hidden = hide;
            foreach (var r in roof) if (r) r.enabled = !hide;
        }
    }
}
