using KSP.Localization;
using RealFuels.Harmony;
using ROUtils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace RealFuels.Tanks
{
    public partial class ModuleFuelTanks : IAnalyticTemperatureModifier, IAnalyticPreview
    {
        public const string CryogenicGroupName = "RFCryogenics";
        public const string CryogenicsGroupDisplayName = "#RF_FuelTankRF_Cryogenics"; // "RF Cryogenics"

        [KSPField(guiActiveEditor = true, guiName = "#RF_FuelTankRF_HighlyPressurized", groupName = guiGroupName)] // Highly Pressurized?
        public bool highlyPressurized = false;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "#RF_FuelTankRF_AddedMLILayers",
            groupName = CryogenicGroupName, groupDisplayName = CryogenicsGroupDisplayName, guiFormat = "F0"), // Added MLI Layers
        UI_FloatRange(minValue = 0, maxValue = 100, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float _numberOfAddedMLILayers = 0; // This is the number of layers added by the player.
        public int numberOfAddedMLILayers => (int)_numberOfAddedMLILayers;

        [KSPField(isPersistant = true, guiActive = true, guiName = "#RF_FuelTankRF_MLILayers",
            groupName = CryogenicGroupName, groupDisplayName = CryogenicsGroupDisplayName, guiFormat = "F0")] // MLI Layers
        public int totalMLILayers = 0;

        [KSPField(isPersistant = true)]
        protected double totalTankArea;

        [KSPField(guiName = "#RF_FuelTankRF_WallTemp", groupName = CryogenicGroupName)] // Wall Temp
        public string sWallTemp;

        [KSPField(guiName = "#RF_FuelTankRF_HeatPenetration", groupName = CryogenicGroupName)] // Heat Penetration
        public string sHeatPenetration;

        [KSPField(guiName = "#RF_FuelTankRF_BoiloffLoss", groupName = CryogenicGroupName)] // Boil-off Loss
        public string sBoiloffLoss;

        // Thermal data captured while loaded, used for processing boiloff BackgroundUpdate on unloaded vessels.
        // Format per entry: "resourceName,boilingPointK,tankAreaM2,conductWPerK,isDewar"
        //   boilingPointK  — boiling point of the propellant (K)
        //   tankAreaM2     — per-tank surface area for MLI/Dewar formulas
        //   conductWPerK   — wall conductance W/K for non-MLI tanks (0 for MLI and Dewar)
        //   isDewar        — 1 for Dewar tanks, 0 otherwise
        [KSPField(isPersistant = true)]
        public string bgBoiloffData = "";

        // UT of the last Kerbalism BackgroundUpdate tick; used to avoid double-applying boiloff
        // when the vessel loads and SetAnalyticTemperature catches up for the unloaded period.
        [KSPField(isPersistant = true)]
        public double bgBoiloffLastUpdate = 0d;

        // Cryocooler params captured at save time for unloaded BackgroundUpdate use.
        // Format: "coolerInputKW,coolerFracAtLowestTemp,lowestTempK"
        [KSPField(isPersistant = true)]
        public string bgCoolerData = "";

        [KSPField]
        public int maxMLILayers = 10;

        [KSPField]
        public float MLIArealCost = 0.20764f;

        [KSPField]
        public float MLIArealDensity = 0.000015f;

        [KSPField]
        public HermiteCurve cryoCoolerEfficiency = new HermiteCurve();

        [KSPField]
        public float maxCoolerInputKW = 0f;

        [KSPField]
        public float coolerBaseMass = 0f; // tonnes, fixed overhead (electronics, housing)

        [KSPField]
        public HermiteCurve coolerMassPerKWInput = new HermiteCurve(); // tonnes/kW, keyed to T_cold (K)

        [KSPField]
        public float coolerBaseCost = 0f;

        [KSPField]
        public HermiteCurve coolerCostPerKWInput = new HermiteCurve(); // funds/kW, keyed to T_cold (K)

        [KSPField(isPersistant = true, guiActiveEditor = true,
            guiName = "#RF_FuelTankRF_CryoCoolerInputPower", guiUnits = " kW", guiFormat = "F2",
            groupName = CryogenicGroupName, groupDisplayName = CryogenicGroupName),
         UI_FloatEdit(minValue = 0f, maxValue = 0f, incrementLarge = 1f, incrementSmall = 0.1f, incrementSlide = 0.01f, sigFigs = 2,
            unit = " kW", scene = UI_Scene.Editor)]
        public float coolerInputKW = 0f;

        [KSPField(guiName = "#RF_FuelTankRF_CryoCoolerLift", groupName = CryogenicGroupName)]
        public string sCoolerLift;

        [KSPField(guiName = "#RF_FuelTankRF_CryoCoolerDraw", groupName = CryogenicGroupName)]
        public string sCoolerDraw;

        [KSPField(guiName = "#RF_FuelTankRF_CryoCoolerCOP", groupName = CryogenicGroupName)]
        public string sCoolerCOP;   // Coefficient of Performance, ratio of the useful cooling provided to the work (energy) required

        private double analyticSkinTemp;
        private readonly Dictionary<string, double> boiloffProducts = new Dictionary<string, double>();

        public int numberOfMLILayers = 0; // base number of layers taken from TANK_DEFINITION configs

        private double currentCoolerLiftKW;
        private double currentCoolerDrawKW;
        private double currentCoolerCOP;

        private double boiloffMassT = 0d;
        public double BoiloffMassRate => boiloffMassT;

        private readonly List<FuelTank> cryoTanks = new List<FuelTank>();   // anything with maxAmount > 0 && vsp > 0
        private readonly List<double> lossInfo = new List<double>();
        private readonly List<double> fluxInfo = new List<double>();
        private readonly Dictionary<FuelTank, double> _perTankFlux = new Dictionary<FuelTank, double>();
        private readonly Dictionary<FuelTank, double> _perTankLift = new Dictionary<FuelTank, double>();

        // Pre-parsed tank boiloff data for background processing.
        internal static readonly ConditionalWeakTable<ProtoPartModuleSnapshot, BgBoiloffCache> bgCache
            = new ConditionalWeakTable<ProtoPartModuleSnapshot, BgBoiloffCache>();

        // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
        [NonSerialized]
        public Dictionary<string, bool> pressurizedFuels = new Dictionary<string, bool>();

        private FlightIntegrator _flightIntegrator;

        double lowestTankTemperature = 300d;

        static readonly ProfilerMarker _profileBgUpdt = new ProfilerMarker("RF.BackgroundUpdate");
        static readonly ProfilerMarker _profileBgUpdtCache = new ProfilerMarker("RF.BackgroundUpdateCache");

        public bool SupportsBoiloff => cryoTanks.Count > 0;
        public bool SupportCryoCooler => maxCoolerInputKW > 0f;
        public bool HasCryoCooler => coolerInputKW > 0f;
        private bool IsProcedural => part.Modules.Contains("SSTUModularPart") || part.Modules.Contains("WingProcedural");

        partial void OnLoadRF(ConfigNode _) { }

        partial void OnAwakeRF()
        {
            // HermiteCurve is not [Serializable], so Unity's Instantiate leaves these fields null
            // on copied instances. BaseFieldList.Load silently skips null IConfigNode fields, so
            // the curves would never get populated. Restore them from the prefab before Load runs.
            //if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
            //{
            //    var prefab = part.FetchModuleFromPrefab(this);
            //    coolerMassPerKWInput = prefab?.coolerMassPerKWInput ?? new HermiteCurve();
            //    coolerCostPerKWInput = prefab?.coolerCostPerKWInput ?? new HermiteCurve();
            //    cryoCoolerEfficiency = prefab?.cryoCoolerEfficiency ?? new HermiteCurve();
            //}

            if (!HighLogic.LoadedSceneIsEditor) return;

            // KSP orders PAW fields by Type.GetFields() reflection order. Because ModuleFuelTanks.cs
            // compiles before ModuleFuelTanksRF.cs, showUI lands before highlyPressurized. Swap them.
            int showUIIdx = -1, pressurizedIdx = -1;
            for (int i = 0; i < Fields.Count; i++)
            {
                if (Fields[i].name == nameof(showUI)) showUIIdx = i;
                else if (Fields[i].name == nameof(highlyPressurized)) pressurizedIdx = i;
            }

            if (showUIIdx >= 0 && pressurizedIdx >= 0 && showUIIdx < pressurizedIdx)
            {
                BaseField tmp = Fields[showUIIdx];
                Fields[showUIIdx] = Fields[pressurizedIdx];
                Fields[pressurizedIdx] = tmp;
            }
        }

        partial void OnSaveRF(ConfigNode _)
        {
            if (!HighLogic.LoadedSceneIsFlight || !SupportsBoiloff)
                return;

            double structuralThermalMass = ComputeStructuralThermalMass();
            var entries = new List<string>(cryoTanks.Count);
            foreach (var tank in cryoTanks)
            {
                if (tank.amount <= 0 || tank.vsp <= 0) continue;

                double tankAreaM2 = tank.totalArea;
                double conductWPerK = 0;
                int isDewar = tank.isDewar ? 1 : 0;

                if (!tank.isDewar && totalMLILayers == 0)
                {
                    double wallF = tank.wallConduction > 0 ? tank.wallThickness / tank.wallConduction : 0;
                    double insulF = tank.insulationConduction > 0 ? tank.insulationThickness / tank.insulationConduction : 0;
                    double resF = tank.resourceConductivity > 0 ? 0.01 / tank.resourceConductivity : 0;
                    conductWPerK = tank.totalArea / Math.Max(double.Epsilon, wallF + insulF + resF);
                }

                entries.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4},{5:R},{6:R},{7:R}",
                    tank.name, tank.temperature, tankAreaM2, conductWPerK, isDewar,
                    tank.hsp, structuralThermalMass * tank.tankRatio, tank.Volume));
            }

            bgBoiloffData = entries.Count > 0 ? string.Join(";", entries) : "";

            bgCoolerData = "";
            if (HasCryoCooler)
            {
                CalculateLowestTankTemperature();
                if (lowestTankTemperature > 0d && lowestTankTemperature < 300d)
                {
                    double frac = cryoCoolerEfficiency.Evaluate(lowestTankTemperature);
                    if (frac > 0d)
                    {
                        bgCoolerData = string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}",
                            coolerInputKW, frac, lowestTankTemperature);
                    }
                }
            }
        }

        partial void OnStartRF(StartState _)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                _flightIntegrator = vessel.vesselModules.Find(x => x is FlightIntegrator) as FlightIntegrator;
                // SupportsBoiloff only becomes known below, after the cache may already have been built
                PatchFlightIntegrator.Invalidate(vessel);
            }

            foreach (var tank in tanksDict.Values)
            {
                if (tank.internalTemp < 0)
                    tank.internalTemp = tank.vsp > 0 ? tank.temperature : (double.IsNaN(part.temperature) ? 300d : part.temperature);

                if (tank.maxAmount > 0 && tank.vsp > 0)
                    cryoTanks.Add(tank);
            }
            CalculateTankArea();

            if (HighLogic.LoadedSceneIsEditor)
            {
                Fields[nameof(_numberOfAddedMLILayers)].guiActiveEditor = maxMLILayers > 0;
                _numberOfAddedMLILayers = Mathf.Clamp(_numberOfAddedMLILayers, 0, maxMLILayers);
                totalMLILayers = numberOfMLILayers + numberOfAddedMLILayers;
                ((UI_FloatRange)Fields[nameof(_numberOfAddedMLILayers)].uiControlEditor).maxValue = maxMLILayers;
                Fields[nameof(_numberOfAddedMLILayers)].uiControlEditor.onFieldChanged = delegate (BaseField field, object value)
                {
                    totalMLILayers = numberOfMLILayers + numberOfAddedMLILayers;
                    UpdateMLILayersDisplay();
                    massDirty = true;
                    CalculateMass();
                };

                Fields[nameof(coolerInputKW)].guiActiveEditor = SupportCryoCooler;
                if (SupportCryoCooler)
                {
                    coolerInputKW = Mathf.Clamp(coolerInputKW, 0f, maxCoolerInputKW);
                    ((UI_FloatEdit)Fields[nameof(coolerInputKW)].uiControlEditor).maxValue = maxCoolerInputKW;
                    Fields[nameof(coolerInputKW)].uiControlEditor.onFieldChanged = delegate (BaseField field, object value)
                    {
                        massDirty = true;
                        CalculateMass();
                    };
                }
            }

            UpdateMLILayersDisplay();

            bool debugBoilActive = SupportsBoiloff && (RFSettings.Instance.debugBoilOff || RFSettings.Instance.debugBoilOffPAW);
            Fields[nameof(sWallTemp)].guiActive = debugBoilActive;
            Fields[nameof(sHeatPenetration)].guiActive = debugBoilActive;
            Fields[nameof(sBoiloffLoss)].guiActive = debugBoilActive;

            bool coolerFlightActive = HighLogic.LoadedSceneIsFlight && HasCryoCooler;
            Fields[nameof(sCoolerLift)].guiActive = coolerFlightActive;
            Fields[nameof(sCoolerDraw)].guiActive = coolerFlightActive;
            Fields[nameof(sCoolerCOP)].guiActive = coolerFlightActive;

            GameEvents.onPartResourceListChange.Add(OnPartResourceListChange);
            GameEvents.onPartDestroyed.Add(OnPartDestroyed);
        }

        partial void CalculateMassRF(ref double mass)
        {
            mass += MLIArealDensity * totalTankArea * totalMLILayers;
            if (HasCryoCooler)
                mass += coolerBaseMass + coolerMassPerKWInput.Evaluate(GetCoolerTargetTemp()) * coolerInputKW;
        }

        partial void GetModuleCostRF(ref double cost)
        {
            // Estimate material cost at 0.10764/m2 treating as Fund = $1000 (for RO purposes)
            // Plus another 0.1 for installation
            cost += MLIArealCost * totalTankArea * totalMLILayers;
            if (HasCryoCooler)
                cost += coolerBaseCost + coolerCostPerKWInput.Evaluate(GetCoolerTargetTemp()) * coolerInputKW;
        }

        partial void UpdateRF()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (UIPartActionController.Instance.GetItem(part) == null) return;

            if ((RFSettings.Instance.debugBoilOff || RFSettings.Instance.debugBoilOffPAW) && SupportsBoiloff)
            {
                sWallTemp = "";
                foreach (var tank in cryoTanks)
                    sWallTemp += $"{tank.internalTemp:F2} | ";
                if (!string.IsNullOrEmpty(sWallTemp))
                    sWallTemp = sWallTemp.Remove(sWallTemp.Length - 3);

                sHeatPenetration = "";
                sBoiloffLoss = "";
                foreach (var m in lossInfo)
                    sBoiloffLoss += $"{m:F4} {Localizer.GetStringByTag("#RF_FuelTankRF_Boiloffunit")} | "; // kg/hr
                foreach (var Q in fluxInfo)
                    sHeatPenetration += Utilities.FormatFlux(Q) + " | ";

                if (!string.IsNullOrEmpty(sBoiloffLoss))
                    sBoiloffLoss = sBoiloffLoss.Remove(sBoiloffLoss.Length - 3);
                if (!string.IsNullOrEmpty(sHeatPenetration))
                    sHeatPenetration = sHeatPenetration.Remove(sHeatPenetration.Length - 3);
            }

            if (HasCryoCooler)
            {
                sCoolerLift = Utilities.FormatFlux(currentCoolerLiftKW);
                sCoolerDraw = Utilities.FormatFlux(currentCoolerDrawKW);
                sCoolerCOP = currentCoolerCOP > 0d ? currentCoolerCOP.ToString("F3") : "—";
            }
        }

        public void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready)
            {
                // For analytic case boiloff will run though SetAnalyticTemperature()
                if (!_flightIntegrator.isAnalytical && SupportsBoiloff)
                    ProcessBoiloff(_flightIntegrator.timeSinceLastUpdate);
            }
        }

        private void UpdateMLILayersDisplay()
        {
            // Editor slider: clarify the number is additional when a base exists
            Fields[nameof(_numberOfAddedMLILayers)].guiUnits = numberOfMLILayers > 0
                ? " " + Localizer.Format("#RF_FuelTankRF_MLILayersTotal", totalMLILayers)
                : string.Empty;

            // Flight: show total only when non-zero; the added-layers slider is editor-only
            Fields[nameof(totalMLILayers)].guiActive = HighLogic.LoadedSceneIsFlight && totalMLILayers > 0;
        }

        private double GetCoolerTargetTemp()
        {
            double lowest = 300d;
            foreach (FuelTank tank in tanksDict.Values)
            {
                if (tank.amount > 0 && tank.vsp > 0 && tank.temperature < lowest)
                    lowest = tank.temperature;
            }

            return lowest;
        }

        /// <summary>
        /// Returns incoming heat flux in W for a single tank from the part skin,
        /// based on tank type and whether the part has MLI.
        /// </summary>
        private double GetIncomingFlux(double skinTemp, FuelTank tank)
        {
            if (tank.isDewar)
                return GetDewarTransferRate(skinTemp, tank.internalTemp, tank.totalArea, tank.Volume, totalMLILayers);

            if (totalMLILayers > 0)
                return GetMLITransferRate(skinTemp, tank.internalTemp, tank.totalArea, tank.Volume, totalMLILayers, vessel.staticPressurekPa);

            return GetBoiloffTransferRate(skinTemp, tank.internalTemp, tank.totalArea, tank);
        }

        /// <summary>
        /// Returns the structural thermal mass of the part (kJ/K): dry mass only, without resources and skin.
        /// Same terms KSP uses for part.thermalMass.
        /// </summary>
        private double ComputeStructuralThermalMass()
            => Math.Max(0d, part.mass * PhysicsGlobals.StandardSpecificHeatCapacity * part.thermalMassModifier - part.skinThermalMass);

        /// <summary>
        /// Returns the thermal mass (kJ/K) that KSP adds to part.thermalMass for resources in boiloff tanks.
        /// Uses the resource definition hsp, same as KSP.
        /// </summary>
        internal double GetBoiloffResourceThermalMass()
        {
            double thermalMass = 0d;
            for (int i = cryoTanks.Count; i-- > 0;)
            {
                PartResource res = cryoTanks[i].resource;
                if (res != null)
                    thermalMass += res.amount * res.info.density * res.info.specificHeatCapacity;
            }
            return thermalMass;
        }

        private double GetBoiloffTransferRate(double outerTemperature, double innerTemperature, double wettedArea, in FuelTank tank)
        {
            double deltaTemp = outerTemperature - innerTemperature;
            double wallFactor = tank.wallConduction > 0 ? tank.wallThickness / tank.wallConduction : 0;
            double insulationFactor = tank.insulationConduction > 0 ? tank.insulationThickness / tank.insulationConduction : 0;
            double resourceFactor = tank.resourceConductivity > 0 ? 0.01 / tank.resourceConductivity : 0;
            double divisor = Math.Max(double.Epsilon, wallFactor + insulationFactor + resourceFactor);

            return deltaTemp * wettedArea / divisor;
        }

        private void ProcessBoiloff(double deltaTime, bool analyticalMode = false)
        {
            Profiler.BeginSample("CalculateTankBoiloff");
            if (totalTankArea <= 0)
            {
                Debug.LogError("RF: CalculateTankBoiloff ran without calculating tank data!");
                CalculateTankArea();
            }

            double skinTemp = analyticalMode ? analyticSkinTemp : part.skinTemperature;

            if (double.IsNaN(skinTemp))
            {
                Debug.LogError($"RF: CalculateTankBoiloff found NaN skinTemperature on {part}");
                Profiler.EndSample();
                return;
            }

            boiloffMassT = 0d;
            lossInfo.Clear();
            fluxInfo.Clear();
            _perTankFlux.Clear();
            _perTankLift.Clear();

            bool hasCryoFuels = CalculateLowestTankTemperature();

            if (fueledByLaunchClamp)
            {
                if (hasCryoFuels)
                {
                    foreach (var tank in cryoTanks)
                        tank.internalTemp = tank.temperature;
                }
                fueledByLaunchClamp = false;
                currentCoolerLiftKW = 0d;
                currentCoolerDrawKW = 0d;
                currentCoolerCOP = 0d;
                Profiler.EndSample();
                return;
            }

            if (deltaTime <= 0 || CheatOptions.InfinitePropellant)
            {
                Profiler.EndSample();
                return;
            }

            // TODO: structuralThermalMass should be split up per-tank
            double structuralThermalMass = ComputeStructuralThermalMass();

            // Pre-pass: compute per-tank incoming flux and tally what the cryocooler could usefully lift.
            // Only at-boiling tanks are coolable; sub-boiling tanks are left to warm normally (no subcooling).
            // Non-cryo resources are not modelled here, their heat capacity stays in part.thermalMass.
            double totalCoolableKW = 0d;
            foreach (FuelTank tank in cryoTanks)
            {
                double q = GetIncomingFlux(skinTemp, tank) * 0.001d;
                _perTankFlux[tank] = q;
                if (tank.amount > 0 && q > 0 && tank.internalTemp >= tank.temperature)
                    totalCoolableKW += q;
            }

            double totalLiftKW = 0d, totalInputKW = 0d;
            if (hasCryoFuels)
                ApplyCryocooling(deltaTime, skinTemp, totalCoolableKW, out totalLiftKW, out totalInputKW);

            double totalAbsorbedQ_kW = 0d;
            foreach (FuelTank tank in cryoTanks)
            {
                double qIn = _perTankFlux.TryGetValue(tank, out double qv) ? qv : 0d;
                double lift = _perTankLift.TryGetValue(tank, out double lv) ? lv : 0d;
                totalAbsorbedQ_kW += CalculateBoiloffForTank(tank, qIn - lift, deltaTime, structuralThermalMass);
            }

            currentCoolerLiftKW = totalLiftKW;
            currentCoolerDrawKW = totalInputKW;
            currentCoolerCOP = totalInputKW > 0d ? totalLiftKW / totalInputKW : 0d;

            // Skin energy balance:
            //   skin → tanks: -totalAbsorbedQ_kW (net flux absorbed by tanks, after any cooling)
            //   tanks → skin via cooler: +totalLiftKW (heat pumped out of tanks)
            //   EC → skin via cooler: +totalInputKW (electrical work turns into heat in the warm end)
            // AddSkinThermalFlux takes kW and multiplies by TimeWarp.fixedDeltaTime internally,
            // so passing the rate is correct here. Skipped in analytic mode.
            if (!analyticalMode)
            {
                double skinFlux = -totalAbsorbedQ_kW + totalLiftKW + totalInputKW;
                if (skinFlux != 0d)
                    part.AddSkinThermalFlux(skinFlux);
            }

            Profiler.EndSample();
        }

        private void ApplyCryocooling(double deltaTime, double skinTemp, double totalCoolableKW, out double totalLiftKW, out double totalInputKW)
        {
            // Cryocooler allocation: COP = (T_cold / (T_hot - T_cold)) * fraction-of-Carnot curve
            totalLiftKW = 0d;
            totalInputKW = 0d;
            if (HasCryoCooler && totalCoolableKW > 0d && skinTemp > lowestTankTemperature)
            {
                double carnot = lowestTankTemperature / (skinTemp - lowestTankTemperature);
                double frac = cryoCoolerEfficiency.Evaluate(lowestTankTemperature);
                double cop = Math.Max(0d, carnot * frac);
                if (cop > 0d)
                {
                    double maxLiftKW = coolerInputKW * cop;
                    double wantedLiftKW = Math.Min(maxLiftKW, totalCoolableKW);
                    double wantedInputKW = wantedLiftKW / cop;
                    double requestedEC = wantedInputKW * deltaTime;
                    double receivedEC = requestedEC > 0d
                        ? part.RequestResource("ElectricCharge", requestedEC, ResourceFlowMode.ALL_VESSEL)
                        : 0d;

                    double scale = requestedEC > 0d ? receivedEC / requestedEC : 0d;
                    if (scale < 0d) scale = 0d;
                    else if (scale > 1d) scale = 1d;

                    totalLiftKW = wantedLiftKW * scale;
                    totalInputKW = wantedInputKW * scale;

                    if (totalLiftKW > 0d)
                    {
                        double liftFrac = totalLiftKW / totalCoolableKW;
                        foreach (FuelTank tank in cryoTanks)
                        {
                            if (tank.amount > 0 && tank.vsp > 0
                                && tank.internalTemp >= tank.temperature
                                && _perTankFlux.TryGetValue(tank, out double q) && q > 0)
                            {
                                _perTankLift[tank] = q * liftFrac;
                            }
                        }
                    }
                }
            }
        }

        private double CalculateBoiloffForTank(FuelTank tank, double Q_kW, double deltaTime, double structuralThermalMass)
        {
            if (tank.totalArea <= 0 || tank.tankRatio <= 0)
                return 0d;

            double thermalMass = structuralThermalMass * tank.tankRatio
                               + tank.amount * tank.density * tank.hsp;
            thermalMass = Math.Max(thermalMass, 1.0);

            if (tank.internalTemp < tank.temperature)
            {
                // Sub-boiling: heat toward boiling point
                tank.internalTemp += Q_kW * deltaTime / thermalMass;
                if (tank.internalTemp > tank.temperature)
                    tank.internalTemp = tank.temperature;
                return Q_kW > 0 ? Q_kW : 0d;
            }
            else
            {
                // At or above boiling point: phase transition holds temperature, all flux → boiloff
                tank.internalTemp = tank.temperature;

                double tankAmount = tank.amount;
                if (tankAmount <= 0 || Q_kW <= 0) return 0d;

                double massLost = Q_kW / tank.vsp * deltaTime;

                lossInfo.Add(Q_kW / tank.vsp * 1000d * 3600d); // kg/hr for display
                fluxInfo.Add(Q_kW);

                double d = tank.density > 0 ? tank.density : 1;
                double lossAmount = Math.Min(massLost / d, tankAmount);

                if (lossAmount > 0)
                {
                    tank.resource.amount -= lossAmount;

                    if (tank.boiloffProductResource != null)
                    {
                        double boiloffProductAmount = -(massLost / tank.boiloffProductResource.density);
                        double retainedAmount = part.RequestResource(tank.boiloffProductResource.id, boiloffProductAmount, ResourceFlowMode.STAGE_PRIORITY_FLOW, Utilities.KerbalismFound);
                        massLost -= retainedAmount * tank.boiloffProductResource.density;

                        if (Utilities.KerbalismFound)
                        {
                            string rName = tank.boiloffProductResource.name;
                            retainedAmount /= deltaTime;
                            boiloffProducts[rName] = boiloffProducts.TryGetValue(rName, out double v) ? v + retainedAmount : retainedAmount;
                        }
                    }
                }

                boiloffMassT += massLost;
                return Q_kW;
            }
        }

        partial void UpdateTankTypeRF(TankDefinition def)
        {
            // Get pressurization
            highlyPressurized = def.highlyPressurized;
            numberOfMLILayers = def.numberOfMLILayers;

            maxMLILayers = def.maxMLILayers >= 0 ? def.maxMLILayers : (int)Fields[nameof(maxMLILayers)].originalValue;
            minUtilization = def.minUtilization > 0 ? def.minUtilization : (float)Fields[nameof(minUtilization)].originalValue;
            maxUtilization = def.maxUtilization > 0 ? def.maxUtilization : (float)Fields[nameof(maxUtilization)].originalValue;

            if (HighLogic.LoadedSceneIsEditor && started)
            {
                Fields[nameof(_numberOfAddedMLILayers)].guiActiveEditor = maxMLILayers > 0;
                _numberOfAddedMLILayers = Mathf.Clamp(_numberOfAddedMLILayers, 0, maxMLILayers);
                ((UI_FloatRange)Fields[nameof(_numberOfAddedMLILayers)].uiControlEditor).maxValue = maxMLILayers;
            }
            totalMLILayers = numberOfMLILayers + numberOfAddedMLILayers;
            if (started) UpdateMLILayersDisplay();

            InitUtilization();

            if (HighLogic.LoadedScene == GameScenes.LOADING)
                UpdateEngineIgnitor(def);
        }

        private void UpdateEngineIgnitor(TankDefinition def)
        {
            pressurizedFuels.Clear();
            foreach (var f in tanksDict.Values)
                pressurizedFuels[f.name] = def.highlyPressurized || f.note.ToLower().Contains("pressurized");
        }

        // Fired from ProcParts when updating the collider and drag cubes, after OnPartVolumeChanged
        [KSPEvent(guiActive = false, active = true)]
        public void OnPartColliderChanged() => CalculateTankArea();

        [KSPEvent]
        public void OnResourceMaxChanged(BaseEventDetails _) => CalculateTankArea();
        private void OnPartResourceListChange(Part p)
        {
            if (p == part)
                CalculateTankArea();
        }

        private void UpdateDragCubes()
        {
            DragCube dragCube = DragCubeSystem.Instance.RenderProceduralDragCube(part);
            part.DragCubes.ClearCubes();
            part.DragCubes.Cubes.Add(dragCube);
            part.DragCubes.ResetCubeWeights();
            part.DragCubes.ForceUpdate(true, true, false);
        }

        public void CalculateTankArea()
        {
            SetTankAreaInfo(volume);

            double areaSpherical = SphericalAreaFromVolume(totalVolume);
            double areaPartsSpherical = CalculateTankAreaFromSphericalSubTanks();

            totalTankArea = Math.Max(areaSpherical, areaPartsSpherical);
            totalTankArea = Math.Max(totalTankArea, 0.1);
            if (RFSettings.Instance.debugBoilOff || RFSettings.Instance.debugBoilOffPAW)
            {
                double areaCubes = CalculateTankAreaCubes();
                Debug.Log($"[RealFuels.ModuleFuelTankRF] {part.name} Area Calcs: DragCube: {areaCubes:F2} | TotalSpherical: {areaSpherical:F2} | SubTankSpherical: {areaPartsSpherical:F2}");
                Debug.Log($"[RealFuels.ModuleFuelTankRF] {part.name}.totalTankArea = {totalTankArea:F2}");
            }
        }

        private void OnPartDestroyed(Part p)
        {
            if (p == part)
            {
                GameEvents.onPartDestroyed.Remove(OnPartDestroyed);
                GameEvents.onPartResourceListChange.Remove(OnPartResourceListChange);
            }
        }

        private bool CalculateLowestTankTemperature()
        {
            lowestTankTemperature = 300;
            foreach (var tank in cryoTanks)
                if (tank.temperature < lowestTankTemperature)
                    lowestTankTemperature = tank.temperature;
            return cryoTanks.Count > 0;
        }

        #region IAnalyticTemperatureModifier
        public void SetAnalyticTemperature(FlightIntegrator fi, double analyticTemp, double predictedInternalTemp, double predictedSkinTemp)
        {
            analyticSkinTemp = predictedSkinTemp;

            if (SupportsBoiloff)
            {
                DebugLog($"{part.name} Analytic Temp = {analyticTemp:F2}, Analytic Skin = {predictedSkinTemp:F2}");

                if (fi.timeSinceLastUpdate < double.MaxValue)
                {
                    double remainingTime;
                    if (bgBoiloffLastUpdate > 0d)
                    {
                        remainingTime = Math.Max(0d, Planetarium.GetUniversalTime() - bgBoiloffLastUpdate);
                        bgBoiloffLastUpdate = 0d;
                    }
                    else
                    {
                        remainingTime = fi.timeSinceLastUpdate;
                    }

                    if (remainingTime > 0d)
                        ProcessBoiloff(remainingTime, fi.isAnalytical);
                }
                else if (CalculateLowestTankTemperature())
                {
                    // Vessel is freshly spawned with cryogenic tanks — initialise per-tank state only
                    foreach (var tank in cryoTanks)
                        tank.internalTemp = tank.temperature;
                }
            }
        }

        public double GetSkinTemperature(out bool lerp)
        {
            lerp = false;
            return Math.Max(analyticSkinTemp, PhysicsGlobals.SpaceTemperature);
        }

        // Return skin temp as the internal temp: the part structure has no meaningful insulation
        // from the skin surface, so it equilibrates with it. Propellant thermal state is managed
        // independently via per-tank internalTemp and must not inflate part.thermalMass here.
        public double GetInternalTemperature(out bool lerp)
        {
            lerp = false;
            return Math.Max(analyticSkinTemp, PhysicsGlobals.SpaceTemperature);
        }
        #endregion

        #region Analytic Preview Interface
        public void AnalyticInfo(FlightIntegrator fi, double sunAndBodyIn, double backgroundRadiation, double radArea, double absEmissRatio, double internalFlux, double convCoeff, double ambientTemp, double maxPartTemp)
        {
            if (!fi.isAnalytical)
                Debug.Log($"[MFTRF] AnalyticInfo called in non-analytic mode for {vessel}.  dT: {fi.timeSinceLastUpdate:F2}s");
        }

        public double InternalFluxAdjust() => 0;
        #endregion

        [System.Diagnostics.Conditional("DEBUG")]
        void DebugLog(string msg)
        {
            Debug.Log("[RealFuels.ModuleFuelTankRF] " + msg);
        }

