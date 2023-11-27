using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;
using ROUtils.DataTypes;

namespace RealFuels
{
    public class FastFlowGraph
    {
        public class Vertex
        {
            public Part part;
            public int index = 0;
            public List<Vertex> edges = new List<Vertex>();

            public Vertex(Part p) { part = p; }
        }

        private List<Part> _parts;
        private List<Vertex> _graph = new List<Vertex>();
        private Dictionary<Part, Vertex> _lookup = new Dictionary<Part, Vertex>();

        private CollectionDictionary<int, Part, HashSet<Part>> _sets = new CollectionDictionary<int, Part, HashSet<Part>>();
        public ICollection<HashSet<Part>> sets => _sets.Values;

        private int curIdx = 1;

        public FastFlowGraph(List<Part> parts)
        {
            _parts = parts;
            CreateGraph();
        }

        public void CreateGraph()
        {
            foreach (var p in _parts)
            {
                var v = new Vertex(p);
                _lookup.Add(p, v);
                _graph.Add(v);
            }

            foreach (var v in _graph)
            {
                FindEdges(v);
            }

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

        private void FindEdges(Vertex vertex)
        {
            var part = vertex.part;
            Vertex other;
            for (int i = 0, count = part.fuelLookupTargets.Count; i < count; ++i)
            {
                if ((!(part.fuelLookupTargets[i] == part.parent) || !part.isAttached) && (!(part.fuelLookupTargets[i] != part.parent) || !part.fuelLookupTargets[i].isAttached))
                {
                    continue;
                }
                if (part.fuelLookupTargets[i] is CompoundPart)
                {
                    if (part.fuelLookupTargets[i].parent != null && _lookup.TryGetValue((part.fuelLookupTargets[i] is CompoundPart) ? part.fuelLookupTargets[i].parent : part.fuelLookupTargets[i], out other))
                    {
                        vertex.edges.AddUnique(other);
                        other.edges.AddUnique(vertex);
                    }
                }
                else if (_lookup.TryGetValue(part.fuelLookupTargets[i], out other))
                {
                    vertex.edges.AddUnique(other);
                    other.edges.AddUnique(vertex);
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
                if (!noFeed && (string.IsNullOrEmpty(part.NoCrossFeedNodeKey) || !attachNode.id.Contains(part.NoCrossFeedNodeKey)) && _lookup.TryGetValue(attachNode.attachedPart, out other))
                {
                    vertex.edges.AddUnique(other);
                    other.edges.AddUnique(vertex);
                }
            }
            if (part.srfAttachNode != null && part.srfAttachNode.attachedPart != null && part.srfAttachNode.attachedPart.fuelCrossFeed && _lookup.TryGetValue(part.srfAttachNode.attachedPart, out other))
            {
                vertex.edges.AddUnique(other);
                other.edges.AddUnique(vertex);
            }
        }
    }
}
