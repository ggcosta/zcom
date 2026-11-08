using UnityEngine;

namespace Zcom
{
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;
        public Vector3 direction;
        public float knockback;
        public bool crit;
        public bool explosive;
        /// <summary>Continuous damage (beams): no damage number / hit sound per tick.</summary>
        public bool quiet;
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(DamageInfo info);
    }
}
