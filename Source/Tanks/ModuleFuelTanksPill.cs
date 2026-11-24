using System;
using System.Linq;
using System.Reflection;
using KSP.Localization;
using RealFuels.Tanks.PillTank;
using UnityEngine;

namespace RealFuels.Tanks
{
    // Which pill tanks' pills the editor draws: none, the one whose PAW is open, or every one on the craft.
    public enum PillTankView { Off, Selected, All }

    // Pill tanks: on parts whose shape a tank doesn't fill (ProceduralParts cones, polygons and Spines, procedural
    // wings: MFSSettings.pillTankShapes / pillTankModules), the tank is the largest pill (capsule) that fits inside the
    // part, and its volume is that pill's (largestPillVolume), not the part's. The fit is solved in the editor whenever
    // the part's skin changes and saved with the part, so flight needs no solve. The pill can be shown in the editor
    // ("Pill tank fit": the selected part's, or all). A tank type can opt out (TankDefinition.pillTank: a wet wing,
    // its tank the part's structure). Any tank can also be fitted for code that asks (trackPillTank); other mods read
    // PillTank / PillTankChanged.
    public partial class ModuleFuelTanks
    {
        // Transforms whose meshes together close the part's volume (comma-separated). Empty: a ProceduralPart's
        // sides and ends, else the part's colliders (see PillSkin).
        [KSPField]
        public string pillSkin = string.Empty;

        // The largest pill's volume, m^3: a pill tank's volume (totalVolume = this * tankVolumeConversion). -1: not a
        // pill tank, or not fitted yet.
        [KSPField(isPersistant = true)]
        public double largestPillVolume = -1;

        // Which pills are drawn (PillTankView, one setting for every tank this session).
        [KSPField(guiActiveEditor = false, guiName = "#RF_FuelTank_PillPreview", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)] // Pill tank fit
        [UI_ChooseOption(options = new[] { nameof(PillTankView.Off), nameof(PillTankView.Selected), nameof(PillTankView.All) }, suppressEditorShipModified = true)]
        [NonSerialized]
        public string pillTankView = nameof(PillTankView.Off);

        [KSPField(guiActiveEditor = false, guiName = "#RF_FuelTank_PillTank", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)] // Pill tank
        public string pillTankInfo = string.Empty;

        // Keep PillTank current in the editor on any tank, even one that isn't a pill tank (for code that uses it).
        [NonSerialized]
        public bool trackPillTank;

        // The latest fit (part frame without scale, metres), or null before the first solve.
        public PillFit PillTank => _pill?.Last;
        // The part's skin changed since PillTank was solved (a new solve is waiting or running).
        public bool PillTankPending => _pill == null || _pill.Pending;
        // The pill is drawn in the editor now.
        public bool PillTankDrawn => _pill != null && _pill.Drawn;
        public event Action<ModuleFuelTanks> PillTankChanged;

        // The tank is a pill tank: a pill part (IsPillTank) with a tank type that takes a pill (TankDefinition.pillTank);
        // its volume is largestPillVolume. Looked up again when the part's volume changes (a new shape, or a shell), the
        // tank type changes, and now and then.
        public bool PillSizesTank
        {
            get
            {
                int f = Time.frameCount;
                if (_isPillEvents != _pillEvents || !ReferenceEquals(_isPillType, type) || f - _isPillFrame >= 30 || f < _isPillFrame)
                {
                    _isPill = IsPillTank(part) && TypeTakesPill(type);
                    _isPillFrame = f;
                    _isPillEvents = _pillEvents;
                    _isPillType = type;
                }
                return _isPill;
            }
        }
        private bool _isPill;
        private int _isPillFrame = int.MinValue / 2, _isPillEvents = -1;
        private string _isPillType;

        private static bool TypeTakesPill(string type) =>
            type == null || !MFSSettings.tankDefinitions.TryGetValue(type, out TankDefinition def) || def.pillTank;

        // "Pill tank fit" is one setting for every tank, this session.
        private static PillTankView? _view;
        public static PillTankView PillTankView
        {
            get => _view ?? MFSSettings.pillTankPreview;
            set => _view = value;
        }
        // Pills drawn at all (on: Selected, unless All already).
        public static bool PillTankShown
        {
            get => PillTankView != PillTankView.Off;
            set { if (value != PillTankShown) PillTankView = value ? PillTankView.Selected : PillTankView.Off; }
        }

