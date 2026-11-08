using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// A group of aliens living in one area of the map. They idle/wander around the camp until one notices the
    /// player, then the whole camp is alerted.
    /// </summary>
    public class AlienCamp : MonoBehaviour
    {
        [System.Serializable]
        public struct Entry
        {
            public AlienType type;
            public Vector3 offset;   // from the camp centre
        }

        public string campName = "Camp";
        public float guardRadius = 7f;
        [Tooltip("When one member spots you, members within this distance of it join in.")]
        public float alarmRadius = 18f;
        public Entry[] aliens = new Entry[0];

        readonly List<Alien> members = new List<Alien>();
        public IReadOnlyList<Alien> Members => members;
        public int Alive
        {
            get
            {
                int n = 0;
                foreach (var m in members) if (m && m.IsAlive) n++;
                return n;
            }
        }

        public bool Spawned { get; private set; }

        void Start()
        {
            foreach (var e in aliens)
            {
                Vector3 pos = transform.position + e.offset;
                var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                var a = AlienFactory.Create(e.type, pos, rot, dormant: true);
                a.SetHome(this, pos, guardRadius);
                members.Add(a);
            }
            Spawned = true;
        }

        public void Alarm(Alien source)
        {
            foreach (var m in members)
                if (m && m != source && m.IsAlive && (m.transform.position - source.transform.position).sqrMagnitude < alarmRadius * alarmRadius)
                    m.Alert(true);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, guardRadius);
        }
    }
}
