using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace RealFuels.Tanks
{
    public class TankCreationGUI : MonoBehaviour
    {
        private Rect guiWindowRect = new Rect(200, 200,500,300);
        private string windowTitle;
        public ModuleFuelTanks tank_module;
        private FuelTank Pressurant { get => tank_module.pressurant; set => tank_module.pressurant = value; }
        private float newVolume;
        private float newPressure;
        private float extraPressurant = 0;
        private string sNewVolume = "0";
        private string sNewPressure = "1";
        private string sExtraPressurant = "0";
        private string storagePressure;
        private readonly Dictionary<FuelTank, bool> tankLocks = new Dictionary<FuelTank,bool>();
        internal void SetTankLock(FuelTank tank, bool b) => tankLocks[tank] = b;

        public void Awake() { }

        public void Setup(ModuleFuelTanks parent)
        {
            tank_module = parent;
            windowTitle = $"{tank_module.part.partInfo.title} Fuel Tank Creation";
            Pressurant = tank_module.tankList.Values.FirstOrDefault(x => x.name == RFSettings.Instance.Pressurants[0]);
            if (Pressurant == null)
            {
                Pressurant = tank_module.tankList.Values.First();
                CyclePressurant();
            }
            storagePressure = tank_module.pressurantStoragePressure.ToString();
        }

        public void OnGUI()
        {
            guiWindowRect = GUILayout.Window(GetInstanceID(), guiWindowRect, GUIWindow, windowTitle, Styles.styleEditorPanel, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
        }

        private string PrintVolume(double value) => value >= 1e6 ? $"{value / 1e6:F1}M" : value >= 1e3 ? $"{value / 1e3:F1}k" : $"{value:F1}";

        private void GUIVolumeRow()
        {
            GUIStyle topStyle = tank_module.UnallocatedVolume >= 0.1 ? Styles.labelYellow : Styles.labelGreen;
            GUILayout.BeginHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Total Volume:");
            GUILayout.Label($"{PrintVolume(tank_module.volume)}{MFSSettings.unitLabel}");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Allocated:");
            GUILayout.Label($"{PrintVolume(tank_module.AllocatedVolume)}{MFSSettings.unitLabel}");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Unallocated:");
            GUILayout.Label($"{tank_module.UnallocatedVolume:F1}{MFSSettings.unitLabel}", topStyle);
            GUILayout.EndHorizontal();

            GUILayout.EndHorizontal();
        }

        private void PressurantVolumeRows()
        {
            GUILayout.BeginHorizontal();
            if (tank_module.highlyPressurized)
            {
                GUILayout.Label("(Highly Pressurized)");
                GUILayout.FlexibleSpace();
            }
            GUIContent pressurantContent = new GUIContent(Pressurant.name, "Choose the pressurant");
            if (GUILayout.Button(pressurantContent))
                CyclePressurant();
            GUILayout.Label(" @", GUILayout.ExpandWidth(false));
            storagePressure = GUILayout.TextField(storagePressure, GUILayout.Width(40));
            GUILayout.Label($"({tank_module.pressurantStoragePressure}) atm", GUILayout.ExpandWidth(false));
            if (GUILayout.Button("Update", GUILayout.ExpandWidth(false)))
                float.TryParse(storagePressure, out tank_module.pressurantStoragePressure);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            float autoVolume = tank_module.tankGroups.Sum(x => x.PressurantVolume);
            float newMaxAmount = Math.Max((autoVolume + extraPressurant) * tank_module.pressurantStoragePressure, 0);
            if (Pressurant.maxAmount != newMaxAmount)
                Pressurant.maxAmount = newMaxAmount;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Auto:");
            GUILayout.Label($"{PrintVolume(autoVolume)}{MFSSettings.unitLabel}");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Extra:");
            sExtraPressurant = GUILayout.TextField(sExtraPressurant, GUILayout.Width(40));
            GUILayout.Label(MFSSettings.unitLabel);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Total:");
            GUILayout.Label($"{PrintVolume(autoVolume + extraPressurant)}{MFSSettings.unitLabel}");
            GUILayout.EndHorizontal();

            float maxAllowedVolume = extraPressurant + tank_module.UnallocatedVolume;
            if (GUILayout.Button("Update", GUILayout.ExpandWidth(false)) && float.TryParse(sExtraPressurant, out float newExtraPressurant))
            {
                newExtraPressurant = (float) Math.Round(newExtraPressurant, 1);
                newExtraPressurant = Math.Min(newExtraPressurant, maxAllowedVolume);
                extraPressurant = newExtraPressurant;
                sExtraPressurant = extraPressurant.ToString();
            }

            GUILayout.EndHorizontal();
        }

        private readonly List<TankGroup> removeList = new List<TankGroup>();
        public void GUIWindow(int windowID)
        {
            GUILayout.Space(20);
            GUILayout.BeginVertical(Styles.styleEditorBox, GUILayout.ExpandHeight(false), GUILayout.ExpandWidth(true));
            GUIVolumeRow();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Mass: {tank_module.massDisplay}");
            GUILayout.FlexibleSpace();
            float cost = tank_module.GetModuleCost(0, ModifierStagingSituation.CURRENT);
            GUILayout.Label($"Cost: {cost:N1}");
            GUILayout.EndHorizontal();

            PressurantVolumeRows();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(Styles.styleEditorBox);
            GUILayout.Label($"Current tank groups: {tank_module.tankGroups.Count}");
            removeList.Clear();
            for (int i=0; i < tank_module.tankGroups.Count; i++)
            {
                var group = tank_module.tankGroups[i];
                float groupUsedVolume = (float)group.tanks.Sum(tank => tank.maxAmount);
                GUILayout.BeginVertical(Styles.styleEditorBox);

                GUILayout.BeginHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{i}: {group.mode}");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUIStyle style = group.UsedVolume == 0 ? Styles.labelOrange
                                 : group.AvailableVolume > 0 ? Styles.labelYellow : Styles.labelGreen;
                GUILayout.Label("Tanks:");
                GUILayout.Label($"{PrintVolume(group.UsedVolume)}", style, GUILayout.ExpandWidth(false));
                GUILayout.Label(MFSSettings.unitLabel, style);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                style = group.AvailableVolume > 0 ? Styles.labelYellow : Styles.labelGreen;
                GUILayout.Label("Available:");
                GUILayout.Label($"{PrintVolume(group.AvailableVolume)}", style, GUILayout.ExpandWidth(false));
                GUILayout.Label(MFSSettings.unitLabel, style);
                GUILayout.EndHorizontal();

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Storage pressure:");
                string prevStore = group.storagePressureEdit;
                group.storagePressureEdit = GUILayout.TextField(prevStore, GUILayout.Width(40));
                if (!prevStore.Equals(group.storagePressureEdit) && float.TryParse(group.storagePressureEdit, out float newStoragePressure))
                    group.storagePressure = newStoragePressure;
                GUILayout.Label("atm");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Tank pressure:");
                if (group.tanks.Count > 0)
                    GUILayout.Label($"{group.storagePressure:F1}", GUILayout.ExpandWidth(false));
                else
                {
                    string prev = group.targetPressureEdit;
                    group.targetPressureEdit = GUILayout.TextField(prev, GUILayout.Width(40));
                    if (!prev.Equals(group.targetPressureEdit) && float.TryParse(group.targetPressureEdit, out float changedPressure))
                        group.UpdateTargetPressure(changedPressure);
                }
                GUILayout.Label("atm");
                GUILayout.EndHorizontal();

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                float maxAllowedVolume = group.volume + tank_module.UnallocatedVolume;
                GUILayout.Label("Allocated Volume:");
                group.volumeEdit = GUILayout.TextField(group.volumeEdit, GUILayout.Width(60));
                GUILayout.Label(MFSSettings.unitLabel);
                if (GUILayout.Button("Update") && float.TryParse(group.volumeEdit, out float newV))
                {
                    newV = Math.Min(newV, group.volume + tank_module.UnallocatedVolume);
                    group.UpdateVolume(newV);
                }
                float newVolumeRatio = GUILayout.HorizontalSlider(group.VolumeAsPercentageOfTotal, 0.001f, 1, GUILayout.Width(150));
                if (newVolumeRatio != group.VolumeAsPercentageOfTotal)
                {
                    newVolumeRatio = Math.Min(newVolumeRatio, maxAllowedVolume / (float)tank_module.volume);
                    newV = newVolumeRatio * (float) tank_module.volume;
                    group.UpdateVolume(newV);
                }
                group.volumeLocked = GUILayout.Toggle(group.volumeLocked, "Lock");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Pressurant:");
                GUILayout.Label($"{PrintVolume(group.PressurantVolume)}", GUILayout.ExpandWidth(false));
                GUILayout.Label(MFSSettings.unitLabel);
                GUILayout.EndHorizontal();

                GUILayout.EndHorizontal();

                foreach (FuelTank tank in tank_module.tankGroups[i].tanks)
                {
                    GUILayout.BeginHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{tank.name}", GUILayout.Width(80));
                    GUILayout.Label($"{PrintVolume(tank.Volume)}", GUILayout.ExpandWidth(false));
                    GUILayout.Label(MFSSettings.unitLabel);
                    GUILayout.Label($"({tank.maxAmount:F1} units)");
                    GUILayout.EndHorizontal();
                    float maxAllowedTankVolume = (float)tank.Volume + group.AvailableVolume;
                    float oldRatio = (float)(tank.Volume / group.volume);
                    float tankRatio = GUILayout.HorizontalSlider(oldRatio, 0.001f, 1, GUILayout.Width(150));
                    if (tankRatio != oldRatio)
                    {
                        tankRatio = Math.Min(tankRatio, maxAllowedTankVolume / group.volume);
                        newV = tankRatio * group.volume;
                        tank.maxAmount = newV * tank.utilization;
                    }
                    GUILayout.Label($"({tankRatio:P1})", GUILayout.ExpandWidth(false), GUILayout.MaxHeight(20));
                    tankLocks.TryGetValue(tank, out bool locked);
                    tankLocks[tank] = GUILayout.Toggle(locked, "Lock");
                    GUILayout.EndHorizontal();
                }
                this.GUIEngines(group);
                if (GUILayout.Button("Deallocate"))
                {
                    removeList.Add(group);
                    group.mode = "manual";
                }
                GUILayout.EndVertical();
            }
            foreach (var group in removeList)
                foreach (var tank in group.tanks)
                    tank.maxAmount = 0;
            tank_module.tankGroups.RemoveAll(x => removeList.Contains(x));
            GUILayout.EndVertical();

            GUILayout.BeginVertical(Styles.styleEditorBox);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Create new subtank group"))
            {
                if (newVolume == 0)
                    ScreenMessages.PostScreenMessage("Specify a non-zero volume!", 5, ScreenMessageStyle.UPPER_CENTER);
                else if (newPressure < 0.1)
                    ScreenMessages.PostScreenMessage("Specify a pressure > 0.1 atm!", 5, ScreenMessageStyle.UPPER_CENTER);
                else
                {
                    newVolume = Math.Min(tank_module.UnallocatedVolume, newVolume);
                    sNewVolume = newVolume.ToString();
                    ScreenMessages.PostScreenMessage($"Creating a new subtank group for volume: {newVolume}!", 10, ScreenMessageStyle.UPPER_CENTER);
                    TankGroup group = new TankGroup(Pressurant, tank_module, 200, newPressure, newVolume);
                    tank_module.tankGroups.Add(group);
                }
            }
            GUILayout.BeginHorizontal();
            string sPrev = sNewVolume;
            GUILayout.Label("Volume:");
            sNewVolume = GUILayout.TextField($"{sPrev}", GUILayout.Width(80));
            if (!sNewVolume.Equals(sPrev))
                float.TryParse(sNewVolume, out newVolume);
            GUILayout.Label(MFSSettings.unitLabel);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            sPrev = sNewPressure;
            GUILayout.Label("Pressure:");
            sNewPressure = GUILayout.TextField($"{sPrev}", GUILayout.Width(40));
            if (!sNewPressure.Equals(sPrev))
                float.TryParse(sNewPressure, out newPressure);
            GUILayout.Label("atm");
            GUILayout.EndHorizontal();

            GUILayout.EndHorizontal();
            if (GUILayout.Button("Close"))
                Destroy(this);
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private readonly List<string> allGroupedTankResourceNames = new List<string>();
        private readonly HashSet<string> displayedParts = new HashSet<string>();

        private void GUIEngines(TankGroup group)
        {
            if (group.AvailableVolume > 0.1)
            {
                displayedParts.Clear();
                allGroupedTankResourceNames.Clear();
                foreach (var g in tank_module.tankGroups)
                    allGroupedTankResourceNames.AddRange(g.tanks.Select(t => t.name));

                GUILayout.BeginVertical(Styles.styleEditorBox);
                GUILayout.Label("Configure group for detected engines:");

                foreach (var kvp in tank_module.usedBy)
                {
                    PartModule source = kvp.Key;
                    FuelInfo info = kvp.Value;
                    if (!displayedParts.Contains(info.title))
                    {
                        bool used = info.propellantVolumeMults.Keys.Where(prop => allGroupedTankResourceNames.Contains(prop.resourceDef.name)).Any();
                        if (!used && GUILayout.Button(info.title))
                            tank_module.ConfigureForRF(info, group);
                        else if (used)
                            GUILayout.Label($"Not available, contains used resource: {info.title}", Styles.labelOrange);
                        displayedParts.Add(info.title);
                    }
                }
                GUILayout.EndVertical();
            }
        }

        private void CyclePressurant()
        {
            int max = RFSettings.Instance.Pressurants.Count;
            int count = max;
            int index = RFSettings.Instance.Pressurants.IndexOf(Pressurant.name);
            Pressurant = null;
            do
            {
                index = (index + 1) % max;
                string p = RFSettings.Instance.Pressurants[index];
                Pressurant = tank_module.tankList.Values.FirstOrDefault(x => x.name == p);
            } while (count-- > 0 && Pressurant == null);
            if (Pressurant == null)
                Pressurant = tank_module.tankList.Values.First();
        }
    }
}
