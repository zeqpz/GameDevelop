// EventBus — typed pub/sub, the Unity twin of our Remotes + Bindables layer
// and the future NETWORK SEAM. Events are plain-data structs (enforced by
// the constraint) so that when Fusion lands, a bridge can subscribe to
// chosen event types and mirror them across the wire without refactoring
// publishers — same shape as Roblox remotes: fire a named event locally,
// transport is someone else's job. Keep payloads simple; anything with scene
// references will carry ids instead once events go networked.
//
// Handlers are isolated: one subscriber throwing never starves the rest.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    public static class EventBus
    {
        static readonly Dictionary<Type, Delegate> _subs = new Dictionary<Type, Delegate>();

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            _subs.TryGetValue(typeof(T), out var d);
            _subs[typeof(T)] = Delegate.Combine(d, handler);
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (!_subs.TryGetValue(typeof(T), out var d)) return;
            var next = Delegate.Remove(d, handler);
            if (next == null) _subs.Remove(typeof(T));
            else _subs[typeof(T)] = next;
        }

        public static void Publish<T>(T evt) where T : struct
        {
            if (!_subs.TryGetValue(typeof(T), out var d) || d == null) return;
            foreach (Action<T> h in d.GetInvocationList())
            {
                try { h(evt); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public static void Clear() => _subs.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _subs.Clear();
    }
}
