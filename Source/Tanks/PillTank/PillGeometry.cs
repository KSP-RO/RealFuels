// Geometry for building a pill solver's skin out of several pieces (colliders): convex hulls, and the outer surface
// of a union of pieces. No Unity dependency.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RealFuels.Tanks.PillTank
{
    // One closed piece of a part's inside. Convex pieces (hulls) also cut away the parts of other pieces' surfaces they
    // cover, so the union's surface has no inner walls; a concave piece's surface is kept whole.
    public sealed class SkinPiece
    {
        public V3[] V;
        public int[] T;
        public bool Convex;
        internal V3 Lo, Hi;
        internal V3[] N;          // convex: face planes (deduplicated), outward n.p = d
        internal double[] D;

        public SkinPiece(V3[] v, int[] t, bool convex)
        {
            V = v; T = t; Convex = convex;
            Lo = Hi = v[0];
            foreach (V3 p in v) { Lo = V3.Min(Lo, p); Hi = V3.Max(Hi, p); }
        }

        // The convex hull of the points as a piece, or null if they are (nearly) flat.
        public static SkinPiece Hull(IList<V3> pts) =>
            PillTank.Hull.Build(pts, out V3[] v, out int[] t) ? new SkinPiece(v, t, true) : null;
    }

    public static class Hull
    {
        // Incremental 3-D convex hull: outward-facing triangles over the hull's own vertices. False when the points
        // span no volume.
        public static bool Build(IList<V3> input, out V3[] verts, out int[] tris)
        {
            verts = null; tris = null;
            if (input.Count < 4) return false;
            V3 lo = input[0], hi = lo;
            foreach (V3 p in input) { lo = V3.Min(lo, p); hi = V3.Max(hi, p); }
            double size = (hi - lo).Len;
            if (size <= 0) return false;
            double eps = 1e-9 * size;

            // duplicates out (meshes repeat vertices along seams)
            var seen = new HashSet<(long, long, long)>();
            var pts = new List<V3>();
            double q = 1e-7 * size;
            foreach (V3 p in input)
                if (seen.Add(((long)Math.Round((p.x - lo.x) / q), (long)Math.Round((p.y - lo.y) / q), (long)Math.Round((p.z - lo.z) / q))))
                    pts.Add(p);
            if (pts.Count < 4) return false;

            // a starting tetrahedron from extreme points
            int i0 = 0, i1 = 0;
            double best = -1;
            for (int ax = 0; ax < 3; ax++)
            {
                int mn = 0, mx = 0;
                for (int i = 1; i < pts.Count; i++)
                {
                    if (Comp(pts[i], ax) < Comp(pts[mn], ax)) mn = i;
                    if (Comp(pts[i], ax) > Comp(pts[mx], ax)) mx = i;
                }
                double d = (pts[mx] - pts[mn]).Len2;
                if (d > best) { best = d; i0 = mn; i1 = mx; }
            }
            V3 dir = (pts[i1] - pts[i0]).Norm;
            int i2 = -1; best = 1e-6 * size;
            for (int i = 0; i < pts.Count; i++)
            {
                V3 w = pts[i] - pts[i0];
                double d = (w - dir * V3.Dot(w, dir)).Len;
                if (d > best) { best = d; i2 = i; }
            }
            if (i2 < 0) return false;
            V3 n0 = V3.Cross(pts[i1] - pts[i0], pts[i2] - pts[i0]).Norm;
            int i3 = -1; best = 1e-6 * size;
            for (int i = 0; i < pts.Count; i++)
            {
                double d = Math.Abs(V3.Dot(pts[i] - pts[i0], n0));
                if (d > best) { best = d; i3 = i; }
            }
            if (i3 < 0) return false;

            V3 cen = (pts[i0] + pts[i1] + pts[i2] + pts[i3]) * 0.25;
            var faces = new List<Face>();
            void Add(int a, int b, int c)
            {
                var f = new Face(pts, a, b, c);
                if (V3.Dot(f.N, cen) - f.D > 0) f = new Face(pts, a, c, b);   // keep the inside behind every face
                faces.Add(f);
            }
            Add(i0, i1, i2); Add(i0, i1, i3); Add(i0, i2, i3); Add(i1, i2, i3);

            // far points first: they're the hull's corners, so fewer slivers
            var order = Enumerable.Range(0, pts.Count).OrderByDescending(i => (pts[i] - cen).Len2).ToList();
            var edges = new Dictionary<(int, int), int>();
            var visible = new List<int>();
            foreach (int pi in order)
            {
                if (pi == i0 || pi == i1 || pi == i2 || pi == i3) continue;
                V3 p = pts[pi];
                visible.Clear();
                for (int f = 0; f < faces.Count; f++)
                    if (V3.Dot(faces[f].N, p) - faces[f].D > eps) visible.Add(f);
                if (visible.Count == 0) continue;
                // the horizon: edges of visible faces whose other side isn't visible
                edges.Clear();
                foreach (int f in visible)
                {
                    Face F = faces[f];
                    edges[(F.A, F.B)] = 1; edges[(F.B, F.C)] = 1; edges[(F.C, F.A)] = 1;
                }
                var horizon = edges.Keys.Where(e => !edges.ContainsKey((e.Item2, e.Item1))).ToList();
                for (int k = visible.Count - 1; k >= 0; k--)
                {
                    int f = visible[k];
                    faces[f] = faces[faces.Count - 1];
                    faces.RemoveAt(faces.Count - 1);
                }
                foreach (var e in horizon) faces.Add(new Face(pts, e.Item1, e.Item2, pi));   // the horizon's winding faces out
            }

            var map = new Dictionary<int, int>();
            var vl = new List<V3>();
            var tl = new List<int>();
            foreach (Face f in faces)
                foreach (int i in new[] { f.A, f.B, f.C })
                {
                    if (!map.TryGetValue(i, out int j)) { j = vl.Count; map[i] = j; vl.Add(pts[i]); }
                    tl.Add(j);
                }
            verts = vl.ToArray();
            tris = tl.ToArray();
            return true;
        }

        struct Face
        {
            public int A, B, C;
            public V3 N;
            public double D;
            public Face(List<V3> p, int a, int b, int c)
            {
                A = a; B = b; C = c;
                V3 n = V3.Cross(p[b] - p[a], p[c] - p[a]);
                double l = n.Len;
                N = l > 0 ? n * (1.0 / l) : new V3(0, 0, 0);
                D = V3.Dot(N, p[a]);
            }
        }

        static double Comp(V3 v, int ax) => ax == 0 ? v.x : (ax == 1 ? v.y : v.z);
    }

    public static class Union
    {
        // A soup of the pieces (each its own closed piece, PieceStart) with the union's outer surface (SV/ST): every
        // piece's triangles less what convex pieces cover. Faces two pieces share (or abut on, facing each other) go;
        // of two coincident faces facing the same way the first piece's stays. `stack` (tanks joined end to end, a few
        // pieces): a nearly convex piece is taken as its hull, and what of a face lies inside (or against) a concave
        // piece goes too.
        public static TriSoup Build(IList<SkinPiece> pieces, bool stack = false)
        {
            // a ProceduralParts cylinder or cone, a stack's tank, is convex but for its facets (rings of different vertex
            // counts zig-zag): taken as its hull (the same vertices, its faces within the facets' sag of the mesh's), so
            // its faces cut others' exactly (the disks where two tanks meet go, an annulus left over stays)
            if (stack && pieces.Count > 1)
                for (int i = 0; i < pieces.Count; i++)
                    if (!pieces[i].Convex && NearlyConvex(pieces[i]) is SkinPiece h) pieces[i] = h;
            var V = new List<V3>();
            var T = new List<int>();
            var starts = new List<int>();
            foreach (SkinPiece p in pieces)
            {
                starts.Add(T.Count / 3);
                int off = V.Count;
                V.AddRange(p.V);
                foreach (int i in p.T) T.Add(off + i);
            }
            // overlapping concave pieces keep their inner walls in the surface: no exact volume from it
            var soup = new TriSoup(V.ToArray(), T.ToArray()) { PieceStart = starts.ToArray(), ExactVolume = pieces.Count < 2 || pieces.All(p => p.Convex) };
            if (pieces.Count < 2 || !stack && !pieces.Any(p => p.Convex)) return soup;

            V3 lo = pieces[0].Lo, hi = pieces[0].Hi;
            foreach (SkinPiece p in pieces) { lo = V3.Min(lo, p.Lo); hi = V3.Max(hi, p.Hi); }
            double eps = 1e-6 * (hi - lo).Len;
            foreach (SkinPiece p in pieces) if (p.Convex) Planes(p, eps);

            var SV = new List<V3>();
            var ST = new List<int>();
            var polys = new List<List<V3>>();
            var next = new List<List<V3>>();
            for (int i = 0; i < pieces.Count; i++)
            {
                SkinPiece pc = pieces[i];
                for (int t = 0; t < pc.T.Length; t += 3)
                {
                    V3 a = pc.V[pc.T[t]], b = pc.V[pc.T[t + 1]], c = pc.V[pc.T[t + 2]];
                    V3 n = V3.Cross(b - a, c - a);
                    if (n.Len2 <= 0) continue;
                    V3 tlo = V3.Min(a, V3.Min(b, c)), thi = V3.Max(a, V3.Max(b, c));
                    polys.Clear();
                    polys.Add(new List<V3> { a, b, c });
                    for (int j = 0; j < pieces.Count && polys.Count > 0; j++)
                    {
                        SkinPiece cut = pieces[j];
                        if (j == i || !(cut.Convex || stack) || !Overlap(tlo, thi, cut.Lo, cut.Hi, eps)) continue;
                        next.Clear();
                        if (cut.Convex) foreach (List<V3> poly in polys) Subtract(poly, n, cut, j < i, eps, next);
                        // a concave piece: what of a polygon lies inside it (or against it, facing in) goes
                        else foreach (List<V3> poly in polys) Carve(poly, n.Norm, cut, 1e-4 * (hi - lo).Len, 4, next);
                        polys.Clear();
                        polys.AddRange(next);
                    }
                    foreach (List<V3> poly in polys)
                    {
                        int off = SV.Count;
                        SV.AddRange(poly);
                        for (int k = 1; k + 1 < poly.Count; k++) { ST.Add(off); ST.Add(off + k); ST.Add(off + k + 1); }
                    }
                }
            }
            soup.SV = SV.ToArray();
            soup.ST = ST.ToArray();
            return soup;
        }

        // The piece's convex hull, if the piece's surface is nowhere far inside it (each face's middle within 0.5% of the
        // piece's size of the hull's surface): convex but for its facets. Else null (a waist, a dip: concave).
        static SkinPiece NearlyConvex(SkinPiece p)
        {
            SkinPiece h = SkinPiece.Hull(p.V);
            double vp = SignedVolume(p);
            // (the hull holding about what the piece does: a hull of many coplanar rings can come out wrong)
            if (h == null || !(vp > 0) || !(Math.Abs(SignedVolume(h) / vp - 1) < 0.05)) return null;
            double size = (p.Hi - p.Lo).Len, sag = 0.005 * size;
            Planes(h, 1e-6 * size);
            for (int t = 0; t < p.T.Length; t += 3)
            {
                V3 c = (p.V[p.T[t]] + p.V[p.T[t + 1]] + p.V[p.T[t + 2]]) * (1.0 / 3);
                double depth = double.PositiveInfinity;
                for (int k = 0; k < h.N.Length; k++) depth = Math.Min(depth, h.D[k] - V3.Dot(h.N[k], c));
                if (depth > sag) return null;
            }
            return h;
        }

        static double SignedVolume(SkinPiece p)
        {
            double v = 0;
            for (int t = 0; t < p.T.Length; t += 3)
                v += V3.Dot(p.V[p.T[t]], V3.Cross(p.V[p.T[t + 1]], p.V[p.T[t + 2]]));
            return v / 6;
        }

        // `poly` (another piece's face, outward normal n) less what of it lies inside concave piece `p`, to `depth`
        // levels: a polygon whose probes are all inside goes; one with some inside is split in four (by its
        // triangles' midpoints) and each part tried again; one with none inside, or at the last level not all, stays.
        static void Carve(List<V3> poly, V3 n, SkinPiece p, double delta, int depth, List<List<V3>> outPolys)
        {
            int inside = Probe(poly, n, p, delta, out int probes);
            if (inside == probes) return;
            if (inside == 0 || depth == 0) { outPolys.Add(poly); return; }
            for (int k = 1; k + 1 < poly.Count; k++)
            {
                V3 a = poly[0], b = poly[k], c = poly[k + 1], ab = (a + b) * 0.5, bc = (b + c) * 0.5, ca = (c + a) * 0.5;
                Carve(new List<V3> { a, ab, ca }, n, p, delta, depth - 1, outPolys);
                Carve(new List<V3> { ab, b, bc }, n, p, delta, depth - 1, outPolys);
                Carve(new List<V3> { ca, bc, c }, n, p, delta, depth - 1, outPolys);
                Carve(new List<V3> { ab, bc, ca }, n, p, delta, depth - 1, outPolys);
            }
        }

        // How many of a polygon's probes are inside concave piece `p`: its middle and each vertex (pulled a little
        // towards the middle), stepped `delta` off it along its outward normal `n` (so a face against p's, facing into
        // it, counts as inside). A point on p's surface may go either way: only all of them inside removes a polygon.
        static int Probe(List<V3> poly, V3 n, SkinPiece p, double delta, out int probes)
        {
            V3 mid = default(V3);
            foreach (V3 q in poly) mid = mid + q;
            mid = mid * (1.0 / poly.Count);
            probes = poly.Count + 1;
            int inside = Inside(p, mid + n * delta) ? 1 : 0;
            foreach (V3 q in poly)
                if (Inside(p, q + (mid - q) * 0.01 + n * delta)) inside++;
            return inside;
        }

        // Inside a closed piece by ray parity, three skewed rays agreeing (any odd one out: not inside).
        static readonly V3[] Rays = { new V3(0.5773, 0.5774, 0.5775).Norm, new V3(-0.6011, 0.3989, 0.6927).Norm, new V3(0.2117, -0.8003, 0.5609).Norm };
        static bool Inside(SkinPiece p, V3 q)
        {
            if (q.x < p.Lo.x || q.y < p.Lo.y || q.z < p.Lo.z || q.x > p.Hi.x || q.y > p.Hi.y || q.z > p.Hi.z) return false;
            foreach (V3 d in Rays)
            {
                int hits = 0;
                for (int t = 0; t < p.T.Length; t += 3)
                    if (RayHits(q, d, p.V[p.T[t]], p.V[p.T[t + 1]], p.V[p.T[t + 2]])) hits++;
                if ((hits & 1) == 0) return false;
            }
            return true;
        }

        // Moller-Trumbore, the far side only (t > 0).
        static bool RayHits(V3 o, V3 d, V3 a, V3 b, V3 c)
        {
            V3 e1 = b - a, e2 = c - a, pv = V3.Cross(d, e2);
            double det = V3.Dot(e1, pv);
            if (Math.Abs(det) < 1e-18) return false;
            double inv = 1 / det;
            V3 tv = o - a;
            double u = V3.Dot(tv, pv) * inv;
            if (u < 0 || u > 1) return false;
            V3 qv = V3.Cross(tv, e1);
            double v = V3.Dot(d, qv) * inv;
            if (v < 0 || u + v > 1) return false;
            return V3.Dot(e2, qv) * inv > 0;
        }

        static bool Overlap(V3 alo, V3 ahi, V3 blo, V3 bhi, double eps) =>
            alo.x <= bhi.x + eps && blo.x <= ahi.x + eps && alo.y <= bhi.y + eps && blo.y <= ahi.y + eps &&
            alo.z <= bhi.z + eps && blo.z <= ahi.z + eps;

        static void Planes(SkinPiece p, double eps)
        {
            var N = new List<V3>();
            var D = new List<double>();
            for (int t = 0; t < p.T.Length; t += 3)
            {
                V3 a = p.V[p.T[t]];
                V3 n = V3.Cross(p.V[p.T[t + 1]] - a, p.V[p.T[t + 2]] - a);
                if (n.Len2 <= 0) continue;
                n = n.Norm;
                double d = V3.Dot(n, a);
                bool dup = false;
                for (int k = 0; k < N.Count && !dup; k++)
                    dup = V3.Dot(N[k], n) > 1 - 1e-9 && Math.Abs(D[k] - d) < eps;
                if (!dup) { N.Add(n); D.Add(d); }
            }
            p.N = N.ToArray();
            p.D = D.ToArray();
        }

        // poly less convex piece `cut`, as convex polygons added to `outPolys`: the part outside plane 1, then the
        // part inside 1 but outside 2, and so on; what is inside every plane is covered. A polygon lying in one of
        // cut's planes counts as covered there, unless it faces the same way and `cutFirst` is false (of two
        // coincident same-way faces, the earlier piece's stays).
        static void Subtract(List<V3> poly, V3 polyNormal, SkinPiece cut, bool cutFirst, double eps, List<List<V3>> outPolys)
        {
            List<V3> cur = poly;
            for (int k = 0; k < cut.N.Length; k++)
            {
                V3 n = cut.N[k];
                double d = cut.D[k];
                bool onPlane = cur.All(p => Math.Abs(V3.Dot(n, p) - d) <= eps);
                if (onPlane)
                {
                    if (V3.Dot(polyNormal, n) > 0 && !cutFirst) { outPolys.Add(cur); return; }
                    continue;
                }
                Split(cur, n, d + eps, out List<V3> outside, out List<V3> inside);
                if (outside != null) outPolys.Add(outside);
                if (inside == null) return;
                cur = inside;
            }
        }

        // Sutherland-Hodgman both ways: the parts with n.p > d (outside) and n.p <= d; null for an empty part.
        static void Split(List<V3> poly, V3 n, double d, out List<V3> outside, out List<V3> inside)
        {
            var o = new List<V3>();
            var i = new List<V3>();
            int m = poly.Count;
            for (int k = 0; k < m; k++)
            {
                V3 p = poly[k], q = poly[(k + 1) % m];
                double sp = V3.Dot(n, p) - d, sq = V3.Dot(n, q) - d;
                if (sp > 0) o.Add(p); else i.Add(p);
                if ((sp > 0) != (sq > 0))
                {
                    V3 x = p + (q - p) * (sp / (sp - sq));
                    o.Add(x); i.Add(x);
                }
            }
            outside = o.Count >= 3 ? o : null;
            inside = i.Count >= 3 ? i : null;
        }
    }
}
