using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RealFuels.Tanks
{
    // ProceduralParts pill tanks stacked end to end (attached by their nodes, chained) and of one tank type: one tank.
    // Their skins together close its inside; the largest pill in that is solved on one of them, the owner (the largest
    // by its shape's volume), which holds all of it; the others hold nothing. Worked out anew each frame (cached for the
    // frame), so attaching, detaching, a new shape or a new tank type regroups at once.
    public sealed class PillGroup
    {
        // By persistentId (a fixed order: the owner's skin signature reads the others in it).
        public readonly List<ModuleFuelTanks> Tanks = new List<ModuleFuelTanks>();
        public ModuleFuelTanks Owner { get; private set; }
        public bool Shared => Tanks.Count > 1;

        private static readonly Dictionary<Part, PillGroup> _cache = new Dictionary<Part, PillGroup>();
        private static int _frame = -1;
        private static PropertyInfo _currentShape;
        private static readonly Dictionary<Type, PropertyInfo> _volume = new Dictionary<Type, PropertyInfo>();

        // The frame's groups dropped: worked out again on the next call (a tank's type changed).
        public static void Forget() => _frame = -1;

        // A pill tank's group (just it, when it joins none).
        public static PillGroup Of(ModuleFuelTanks m)
        {
            if (Time.frameCount != _frame)
            {
                _cache.Clear();
                _frame = Time.frameCount;
            }
            if (_cache.TryGetValue(m.part, out PillGroup g)) return g;
            g = new PillGroup();
            _cache[m.part] = g;
            var todo = new Stack<ModuleFuelTanks>();
            todo.Push(m);
            bool procedural = Procedural(m.part) != null;
            while (todo.Count > 0)
            {
                ModuleFuelTanks t = todo.Pop();
                g.Tanks.Add(t);
                if (!procedural) break;     // (a wing, say: alone)
                foreach (Part n in Stacked(t.part))
                {
                    if (_cache.ContainsKey(n) || !(Joins(n, m.type) is ModuleFuelTanks nt)) continue;
                    _cache[n] = g;
                    todo.Push(nt);
                }
            }
            g.Tanks.Sort((a, b) => a.part.persistentId.CompareTo(b.part.persistentId));
            float most = float.NegativeInfinity;
            foreach (ModuleFuelTanks t in g.Tanks)
            {
                float v = ShapeVolume(t.part);
                if (v > most) { most = v; g.Owner = t; }
            }
            return g;
        }

        // The parts attached to p by a node: its parent (when p hangs on one of the parent's nodes) and its children
        // so attached. Not surface attachment (a radial booster is a tank of its own).
        private static IEnumerable<Part> Stacked(Part p)
        {
            if (p.parent != null && p.attachMode == AttachModes.STACK) yield return p.parent;
            foreach (Part c in p.children)
                if (c.attachMode == AttachModes.STACK) yield return c;
        }

        // n's tank, if it joins a group of tanks of `type`: a ProceduralPart pill tank of that type.
        private static ModuleFuelTanks Joins(Part n, string type) =>
            Procedural(n) != null && n.FindModuleImplementing<ModuleFuelTanks>() is ModuleFuelTanks t && t.type == type && t.PillSizesTank ? t : null;

        private static PartModule Procedural(Part p) => p.Modules.Contains("ProceduralPart") ? p.Modules["ProceduralPart"] : null;

        // The ProceduralPart's shape's volume (m^3), 0 if it can't be read.
        private static float ShapeVolume(Part p)
        {
            PartModule pp = Procedural(p);
            if (pp == null) return 0;
            _currentShape ??= pp.GetType().GetProperty("CurrentShape", BindingFlags.Instance | BindingFlags.Public);
            if (!(_currentShape?.GetValue(pp) is PartModule shape)) return 0;
            Type st = shape.GetType();
            if (!_volume.TryGetValue(st, out PropertyInfo vol)) _volume[st] = vol = st.GetProperty("Volume", BindingFlags.Instance | BindingFlags.Public);
            return vol?.GetValue(shape) is float f ? f : 0;
        }

        // The ProceduralPart's own centre of mass for its shape (what ProceduralParts sets), zero if it can't be read.
        internal static Vector3 ShapeCoM(Part p)
        {
            PartModule pp = Procedural(p);
            if (pp == null) return Vector3.zero;
            _currentShape ??= pp.GetType().GetProperty("CurrentShape", BindingFlags.Instance | BindingFlags.Public);
            return _currentShape?.GetValue(pp) is PartModule shape && shape.GetType().GetProperty("CoMOffset")?.GetValue(shape) is Vector3 v ? v : Vector3.zero;
        }
    }
}
