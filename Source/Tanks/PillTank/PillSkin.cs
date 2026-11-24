using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels.Tanks.PillTank
{
    // Where a part's inside comes from, read on the main thread into the solver's frame (the part's, without scale,
    // so in metres). In order of preference:
    //  - named transforms (the module's pillSkin): their meshes together close the volume
    //  - a ProceduralPart's sides and ends (its visible skin)
    //  - the part's colliders: convex mesh colliders as their hulls (what physics uses), boxes, spheres and capsules
    //    as hulls of their outlines, concave mesh colliders as their triangles; the inside is their union
    //  - its visible meshes, each taken as closed
    public sealed class PillSkin
    {
        public enum Kind { None, Named, ProceduralPart, Colliders, Meshes }

        private readonly Part _part;
        private readonly string[] _names;
        private readonly List<Component> _found = new List<Component>();
        public Kind Source { get; private set; }

        public PillSkin(Part part, string names)
        {
            _part = part;
            _names = string.IsNullOrEmpty(names)
                ? new string[0]
                : names.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private Transform Model => _part.partTransform.Find("model") ?? _part.partTransform;

        // Finds the sources again; a hash of their meshes and placement, which changes when the skin does. `content`:
        // the same without the objects' identities (a skin rebuilt the same, new meshes and all, keeps it).
        public int Signature(out int content)
        {
            Gather();
            Matrix4x4 toPart = ToPart();
            unchecked
            {
                int h = (int)Source * 31 + _found.Count, ids = 0;
                foreach (Component c in _found)
                {
                    ids = ids * 31 + c.GetInstanceID();
                    Mesh m = MeshOf(c);
                    if (m != null)
                    {
                        ids = ids * 31 + m.GetInstanceID();
                        h = h * 31 + m.vertexCount;
                        h = h * 31 + m.bounds.center.GetHashCode();
                        h = h * 31 + m.bounds.size.GetHashCode();
                        h = h * 31 + VertexSample(m);
                    }
                    else h = h * 31 + PrimitiveHash(c);
                    Matrix4x4 M = toPart * c.transform.localToWorldMatrix;
                    for (int i = 0; i < 15; i++) h = h * 31 + Mathf.Round(M[i] * 1e4f).GetHashCode();   // column-major: 12-14 the offset
                }
                content = h;
                return h * 397 + ids;
            }
        }

        // A mesh rebuilt in place within the same bounds and vertex count (a new airfoil, say) changes its vertices: a
        // hash of a spread of them. Main thread only (one buffer, reused).
        private static readonly List<Vector3> _verts = new List<Vector3>();
        private const int Samples = 32;
        private static int VertexSample(Mesh m)
        {
            if (!m.isReadable) return 0;
            m.GetVertices(_verts);
            int n = _verts.Count, h = 0;
            if (n == 0) return 0;
            int step = Math.Max(1, n / Samples);
            unchecked
            {
                for (int i = 0; i < n; i += step) h = h * 31 + _verts[i].GetHashCode();
                h = h * 31 + _verts[n - 1].GetHashCode();
            }
            return h;
        }

        private Matrix4x4 ToPart()
        {
            Transform pt = _part.transform;
            return Matrix4x4.TRS(pt.position, pt.rotation, Vector3.one).inverse;
        }

        private void Gather()
        {
            _found.Clear();
            Source = Kind.None;
            Transform model = Model;
            if (_names.Length > 0)
            {
                foreach (string n in _names)
                    foreach (Transform t in _part.FindModelTransforms(n))
                        AddFilter(t);
                if (_found.Count > 0) { Source = Kind.Named; return; }
            }

            PartModule pp = _part.Modules.Contains("ProceduralPart") ? _part.Modules["ProceduralPart"] : null;
            if (pp != null)
            {
                foreach (string f in new[] { "sidesName", "endsName" })
                {
                    string n = pp.Fields.GetValue<string>(f);
                    Transform t = string.IsNullOrEmpty(n) ? null : _part.FindModelTransform(n);
                    if (t != null) AddFilter(t);
                }
                if (_found.Count > 0) { Source = Kind.ProceduralPart; return; }
            }

            foreach (Collider c in model.GetComponentsInChildren<Collider>(false))
            {
                if (!c.enabled || !Solid(c) || !Ours(c.transform)) continue;
                if (c is MeshCollider mc ? mc.sharedMesh != null && mc.sharedMesh.isReadable
                    : c is BoxCollider || c is SphereCollider || c is CapsuleCollider)
                    _found.Add(c);
            }
            if (_found.Count > 0) { Source = Kind.Colliders; return; }

            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(false))
                if (Ours(mf.transform) && mf.GetComponent<Renderer>() is Renderer r && r.enabled) AddFilter(mf.transform);
            if (_found.Count > 0) Source = Kind.Meshes;
        }

        // A part's body: in the editor KSP makes every part collider a trigger, so there only airlocks and ladders
        // (by their tags) are left out.
        public static bool Solid(Collider c) =>
            !c.CompareTag("Airlock") && !c.CompareTag("Ladder") && (!c.isTrigger || HighLogic.LoadedSceneIsEditor);

        // Not under another part (attached parts hang under their parent's transform).
        private bool Ours(Transform t) => t.GetComponentInParent<Part>() == _part;

        private void AddFilter(Transform t)
        {
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable) _found.Add(mf);
        }

        private static Mesh MeshOf(Component c) =>
            c is MeshFilter mf ? mf.sharedMesh : c is MeshCollider mc ? mc.sharedMesh : null;

        private static int PrimitiveHash(Component c)
        {
            switch (c)
            {
                case BoxCollider b: return b.center.GetHashCode() ^ b.size.GetHashCode();
                case SphereCollider s: return s.center.GetHashCode() ^ s.radius.GetHashCode();
                case CapsuleCollider k: return k.center.GetHashCode() ^ k.radius.GetHashCode() ^ k.height.GetHashCode() ^ k.direction;
                default: return 0;
            }
        }

        // The skin as the solver's soup (pieces and their union's surface). Main thread only.
        public TriSoup Capture()
        {
            Gather();
            Matrix4x4 toPart = ToPart();
            var pieces = new List<SkinPiece>();
            if (Source == Kind.Named || Source == Kind.ProceduralPart)
            {
                // all of them together close the volume: one piece
                var v = new List<V3>();
                var t = new List<int>();
                foreach (Component c in _found) AppendMesh(MeshOf(c), toPart * c.transform.localToWorldMatrix, v, t);
                if (t.Count >= 12) pieces.Add(new SkinPiece(v.ToArray(), t.ToArray(), false));
            }
            else
            {
                foreach (Component c in _found)
                {
                    Matrix4x4 M = toPart * c.transform.localToWorldMatrix;
                    SkinPiece p = null;
                    if (c is MeshCollider mc && !mc.convex || c is MeshFilter)
                    {
                        var v = new List<V3>();
                        var t = new List<int>();
                        AppendMesh(MeshOf(c), M, v, t);
                        if (t.Count >= 12) p = new SkinPiece(v.ToArray(), t.ToArray(), false);
                    }
                    else
                    {
                        var pts = new List<V3>();
                        foreach (Vector3 q in Outline(c)) pts.Add(Of(M.MultiplyPoint3x4(q)));
                        p = SkinPiece.Hull(pts);
                    }
                    if (p != null) pieces.Add(p);
                }
            }
            if (pieces.Count == 0) return null;
            TriSoup soup = Union.Build(pieces);
            soup.Hints.Add(new V3(0, 1, 0));    // a stack part's long axis
            return soup;
        }

        private static V3 Of(Vector3 p) => new V3(p.x, p.y, p.z);

        private static void AppendMesh(Mesh m, Matrix4x4 M, List<V3> verts, List<int> tris)
        {
            if (m == null) return;
            int off = verts.Count;
            foreach (Vector3 v in m.vertices) verts.Add(Of(M.MultiplyPoint3x4(v)));
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (m.GetTopology(s) != MeshTopology.Triangles) continue;
                foreach (int i in m.GetTriangles(s)) tris.Add(off + i);
            }
        }

        // Points (collider's local frame) whose hull is the collider, or inside it for the round ones.
        private static IEnumerable<Vector3> Outline(Component c)
        {
            switch (c)
            {
                case MeshCollider mc:
                    foreach (Vector3 v in mc.sharedMesh.vertices) yield return v;
                    break;
                case BoxCollider b:
                    for (int i = 0; i < 8; i++)
                        yield return b.center + Vector3.Scale(b.size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    break;
                case SphereCollider s:
                    foreach (Vector3 n in Sphere()) yield return s.center + s.radius * n;
                    break;
                case CapsuleCollider k:
                    Vector3 ax = k.direction == 0 ? Vector3.right : k.direction == 1 ? Vector3.up : Vector3.forward;
                    float half = Mathf.Max(0f, k.height * 0.5f - k.radius);
                    foreach (Vector3 n in Sphere())
                    {
                        float d = Vector3.Dot(n, ax);
                        if (d >= -0.2f) yield return k.center + k.radius * n + ax * half;
                        if (d <= 0.2f) yield return k.center + k.radius * n - ax * half;
                    }
                    break;
            }
        }

        private static IEnumerable<Vector3> Sphere()
        {
            const int rings = 8, seg = 16;
            yield return Vector3.up;
            yield return Vector3.down;
            for (int i = 1; i < rings; i++)
            {
                float phi = Mathf.PI * i / rings - Mathf.PI / 2f;
                for (int j = 0; j < seg; j++)
                {
                    float th = 2f * Mathf.PI * j / seg;
                    yield return new Vector3(Mathf.Cos(phi) * Mathf.Cos(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(th));
                }
            }
        }
    }
}
