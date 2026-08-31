// Services — the composition root's registry. One flat, typed locator:
// ServiceHost registers concrete instances at world build; consumers resolve
// lazily (TryGet) so edit-time-built worlds that haven't run Awake yet fail
// soft instead of throwing. No DI framework — the Roblox build's "one
// require() away" ergonomics, typed.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    public static class Services
    {
        static readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();

        public static T Register<T>(T instance) where T : class
        {
            _map[typeof(T)] = instance;
            return instance;
        }

        public static T Get<T>() where T : class =>
            _map.TryGetValue(typeof(T), out var o)
                ? (T)o
                : throw new InvalidOperationException($"[Services] {typeof(T).Name} not registered");

        public static bool TryGet<T>(out T service) where T : class
        {
            if (_map.TryGetValue(typeof(T), out var o)) { service = (T)o; return true; }
            service = null;
            return false;
        }

        public static void Clear() => _map.Clear();

        // Enter-Play-Mode-Options safe: statics survive disabled domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _map.Clear();
    }
}
