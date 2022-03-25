using System;
using System.Collections.Generic;
using System.Linq;

namespace RealFuels.Tanks
{
    public class TankGroup
    {
        [Persistent] public string mode = "manual";
        public FuelTank pressurant;
        public ModuleFuelTanks tank_module;
        public List<FuelTank> tanks = new List<FuelTank>();
        [Persistent] public float targetPressure;
        [Persistent] public float storagePressure;
        [Persistent] public float volume;
        [Persistent] public bool volumeLocked = false;

        public string volumeEdit;
        public string storagePressureEdit;
        public string targetPressureEdit;
        private float GasVolume => (float)tanks.Where(t => MFSSettings.resourceGasses.Contains(t.name)).Sum(t => t.Volume);
        public float PressurantVolume => (volume - GasVolume) * targetPressure / tank_module.pressurantStoragePressure;
        public float UsedVolume => (float)tanks.Sum(tank => tank.Volume);
        public float AvailableVolume => volume - UsedVolume - PressurantVolume;
        public float FilledVolume => (float)tanks.Sum(tank => tank.FilledVolume);
        public float VolumeAsPercentageOfTotal => volume / (float)tank_module.volume;
        public float VolumeAsPercentageOfAllocated => volume / tank_module.AllocatedVolume;
        public float CurrentRequiredPressurantVolume => Math.Max(1, UsedVolume - FilledVolume) * targetPressure / tank_module.pressurantStoragePressure;
        public float CurrentAvailablePressurantVolume => (float)(pressurant.amount / tank_module.pressurantStoragePressure);

        public TankGroup() { }
        public TankGroup(FuelTank pressurant, ModuleFuelTanks tank_module, float storagePressure, float targetPressure, float volume)
        {
            this.pressurant = pressurant;
            this.tank_module = tank_module;
            this.storagePressure = storagePressure;
            this.targetPressure = targetPressure;
            this.volume = volume;
            targetPressureEdit = targetPressure.ToString();
            storagePressureEdit = storagePressure.ToString();
            volumeEdit = volume.ToString();
        }

        public static TankGroup Create(ModuleFuelTanks tank_module, ConfigNode node)
        {
            TankGroup group = ConfigNode.CreateObjectFromConfig<TankGroup>(node);
            group.pressurant = tank_module.pressurant;
            group.targetPressureEdit = group.targetPressure.ToString();
            group.storagePressureEdit = group.storagePressure.ToString();
            group.volumeEdit = group.volume.ToString();

            group.tank_module = tank_module;
            string[] tankNames = node.GetValues("tank");
            group.tanks.AddRange(tank_module.tankList.Where(t => tankNames.Contains(t.Key)).Select(x => x.Value));

            return group;
        }

        public void UpdateTargetPressure(float newPressure)
        {
            float oldVolume = volume - PressurantVolume;
            targetPressure = newPressure;
            float newVolume = volume - PressurantVolume;
            float volumeRatio = newVolume / oldVolume;
            foreach (var tank in tanks)
            {
                bool save_propagate = tank.propagate;
                tank.propagate = false;
                tank.maxAmount *= volumeRatio;
                tank.propagate = save_propagate;
            }
        }

        public void UpdateVolume(float newVolume)
        {
            float volumeRatio = newVolume / volume;
            foreach (var tank in tanks)
            {
                bool save_propagate = tank.propagate;
                tank.propagate = false;
                tank.maxAmount *= volumeRatio;
                tank.propagate = save_propagate;
            }
            volume = newVolume;
            volumeEdit = volume.ToString();
        }
    }
}
