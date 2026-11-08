using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// XCOM-style tilted isometric camera. Follows the player with a look-ahead towards the cursor
    /// (Duckov-style), plus trauma-based shake and directional kicks.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }

        public Transform target;
        [Header("Framing")]
        public float pitch = 52f;
        public float yaw = 45f;
        public float distance = 25f;
        public float fieldOfView = 30f;
        [Header("Follow")]
        public float followSmoothTime = 0.12f;
        public float lookAhead = 0.3f;
        public float maxLookAhead = 5f;
        [Header("Shake")]
        public float maxShakeOffset = 0.7f;
        public float maxShakeRoll = 2.5f;
        public float traumaDecay = 1.5f;

        Camera cam;
        float trauma;
        Vector3 kick;
        Vector3 focus, focusVel;
        PlayerController player;

        public Camera Cam => cam;

        void Awake()
        {
            I = this;
            cam = GetComponent<Camera>();
            cam.fieldOfView = fieldOfView;
            if (target) focus = target.position;
        }

        public void AddTrauma(float amount) { trauma = Mathf.Clamp01(trauma + amount); }
        public void Kick(Vector3 worldDir, float amount) { kick += worldDir.normalized * amount; }

        void LateUpdate()
        {
            if (!target) return;
            if (!player) player = target.GetComponent<PlayerController>();
            float dt = Time.unscaledDeltaTime;

            Vector3 desired = target.position;
            if (player && player.CanAct)
            {
                Vector3 la = player.AimPoint - target.position;
                la.y = 0f;
                desired += Vector3.ClampMagnitude(la * lookAhead, maxLookAhead);
            }
            focus = Vector3.SmoothDamp(focus, desired, ref focusVel, followSmoothTime, Mathf.Infinity, dt);

            kick = Vector3.Lerp(kick, Vector3.zero, 1f - Mathf.Exp(-16f * dt));
            trauma = Mathf.Max(0f, trauma - traumaDecay * dt);
            float s = trauma * trauma;
            float t = Time.unscaledTime * 24f;
            Vector3 shake = new Vector3(
                Mathf.PerlinNoise(t, 0.1f) - 0.5f,
                Mathf.PerlinNoise(0.2f, t) - 0.5f,
                0f) * (2f * maxShakeOffset * s);
            float roll = (Mathf.PerlinNoise(t * 0.8f, 7.7f) - 0.5f) * 2f * maxShakeRoll * s;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = focus + kick - rot * Vector3.forward * distance;
            transform.SetPositionAndRotation(pos + rot * shake, rot * Quaternion.Euler(0f, 0f, roll));
        }

        public void SnapToTarget()
        {
            if (!target) return;
            focus = target.position;
            LateUpdate();
        }
    }
}
