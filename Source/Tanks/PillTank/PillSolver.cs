// Largest pill (capsule) tank(s) inscribed in a closed triangle mesh.
// No Unity dependency (own V3 type): it runs on a worker thread.
//
//  1. voxelize inside/outside by ray parity along z
//  2. exact Euclidean distance transform (Felzenszwalb-Huttenlocher) -> interior distance D
//  3. global line search: centre c + direction u; running minima of D outward from c give the
//     axis length L(r) for every r at once -> best r per line in O(n)
//  4. Nelder-Mead refine of the best few lines on the voxel field (and, a taper allowed, the radius's slope
//     along the line: the room past r0 + k t is D(t) - k t, the same running minima)
//  5. exact fit on the axis found: the room at fine steps along it (BVH nearest-triangle query), then the
//     slope and ends (each moved a little) with the most volume and its rigorous radius (distance is
//     1-Lipschitz); optional polish: Nelder-Mead on the endpoints with that fit
//     Flatter end domes (Solver.Dome < 1): steps 3-4 again, round, on the part stretched along the axis found
//     (and the hints), and the room in step 5 a spheroid's
//  6. optional greedy packing: carve the tank out of the voxel field and repeat
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace RealFuels.Tanks.PillTank
{
    public struct V3
    {
        public double x, y, z;
        public V3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 operator -(V3 a) => new V3(-a.x, -a.y, -a.z);
        public static V3 operator *(V3 a, double s) => new V3(a.x * s, a.y * s, a.z * s);
        public static V3 operator *(double s, V3 a) => new V3(a.x * s, a.y * s, a.z * s);
        public static double Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static V3 Cross(V3 a, V3 b) => new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static V3 Min(V3 a, V3 b) => new V3(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
        public static V3 Max(V3 a, V3 b) => new V3(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
        public double Len2 => x * x + y * y + z * z;
        public double Len => Math.Sqrt(Len2);
        public V3 Norm => this * (1.0 / Len);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:F3}, {1:F3}, {2:F3})", x, y, z);
    }

    // A pill, maybe tapered: the spheres along axis AB, their radius going linearly from R (at A) to R2 (at B). That's
    // the hull of the two end spheres (|R - R2| < length): a cone between round ends. Domes flatter than hemispheres
    // (E < 1: their height over the radius): spheroids for the spheres, E times as deep along the axis as across, so the
    // pill is a round one L / E long squashed along its axis by E.
    public sealed class Capsule
    {
        public V3 A, B;
        public double R, R2;
        public double E = 1;
        public double Length => (B - A).Len;
        public double Volume => Vol(R, R2, Length, E);
        public static double Vol(double r, double L) => Math.PI * r * r * L + 4.0 / 3.0 * Math.PI * r * r * r;
        public static double Vol(double r1, double r2, double L, double e) => e == 1 ? Vol(r1, r2, L) : e * Vol(r1, r2, L / e);

        // The hull of spheres r1 and r2 a distance L apart: each sphere's cap out to the tangent circle, and the cone's
        // frustum between them (half-angle a: sin a = (r1 - r2) / L, the tangent circles r cos a across).
        public static double Vol(double r1, double r2, double L)
        {
            if (r1 < r2) { double t = r1; r1 = r2; r2 = t; }
            if (!(L > r1 - r2)) return 4.0 / 3.0 * Math.PI * r1 * r1 * r1;    // the small sphere inside the big one
            double s = (r1 - r2) / L, c2 = 1 - s * s;
            double h1 = r1 * (1 + s), h2 = r2 * (1 - s);
            return Math.PI / 3 * (h1 * h1 * (3 * r1 - h1) + h2 * h2 * (3 * r2 - h2) + L * c2 * c2 * (r1 * r1 + r1 * r2 + r2 * r2));
        }

        // The volume's centroid (on the axis): the caps' and the frustum's, weighted by their volumes. A cap h high
        // on a sphere r has its centroid 3 (2r - h)^2 / (4 (3r - h)) from the sphere's centre; a frustum l long, radii
        // p1 and p2, l (p1^2 + 2 p1 p2 + 3 p2^2) / (4 (p1^2 + p1 p2 + p2^2)) from its p1 end. (Flatter domes: the round
        // pill's, squashed.)
        public V3 Centroid
        {
            get
            {
                double len = Length, L = len / E;
                if (!(L > Math.Abs(R - R2))) return R >= R2 ? A : B;     // one sphere (the other inside it)
                V3 u = (B - A) * (E / len);
                double s = (R - R2) / L, c = Math.Sqrt(1 - s * s);
                double h1 = R * (1 + s), h2 = R2 * (1 - s);
                double v1 = Math.PI / 3 * h1 * h1 * (3 * R - h1), v2 = Math.PI / 3 * h2 * h2 * (3 * R2 - h2);
                double x1 = R > 0 ? -3 * (2 * R - h1) * (2 * R - h1) / (4 * (3 * R - h1)) : 0;
                double x2 = L + (R2 > 0 ? 3 * (2 * R2 - h2) * (2 * R2 - h2) / (4 * (3 * R2 - h2)) : 0);
                double p1 = R * c, p2 = R2 * c, l = L * c * c, q = p1 * p1 + p1 * p2 + p2 * p2;
                double vf = Math.PI / 3 * l * q;
                double xf = R * s + (q > 0 ? l * (p1 * p1 + 2 * p1 * p2 + 3 * p2 * p2) / (4 * q) : 0);
                double v = v1 + vf + v2;
                return v > 0 ? A + u * ((v1 * x1 + vf * xf + v2 * x2) / v) : A + u * (0.5 * L);
            }
        }
    }

    // Closed triangle surfaces (any triangle soup that encloses a volume; seams and duplicate vertices are fine).
    // One piece, or several (PieceStart) whose union is the inside: each piece is voxelized on its own, and the exact
    // distances use the union's outer surface (SV/ST, from PillSkin.Union) when given, else every triangle (inner
    // walls included, which only makes the radius smaller).
    public sealed class TriSoup
    {
        public V3[] V;
        public int[] T;            // 3 indices per triangle
        public int[] PieceStart;   // first triangle of each closed piece; null: one piece
        public V3[] SV;            // the outer surface of the union (null: V/T)
        public int[] ST;
        public List<V3> Hints = new List<V3>();   // extra search directions (e.g. the part's long axis)
        public bool ExactVolume = true;           // Volume() is the inside's (false: overlaps would count twice)
        public int TriCount => T.Length / 3;
        public int Pieces => PieceStart?.Length ?? 1;
        public V3[] SurfV => SV ?? V;
        public int[] SurfT => ST ?? T;

        public TriSoup(V3[] v, int[] t) { V = v; T = t; }

        // Triangles [first, end) of piece p.
        public void Piece(int p, out int first, out int end)
        {
            if (PieceStart == null) { first = 0; end = TriCount; return; }
            first = PieceStart[p];
            end = p + 1 < PieceStart.Length ? PieceStart[p + 1] : TriCount;
        }

        public void Bounds(out V3 lo, out V3 hi)
        {
            lo = hi = V[0];
            foreach (var v in V) { lo = V3.Min(lo, v); hi = V3.Max(hi, v); }
        }

        // The enclosed volume (of the union's outer surface when there is one); see ExactVolume.
        public double Volume()
        {
            V3[] v = SurfV; int[] t = SurfT;
            double s = 0;
            for (int i = 0; i < t.Length; i += 3)
                s += V3.Dot(v[t[i]], V3.Cross(v[t[i + 1]], v[t[i + 2]]));
            return Math.Abs(s / 6.0);
        }
    }

    internal static class Par
    {
        public static bool Threads = true;

        // Two cores left for the game's own threads (Unity's main and render threads).
        private static readonly ParallelOptions Options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };

        public static void For<TL>(int n, Func<TL> init, Action<int, TL> body)
        {
            if (!Threads) { var t = init(); for (int i = 0; i < n; i++) body(i, t); return; }
            Parallel.For(0, n, Options, init, (i, s, t) => { body(i, t); return t; }, t => { });
        }
    }

    // ------------------------------------------------------------------------------------------ voxels

    public sealed class Grid
    {
        public readonly double H;
        public readonly V3 Lo;
        public readonly int Nx, Ny, Nz;
        public bool[] Inside;
        public float[] D;
        public double Dmax;

        public int Cells => Nx * Ny * Nz;
        public int Idx(int i, int j, int k) => (i * Ny + j) * Nz + k;
        public V3 Center(int i, int j, int k) => new V3(Lo.x + (i + 0.5) * H, Lo.y + (j + 0.5) * H, Lo.z + (k + 0.5) * H);
        public V3 Center(int idx) { int k = idx % Nz, ij = idx / Nz; return Center(ij / Ny, ij % Ny, k); }
        public double Diag => new V3(Nx * H, Ny * H, Nz * H).Len;

        public Grid(TriSoup m, double h, int pad = 3)
        {
            H = h;
            m.Bounds(out V3 lo, out V3 hi);
            var p = new V3(pad * h, pad * h, pad * h);
            lo = lo - p; hi = hi + p;
            lo = lo - new V3(0.3172 * h, 0.2719 * h, 0);    // jitter: rays never hit mesh edges exactly
            Lo = lo;
            Nx = (int)Math.Ceiling((hi.x - lo.x) / h); Ny = (int)Math.Ceiling((hi.y - lo.y) / h); Nz = (int)Math.Ceiling((hi.z - lo.z) / h);
            Inside = Voxelize(m);
        }

        // Inside = inside any piece (each piece by its own ray parity, over the columns its bounds cover).
        bool[] Voxelize(TriSoup m)
        {
            int nz1 = Nz + 1;
            var diff = new short[Nx * Ny * nz1];
            var inside = new bool[Cells];
            for (int p = 0; p < m.Pieces; p++)
            {
                m.Piece(p, out int first, out int end);
                if (end <= first) continue;
                V3 lo = m.V[m.T[3 * first]], hi = lo;
                for (int i = 3 * first; i < 3 * end; i++) { lo = V3.Min(lo, m.V[m.T[i]]); hi = V3.Max(hi, m.V[m.T[i]]); }
                int i0 = Math.Max(0, (int)Math.Floor((lo.x - Lo.x) / H - 0.5)), i1 = Math.Min(Nx - 1, (int)Math.Ceiling((hi.x - Lo.x) / H - 0.5));
                int j0 = Math.Max(0, (int)Math.Floor((lo.y - Lo.y) / H - 0.5)), j1 = Math.Min(Ny - 1, (int)Math.Ceiling((hi.y - Lo.y) / H - 0.5));
                if (i1 < i0 || j1 < j0) continue;
                for (int i = i0; i <= i1; i++)
                    Array.Clear(diff, (i * Ny + j0) * nz1, (j1 - j0 + 1) * nz1);
                Rasterize(m, first, end, diff);
                int nj = j1 - j0 + 1;
                Par.For((i1 - i0 + 1) * nj, () => 0, (c, _) =>
                {
                    int col = (i0 + c / nj) * Ny + j0 + c % nj;
                    int s = 0, src = col * nz1, dst = col * Nz;
                    for (int k = 0; k < Nz; k++) { s += diff[src + k]; if ((s & 1) != 0) inside[dst + k] = true; }
                });
            }
            return inside;
        }

        void Rasterize(TriSoup m, int first, int end, short[] diff)
        {
            int nz1 = Nz + 1;
            for (int t = 3 * first; t < 3 * end; t += 3)
            {
                V3 a = m.V[m.T[t]], b = m.V[m.T[t + 1]], c = m.V[m.T[t + 2]];
                int ia = (int)Math.Ceiling((Math.Min(a.x, Math.Min(b.x, c.x)) - Lo.x) / H - 0.5);
                int ib = (int)Math.Floor((Math.Max(a.x, Math.Max(b.x, c.x)) - Lo.x) / H - 0.5);
                int ja = (int)Math.Ceiling((Math.Min(a.y, Math.Min(b.y, c.y)) - Lo.y) / H - 0.5);
                int jb = (int)Math.Floor((Math.Max(a.y, Math.Max(b.y, c.y)) - Lo.y) / H - 0.5);
                if (ib < ia || jb < ja) continue;
                double den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                if (Math.Abs(den) < 1e-14) continue;
                for (int i = ia; i <= ib; i++)
                {
                    double x = Lo.x + (i + 0.5) * H;
                    for (int j = ja; j <= jb; j++)
                    {
                        double y = Lo.y + (j + 0.5) * H;
                        double w0 = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / den;
                        double w1 = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / den;
                        double w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        double zh = w0 * a.z + w1 * b.z + w2 * c.z;
                        int k = (int)Math.Ceiling((zh - Lo.z) / H - 0.5);
                        k = k < 0 ? 0 : (k > Nz ? Nz : k);
                        int col = (i * Ny + j) * nz1;
                        diff[col]++;            // voxels below the hit see it "above" them
                        diff[col + k]--;
                    }
                }
            }
        }

        sealed class EdtBuf
        {
            public double[] f, d, z; public int[] v;
            public EdtBuf(int n) { f = new double[n]; d = new double[n]; z = new double[n + 1]; v = new int[n]; }
        }

        public void ComputeDistance(bool[] inside, double margin)
        {
            int N = Cells;
            var f = new float[N];
            for (int i = 0; i < N; i++) f[i] = inside[i] ? 1e20f : 0f;
            int nmax = Math.Max(Nx, Math.Max(Ny, Nz));
            Pass(f, Nz, 1, Nx * Ny, l => l * Nz, nmax);
            Pass(f, Ny, Nz, Nx * Nz, l => (l / Nz) * Ny * Nz + l % Nz, nmax);
            Pass(f, Nx, Ny * Nz, Ny * Nz, l => l, nmax);
            D = new float[N];
            double dmax = 0, half = 0.5 * H + margin;
            for (int i = 0; i < N; i++)
            {
                double d = Math.Sqrt(f[i]) * H - half;
                D[i] = (float)d;
                if (d > dmax) dmax = d;
            }
            Dmax = dmax;
        }

        static void Pass(float[] f, int n, int stride, int lines, Func<int, int> start, int nmax)
        {
            Par.For(lines, () => new EdtBuf(nmax), (l, b) =>
            {
                int s = start(l);
                for (int q = 0; q < n; q++) b.f[q] = f[s + q * stride];
                Dt1(b.f, n, b.d, b.v, b.z);
                for (int q = 0; q < n; q++) f[s + q * stride] = (float)b.d[q];
            });
        }

        // 1-D squared distance transform of sampled function f (lower envelope of parabolas).
        static void Dt1(double[] f, int n, double[] d, int[] v, double[] z)
        {
            int k = 0; v[0] = 0; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s;
                while (true)
                {
                    int vk = v[k];
                    s = ((f[q] + (double)q * q) - (f[vk] + (double)vk * vk)) / (2.0 * (q - vk));
                    if (s <= z[k]) { k--; continue; }
                    break;
                }
                k++; v[k] = q; z[k] = s; z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                double dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        public double Sample(V3 p)
        {
            double qx = (p.x - Lo.x) / H - 0.5, qy = (p.y - Lo.y) / H - 0.5, qz = (p.z - Lo.z) / H - 0.5;
            if (qx < 0 || qy < 0 || qz < 0 || qx > Nx - 1 || qy > Ny - 1 || qz > Nz - 1) return -1.0;
            int i = Math.Min((int)qx, Nx - 2), j = Math.Min((int)qy, Ny - 2), k = Math.Min((int)qz, Nz - 2);
            double fx = qx - i, fy = qy - j, fz = qz - k;
            int o = (i * Ny + j) * Nz + k, sy = Nz, sx = Ny * Nz;
            double c00 = D[o] + (D[o + 1] - D[o]) * fz;
            double c01 = D[o + sy] + (D[o + sy + 1] - D[o + sy]) * fz;
            double c10 = D[o + sx] + (D[o + sx + 1] - D[o + sx]) * fz;
            double c11 = D[o + sx + sy] + (D[o + sx + sy + 1] - D[o + sx + sy]) * fz;
            double c0 = c00 + (c01 - c00) * fy, c1 = c10 + (c11 - c10) * fy;
            return c0 + (c1 - c0) * fx;
        }
    }

    // ------------------------------------------------------------------------------------------ exact distance

    public sealed class TriBvh
    {
        struct Node { public V3 lo, hi; public int left, right, start, count; }   // leaf: count > 0
        readonly V3[] a, b, c;
        readonly int[] order;
        readonly List<Node> nodes = new List<Node>();
        Node[] N;
        const int Leaf = 4;

        // Over the soup's outer surface (SurfV/SurfT).
        public TriBvh(TriSoup m)
        {
            V3[] V = m.SurfV; int[] T = m.SurfT;
            int n = T.Length / 3;
            a = new V3[n]; b = new V3[n]; c = new V3[n];
            var cen = new V3[n];
            order = new int[n];
            for (int t = 0; t < n; t++)
            {
                a[t] = V[T[3 * t]]; b[t] = V[T[3 * t + 1]]; c[t] = V[T[3 * t + 2]];
                cen[t] = (a[t] + b[t] + c[t]) * (1.0 / 3.0);
                order[t] = t;
            }
            Build(cen, 0, n);
            N = nodes.ToArray();
        }

        int Build(V3[] cen, int s, int e)
        {
            int id = nodes.Count;
            nodes.Add(default(Node));
            V3 lo = a[order[s]], hi = lo, clo = cen[order[s]], chi = clo;
            for (int i = s; i < e; i++)
            {
                int t = order[i];
                lo = V3.Min(lo, V3.Min(a[t], V3.Min(b[t], c[t]))); hi = V3.Max(hi, V3.Max(a[t], V3.Max(b[t], c[t])));
                clo = V3.Min(clo, cen[t]); chi = V3.Max(chi, cen[t]);
            }
            var node = new Node { lo = lo, hi = hi };
            if (e - s <= Leaf) { node.start = s; node.count = e - s; nodes[id] = node; return id; }
            V3 ext = chi - clo;
            int axis = ext.x >= ext.y && ext.x >= ext.z ? 0 : (ext.y >= ext.z ? 1 : 2);
            Array.Sort(order, s, e - s, Comparer<int>.Create((p, q) => Comp(cen[p], axis).CompareTo(Comp(cen[q], axis))));
            int mid = (s + e) / 2;
            node.left = Build(cen, s, mid);
            node.right = Build(cen, mid, e);
            node.start = -1;
            nodes[id] = node;
            return id;
        }

        static double Comp(V3 v, int ax) => ax == 0 ? v.x : (ax == 1 ? v.y : v.z);

        static double BoxDist2(V3 p, V3 lo, V3 hi)
        {
            double dx = p.x < lo.x ? lo.x - p.x : (p.x > hi.x ? p.x - hi.x : 0);
            double dy = p.y < lo.y ? lo.y - p.y : (p.y > hi.y ? p.y - hi.y : 0);
            double dz = p.z < lo.z ? lo.z - p.z : (p.z > hi.z ? p.z - hi.z : 0);
            return dx * dx + dy * dy + dz * dz;
        }

        // Squared distance to the nearest triangle, or `bound2` if nothing is closer than that.
        public double Query(V3 p, double bound2, int[] stack)
        {
            double best = bound2;
            int sp = 0;
            stack[sp++] = 0;
            while (sp > 0)
            {
                ref Node nd = ref N[stack[--sp]];
                if (BoxDist2(p, nd.lo, nd.hi) >= best) continue;
                if (nd.count > 0)
                {
                    for (int i = nd.start; i < nd.start + nd.count; i++)
                    {
                        int t = order[i];
                        double d2 = (ClosestPt(p, a[t], b[t], c[t]) - p).Len2;
                        if (d2 < best) best = d2;
                    }
                    continue;
                }
                int l = nd.left, r = nd.right;
                double dl = BoxDist2(p, N[l].lo, N[l].hi), dr = BoxDist2(p, N[r].lo, N[r].hi);
                if (dl < dr) { if (dr < best) stack[sp++] = r; if (dl < best) stack[sp++] = l; }
                else { if (dl < best) stack[sp++] = l; if (dr < best) stack[sp++] = r; }
            }
            return best;
        }

        // The triangles with lengths along u counted s (>= 1) times over (each point v to v + (s - 1)(v.u) u): for the
        // room for a spheroid 1 / s as deep along u as across (a triangle's nearest point in that metric is its
        // stretched copy's nearest to p's), made once per axis.
        public sealed class Stretched
        {
            internal V3 U;
            internal double S;
            internal V3[] a, b, c;
            internal V3 Map(V3 v) => v + U * ((S - 1) * V3.Dot(v, U));
        }

        public Stretched Stretch(V3 u, double s)
        {
            var st = new Stretched { U = u, S = s };
            st.a = Array.ConvertAll(a, st.Map); st.b = Array.ConvertAll(b, st.Map); st.c = Array.ConvertAll(c, st.Map);
            return st;
        }

        // Query in that metric (st null: the plain one). A box culls by its plain distance (no more than its distance in
        // that metric; a tighter bound costs more than it saves).
        public double Query(V3 p, double bound2, int[] stack, Stretched st)
        {
            if (st == null) return Query(p, bound2, stack);
            double best = bound2;
            V3 q = st.Map(p);
            int sp = 0;
            stack[sp++] = 0;
            while (sp > 0)
            {
                ref Node nd = ref N[stack[--sp]];
                if (BoxDist2(p, nd.lo, nd.hi) >= best) continue;
                if (nd.count > 0)
                {
                    for (int i = nd.start; i < nd.start + nd.count; i++)
                    {
                        int t = order[i];
                        double d2 = (ClosestPt(q, st.a[t], st.b[t], st.c[t]) - q).Len2;
                        if (d2 < best) best = d2;
                    }
                    continue;
                }
                int l = nd.left, r = nd.right;
                double dl = BoxDist2(p, N[l].lo, N[l].hi), dr = BoxDist2(p, N[r].lo, N[r].hi);
                if (dl < dr) { if (dr < best) stack[sp++] = r; if (dl < best) stack[sp++] = l; }
                else { if (dl < best) stack[sp++] = l; if (dr < best) stack[sp++] = r; }
            }
            return best;
        }

        // Ericson, Real-Time Collision Detection 5.1.5
        static V3 ClosestPt(V3 p, V3 a, V3 b, V3 c)
        {
            V3 ab = b - a, ac = c - a, ap = p - a;
            double d1 = V3.Dot(ab, ap), d2 = V3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            V3 bp = p - b;
            double d3 = V3.Dot(ab, bp), d4 = V3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            V3 cp = p - c;
            double d5 = V3.Dot(ab, cp), d6 = V3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            double den = 1.0 / (va + vb + vc);
            return a + ab * (vb * den) + ac * (vc * den);
        }
    }

    // ------------------------------------------------------------------------------------------ solver

    public sealed class Timings
    {
        public double Grid, Edt, Search, FinalOnly, Polish;
        public int Cells, Lines, PolishEvals; public double H, Dmax;
    }

    public sealed class TankResult
    {
        public Capsule Voxel, Checked, Polished;     // voxel answer, voxel answer with rigorous exact r, full polish
        public Timings T = new Timings();
    }

    public sealed class Solver
    {
        public double Budget = 2.5e6, MinCellsRadius = 10, Gap = 0.02, Margin = 0;
        public int Dirs = 256, Centers = 40, Refine = 6;
        public bool Polish = false;      // full exact optimisation (slower); off = guaranteed-fit radius only
        public double MaxTaper = 0;      // the most a pill's radius may change along its axis, per unit length (0: none)
        public double Dome = 1;          // the pill's end domes' height over its radius (Capsule.E; 1: hemispheres)
        private V3? _fixedDir;           // a dome search's (DomeSearch): the one direction searched
        private bool _voxelOnly;         // ... and only the voxel search's pill wanted

        TriSoup mesh;
        Grid grid;
        TriBvh bvh;
        readonly List<Capsule> placed = new List<Capsule>();

        public Solver(TriSoup m) { mesh = m; }
        // The mesh, when it is made after the solver (on the thread that solves).
        public TriSoup Mesh { set => mesh = value; }

        // The inside's volume as the (final) grid has it, once Solve has run.
        public double VoxelVolume { get; private set; }

        // Set from any thread to stop a solve: Solve throws OperationCanceledException at its next check.
        public volatile bool Cancel;
        private Solver _outer;          // a stretched solve (SolveStretched): the solver it runs for
        private bool Cancelled => Cancel || (_outer != null && _outer.Cancel);
        private void Check() { if (Cancelled) throw new OperationCanceledException(); }

        // A part thinner than this share of its longest side is solved stretched when the grid can't resolve it.
        private const double Thin = 0.25;

        public List<TankResult> Solve(int nTanks)
        {
            var res = new List<TankResult>();
            var sw = Stopwatch.StartNew();
            mesh.Bounds(out V3 lo, out V3 hi);
            V3 ext = hi - lo;
            if (!(Math.Max(ext.x, Math.Max(ext.y, ext.z)) > 1e-6)) return res;    // a point (or NaN): nothing to fill
            double h = CellSize(ext, Budget);
            grid = new Grid(mesh, h);
            double tGrid = sw.Elapsed.TotalSeconds; sw.Restart();
            Check();
            grid.ComputeDistance(grid.Inside, Margin);
            double tEdt = sw.Elapsed.TotalSeconds; sw.Restart();
            Check();
            if (grid.Dmax / h < MinCellsRadius)               // too coarse for this thickness: refine (cap 3x budget)
            {
                double h2 = Math.Max(grid.Dmax / MinCellsRadius, CellSize(ext, 3 * Budget));
                if (h2 < 0.95 * h)
                {
                    h = h2;
                    grid = new Grid(mesh, h);
                    tGrid += sw.Elapsed.TotalSeconds; sw.Restart();
                    grid.ComputeDistance(grid.Inside, Margin);
                    tEdt += sw.Elapsed.TotalSeconds; sw.Restart();
                }
            }
            Check();
            bvh = _voxelOnly ? null : new TriBvh(mesh);
            tGrid += sw.Elapsed.TotalSeconds; sw.Restart();
            // thinner than the grid can resolve (a thin wing: its thickness a few cells or none): search it stretched
            if (grid.Dmax <= 2 * grid.H && _outer == null && Stretch(ext, out V3 s))
                return SolveStretched(nTanks, s);
            int count = 0;
            foreach (bool b in grid.Inside) if (b) count++;
            VoxelVolume = count * grid.H * grid.H * grid.H;
            bool[] inside = nTanks > 1 ? (bool[])grid.Inside.Clone() : grid.Inside;
            for (int n = 0; n < nTanks; n++)
            {
                Check();
                var tr = new TankResult();
                if (n > 0)
                {
                    sw.Restart();
                    grid.ComputeDistance(inside, Margin);
                    tr.T.Edt = sw.Elapsed.TotalSeconds;
                }
                else { tr.T.Grid = tGrid; tr.T.Edt = tEdt; }
                tr.T.Cells = grid.Cells; tr.T.H = grid.H; tr.T.Dmax = grid.Dmax;
                if (grid.Dmax <= 2 * grid.H) break;

                sw.Restart();
                tr.Voxel = VoxelSearch(out tr.T.Lines);
                tr.T.Search = sw.Elapsed.TotalSeconds;
                if (_voxelOnly) { res.Add(tr); break; }

                Check();
                sw.Restart();
                double r0 = Math.Max(tr.Voxel.R, tr.Voxel.R2);
                var stack = new int[256];
                double fine = Math.Min(0.002, r0 / 100);
                tr.Checked = ExactFit(tr.Voxel.A, tr.Voxel.B, fine, stack, MaxTaper, Reach(r0));
                if (Dome < 1 && placed.Count == 0 && DomeSearch(tr.Voxel, stack) is Capsule dc && dc.Volume > tr.Checked.Volume)
                    tr.Checked = dc;
                tr.T.FinalOnly = sw.Elapsed.TotalSeconds;

                if (Polish)
                {
                    sw.Restart();
                    tr.Polished = ExactPolish(tr.Voxel, out tr.T.PolishEvals);
                    tr.T.Polish = sw.Elapsed.TotalSeconds;
                }
                var best = tr.Polished ?? tr.Checked;
                res.Add(tr);
                placed.Add(new Capsule { A = best.A, B = best.B, R = best.R + Gap, R2 = best.R2 + Gap });
                if (n == nTanks - 1) break;

                // carve: voxels within r+gap of the axis are no longer free (the wider end's r: a tapered one's too)
                var g = grid; var A = best.A; var B = best.B; double rr = Math.Max(best.R, best.R2) + Gap + 0.5 * g.H;
                Par.For(g.Cells, () => 0, (i, _) => { if (inside[i] && SegDist(g.Center(i), A, B) < rr) inside[i] = false; });
            }
            return res;
        }

        // How far an exact fit may move the ends of an axis the voxel search found, on a pill r round: the search's ends
        // are a few cells off either way, and flatter domes let the axis run on past where a round end would touch.
        static double Reach(double r) => 0.5 * r;

        // Flatter domes (Dome < 1). The pill, on the part stretched 1 / Dome times along its axis, is a round one: the
        // round search runs again on the part so stretched, along that direction only, for each of the round search's
        // axis and the hints (a stack part's long axis: a squat part's domes stand on its ends, where the round search
        // finds a ball, any way round). The best of those, fitted exactly on the part itself; null if none fits.
        Capsule DomeSearch(Capsule round, int[] stack)
        {
            var dirs = new List<V3> { (round.B - round.A).Norm };     // (a ball's too: VoxelSearch keeps its line's)
            foreach (V3 hnt in mesh.Hints)
            {
                V3 h = hnt.Norm;
                if (!dirs.Exists(d => Math.Abs(V3.Dot(d, h)) > 0.995)) dirs.Add(h);
            }
            Capsule best = null;
            double s = 1 / Dome;
            foreach (V3 u in dirs)
            {
                Check();
                Func<V3, V3> st = v => v + u * ((s - 1) * V3.Dot(v, u));
                var sm = new TriSoup(mesh.V.Select(st).ToArray(), mesh.T)
                {
                    PieceStart = mesh.PieceStart,
                    SV = mesh.SV?.Select(st).ToArray(),
                    ST = mesh.ST,
                    ExactVolume = mesh.ExactVolume,
                };
                var inner = new Solver(sm)
                {
                    Budget = 0.5 * Budget, MinCellsRadius = MinCellsRadius, Gap = Gap, Margin = Margin,
                    Centers = Centers, Refine = Refine, MaxTaper = MaxTaper * Dome,     // (a slope per length stretched)
                    _fixedDir = u, _voxelOnly = true, _outer = this,
                };
                List<TankResult> found = inner.Solve(1);
                if (found.Count == 0) continue;
                Capsule v = found[0].Voxel;
                Func<V3, V3> back = p => p + u * ((Dome - 1) * V3.Dot(p, u));
                double rv = Math.Max(v.R, v.R2);
                Capsule c = ExactFit(back(v.A), back(v.B), Math.Min(0.002, rv / 100), stack, MaxTaper, Reach(rv), u);
                if (Math.Min(c.R, c.R2) > 0 && (best == null || c.Volume > best.Volume)) best = c;
            }
            return best;
        }

        // Per-axis stretch that brings each side to at least Thin of the longest; false when none is thin.
        static bool Stretch(V3 ext, out V3 s)
        {
            double max = Math.Max(ext.x, Math.Max(ext.y, ext.z)), want = Thin * max;
            Func<double, double> f = e => e >= want ? 1 : want / Math.Max(e, 1e-9 * max);
            s = new V3(f(ext.x), f(ext.y), f(ext.z));
            return s.x > 1 || s.y > 1 || s.z > 1;
        }

        static V3 Mul(V3 a, V3 s) => new V3(a.x * s.x, a.y * s.y, a.z * s.z);
        static V3 Div(V3 a, V3 s) => new V3(a.x / s.x, a.y / s.y, a.z / s.z);

        // The axes searched on the part stretched (a linear map keeps a segment inside the part inside it, both ways),
        // then mapped back, each given its exact radius in the part itself. The search's capsule isn't the part's (its
        // round section stretched), but the axis it finds runs along the part's thickest stretch.
        List<TankResult> SolveStretched(int nTanks, V3 s)
        {
            var sm = new TriSoup(mesh.V.Select(v => Mul(v, s)).ToArray(), mesh.T)
            {
                PieceStart = mesh.PieceStart,
                SV = mesh.SV?.Select(v => Mul(v, s)).ToArray(),
                ST = mesh.ST,
                ExactVolume = mesh.ExactVolume,
            };
            foreach (V3 hnt in mesh.Hints) sm.Hints.Add(Mul(hnt, s).Norm);
            var inner = new Solver(sm)
            {
                Budget = Budget, MinCellsRadius = MinCellsRadius, Gap = Gap, Margin = Margin,
                Dirs = Dirs, Centers = Centers, Refine = Refine, Polish = false, _outer = this,
                MaxTaper = 0,       // (a stretched taper isn't one: the axis only, its taper fitted below)
            };
            List<TankResult> found = inner.Solve(nTanks);
            VoxelVolume = inner.VoxelVolume / (s.x * s.y * s.z);
            var res = new List<TankResult>();
            var stack = new int[256];
            double shrink = Math.Max(s.x, Math.Max(s.y, s.z));      // the search's radius, at most this much too big
            foreach (TankResult t in found)
            {
                Check();
                V3 A = Div(t.Voxel.A, s), B = Div(t.Voxel.B, s);
                double fine = Math.Max(2e-5, Math.Min(0.002, t.Voxel.R / shrink / 100));
                Capsule c = ExactFit(A, B, fine, stack, MaxTaper, Reach(t.Voxel.R / shrink));
                double r = Math.Min(c.R, c.R2);
                if (!(r > 0)) break;
                // the stretched search's round ends were fat: the axis runs on, at about this radius, to the part's
                // ends (kept only if that's the bigger pill)
                V3 A2 = A, B2 = B;
                Extend(ref A2, ref B2, r, fine, stack);
                Extend(ref B2, ref A2, r, fine, stack);
                Capsule c2 = ExactFit(A2, B2, fine, stack, MaxTaper, Reach(r));
                if (Math.Min(c2.R, c2.R2) > 0 && c2.Volume > c.Volume) c = c2;
                double rv = t.Voxel.R / shrink;
                res.Add(new TankResult { Voxel = new Capsule { A = A, B = B, R = rv, R2 = rv }, Checked = c, T = t.T });
                placed.Add(new Capsule { A = c.A, B = c.B, R = c.R + Gap, R2 = c.R2 + Gap });     // the one kept (maybe stretched)
            }
            return res;
        }

        // Moves `end` on along the axis (away from `other`) while the room around it stays near r (within a few
        // percent: the caller measures the radius again over the whole axis). Distance is 1-Lipschitz, so samples
        // `st` apart at least `need` from the skin leave every point between at least need - st/2.
        void Extend(ref V3 end, ref V3 other, double r, double fine, int[] stack)
        {
            V3 u = end - other;
            double len = u.Len;
            if (!(len > 0)) return;
            u = u * (1 / len);
            double st = Math.Max(0.02 * r, fine), need = 0.97 * r + 0.5 * st, max = grid.Diag;
            V3 e = end;
            double d = 0;
            for (int i = 1; i * st <= max; i++)
            {
                if ((i & 1023) == 0) Check();
                V3 p = e + u * (i * st);
                double ob = double.PositiveInfinity;
                foreach (var o in placed) ob = Math.Min(ob, Clear(p, o));
                if (ob < need || Math.Sqrt(bvh.Query(p, need * need, stack)) < need) break;
                d = i * st;
            }
            end = e + u * d;
        }

        // The smallest cell size whose grid (the bounds plus Grid's padding, rounded up) has at most `cells` cells. Flat
        // or thin bounds would otherwise make the padding most of the grid (or the size zero).
        static double CellSize(V3 ext, double cells, int pad = 3)
        {
            double n = 2 * pad + 2;      // padding both sides, plus the jitter and rounding up
            Func<double, double> count = h => (ext.x / h + n) * (ext.y / h + n) * (ext.z / h + n);
            double hi = Math.Max(ext.x, Math.Max(ext.y, ext.z));
            if (count(hi) > cells) return hi;      // can't be met: the coarsest grid that still spans the part
            double lo = hi;
            while (count(lo) <= cells) lo *= 0.5;
            for (int i = 0; i < 50; i++) { double m = 0.5 * (lo + hi); if (count(m) <= cells) hi = m; else lo = m; }
            return hi;
        }

        // ---------------------------------------------------------------- line search

        sealed class LineBuf
        {
            public double[] R, L;
            public LineBuf(int n) { R = new double[n + 1]; L = new double[n + 1]; }
        }

        // Running minima outward from c of the room past a radius growing k per unit length along u (the room left
        // for the radius at c).
        int March(V3 c, V3 u, double step, int nmax, double d0, double k, double[] arr)
        {
            arr[0] = d0;
            double m = d0;
            int n = 1;
            for (int i = 1; i <= nmax; i++)
            {
                double d = grid.Sample(c + u * (i * step)) - k * (i * step);
                if (d < m) m = d;
                if (m <= 0) break;           // running min only falls: no positive radius past here
                arr[n++] = m;
            }
            return n;
        }

        // Best pill on line (c, u) whose radius grows k per unit length along u: returns volume (or -1), the radius at
        // each end (rL at c - u tL, rR at c + u tR) and the axis extents tL / tR from c.
        double EvalLine(V3 c, V3 u, double k, double step, int nmax, LineBuf buf, out double rL, out double rR, out double tL, out double tR)
        {
            rL = rR = tL = tR = 0;
            double d0 = grid.Sample(c);
            if (d0 <= 0) return -1;
            int nR = March(c, u, step, nmax, d0, k, buf.R);
            int nL = March(c, -u, step, nmax, d0, -k, buf.L);
            double best = -1;
            Scan(buf.R, nR, buf.L, nL, step, k, false, ref best, ref rL, ref rR, ref tL, ref tR);
            Scan(buf.L, nL, buf.R, nR, step, -k, true, ref best, ref rL, ref rR, ref tL, ref tR);
            return best;
        }

        // Candidate radii at c from side P (non-increasing running minima); count of side Q >= r by pointer. kP: the
        // slope towards P (a side whose end would narrow past nothing is cut back to a point there).
        static void Scan(double[] P, int nP, double[] Q, int nQ, double step, double kP, bool swapped,
                         ref double best, ref double rL, ref double rR, ref double tL, ref double tR)
        {
            int j = 0;
            for (int i = 0; i < nP; i++)
            {
                double rr = P[i];
                while (j < nQ && Q[j] >= rr) j++;
                double tP = step * i, tQ = step * (j - 1);
                if (kP < 0) tP = Math.Min(tP, rr / -kP);
                if (kP > 0) tQ = Math.Min(tQ, rr / kP);
                double rP = rr + kP * tP, rQ = rr - kP * tQ;
                double v = Capsule.Vol(rP, rQ, tP + tQ);
                if (v > best)
                {
                    best = v;
                    if (swapped) { tL = tP; rL = rP; tR = tQ; rR = rQ; } else { tR = tP; rR = rP; tL = tQ; rL = rQ; }
                }
            }
        }

        static V3 Dir(double th, double ph) => new V3(Math.Sin(th) * Math.Cos(ph), Math.Sin(th) * Math.Sin(ph), Math.Cos(th));

        List<V3> SeedCenters(int k)
        {
            var g = grid;
            double thr = 0.3 * g.Dmax;
            var cand = new List<KeyValuePair<float, int>>();
            var locks = new object();
            Par.For(g.Nx - 2, () => new List<KeyValuePair<float, int>>(), (ii, local) =>
            {
                int i = ii + 1;
                local.Clear();
                for (int j = 1; j < g.Ny - 1; j++)
                    for (int kk = 1; kk < g.Nz - 1; kk++)
                    {
                        int o = g.Idx(i, j, kk);
                        float d = g.D[o];
                        if (d <= thr) continue;
                        bool max = true;
                        for (int di = -1; di <= 1 && max; di++)
                            for (int dj = -1; dj <= 1 && max; dj++)
                                for (int dk = -1; dk <= 1; dk++)
                                    if (g.D[o + (di * g.Ny + dj) * g.Nz + dk] > d) { max = false; break; }
                        if (max) local.Add(new KeyValuePair<float, int>(d, o));
                    }
                if (local.Count > 0) lock (locks) cand.AddRange(local);
            });
            cand.Sort((p, q) => q.Key != p.Key ? q.Key.CompareTo(p.Key) : p.Value.CompareTo(q.Value));
            double spacing = g.Diag / 15;
            var kept = new List<V3>();
            for (int i = 0; i < cand.Count && i < 6000 && kept.Count < k; i++)
            {
                V3 p = g.Center(cand[i].Value);
                bool ok = true;
                foreach (var q in kept) if ((p - q).Len <= spacing) { ok = false; break; }
                if (ok) kept.Add(p);
            }
            return kept;
        }

        List<V3> Directions()
        {
            var U = new List<V3>();
            if (_fixedDir is V3 only) { U.Add(only); return U; }
            for (int i = 0; i < Dirs; i++)
            {
                double z = (i + 0.5) / Dirs, ph = Math.PI * (1 + Math.Sqrt(5)) * (i + 0.5), s = Math.Sqrt(1 - z * z);
                U.Add(new V3(s * Math.Cos(ph), s * Math.Sin(ph), z));
            }
            U.AddRange(PcaDirs());
            foreach (var hnt in mesh.Hints) U.Add(hnt.Norm);
            return U;
        }

        IEnumerable<V3> PcaDirs()
        {
            var g = grid;
            double sw = 0; V3 mu = default(V3);
            for (int o = 0; o < g.Cells; o++) { double w = g.D[o]; if (w <= 0) continue; w *= w; sw += w; mu = mu + g.Center(o) * w; }
            mu = mu * (1 / sw);
            var C = new double[3, 3];
            for (int o = 0; o < g.Cells; o++)
            {
                double w = g.D[o]; if (w <= 0) continue; w *= w;
                V3 d = g.Center(o) - mu;
                double[] v = { d.x, d.y, d.z };
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) C[i, j] += w * v[i] * v[j];
            }
            var E = Jacobi(C);
            for (int i = 0; i < 3; i++) yield return new V3(E[0, i], E[1, i], E[2, i]);
        }

        static double[,] Jacobi(double[,] A)
        {
            var a = (double[,])A.Clone();
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off < 1e-15 * (Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]) + 1e-300)) break;
                for (int p = 0; p < 2; p++)
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-300) continue;
                        double th = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
                        double c = Math.Cos(th), s = Math.Sin(th);
                        for (int k = 0; k < 3; k++)
                        {
                            double akp = a[k, p], akq = a[k, q];
                            a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double apk = a[p, k], aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double vkp = v[k, p], vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq;
                        }
                    }
            }
            return v;
        }

        Capsule VoxelSearch(out int nLines)
        {
            // a march's reach: the whole part from a centre at one end of it (the fattest spot, where the centres are
            // seeded, can be at an end: a cone's base). A march stops where it leaves the part.
            double step = grid.H;
            int nmax = (int)Math.Ceiling(grid.Diag / step);
            var C = SeedCenters(Centers);
            var U = Directions();
            int nu = U.Count;
            nLines = C.Count * nu;
            var Vb = new double[nLines];
            Par.For(nLines, () => new LineBuf(nmax), (li, buf) =>
                Vb[li] = EvalLine(C[li / nu], U[li % nu], 0, step, nmax, buf, out _, out _, out _, out _));
            var idx = new int[nLines];
            for (int i = 0; i < nLines; i++) idx[i] = i;
            Array.Sort((double[])Vb.Clone(), idx);
            Array.Reverse(idx);

            // the best lines, one per line: a parallel line through a nearby centre is the same line (a long part's
            // centres all lie on it, and would fill every refine slot)
            var seeds = new List<double[]>();
            var tried = new List<int>();
            double near = grid.Diag / 15;
            foreach (int li in idx)
            {
                int ci = li / nu; V3 u = U[li % nu];
                bool dup = false;
                foreach (int t in tried)
                {
                    V3 ut = U[t % nu];
                    if (Math.Abs(V3.Dot(ut, u)) <= 0.98) continue;
                    V3 w = C[ci] - C[t / nu];
                    if ((w - ut * V3.Dot(w, ut)).Len < near) { dup = true; break; }
                }
                if (dup) continue;
                tried.Add(li);
                V3 c = C[ci];
                seeds.Add(new[] { c.x, c.y, c.z, Math.Acos(Math.Max(-1, Math.Min(1, u.z))), Math.Atan2(u.y, u.x), 0 });
                if (seeds.Count >= Refine) break;
            }

            // refined: the line (its centre only, the direction fixed), and (a taper allowed) its slope, from none. A
            // seed is (centre, theta, phi, slope); the simplex moves the free ones of those.
            Check();
            var free = new List<int> { 0, 1, 2 };
            if (_fixedDir == null) { free.Add(3); free.Add(4); }
            if (MaxTaper > 0) free.Add(5);
            int dims = free.Count;
            Func<double[], double> Slope = x => MaxTaper > 0 ? Math.Max(-MaxTaper, Math.Min(MaxTaper, x[5])) : 0;
            var results = new double[seeds.Count][];
            var fvals = new double[seeds.Count];
            Par.For(seeds.Count, () => new LineBuf(nmax), (si, buf) =>
            {
                double[] seed = seeds[si];
                Func<double[], double[]> Full = x => { var y = (double[])seed.Clone(); for (int i = 0; i < dims; i++) y[free[i]] = x[i]; return y; };
                Func<double[], double> fy = y => -EvalLine(new V3(y[0], y[1], y[2]), Dir(y[3], y[4]), Slope(y), step, nmax, buf, out _, out _, out _, out _);
                // the slope's start: the best of a sweep on the seed's line (the volume is flat in it near none, a
                // simplex from there stalls short of a long taper)
                if (MaxTaper > 0)
                {
                    double fb = fy(seed), k0 = 0;
                    for (int i = 0; i <= 16; i++)
                    {
                        seed[5] = MaxTaper * (i / 8.0 - 1);
                        double fk = fy(seed);
                        if (fk < fb) { fb = fk; k0 = seed[5]; }
                    }
                    seed[5] = k0;
                }
                var x0 = free.Select(i => seed[i]).ToArray();
                var simplex = new double[dims + 1][];
                for (int k = 0; k <= dims; k++)
                {
                    simplex[k] = (double[])x0.Clone();
                    if (k > 0) { int p = free[k - 1]; simplex[k][k - 1] += p < 3 ? 2 * grid.H : (p < 5 ? 0.06 : 0.5 * MaxTaper); }
                }
                results[si] = Full(NelderMead(x => fy(Full(x)), simplex, 500, 1e-4, 1e-6, out fvals[si], out _));
            });
            int bi = 0;
            for (int i = 1; i < fvals.Length; i++) if (fvals[i] < fvals[bi]) bi = i;
            var xb = results[bi];
            V3 cc = new V3(xb[0], xb[1], xb[2]), uu = Dir(xb[3], xb[4]);
            EvalLine(cc, uu, Slope(xb), step, nmax, new LineBuf(nmax), out double rL, out double rR, out double tL, out double tR);
            // (a ball keeps its line's direction: the flatter domes' fit goes by it)
            if (tL + tR < 1e-6 * grid.H) tL = tR = 1e-6 * grid.H;
            return new Capsule { A = cc - uu * tL, B = cc + uu * tR, R = rL, R2 = rR };
        }

        // ---------------------------------------------------------------- exact

        public static double SegDist(V3 p, V3 a, V3 b)
        {
            V3 ab = b - a;
            double t = V3.Dot(p - a, ab) / Math.Max(ab.Len2, 1e-30);
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            return (p - (a + ab * t)).Len;
        }

        // Room around p left by a tank already placed: at least this (a tapered one taken at its wider end).
        static double Clear(V3 p, Capsule o) => SegDist(p, o.A, o.B) - Math.Max(o.R, o.R2);

        // The pill on axis AB sure to fit with the most volume, its radius going from r (at A) by slope k (|k| <=
        // maxTaper): the room d_i at points s_i along the axis (exact distance to the skin, and to tanks placed; for
        // flatter domes, the spheroids' room: lengths along the axis counted 1 / Dome times over), then
        // r(k) = min(d_i - k s_i) - (1 / Dome + |k|) h/2 (d - k s is that Lipschitz along the axis, the points h apart)
        // and the k with the most volume. reach: how far either end may move, in or out, for more volume (each end on
        // the points within that of it; 0: A and B as they are). dir: the axis's direction (else B - A's, which a
        // pill of no length lacks). R <= 0: nothing fits.
        Capsule ExactFit(V3 A, V3 B, double step, int[] stack, double maxTaper, double reach = 0, V3? dir = null)
        {
            double L = (B - A).Len, e = Dome, lip = 1 / e;
            V3 u = dir ?? (L > 0 ? (B - A) * (1 / L) : new V3(0, 1, 0));
            double wIn = Math.Min(reach, 0.45 * L), s0 = -reach, Lt = L + 2 * reach;
            int m = Math.Max(2, (int)Math.Ceiling(Lt / step) + 1);
            double h = Lt / (m - 1);
            // the points [lo, hi] are always in the pill; its start is one of the points up to lo, its end one from hi
            int lo = Math.Min(m - 1, (int)Math.Round((reach + wIn) / h)), hi = Math.Max(lo, Math.Min(m - 1, (int)Math.Round((reach + L - wIn) / h)));
            var d = new double[m];
            TriBvh.Stretched sq = e < 1 ? bvh.Stretch(u, lip) : null;
            // a point whose room is past `bound` can't be the tightest for any slope allowed (some point before it, and
            // in the pill whenever it is, is tighter for all of them): its query stops there (and the room taken as
            // `bound`, which changes nothing). (The start's points: each queried in full.)
            double lead = double.PositiveInfinity;      // min over the points from lo so far of d_j - maxTaper s_j
            for (int i = 0; i < m; i++)
            {
                if ((i & 255) == 255) Check();     // a long axis at a fine step: many queries
                double s = s0 + h * i;
                V3 p = A + u * s;
                double ob = i > lo ? lead + maxTaper * s : double.PositiveInfinity;
                foreach (var o in placed) ob = Math.Min(ob, Clear(p, o));      // (a plain distance: no more than the room)
                d[i] = ob > 0 ? Math.Sqrt(bvh.Query(p, ob * ob, stack, sq)) : ob;
                if (i >= lo) lead = Math.Min(lead, d[i] - maxTaper * s);
            }

            // the ends tried: every few points (some 33 each), the outermost too
            var starts = new List<int>();
            for (int i = lo, st = Math.Max(1, lo / 32); i >= 0; i -= st) starts.Add(i);
            if (starts[starts.Count - 1] != 0) starts.Add(0);
            var ends = new List<int>();
            for (int i = hi, st = Math.Max(1, (m - 1 - hi) / 32); i < m; i += st) ends.Add(i);
            if (ends[ends.Count - 1] != m - 1) ends.Add(m - 1);
            var left = new double[starts.Count];
            var right = new double[ends.Count];
            int bs = lo, be = hi;
            double br = 0;
            // the most volume with slope k (-1: none), its ends (bs, be) and r at s = 0 (br)
            Func<double, double> Fit = k =>
            {
                double core = double.PositiveInfinity;
                for (int i = lo; i <= hi; i++) core = Math.Min(core, d[i] - k * (s0 + h * i));
                double run = double.PositiveInfinity;
                for (int i = lo, c = 0; i >= 0 && c < starts.Count; i--)
                {
                    if (i < lo) run = Math.Min(run, d[i] - k * (s0 + h * i));
                    if (starts[c] == i) left[c++] = run;
                }
                run = double.PositiveInfinity;
                for (int i = hi, c = 0; i < m && c < ends.Count; i++)
                {
                    if (i > hi) run = Math.Min(run, d[i] - k * (s0 + h * i));
                    if (ends[c] == i) right[c++] = run;
                }
                double lips = (lip + Math.Abs(k)) * 0.5 * h + Margin, best = -1;
                for (int a = 0; a < starts.Count; a++)
                {
                    double sa = s0 + h * starts[a], ma = Math.Min(core, left[a]);
                    for (int b = 0; b < ends.Count; b++)
                    {
                        double sb = s0 + h * ends[b], r = Math.Min(ma, right[b]) - lips, r1 = r + k * sa, r2 = r + k * sb;
                        if (!(r1 > 0 && r2 > 0)) continue;
                        double v = Capsule.Vol(r1, r2, sb - sa, e);
                        if (v > best) { best = v; bs = starts[a]; be = ends[b]; br = r; }
                    }
                }
                return best;
            };
            double kb = 0, vb = Fit(0);
            if (maxTaper > 0)
            {
                // a coarse sweep, then a golden-section search around the best
                const int n = 32;
                double dk = 2 * maxTaper / n;
                for (int i = 0; i <= n; i++)
                {
                    double k = -maxTaper + dk * i, v = Fit(k);
                    if (v > vb) { vb = v; kb = k; }
                }
                double klo = Math.Max(-maxTaper, kb - dk), khi = Math.Min(maxTaper, kb + dk);
                const double g = 0.6180339887498949;
                double x1 = khi - g * (khi - klo), x2 = klo + g * (khi - klo), v1 = Fit(x1), v2 = Fit(x2);
                for (int it = 0; it < 40 && khi - klo > 1e-6 * maxTaper; it++)
                {
                    if (v1 < v2) { klo = x1; x1 = x2; v1 = v2; x2 = klo + g * (khi - klo); v2 = Fit(x2); }
                    else { khi = x2; x2 = x1; v2 = v1; x1 = khi - g * (khi - klo); v1 = Fit(x1); }
                }
                if (v1 > vb) { vb = v1; kb = x1; }
                if (v2 > vb) { vb = v2; kb = x2; }
                // a taper that hardly adds (a cylinder's, from an axis a little off centre): a plain pill
                if (Fit(0) >= 0.99 * vb) kb = 0;
            }
            if (Fit(kb) < 0) return new Capsule { A = A, B = B, R = -1, R2 = -1, E = e };
            double sA = s0 + h * bs, sB = s0 + h * be;
            return new Capsule { A = A + u * sA, B = A + u * sB, R = br + kb * sA, R2 = br + kb * sB, E = e };
        }

        Capsule ExactPolish(Capsule v, out int evals)
        {
            double r0 = Math.Max(v.R, v.R2), coarse = Math.Max(r0 / 6, grid.H / 2);
            var stack = new int[256];
            double H = grid.H;
            Func<double[], double> f = x =>
            {
                V3 a = new V3(x[0], x[1], x[2]), b = new V3(x[3], x[4], x[5]);
                if (Math.Min(grid.Sample(a), grid.Sample(b)) < -H) return 0;   // endpoint left the shape
                Capsule c = ExactFit(a, b, coarse, stack, MaxTaper);
                return Math.Min(c.R, c.R2) > 0 ? -c.Volume : 0;
            };
            var x0 = new[] { v.A.x, v.A.y, v.A.z, v.B.x, v.B.y, v.B.z };
            var simplex = new double[7][];
            for (int k = 0; k < 7; k++) { simplex[k] = (double[])x0.Clone(); if (k > 0) simplex[k][k - 1] += 0.3 * r0; }
            var xb = NelderMead(f, simplex, 600, 1e-5, 1e-7, out double fb, out evals);
            if (!(fb < f(x0))) xb = x0;
            V3 A = new V3(xb[0], xb[1], xb[2]), B = new V3(xb[3], xb[4], xb[5]);
            return ExactFit(A, B, Math.Min(0.002, r0 / 100), stack, MaxTaper, Reach(r0));
        }

        // ---------------------------------------------------------------- Nelder-Mead (scipy defaults, non-adaptive)

        static double[] NelderMead(Func<double[], double> f, double[][] sim, int maxiter, double xatol, double fatol,
                                   out double fbest, out int nfev)
        {
            const double rho = 1, chi = 2, psi = 0.5, sigma = 0.5;
            int n = sim.Length - 1;
            var fs = new double[n + 1];
            nfev = 0;
            for (int i = 0; i <= n; i++) { fs[i] = f(sim[i]); nfev++; }
            var ord = new int[n + 1];
            Action sort = () =>
            {
                for (int i = 0; i <= n; i++) ord[i] = i;
                var key = (double[])fs.Clone();
                Array.Sort(key, ord);
                var s2 = new double[n + 1][]; var f2 = new double[n + 1];
                for (int i = 0; i <= n; i++) { s2[i] = sim[ord[i]]; f2[i] = fs[ord[i]]; }
                Array.Copy(s2, sim, n + 1); Array.Copy(f2, fs, n + 1);
            };
            sort();
            var xbar = new double[n];
            Func<double, double[]> along = coef => { var x = new double[n]; for (int k = 0; k < n; k++) x[k] = (1 + coef) * xbar[k] - coef * sim[n][k]; return x; };
            for (int it = 0; it < maxiter; it++)
            {
                double dx = 0, df = 0;
                for (int i = 1; i <= n; i++)
                {
                    df = Math.Max(df, Math.Abs(fs[0] - fs[i]));
                    for (int k = 0; k < n; k++) dx = Math.Max(dx, Math.Abs(sim[i][k] - sim[0][k]));
                }
                if (dx <= xatol && df <= fatol) break;
                for (int k = 0; k < n; k++) { double s = 0; for (int i = 0; i < n; i++) s += sim[i][k]; xbar[k] = s / n; }
                var xr = along(rho); double fxr = f(xr); nfev++;
                bool shrink = false;
                if (fxr < fs[0])
                {
                    var xe = along(rho * chi); double fxe = f(xe); nfev++;
                    if (fxe < fxr) { sim[n] = xe; fs[n] = fxe; } else { sim[n] = xr; fs[n] = fxr; }
                }
                else if (fxr < fs[n - 1]) { sim[n] = xr; fs[n] = fxr; }
                else if (fxr < fs[n])
                {
                    var xc = along(psi * rho); double fxc = f(xc); nfev++;
                    if (fxc <= fxr) { sim[n] = xc; fs[n] = fxc; } else shrink = true;
                }
                else
                {
                    var xcc = along(-psi); double fxcc = f(xcc); nfev++;
                    if (fxcc < fs[n]) { sim[n] = xcc; fs[n] = fxcc; } else shrink = true;
                }
                if (shrink)
                    for (int i = 1; i <= n; i++)
                    {
                        for (int k = 0; k < n; k++) sim[i][k] = sim[0][k] + sigma * (sim[i][k] - sim[0][k]);
                        fs[i] = f(sim[i]); nfev++;
                    }
                sort();
            }
            fbest = fs[0];
            return sim[0];
        }
    }
}
