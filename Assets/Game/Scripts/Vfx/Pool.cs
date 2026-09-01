// Pool — the generic pooler under every effect (and later: shells, tracers,
// decals, drops). Leases inactive instances, builds via the supplied factory
// when dry, releases by deactivating. No timers here — owners manage
// lifetimes; PooledLife is offered for fire-and-forget cases.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Vfx
{
    public class Pool
    {
        readonly Transform _parent;
        readonly Func<GameObject> _build;
        readonly Stack<GameObject> _free = new Stack<GameObject>();
        public int LiveCount { get; private set; }

        public Pool(Transform parent, Func<GameObject> build)
        {
            _parent = parent;
            _build = build;
        }

        public GameObject Lease()
        {
            GameObject go = null;
            while (_free.Count > 0 && go == null) go = _free.Pop();   // skip destroyed
            if (go == null)
            {
                go = _build();
                go.transform.SetParent(_parent, true);
            }
            go.SetActive(true);
            LiveCount++;
            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            go.transform.SetParent(_parent, true);
            _free.Push(go);
            LiveCount--;
        }
    }
}