#region Cryogenics

        // Heat leak into cryogenic tanks, in W:
        //   Q = A * q_blanket(N) + A * q_gas(N, P) + G_struct(N, V) * (T_outer - T_inner)
        // A is tank area (m^2), V tank volume (L), N the MLI layer count and P ambient pressure.
        // N also stands for the overall thermal design of the tank: skirts, struts, plumbing and
        // engine mounts improve together with the blanket. 1 layer is basic construction and
        // 100 layers is the best known design.

        private const int MaxDesignLayers = 100;

        // Lockheed solid conduction term Nd^2.56 for a layer density Nd of 10.055 layers/cm
        private static readonly double LayerDensityTerm = Math.Pow(10.055, 2.56);

        /// <summary>
        /// Returns the heat leak in W into an MLI-insulated stage tank.
        /// </summary>
        /// <remarks>
        /// Blanket flux comes from <see cref="GetMLIBlanketFlux"/>. Gas conduction through the blanket
        /// uses q_gas = Qv * P * (Th^0.5 - Tc^0.5) / N, with P in torr and Qv = RFSettings.QvCoefficient.
        /// The gas term assumes free molecular flow (Knudsen number above 10), so pressure is capped at 0.1 kPa.
        /// Structure uses the stage end points of <see cref="GetStructuralConductance"/>.
        /// </remarks>
        private static double GetMLITransferRate(double outerTemp, double innerTemp, double area, double volumeLiters, int mliLayers, double pressureKPa = 0)
        {
            int layers = Math.Max(mliLayers, 1);

            double gasFlux = 0;
            if (pressureKPa > 0)
            {
                double pressureTorr = Math.Min(pressureKPa, 0.1) * 7.500616851;
                gasFlux = RFSettings.Instance.QvCoefficient * pressureTorr * (Math.Sqrt(outerTemp) - Math.Sqrt(innerTemp)) / layers; // [W/m^2]
            }

            double flux = GetMLIBlanketFlux(outerTemp, innerTemp, layers) + gasFlux; // [W/m^2]
            double conductance = GetStructuralConductance(volumeLiters, layers,
                RFSettings.Instance.stageStructuralConductanceBasic,
                RFSettings.Instance.stageStructuralConductanceBest); // [W/K]

            return flux * area + conductance * (outerTemp - innerTemp); // [W]
        }

        /// <summary>
        /// Returns the heat leak in W into a vacuum-jacketed (Dewar) tank.
        /// </summary>
        /// <remarks>
        /// The blanket sits inside a vacuum jacket. It performs as in hard vacuum at any ambient pressure,
        /// so there is no gas conduction term. Without added layers the jacket wall still acts as one
        /// reflective layer.
        /// The tank hangs on struts instead of load-bearing skirts, so structure uses the Dewar end points
        /// of <see cref="GetStructuralConductance"/>. These are 15x lower than for a stage tank at the same
        /// layer count, which makes Dewars the choice for small cryogenic tanks.
        /// </remarks>
        private static double GetDewarTransferRate(double outerTemp, double innerTemp, double area, double volumeLiters, int mliLayers)
        {
            int layers = Math.Max(mliLayers, 1);

            double flux = GetMLIBlanketFlux(outerTemp, innerTemp, layers); // [W/m^2]
            double conductance = GetStructuralConductance(volumeLiters, layers,
                RFSettings.Instance.dewarStructuralConductanceBasic,
                RFSettings.Instance.dewarStructuralConductanceBest); // [W/K]

            return flux * area + conductance * (outerTemp - innerTemp); // [W]
        }

        /// <summary>
        /// Returns the heat flux in W/m^2 through an installed MLI blanket in hard vacuum.
        /// </summary>
        /// <remarks>
        /// Lockheed equation for double aluminized Mylar with silk net spacers, NASA CR-134477 Eq. 4-14:
        ///   q = Cs * Nd^2.56 * Tm * (Th - Tc) / (N + 1) + Cr * e * (Th^4.67 - Tc^4.67) / N
        /// Cs = 8.95e-8, Cr = 5.39e-10, e = 0.031, Nd = 10.055 layers/cm, Tm = (Th + Tc) / 2.
        /// The coefficients are scaled from the original mW/m^2 to W/m^2.
        ///
        /// The equation was fitted for cold boundaries above 70 K but also holds for LH2. At 45 layers and
        /// 305 K it gives 0.18 W/m^2, while the 18 m^3 MHTB LH2 tank measured 0.22 W/m^2 (NASA TM-2001-211089).
        ///
        /// The equation describes a bare blanket. Installed blankets lose more through seams, pins and
        /// penetrations, so the result is multiplied by RFSettings.mliInstallationFactor (1.5):
        /// - LZBO tank, 75 layers, 220 K (Johnson, NASA TFAWS MLI course): a Lockheed equation (CR-134477
        ///   Eq. 4-56) predicted 0.66 W, measured was 2.6 W. Seams added 0.39 W, pins 0.30 W and
        ///   penetrations 0.25 W.
        /// - Titan/Centaur LH2 tank sidewall, 3 layers (AIAA 2007-5845): flight data 3.2-4.7 W/m^2,
        ///   the equation gives 2.4 W/m^2.
        /// </remarks>
        private static double GetMLIBlanketFlux(double outerTemp, double innerTemp, int layers)
        {
            const double solidCoefficient = 8.95e-8;
            const double radiationCoefficient = 5.39e-10;
            const double emissivity = 0.031;

            double meanTemp = (outerTemp + innerTemp) * 0.5;
            double solid = solidCoefficient * LayerDensityTerm * meanTemp * (outerTemp - innerTemp) / (layers + 1);
            double radiation = radiationCoefficient * emissivity * (Math.Pow(outerTemp, 4.67) - Math.Pow(innerTemp, 4.67)) / layers;

            return RFSettings.Instance.mliInstallationFactor * (solid + radiation); // [W/m^2]
        }

        /// <summary>
        /// Returns the thermal conductance in W/K through everything that is not MLI blanket:
        /// skirts, struts, feedlines, wiring, engine mounts and common bulkheads.
        /// </summary>
        /// <remarks>
        /// G = c(N) * V^(1/3), with V in liters and c in W/K per L^(1/3).
        /// c(N) = c_basic * (c_best / c_basic)^(log10(N) / 2), N clamped to 1..100.
        /// This is log-linear in N, from c_basic at 1 layer to c_best at 100 layers.
        ///
        /// A skirt conducts pi * D * t * integral(k dT) / L. With fixed wall thickness t and length L
        /// it scales with tank diameter D, which is proportional to V^(1/3).
        ///
        /// Stage tanks (RFSettings.stageStructuralConductanceBasic/Best):
        /// - 1 layer, c = 0.15: bare aluminium skirts and metal mounts. On SHIIVER (NASA TP-20205008233)
        ///   an Al 6061 skirt, 4.76 mm thick and 1.52 m long, conducted 1100-1250 W into a 4 m diameter,
        ///   31.1 m^3 LH2 tank. That is about 90 W per meter of circumference, or c = 0.12-0.14 per skirt.
        /// - 3 layers, c = 0.059: Titan/Centaur as flown with 3-layer MLI (AIAA 2007-5845). The 53.5 m^3
        ///   LH2 tank took 733-909 W, the 16.3 m^3 LO2 tank 381-615 W net. Without the blanket share
        ///   (skin at 300 K) this gives c = 0.046-0.095. This lumps in the engine mounts, RCS and the
        ///   common bulkhead.
        /// - 100 layers, c = 0.003: ULA's long-term Centaur projection of about 0.1%/day system boiloff,
        ///   with composite struts, vapor-cooled engine mount and forward bulkhead, a better common
        ///   bulkhead and the RCS moved off the tanks. The model gives 0.07%/day for Centaur-size tanks.
        ///
        /// Dewar tanks (RFSettings.dewarStructuralConductanceBasic/Best), supported by struts:
        /// - 1 layer, c = 0.01: plumbing and wiring of a flight tank without skirts. The Centaur G-prime
        ///   LH2 tank penetrations took 108 W on 53.5 m^3 (NASA TM-89825).
        /// - 100 layers, c = 0.0002: MHTB, an 18 m^3 LH2 test tank on 12 fiberglass struts. Struts,
        ///   plumbing and instrumentation were 13-17% of its total heat input, about 1.2-1.6 W.
        /// </remarks>
        private static double GetStructuralConductance(double volumeLiters, int layers, double basic, double best)
        {
            if (basic <= 0 || best <= 0)
                return 0d;

            double n = Math.Min(Math.Max(layers, 1), MaxDesignLayers);
            double c = basic * Math.Pow(best / basic, Math.Log10(n) / Math.Log10(MaxDesignLayers));
            return c * Math.Pow(volumeLiters, 1d / 3); // [W/K]
        }

        #endregion

        #region Kerbalism

        /// <summary>
        /// Called by Kerbalism for unloaded (background) vessels via reflection.
        /// For each cryo propellant, computes boiloff directly from the Kerbalism vessel temperature
        /// using the appropriate heat-transfer formula (MLI, conduction, or Dewar radiation).
        /// </summary>
        public static string BackgroundUpdate(
            Vessel vessel,
            ProtoPartSnapshot proto_part,
            ProtoPartModuleSnapshot proto_module,
            PartModule partModule,
            Part part,
            Dictionary<string, double> availableResources,
            List<KeyValuePair<string, double>> resourceChangeRequest,
            double elapsed_s)
        {
            using var auto = _profileBgUpdt.Auto();
            bool hasGeometry = KerbalismInterface.TryGetThermalData(vessel, out double vesselTemp, out _);
            if (!hasGeometry)
                return string.Empty;

            if (!bgCache.TryGetValue(proto_module, out BgBoiloffCache cache))
            {
                using var auto2 = _profileBgUpdtCache.Auto();
                string data = proto_module.moduleValues.GetValue(nameof(bgBoiloffData));
                if (string.IsNullOrEmpty(data))
                    return string.Empty;

                string coolerData = proto_module.moduleValues.GetValue(nameof(bgCoolerData)) ?? "";
                int.TryParse(proto_module.moduleValues.GetValue(nameof(totalMLILayers)), out int mliLayers);
                cache = BgBoiloffCache.Build(data, coolerData, mliLayers);
                cache.InitTemps(proto_module);

                bgCache.Remove(proto_module);
                bgCache.Add(proto_module, cache);
            }

            double totalCoolableKW = ProcessBackgroundHeatLeakage(vesselTemp, cache);
            double liftFrac = ProcessBackgroundCryocooling(availableResources, resourceChangeRequest, elapsed_s, vesselTemp, cache, totalCoolableKW);
            bool anyBoiloff = ApplyBackgroundTankFlux(availableResources, resourceChangeRequest, elapsed_s, cache, liftFrac);

            return anyBoiloff ? Localizer.GetStringByTag("#RF_FuelTankRF_kerbalismtips") : string.Empty;
        }

        /// <summary>
        /// compute per-tank Q_kW and tally coolable demand
        /// </summary>
        /// <param name="vesselTemp"></param>
        /// <param name="cache"></param>
        /// <returns></returns>
        private static double ProcessBackgroundHeatLeakage(double vesselTemp, BgBoiloffCache cache)
        {
            Profiler.BeginSample("ProcessBackgroundHeatLeakage");
            double totalCoolableKW = 0d;
            for (int i = 0; i < cache.Tanks.Length; i++)
            {
                BgTankEntry entry = cache.Tanks[i];
                double internalTemp = cache.InternalTemps[i];
                double q;
                if (entry.IsDewar)
                    q = GetDewarTransferRate(vesselTemp, internalTemp, entry.TankAreaM2, entry.Volume, cache.MliLayers) * 0.001;
                else if (cache.MliLayers > 0 && entry.TankAreaM2 > 0)
                    q = GetMLITransferRate(vesselTemp, internalTemp, entry.TankAreaM2, entry.Volume, cache.MliLayers) * 0.001;
                else if (entry.ConductWPerK > 0)
                    q = entry.ConductWPerK * (vesselTemp - internalTemp) * 0.001;
                else
                    q = 0d;

                cache.FluxScratch[i] = q;
                if (q > 0 && internalTemp >= entry.BoilingPointK)
                    totalCoolableKW += q;
            }
            Profiler.EndSample();

            return totalCoolableKW;
        }

        private static double ProcessBackgroundCryocooling(Dictionary<string, double> availableResources, List<KeyValuePair<string, double>> resourceChangeRequest, double elapsed_s, double vesselTemp, BgBoiloffCache cache, double totalCoolableKW)
        {
            Profiler.BeginSample("ProcessBackgroundCryocooling");
            double liftFrac = 0d;
            if (cache.CoolerInputKW > 0d && cache.CoolerFrac > 0d && totalCoolableKW > 0d
                && vesselTemp > cache.CoolerLowestTempK)
            {
                double carnot = cache.CoolerLowestTempK / (vesselTemp - cache.CoolerLowestTempK);
                double cop = carnot * cache.CoolerFrac;
                if (cop > 0d)
                {
                    double maxLiftKW = cache.CoolerInputKW * cop;
                    double wantedLiftKW = Math.Min(maxLiftKW, totalCoolableKW);
                    double wantedInputKW = wantedLiftKW / cop;
                    double ecAvail = availableResources.TryGetValue("ElectricCharge", out double ev) ? ev : 0d;
                    double requestedEC = wantedInputKW * elapsed_s;
                    double scale = requestedEC > 0d ? Math.Min(1d, ecAvail / requestedEC) : 0d;
                    if (scale < 0d) scale = 0d;
                    double actualInputKW = wantedInputKW * scale;
                    double actualLiftKW = wantedLiftKW * scale;
                    if (actualInputKW > 0d)
                        resourceChangeRequest.Add(new KeyValuePair<string, double>("ElectricCharge", -actualInputKW));
                    if (actualLiftKW > 0d)
                        liftFrac = actualLiftKW / totalCoolableKW;
                }
            }
            Profiler.EndSample();

            return liftFrac;
        }

        private static bool ApplyBackgroundTankFlux(Dictionary<string, double> availableResources, List<KeyValuePair<string, double>> resourceChangeRequest, double elapsed_s, BgBoiloffCache cache, double liftFrac)
        {
            Profiler.BeginSample("ApplyBackgroundTankFlux");
            bool anyRequest = false;
            for (int i = 0; i < cache.Tanks.Length; i++)
            {
                BgTankEntry entry = cache.Tanks[i];
                double internalTemp = cache.InternalTemps[i];
                double Q_kW = cache.FluxScratch[i];

                if (Q_kW > 0d && internalTemp >= entry.BoilingPointK && liftFrac > 0d)
                    Q_kW -= Q_kW * liftFrac;

                if (Q_kW == 0d) continue;

                if (internalTemp < entry.BoilingPointK)
                {
                    // Sub-boiling: advance internalTemp through thermal mass
                    double amount = availableResources.TryGetValue(entry.Name, out double a) ? a : 0d;
                    double thermalMass = entry.StructThermalMassKJ + amount * entry.Density * entry.Hsp;
                    thermalMass = Math.Max(thermalMass, 1.0);
                    cache.InternalTemps[i] = Math.Min(internalTemp + Q_kW * elapsed_s / thermalMass, entry.BoilingPointK);
                }
                else if (Q_kW > 0)
                {
                    // At boiling point: residual flux drives boiloff
                    double rateKgS = Q_kW / entry.Vsp;
                    resourceChangeRequest.Add(new KeyValuePair<string, double>(entry.Name, -rateKgS / entry.Density));
                    anyRequest = true;
                }
            }
            Profiler.EndSample();

            return anyRequest;
        }

        internal static void PersistBackgroundTankTemps(ProtoPartModuleSnapshot proto_module, BgBoiloffCache cache)
        {
            Profiler.BeginSample("PersistBackgroundTankTemps");
            foreach (ConfigNode tankNode in proto_module.moduleValues.GetNodes("TANK"))
            {
                string tName = tankNode.GetValue("name");
                if (tName == null) continue;
                for (int i = 0; i < cache.Tanks.Length; i++)
                {
                    if (cache.Tanks[i].Name == tName)
                    {
                        tankNode.SetValue("internalTemp", cache.InternalTemps[i], true);
                        break;
                    }
                }
            }
            Profiler.EndSample();
        }

        /// <summary>
        /// Called by Kerbalism every frame for loaded vessels. Uses their resource system when Kerbalism is installed.
        /// </summary>
        public virtual string ResourceUpdate(Dictionary<string, double> availableResources, List<KeyValuePair<string, double>> resourceChangeRequest)
        {
            foreach (var resourceRequest in boiloffProducts)
            {
                var definition = PartResourceLibrary.Instance.GetDefinition(resourceRequest.Key);
                if (definition is null)
                    continue;

                resourceChangeRequest.Add(new KeyValuePair<string, double>(resourceRequest.Key, -resourceRequest.Value));
            }
            boiloffProducts.Clear();

            return Localizer.GetStringByTag("#RF_FuelTankRF_kerbalismtips"); // "boiloff product"
        }

        #endregion

        #region Tank Dimensions

        private void SetTankAreaInfo(double volume)
        {
            foreach (var tank in tanksDict.Values)
            {
                double amt = tank.maxAmount;
                if (amt > 0 && tank.utilization > 0)
                    amt /= tank.utilization;
                tank.totalArea = SphericalAreaFromVolume(amt);
                tank.tankRatio = amt / volume;
            }
        }

        private double CalculateTankAreaCubes()
        {
            double area = 0;
            if (IsProcedural && !part.DragCubes.None)
                UpdateDragCubes();
            // part.DragCubes.WeightedArea hasn't been computed in Editor
            // Recompute from the base cubes.
            foreach (var cube in part.DragCubes?.Cubes)
                for (int i = 0; i < 6; i++)
                    area += cube.Weight * cube.Area[i];
            return area;
        }

        private double SphericalAreaFromVolume(double volume)
        {
            double radius = Math.Pow(volume * 0.001 * 0.75f / Math.PI, 1f / 3);
            double area = 4 * Math.PI * radius * radius;
            return area;
        }

        private double CalculateTankAreaFromSphericalSubTanks()
        {
            double area = 0;
            foreach (var tank in tanksDict.Values)
                area += tank.totalArea;
            /*
            if (RFSettings.Instance.debugBoilOff)
                Debug.Log($"[RealFuels.ModuleFuelTankRF] {tank.name} (isDewar: {tank.isDewar}): tankRatio = {tank.tankRatio:F2} | maxAmount = {tank.maxAmount:F2} | surface area = {tank.totalArea}");
            */
            return area;
        }

#endregion
    }
}