        private PillPreview _pill;
        private int _pillEvents;
        // The part's own volume (tank units) from its latest volume event; 0 before one (_partVolumeKnown). Less than
        // the shape's when another module takes some of it (RP-1's procedural avionics): a ProceduralParts pill tank is
        // capped at it. (Not a B9 wing: its volume is an estimate, can be under the pill that fits.)
        private double _partTotalVolume;
        private bool _partVolumeKnown;
        private bool _pillLine;
        private static PropertyInfo _currentShape;

        // A part a pill tank goes in: one with a pill module, or a ProceduralPart whose current shape is a pill shape
        // and not a hollow shell.
        public static bool IsPillTank(Part p)
        {
            foreach (PartModule m in p.Modules)
            {
                if (MFSSettings.pillTankModules.Contains(m.moduleName)) return true;
                if (m.moduleName != "ProceduralPart") continue;
                _currentShape ??= m.GetType().GetProperty("CurrentShape", BindingFlags.Instance | BindingFlags.Public);
                if (!(_currentShape?.GetValue(m) is PartModule shape) || !MFSSettings.pillTankShapes.Contains(shape.moduleName)) return false;
                return !(shape.Fields["shellMode"] is BaseField f && f.GetValue(shape) is bool shell && shell);
            }
            return false;
        }

        private void InitPill()
        {
            pillTankView = PillTankView.ToString();
            // hidden until UpdatePill finds a pill tank (the field's attribute alone doesn't keep it hidden)
            _pillLine = false;
            BaseField view = Fields[nameof(pillTankView)];
            view.guiActiveEditor = false;
            Fields[nameof(pillTankInfo)].guiActiveEditor = false;
            if (view.uiControlEditor is UI_ChooseOption choose)
                choose.display = new[] { "#RF_FuelTank_PillOff", "#RF_FuelTank_PillSelected", "#RF_FuelTank_PillAll" }
                    .Select(Localizer.GetStringByTag).ToArray(); // Off, Selected, All
            view.uiControlEditor.onFieldChanged += (f, o) => SetView();
            view.uiControlEditor.onSymmetryFieldChanged += (f, o) => SetView();
        }

        private void SetView()
        {
            if (Enum.TryParse(pillTankView, out PillTankView v)) PillTankView = v;
        }

        private void UpdatePill()
        {
            PillTankView view = PillTankView;
            pillTankView = view.ToString();
            bool pillTank = PillSizesTank;
            bool parts = EditorLogic.fetch != null && EditorLogic.fetch.editorScreen == EditorScreen.Parts;
            bool open = part.PartActionWindow != null && parts;
            bool draw = pillTank && parts && (view == PillTankView.All || view == PillTankView.Selected && open);
            // a tank that was a pill tank keeps solving until it has the part's volume back (RestorePartVolume)
            bool solve = pillTank || trackPillTank || largestPillVolume >= 0;
            if (solve && _pill == null)
            {
                // a fit saved with the part (largestPillVolume) holds until its skin changes
                _pill = new PillPreview(part, pillSkin, saved: largestPillVolume >= 0);
                _pill.Changed += OnPillSolved;
            }
            _pill?.Tick(solve, draw, pillTank && open, _pillEvents);
            if (pillTank && _pill != null && _pill.Pending) PillLaunchLock.PendingFrame = Time.frameCount;

            // made a pill tank or not with no volume event (the tank type switched, or a craft saved before the type
            // took no pill): a pill tank with no pill volume takes its fit's (OnPillSolved does once a new fit lands);
            // another with one, the part's volume back
            if (pillTank && largestPillVolume < 0 && _pill?.Last != null && !_pill.Pending) ApplyPill(_pill.Last);
            else if (!pillTank && largestPillVolume >= 0) RestorePartVolume();

            // the PAW's pill lines: on pill tanks
            if (pillTank != _pillLine)
            {
                _pillLine = pillTank;
                Fields[nameof(pillTankView)].guiActiveEditor = _pillLine;
                Fields[nameof(pillTankInfo)].guiActiveEditor = _pillLine;
                MonoUtilities.RefreshPartContextWindow(part);
            }
            if (_pillLine && part.PartActionWindow != null)
            {
                PillFit last = _pill?.Last;
                bool pending = _pill == null || _pill.Pending;
                if (!_infoSet || last != _infoFit || pending != _infoPending)
                {
                    pillTankInfo = PillInfo();
                    _infoSet = true;
                    _infoFit = last;
                    _infoPending = pending;
                }
            }
        }
        private PillFit _infoFit;
        private bool _infoSet, _infoPending;

