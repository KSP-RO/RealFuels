using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RealFuels.Tanks.PillTank
{
    // The largest pill (capsule) pressure tank that fits inside a part: the part's skin (PillSkin) is read on the main
    // thread once it has held still for a moment, solved on a worker thread (PillSolver: voxel search, then the
    // guaranteed-fit exact radius), and optionally drawn as a translucent capsule through the skin. While the skin
    // changes the last pill stays up, faded, until the new one is in. The capsule is an object of its own that follows
    // the part, not a child of it: KSP clones a part's whole hierarchy (copies, symmetry), and a clone would keep it.
    public sealed class PillPreview : IDisposable
    {
        private const string ModTag = "[PillTank]";
        private const float Settle = 0.3f;              // s the skin must hold still before a solve
        private const float Alpha = 0.55f, StaleAlpha = 0.18f;
        private static readonly Color Tint = new Color(1f, 0.55f, 0.2f);
        private const int SigError = 0x5ca1ab1e;        // the signature when the skin can't be read

        private readonly Part _part;
        private readonly PillSkin _skin;
        private int _want = int.MinValue, _solved = int.MinValue, _running, _extra;
        private bool _any, _idle;
        // A saved fit (a craft loaded, a part copied, an undo) holds until the skin's content first changes: its solve
        // runs, but isn't Pending (the launch lock's) meanwhile.
        private bool _saved;
        private int _savedSkin;
        private string _logged;
        private static int _readFrame = -1;
        private static Task<PillFit> _busy;   // the solve under way that others wait for
        private float _changedAt;
        private Task<PillFit> _task;
        private Solver _solver;
        private GameObject _go;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _props;
        private static Material _mat;

        public PillFit Last { get; private set; }
        // Other parts whose skins join this one's (a stack of tanks sharing one pill), in a fixed order; empty: none.
        public IList<Part> Others { set => _skin.Others = value; }
        // The pill's end domes' height over its radius (Solver.Dome; the tank type's). Set before Tick; a change is the
        // caller's to fold into `extra`.
        public double Dome
        {
            get => _dome;
            set
            {
                if (value == _dome) return;
                if (_any) _saved = false;     // a saved fit was for the old domes: the new one is pending
                _dome = value;
            }
        }
        private double _dome = 1;
        public bool Drawn => _go != null && _go.activeSelf;
        public bool Pending => _solved != _want && !_saved;
        public event Action Changed;               // a new result landed

        // saved: the part already has its fit for the skin it has now (saved with it)
        public PillPreview(Part part, string skinTransforms, bool saved = false)
        {
            _part = part;
            _skin = new PillSkin(part, skinTransforms);
            _saved = saved;
        }

        // Call every frame. solve: keep the fit current; draw: show it (implies solve); focus: the part being edited
        // (its skin checked every frame, its solve ahead of the others). `extra` is folded into the skin's signature
        // (bump it to force a new solve).
        public void Tick(bool solve, bool draw, bool focus = false, int extra = 0)
        {
            if (!solve && !draw) { SetVisible(false); _idle = true; return; }
            float now = Time.realtimeSinceStartup;
            // the skin's signature: every frame on the part being edited, back from idle, or when `extra` moves (the part
            // says it changed), else every 10th (so an unannounced change can take up to 10 frames to show in Pending)
            if (focus || _idle || !_any || extra != _extra || Time.frameCount % 10 == 0)
            {
                _idle = false;
                _extra = extra;
                int sig, content;
                try { sig = unchecked(_skin.Signature(out content) * 397 + extra); }
                catch (Exception e) { Report("reading the skin", e); sig = content = SigError; }
                if (!_any) _savedSkin = content;
                else if (content != _savedSkin) _saved = false;
                if (!_any || sig != _want) { _want = sig; _changedAt = now; _any = true; }
            }

            if (_task != null && _task.IsCompleted)
            {
                PillFit r = _task.IsFaulted ? new PillFit { Error = _task.Exception?.GetBaseException().Message } : _task.Result;
                if (_running == _want && !r.Cancelled)     // (cancelled: started again below)
                {
                    if (r.Error != null) Debug.Log($"{ModTag} {_part.partInfo?.name}: {r.Error}");
                    Land(r, _running, draw);
                }
                _task = null;
                _solver = null;
            }
            // the skin changed under a running solve: stop it (its answer would be thrown away)
            else if (_task != null && _running != _want && _solver != null) _solver.Cancel = true;
            // a held mouse button is a drag (gizmo or PAW slider) still going: wait for the release. One solve at a time
            // among all parts (a craft loading with many pill tanks would otherwise run them all at once, fighting the
            // game for the CPU, and their allocations for the GC); the part being edited doesn't wait behind them. One
            // skin read (main thread) a frame.
            bool free = _busy == null || _busy.IsCompleted;
            if (_task == null && _solved != _want && now - _changedAt >= Settle && !Input.GetMouseButton(0) && _readFrame != Time.frameCount && (free || focus))
            {
                _readFrame = Time.frameCount;
                Start(draw);
                if (free && _task != null) _busy = _task;
            }

            if (draw && Last != null && _go == null) Draw(Last);
            SetVisible(draw && Last != null && Last.Error == null);
            if (_go != null && draw) Follow();
            if (_renderer != null && draw)
            {
                _props ??= new MaterialPropertyBlock();
                _props.SetColor("_Color", new Color(1f, 1f, 1f, Pending ? StaleAlpha : Alpha));
                _renderer.SetPropertyBlock(_props);
            }
        }

        private void Start(bool draw)
        {
            List<SkinPiece> pieces;
            PillSkin.Kind kind;
            var sw = Stopwatch.StartNew();
            bool stack;
            try { pieces = _skin.Capture(); kind = _skin.Source; stack = _skin.Others.Count > 0 && kind == PillSkin.Kind.ProceduralPart; }
            catch (Exception e)
            {
                Report("reading the skin", e);
                Land(new PillFit { Source = _skin.Source.ToString(), Error = "the part's skin could not be read" }, _want, draw);
                return;
            }
            double read = sw.Elapsed.TotalSeconds;
            _running = _want;
            // (the pieces' union on the worker too: it tests faces against other pieces)
            var solver = new Solver(null) { Polish = false, MaxTaper = MaxTaper, Dome = Dome };
            _solver = solver;
            _task = Task.Run(() =>
            {
                PillFit r;
                try
                {
                    TriSoup soup = PillSkin.Soup(pieces, stack);
                    solver.Mesh = soup;
                    r = Solve(soup, solver, kind);
                }
                catch (OperationCanceledException) { r = new PillFit { Source = kind.ToString(), Error = "cancelled", Cancelled = true }; }
                r.ReadSeconds = read;
                return r;
            });
        }

        // MFSSettings.pillTankMaxTaper (a half-angle) as the most the radius may change per unit length: the round
        // cone's sides make that angle when the end radii differ by its sine times the length.
        private static double MaxTaper => Math.Sin(Math.Min(Math.Max(MFSSettings.pillTankMaxTaper, 0), 60) * Math.PI / 180);

        private void Land(PillFit r, int sig, bool draw)
        {
            Last = r;
            _solved = sig;
            if (_go != null || draw) Draw(r);
            Changed?.Invoke();
        }

        // Logs an error once (until a different one comes up).
        private void Report(string what, Exception e)
        {
            string s = $"{what}: {e.GetType().Name}: {e.Message}";
            if (s == _logged) return;
            _logged = s;
            Debug.LogError($"{ModTag} {_part.partInfo?.name}: {what}: {e}");
        }

        private static PillFit Solve(TriSoup soup, Solver solver, PillSkin.Kind kind)
        {
            var sw = Stopwatch.StartNew();
            var r = new PillFit { Source = kind.ToString() };
            if (soup == null || soup.TriCount < 4 || soup.SurfT.Length < 12) { r.Error = "no closed skin to fit a tank in"; r.Empty = true; return r; }
            r.Pieces = soup.Pieces;
            List<TankResult> res = solver.Solve(1);
            r.PartVolume = (float)(soup.ExactVolume ? soup.Volume() : solver.VoxelVolume);
            r.Seconds = sw.Elapsed.TotalSeconds;
            if (res.Count == 0 || !(Math.Min(res[0].Checked.R, res[0].Checked.R2) > 0)) { r.Error = "no room for a tank"; r.Empty = true; return r; }
            Capsule c = res[0].Checked;
            // a pill can't hold more than the part: one that does sat in a skin read wrong (open, so its inside test
            // fails). The voxel volume (no exact one) is a little rough: some slack.
            if (r.PartVolume > 0 && c.Volume > r.PartVolume * (soup.ExactVolume ? 1.001 : 1.1))
            {
                r.Error = $"the pill ({c.Volume:F3} m^3) came out larger than the part ({r.PartVolume:F3} m^3): its skin isn't closed";
                return r;
            }
            r.A = new Vector3((float)c.A.x, (float)c.A.y, (float)c.A.z);
            r.B = new Vector3((float)c.B.x, (float)c.B.y, (float)c.B.z);
            r.R = (float)c.R;
            r.R2 = (float)c.R2;
            r.E = (float)c.E;
            r.Volume = (float)c.Volume;
            V3 m = c.Centroid;
            r.Centroid = new Vector3((float)m.x, (float)m.y, (float)m.z);
            return r;
        }

        // ---------------------------------------------------------------- drawing

        private static Material Mat
        {
            get
            {
                if (_mat != null) return _mat;
                _mat = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "PillTankPreview", hideFlags = HideFlags.HideAndDontSave };
                _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
                _mat.SetInt("_ZWrite", 0);
                _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);   // it sits inside the skin
                _mat.renderQueue = 4000;
                return _mat;
            }
        }

        private void Draw(PillFit r)
        {
            if (r.Error != null) return;
            if (_go == null)
            {
                _go = new GameObject("PillTankPreview");
                _go.layer = _part.gameObject.layer;
                _mesh = new Mesh { name = "PillTankPreview" };
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _go.AddComponent<MeshRenderer>();
                _renderer.sharedMaterial = Mat;
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
            }
            Follow();
            BuildCapsule(_mesh, r.A, r.B, r.R, r.R2, r.E, Tint);
        }

        // The part's frame without scale (the one the solve ran in).
        private void Follow()
        {
            Transform pt = _part.transform;
            _go.transform.SetPositionAndRotation(pt.position, pt.rotation);
        }

        // Capsule mesh (ra at a, rb at b: a tapered one's sides a cone touching both spheres; domes e as high as round)
        // with simple baked shading (vertex colours; the shader multiplies them by _Color). Flatter domes: the round pill
        // len / e long, squashed along its axis by e.
        private static void BuildCapsule(Mesh mesh, Vector3 a, Vector3 b, float ra, float rb, float e, Color col)
        {
            const int seg = 40, half = 10;       // around; rows per end (plus the tangent circle)
            Vector3 u = b - a;
            float len = u.magnitude;
            u = len > 1e-6f ? u / len : Vector3.up;
            e = Mathf.Clamp(e, 0.05f, 1f);
            // the sides' latitude on both spheres (0 for a plain pill): sin = (ra - rb) / len (the round pill's)
            float alpha = Mathf.Asin(Mathf.Clamp(len > 1e-6f ? (ra - rb) * e / len : 0f, -0.99f, 0.99f));
            Vector3 e1 = Vector3.Cross(u, Mathf.Abs(u.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 e2 = Vector3.Cross(u, e1);
            Vector3 light = new Vector3(0.35f, 0.55f, -0.75f).normalized;
            int rows = 2 * (half + 1);
            var verts = new Vector3[rows * (seg + 1)];
            var cols = new Color[verts.Length];
            for (int i = 0; i < rows; i++)
            {
                bool top = i > half;
                float phi = top ? alpha + (Mathf.PI / 2f - alpha) * (i - half - 1) / half : -Mathf.PI / 2f + (Mathf.PI / 2f + alpha) * i / half;
                Vector3 c = top ? b : a;
                float r = top ? rb : ra;
                for (int j = 0; j <= seg; j++)
                {
                    float th = 2f * Mathf.PI * j / seg;
                    Vector3 rad = Mathf.Cos(th) * e1 + Mathf.Sin(th) * e2;
                    int k = i * (seg + 1) + j;
                    verts[k] = c + r * (e * Mathf.Sin(phi) * u + Mathf.Cos(phi) * rad);
                    Vector3 n = (Mathf.Sin(phi) / e * u + Mathf.Cos(phi) * rad).normalized;     // (squashed: the normal less so)
                    float shade = 0.5f + 0.5f * Mathf.Max(0f, Vector3.Dot(n, light)) + 0.15f * Mathf.Pow(1f - Mathf.Abs(Vector3.Dot(n, u)), 4f);
                    cols[k] = new Color(col.r * shade, col.g * shade, col.b * shade, 1f);
                }
            }
            var tris = new int[(rows - 1) * seg * 6];
            int t = 0;
            for (int i = 0; i < rows - 1; i++)
                for (int j = 0; j < seg; j++)
                {
                    int p = i * (seg + 1) + j, q = p + 1, pu = p + seg + 1, qu = pu + 1;   // pu/qu: the row further along u
                    tris[t++] = p; tris[t++] = q; tris[t++] = pu;      // Cross(q-p, pu-p) points outward (Unity: front)
                    tris[t++] = q; tris[t++] = qu; tris[t++] = pu;
                }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        private void SetVisible(bool v)
        {
            if (_go != null && _go.activeSelf != v) _go.SetActive(v);
        }

        public void Dispose()
        {
            // a solve still running for a part that's gone: stopped, and the others no longer wait for it
            if (_task != null && !_task.IsCompleted)
            {
                if (_solver != null) _solver.Cancel = true;
                if (_busy == _task) _busy = null;
            }
            _task = null;
            _solver = null;
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            _go = null;
            _mesh = null;
        }
    }

    // A solved pill, in the part's frame without scale (metres).
    public sealed class PillFit
    {
        public Vector3 A, B;                  // axis ends
        public float R, R2;                   // m: the radius at A, at B (a tapered pill: between them linearly)
        public float E = 1;                   // the end domes' height over the radius (1: hemispheres)
        public float Volume;                  // m^3
        public Vector3 Centroid;              // the pill's volume's
        public float PartVolume;              // m^3 enclosed by the skin it was fitted in
        public string Source;                 // where the skin came from (PillSkin.Kind)
        public int Pieces;                    // closed pieces in that skin
        public double Seconds;                // the solve (worker thread)
        public double ReadSeconds;            // reading the skin (main thread)
        public string Error;                  // set when no pill could be placed
        public bool Empty;                    // with Error: the skin was read and holds no pill (else it couldn't be read)
        internal bool Cancelled;              // the solve was stopped (never landed)
        public float Length => (B - A).magnitude;
        public bool Tapered => Mathf.Abs(R - R2) > 0.005f * Mathf.Max(R, R2);
        public float RadiusAt(float t) => R + (R2 - R) * t;      // t: 0 at A, 1 at B
    }
}
