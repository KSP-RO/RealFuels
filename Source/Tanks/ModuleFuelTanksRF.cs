using KSP.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
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

        [KSPField(isPersistant = true, guiActive= true, guiActiveEditor = true, guiName = "#RF_FuelTankRF_MLILayers",
            groupName = CryogenicGroupName, groupDisplayName = CryogenicsGroupDisplayName, guiFormat = "F0"), // MLI Layers
        UI_FloatRange(minValue = 0, maxValue = 100, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float _numberOfAddedMLILayers = 0; // This is the number of layers added by the player.
        public int numberOfAddedMLILayers => (int)_numberOfAddedMLILayers;

        [KSPField(isPersistant = true)]
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

        [KSPField]
        public int maxMLILayers = 10;

        [KSPField]
        public float MLIArealCost = 0.20764f;

        [KSPField]
        public float MLIArealDensity = 0.000015f;

        private double analyticSkinTemp;
        private readonly Dictionary<string, double> boiloffProducts = new Dictionary<string, double>();

        public int numberOfMLILayers = 0; // base number of layers taken from TANK_DEFINITION configs

        private double boiloffMassT = 0d;
        public double BoiloffMassRate => boiloffMassT;

        private readonly List<FuelTank> cryoTanks = new List<FuelTank>();   // anything with maxAmount > 0 && vsp > 0
        private readonly List<double> lossInfo = new List<double>();
        private readonly List<double> fluxInfo = new List<double>();

        // Pre-parsed tank boiloff data for background processing.
        private static readonly ConditionalWeakTable<ProtoPartModuleSnapshot, BgBoiloffCache> _bgCache
            = new ConditionalWeakTable<ProtoPartModuleSnapshot, BgBoiloffCache>();

        // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
        [NonSerialized]
        public Dictionary<string, bool> pressurizedFuels = new Dictionary<string, bool>();

        private FlightIntegrator _flightIntegrator;

        double lowestTankTemperature = 300d;

        private static double ConductionFactors => RFSettings.Instance.globalConductionCompensation ? Math.Max(1.0d, PhysicsGlobals.ConductionFactor) : 1d;
        public bool SupportsBoiloff => cryoTanks.Count > 0;
        private bool IsProcedural => part.Modules.Contains("SSTUModularPart") || part.Modules.Contains("WingProcedural");

        partial void OnLoadRF(ConfigNode _) { }

        partial void OnAwakeRF()
        {
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
            if (!HighLogic.LoadedSceneIsFlight || cryoTanks.Count == 0)
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

                entries.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4},{5:R},{6:R}",
                    tank.name, tank.temperature, tankAreaM2, conductWPerK, isDewar,
                    tank.hsp, structuralThermalMass * tank.tankRatio));
            }

            bgBoiloffData = entries.Count > 0 ? string.Join(";", entries) : "";
        }

        partial void OnStartRF(StartState _)
        {
            if (HighLogic.LoadedSceneIsFlight)
                _flightIntegrator = vessel.vesselModules.Find(x => x is FlightIntegrator) as FlightIntegrator;

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
                    massDirty = true;
                    CalculateMass();
                };
            }

            bool debugBoilActive = SupportsBoiloff && (RFSettings.Instance.debugBoilOff || RFSettings.Instance.debugBoilOffPAW);
            Fields[nameof(sWallTemp)].guiActive = debugBoilActive;
            Fields[nameof(sHeatPenetration)].guiActive = debugBoilActive;
            Fields[nameof(sBoiloffLoss)].guiActive = debugBoilActive;

            GameEvents.onPartResourceListChange.Add(OnPartResourceListChange);
            GameEvents.onPartDestroyed.Add(OnPartDestroyed);
        }

        partial void CalculateMassRF(ref double mass)
        {
            mass += MLIArealDensity * totalTankArea * totalMLILayers;
        }

        partial void GetModuleCostRF(ref double cost)
        {
            // Estimate material cost at 0.10764/m2 treating as Fund = $1000 (for RO purposes)
            // Plus another 0.1 for installation
            cost += MLIArealCost * totalTankArea * totalMLILayers;
        }

        partial void UpdateRF()
        {
            if (HighLogic.LoadedSceneIsFlight && (RFSettings.Instance.debugBoilOff || RFSettings.Instance.debugBoilOffPAW) && SupportsBoiloff &&
                UIPartActionController.Instance.GetItem(part) != null)
            {
                sWallTemp = "";
                foreach (var tank in cryoTanks)
                    sWallTemp += $"{tank.internalTemp:F2} | ";
                if (!string.IsNullOrEmpty(sWallTemp))
                    sWallTemp = sWallTemp.Remove(sWallTemp.Length - 3);
                string MLIText = totalMLILayers > 0 ? $"{GetMLITransferRate(part.skinTemperature, lowestTankTemperature):F2} W/m²" : Localizer.GetStringByTag("#RF_FuelTankRF_NoMLI"); // "No MLI"
                sWallTemp += $" ({MLIText} * {part.radiativeArea:F2} m²)";

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
        }

        public void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready)
            {
                if (!_flightIntegrator.isAnalytical && SupportsBoiloff)
                    ProcessBoiloff(_flightIntegrator.timeSinceLastUpdate);
            }
        }

        /// <summary>
        /// Returns incoming heat flux in W for a single tank from the part skin,
        /// based on tank type and whether the part has MLI.
        /// </summary>
        private double GetIncomingFlux(double skinTemp, FuelTank tank)
        {
            if (tank.isDewar)
                return GetDewarTransferRate(skinTemp, tank.internalTemp, tank.totalArea);

            if (totalMLILayers > 0)
                return GetMLITransferRate(skinTemp, tank.internalTemp) * tank.totalArea;

            return GetBoiloffTransferRate(skinTemp, tank.internalTemp, tank.totalArea, tank);
        }

        /// <summary>
        /// Returns the structural thermal mass of the part (kJ/K), i.e. part.thermalMass
        /// minus the thermal contribution of all resources using KSP's standard specific heat.
        /// </summary>
        private double ComputeStructuralThermalMass()
        {
            double resourceThermalMass = 0;
            foreach (PartResource res in part.Resources)
                resourceThermalMass += res.amount * res.info.density * PhysicsGlobals.StandardSpecificHeatCapacity;
            return Math.Max(0, part.thermalMass - resourceThermalMass);
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

            bool hasCryoFuels = CalculateLowestTankTemperature();
            if (hasCryoFuels && MFSSettings.radiatorMinTempMult >= 0d)
                part.radiatorMax = lowestTankTemperature * MFSSettings.radiatorMinTempMult / part.maxTemp;

            if (fueledByLaunchClamp)
            {
                if (hasCryoFuels)
                {
                    foreach (var tank in cryoTanks)
                        tank.internalTemp = tank.temperature;
                }
                fueledByLaunchClamp = false;
                Profiler.EndSample();
                return;
            }

            if (deltaTime <= 0 || CheatOptions.InfinitePropellant)
            {
                Profiler.EndSample();
                return;
            }

            // TODO: structuralThermalMass should be split up per-tank
            // TODO2: KSP will internally still assign a part.thermalMass value that includes resources
            double structuralThermalMass = ComputeStructuralThermalMass();
            double totalAbsorbedQ_kW = 0d;

            foreach (FuelTank tank in tanksDict.Values)
            {
                totalAbsorbedQ_kW += CalculateBoiloffForTank(tank, deltaTime, skinTemp, structuralThermalMass);
            }

            // Feed the absorbed heat back as a skin heat sink. AddSkinThermalFlux takes kW and
            // multiplies by TimeWarp.fixedDeltaTime internally, so passing the rate is correct here.
            // Skipped in analytic mode: should only have a very small effect.
            if (!analyticalMode && totalAbsorbedQ_kW != 0)
                part.AddSkinThermalFlux(-totalAbsorbedQ_kW);

            Profiler.EndSample();
        }

        private double CalculateBoiloffForTank(FuelTank tank, double deltaTime, double skinTemp, double structuralThermalMass)
        {
            if (tank.totalArea <= 0 || tank.tankRatio <= 0)
                return 0d;

            double Q_kW = GetIncomingFlux(skinTemp, tank) * 0.001d;

            double thermalMass = structuralThermalMass * tank.tankRatio
                               + tank.amount * tank.density * tank.hsp;
            thermalMass = Math.Max(thermalMass, 1.0);

            if (tank.vsp <= 0 || tank.internalTemp < tank.temperature)
            {
                if (tank.vsp <= 0)
                {
                    // Non-cryo: clamp internalTemp at skinTemp to prevent overshoot.
                    // At high warp the large deltaTime can push internalTemp past skinTemp in a
                    // single step; the resulting oscillation with asymmetric flux handling bleeds
                    // energy from the skin each cycle, eventually driving it to 0 K (or infinity).
                    double prevTemp = tank.internalTemp;
                    double newTemp = prevTemp + Q_kW * deltaTime / thermalMass;
                    newTemp = Q_kW >= 0 ? Math.Min(newTemp, skinTemp) : Math.Max(newTemp, skinTemp);
                    tank.internalTemp = newTemp;
                    return (newTemp - prevTemp) * thermalMass / deltaTime;
                }
                else
                {
                    // Sub-boiling cryo: heat toward boiling point
                    tank.internalTemp += Q_kW * deltaTime / thermalMass;
                    if (tank.internalTemp > tank.temperature)
                        tank.internalTemp = tank.temperature;
                    return Q_kW > 0 ? Q_kW : 0d;
                }
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
        // TODO MLI convective coefficient needs some research. I chose a value that would allow MLI in-atmo to provide better insulation than a naked tank.
        //      But it should probably be based on the gas composition of the planet involved?

        /// <summary>
        /// Transfer rate through multilayer insulation in watts/m2 via radiation, conduction and convection (conduction through gas in the layers).
        /// Can be called in real time substituting skin temp and internal temp for hot and cold.
        /// </summary>
        private double GetMLITransferRate(double outerTemperature, double innerTemperature)
            => GetMLITransferRate(outerTemperature, innerTemperature, totalMLILayers, vessel.staticPressurekPa);

        private static double GetMLITransferRate(double outerTemp, double innerTemp, int mliLayers, double pressureKPa = 0)
        {
            const double QrCoefficient = 0.0000000004944; // typical MLI radiation flux coefficient
            const double QcCoefficient = 0.0000000895;    // typical MLI conductive flux coefficient
            const double emissivity    = 0.03;            // typical reflective mylar emissivity
            const double layerDensity  = 10.055;          // layer density (layers/cm)

            double radiation  = QrCoefficient * emissivity * (Math.Pow(outerTemp, 4.67) - Math.Pow(innerTemp, 4.67)) / mliLayers;
            double conduction = QcCoefficient * Math.Pow(layerDensity, 2.63) * ((outerTemp + innerTemp) / 2) / (mliLayers + 1) * (outerTemp - innerTemp);
            double result = radiation + conduction;
            if (pressureKPa > 0)
                result += RFSettings.Instance.QvCoefficient * (pressureKPa * 7.500616851) * (Math.Pow(outerTemp, 0.52) - Math.Pow(innerTemp, 0.52)) / mliLayers;
            return result;
        }

        /// <summary>
        /// Transfer rate through Dewar walls via radiation across the vacuum gap.
        /// </summary>
        private static double GetDewarTransferRate(double hot, double cold, double area)
        {
            // TODO Just radiation now; need to calculate conduction through piping/lid, etc
            double emissivity = 0.005074871897; // corrected and rounded value for concentric surfaces, actual emissivity of each surface is assumed to be 0.01 for silvered or aluminized coating
            return PhysicsGlobals.StefanBoltzmanConstant * emissivity * area * (Math.Pow(hot,4) - Math.Pow(cold,4));
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
            string data = proto_module.moduleValues.GetValue(nameof(bgBoiloffData));
            if (string.IsNullOrEmpty(data)) return string.Empty;

            bool hasGeometry = KerbalismInterface.TryGetThermalData(vessel, out double vesselTemp, out _);
            if (!hasGeometry) return string.Empty;

            if (!_bgCache.TryGetValue(proto_module, out BgBoiloffCache cache) || cache.DataVersion != data)
            {
                int.TryParse(proto_module.moduleValues.GetValue(nameof(totalMLILayers)), out int mliLayers);
                cache = BuildBgBoiloffCache(data, mliLayers);
                _bgCache.Remove(proto_module);
                _bgCache.Add(proto_module, cache);
            }

            // On first use of a cache instance, seed InternalTemps from the persisted TANK nodes
            bool needsInit = false;
            for (int i = 0; i < cache.Tanks.Length; i++)
            {
                if (cache.InternalTemps[i] < 0)
                {
                    needsInit = true;
                    break;
                }
            }

            if (needsInit)
            {
                var tempLookup = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (ConfigNode tankNode in proto_module.moduleValues.GetNodes("TANK"))
                {
                    string tName = tankNode.GetValue("name");
                    string sVal = tankNode.GetValue("internalTemp");
                    if (tName != null && double.TryParse(sVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
                        tempLookup[tName] = t;
                }
                for (int i = 0; i < cache.Tanks.Length; i++)
                {
                    if (cache.InternalTemps[i] < 0)
                        cache.InternalTemps[i] = tempLookup.TryGetValue(cache.Tanks[i].Name, out double v) && v > 0
                            ? v : cache.Tanks[i].BoilingPointK;
                }
            }

            bool anyRequest = false;
            for (int i = 0; i < cache.Tanks.Length; i++)
            {
                BgTankEntry entry = cache.Tanks[i];
                double internalTemp = cache.InternalTemps[i];

                double Q_kW;
                if (entry.IsDewar)
                {
                    Q_kW = GetDewarTransferRate(vesselTemp, internalTemp, entry.TankAreaM2) * 0.001;
                    if (Q_kW == 0) continue;
                }
                else if (cache.MliLayers > 0 && entry.TankAreaM2 > 0)
                {
                    Q_kW = GetMLITransferRate(vesselTemp, internalTemp, cache.MliLayers) * entry.TankAreaM2 * 0.001;
                    if (Q_kW == 0) continue;
                }
                else if (entry.ConductWPerK > 0)
                {
                    double deltaTemp = vesselTemp - internalTemp;
                    if (deltaTemp == 0) continue;
                    Q_kW = entry.ConductWPerK * deltaTemp * 0.001;
                }
                else continue;

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
                    // At boiling point: all flux drives boiloff
                    double rateKgS = Q_kW / entry.Vsp;
                    resourceChangeRequest.Add(new KeyValuePair<string, double>(entry.Name, -rateKgS / entry.Density));
                    anyRequest = true;
                }
            }

            // Persist updated internalTemps so they survive cache invalidation and vessel reload
            foreach (ConfigNode tankNode in proto_module.moduleValues.GetNodes("TANK"))
            {
                string tName = tankNode.GetValue("name");
                if (tName == null) continue;
                for (int i = 0; i < cache.Tanks.Length; i++)
                {
                    if (cache.Tanks[i].Name == tName)
                    {
                        var sTemp = cache.InternalTemps[i].ToString("R", CultureInfo.InvariantCulture);
                        tankNode.SetValue("internalTemp", sTemp, true);
                        break;
                    }
                }
            }

            proto_module.moduleValues.SetValue("bgBoiloffLastUpdate",
                Planetarium.GetUniversalTime().ToString(CultureInfo.InvariantCulture));

            return anyRequest ? Localizer.GetStringByTag("#RF_FuelTankRF_kerbalismtips") : string.Empty;
        }

        private static BgBoiloffCache BuildBgBoiloffCache(string data, int mliLayers)
        {
            var tanks = new List<BgTankEntry>();

            foreach (string entry in data.Split(';'))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                string[] split = entry.Split(',');
                if (split.Length != 7) continue;

                string resourceName = split[0];
                if (!double.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double boilingPointK)) continue;
                if (!double.TryParse(split[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double tankAreaM2)) continue;
                if (!double.TryParse(split[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double conductWPerK)) continue;
                if (!int.TryParse(split[4], out int isDewarInt)) continue;
                if (!double.TryParse(split[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double hsp)) continue;
                if (!double.TryParse(split[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double structThermalMassKJ)) continue;

                PartResourceDefinition resDef = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (resDef == null || resDef.density <= 0d) continue;
                if (!MFSSettings.resourceVsps.TryGetValue(resourceName, out double vsp) || vsp <= 0) continue;

                tanks.Add(new BgTankEntry
                {
                    Name                = resourceName,
                    Vsp                 = vsp,
                    Density             = resDef.density,
                    BoilingPointK       = boilingPointK,
                    TankAreaM2          = tankAreaM2,
                    ConductWPerK        = conductWPerK,
                    IsDewar             = isDewarInt != 0,
                    Hsp                 = hsp,
                    StructThermalMassKJ = structThermalMassKJ,
                });
            }

            return new BgBoiloffCache(data, mliLayers, tanks.ToArray());
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