        // With no pill, a pill tank keeps this share of the part's volume: never 0, so the fuel mix (kept only as each
        // fuel's amount) scales back when a pill fits again.
        public const double NoPillShare = 0.01;
        private const double MinNoPillVolume = 1e-3;

        private void OnPillSolved()
        {
            ApplyPill(_pill.Last);
            PillTankChanged?.Invoke(this);
        }

        // A fit landed: a pill tank takes its volume (a ProceduralPart's, no more than its volume event allows); with no pill,
        // NoPillShare of the part's (a skin that couldn't be read leaves the volume as it was).
        internal void ApplyPill(PillFit r)
        {
            if (!PillSizesTank || r == null || (r.Error != null && !r.Empty)) return;
            double total;
            if (r.Error == null)
            {
                largestPillVolume = r.Volume;
                total = r.Volume * tankVolumeConversion;
                if (_partVolumeKnown && part.Modules.Contains("ProceduralPart")) total = Math.Min(total, _partTotalVolume);
            }
            else
            {
                double partVolume = _partTotalVolume > 0 ? _partTotalVolume : r.PartVolume * tankVolumeConversion;
                if (!(partVolume > 0)) return;     // nothing to go by: as it was
                largestPillVolume = 0;
                total = Math.Max(NoPillShare * partVolume, MinNoPillVolume);
            }
            SetTotalVolume(total);
        }

        // No longer a pill tank: the part's volume back (from its latest volume event, else its fit's), the fuel mix
        // scaled with it. With neither yet, waits for the fit; with a fit and still none, leaves the volume as it is.
        private void RestorePartVolume()
        {
            PillFit r = _pill?.Last;
            bool settled = r != null && !_pill.Pending;
            double partVolume = _partTotalVolume > 0 ? _partTotalVolume : settled ? r.PartVolume * tankVolumeConversion : 0;
            if (!(partVolume > 0) && !settled) return;
            largestPillVolume = -1;
            if (partVolume > 0) SetTotalVolume(partVolume);
        }

        private void SetTotalVolume(double total)
        {
            if (Math.Abs(total - totalVolume) <= 1e-6 * Math.Max(1, totalVolume)) return;
            ChangeTotalVolume(total);
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch?.ship != null)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        private string PillInfo()
        {
            PillFit r = _pill?.Last;
            if (r == null) return Localizer.GetStringByTag("#RF_FuelTank_PillSolving"); // solving...
            string s;
            if (r.Error != null) s = Localizer.GetStringByTag("#RF_FuelTank_PillNone"); // none fits
            else
            {
                // in the tank's units (kL in Real Fuels), like its Volume line
                string units = KSPUtil.PrintSI(r.Volume * tankVolumeConversion, MFSSettings.unitLabel);
                s = Localizer.Format("#RF_FuelTank_PillInfo", units, r.R.ToString("F2"), r.Length.ToString("F2"));
            }
            return _pill.Pending ? $"{s} {Localizer.GetStringByTag("#RF_FuelTank_PillUpdating")}" : s; // (updating)
        }

        private void DisposePill()
        {
            _pill?.Dispose();
            _pill = null;
        }
    }

    // No launch or save while a pill tank's fit is pending (the craft would go out with the volume from before the
    // change): the fits land within a few seconds. A fit that never settles (a skin that keeps changing) releases the
    // lock after MaxLock.
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class PillLaunchLock : MonoBehaviour
    {
        private const string LockId = "RealFuelsPillTankFit";
        private const ControlTypes Locks = ControlTypes.EDITOR_LAUNCH | ControlTypes.EDITOR_SAVE;
        private const float MaxLock = 30f;

        // The last frame a pill tank's fit was pending (set by the tanks).
        internal static int PendingFrame = -10;
        private float _since = -1;
        private bool _locked;

        private void Update()
        {
            bool pending = Time.frameCount - PendingFrame <= 2;
            float now = Time.realtimeSinceStartup;
            if (!pending) _since = -1;
            else if (_since < 0) _since = now;
            bool want = pending && now - _since < MaxLock;
            if (pending && !want && _locked) Debug.LogWarning("[PillTank] fits still pending after 30 s: launch and save unlocked");
            if (want == _locked) return;
            _locked = want;
            if (want) InputLockManager.SetControlLock(Locks, LockId);
            else InputLockManager.RemoveControlLock(LockId);
        }

        private void OnDestroy()
        {
            if (_locked) InputLockManager.RemoveControlLock(LockId);
        }
    }
}
