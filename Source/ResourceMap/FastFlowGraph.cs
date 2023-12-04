using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public abstract class FastFlowGraph
    {
        public virtual void Create(List<ProtoPartSnapshot> parts) { }
        public virtual void Create(List<Part> parts) { }
    }

    public abstract class FastFlowGraph<T> : FastFlowGraph where T : class
    {
        public abstract class Vertex
        {
            protected FastFlowGraph<T> _graph;

            public int index = 0;
            public T part;
            public List<Vertex> edges = new List<Vertex>();

            public Vertex(T t, FastFlowGraph<T> g) { part = t; _graph = g; }
            public abstract void FindEdges();
        }

        protected List<Vertex> _graph = new List<Vertex>();

        // These have to be public for the derived classes.
        // I mean, we could have IReadOnly* accessors, but bleh.
        public List<T> _parts;
        public Dictionary<T, Vertex> _lookup = new Dictionary<T, Vertex>();

        protected CollectionDictionary<int, T, HashSet<T>> _sets = new CollectionDictionary<int, T, HashSet<T>>();
        public ICollection<HashSet<T>> sets => _sets.Values;

        public void CreateGraph()
        {
            foreach (var v in _graph)
            {
                v.FindEdges();
            }

            int curIdx = 1;
            Stack<Vertex> stack = new Stack<Vertex>();
            foreach (var v in _graph)
            {
                if (v.index > 0)
                    continue;

                stack.Push(v);
                do
                {
                    var vert = stack.Pop();
                    vert.index = curIdx;
                    _sets.Add(curIdx, vert.part);

                    foreach (var other in vert.edges)
                        if (other.index == 0)
                            stack.Push(other);
                }
                while (stack.Count > 0);

                ++curIdx;
            }
        }
    }

    public class FastFlowGraphP : FastFlowGraph<Part>
    {
        public class VertexP : Vertex
        {
            public VertexP(Part p, FastFlowGraph<Part> g) : base(p, g) { }

            public override void FindEdges()
            {
                Vertex other;
                for (int i = 0, count = part.fuelLookupTargets.Count; i < count; ++i)
                {
                    if ((!(part.fuelLookupTargets[i] == part.parent) || !part.isAttached) && (!(part.fuelLookupTargets[i] != part.parent) || !part.fuelLookupTargets[i].isAttached))
                    {
                        continue;
                    }
                    if (part.fuelLookupTargets[i] is CompoundPart)
                    {
                        if (part.fuelLookupTargets[i].parent != null && _graph._lookup.TryGetValue((part.fuelLookupTargets[i] is CompoundPart) ? part.fuelLookupTargets[i].parent : part.fuelLookupTargets[i], out other))
                        {
                            edges.AddUnique(other);
                            other.edges.AddUnique(this);
                        }
                    }
                    else if (_graph._lookup.TryGetValue(part.fuelLookupTargets[i], out other))
                    {
                        edges.AddUnique(other);
                        other.edges.AddUnique(this);
                    }
                }
                if (!part.fuelCrossFeed)
                {
                    return;
                }
                for (int i = 0, count2 = part.attachNodes.Count; i < count2; ++i)
                {
                    AttachNode attachNode = part.attachNodes[i];
                    if (attachNode.attachedPart == null || !attachNode.ResourceXFeed)
                    {
                        continue;
                    }
                    bool noFeed = false;
                    AttachNode attachNode2 = attachNode.FindOpposingNode();
                    if (attachNode2 != null)
                    {
                        noFeed = !attachNode2.ResourceXFeed && !attachNode2.AllowOneWayXFeed;
                    }
                    if (!noFeed && (string.IsNullOrEmpty(part.NoCrossFeedNodeKey) || !attachNode.id.Contains(part.NoCrossFeedNodeKey)) && _graph._lookup.TryGetValue(attachNode.attachedPart, out other))
                    {
                        edges.AddUnique(other);
                        other.edges.AddUnique(this);
                    }
                }
                if (part.srfAttachNode != null && part.srfAttachNode.attachedPart != null && part.srfAttachNode.attachedPart.fuelCrossFeed && _graph._lookup.TryGetValue(part.srfAttachNode.attachedPart, out other))
                {
                    edges.AddUnique(other);
                    other.edges.AddUnique(this);
                }
            }
        }

        public override void Create(List<Part> parts)
        {
            foreach (var p in parts)
            {
                var v = new VertexP(p, this);
                _lookup.Add(p, v);
                _graph.Add(v);
            }
            CreateGraph();
        }
    }

    public class FastFlowGraphS : FastFlowGraph<ProtoPartSnapshot>
    {
        public class VertexS : Vertex
        {
            protected int _idx;

            public VertexS(ProtoPartSnapshot p, int idx, FastFlowGraph<ProtoPartSnapshot> g) : base(p, g) { _idx = idx; }

            public override void FindEdges()
            {
                Vertex other;
                var prefab = part.partInfo.partPrefab;

                // TODO: Support fuel lines and things that toggle crossfeed.
                // For now, just use the partInfo.
                if (!prefab.fuelCrossFeed)
                {
                    return;
                }
                for (int i = 0, count2 = part.attachNodes.Count; i < count2; ++i)
                {
                    var attachNode = part.attachNodes[i];
                    if (attachNode.partIdx < 0 || attachNode.partIdx >= _graph._parts.Count || !prefab.attachNodes[i].ResourceXFeed)
                        continue;

                    var otherPart = _graph._parts[attachNode.partIdx];

                    bool noFeed = false;
                    AttachNode attachNode2 = null;
                    AttachNodeSnapshot anSnap = null;
                    for (int j = otherPart.attachNodes.Count; j-- > 0;)
                    {
                        var an = otherPart.attachNodes[j];
                        if (an.partIdx == _idx)
                        {
                            anSnap = an;
                            attachNode2 = otherPart.partPrefab.attachNodes[j];
                            break;
                        }
                    }
                    if (anSnap == null)
                    {
                        if (otherPart.srfAttachNode.partIdx == _idx)
                        {
                            anSnap = otherPart.srfAttachNode;
                            attachNode2 = otherPart.partPrefab.srfAttachNode;
                        }
                    }

                    if (attachNode2 != null)
                    {
                        noFeed = !attachNode2.ResourceXFeed && !attachNode2.AllowOneWayXFeed;
                    }
                    if (!noFeed && (string.IsNullOrEmpty(prefab.NoCrossFeedNodeKey) || !attachNode.id.Contains(prefab.NoCrossFeedNodeKey)) && _graph._lookup.TryGetValue(otherPart, out other))
                    {
                        edges.AddUnique(other);
                        other.edges.AddUnique(this);
                    }
                }
                if (part.srfAttachNode != null && part.srfAttachNode.partIdx >= 0 && part.srfAttachNode.partIdx < _graph._parts.Count)
                {
                    var otherPart = _graph._parts[part.srfAttachNode.partIdx];
                    if (otherPart.partPrefab.fuelCrossFeed && _graph._lookup.TryGetValue(otherPart, out other))
                    {
                        edges.AddUnique(other);
                        other.edges.AddUnique(this);
                    }
                }
            }
        }

        public override void Create(List<ProtoPartSnapshot> parts)
        {
            _parts = parts;

            for(int i = parts.Count; i-- > 0;)
            {
                var p = parts[i];
                var v = new VertexS(p, i, this);
                _lookup.Add(p, v);
                _graph.Add(v);
            }
            CreateGraph();
        }
    }
}
