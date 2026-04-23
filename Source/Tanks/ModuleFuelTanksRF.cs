using KSP.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Profiling;

namespace RealFuels.Tanks
{
    public partial class ModuleFuelTanks : IAnalyticTemperatureModifier, IAnalyticPreview
    {
        public const string BoiloffGroupName = "RFBoiloffDebug";
        public const string BoiloffGroupDisplayName = "#RF_FuelTankRF_Boiloff"; // "RF Boiloff"
        private double analyticSkinTemp;
        private double analyticInternalTemp;
        public bool SupportsBoiloff => cryoTanks.Count > 0;
        private readonly Dictionary<string, double> boiloffProducts = new Dictionary<string, double>();

        public int numberOfMLILayers = 0; // base number of layers taken from TANK_DEFINITION configs

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "#RF_FuelTankRF_MLILayers", guiUnits = "#", guiFormat = "F0"), // MLI Layers
        UI_FloatRange(minValue = 0, maxValue = 100, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float _numberOfAddedMLILayers = 0; // This is the number of layers added by the player.
        public int numberOfAddedMLILayers { get => (int)_numberOfAddedMLILayers; }

        [KSPField(isPersistant = true)]
        public int totalMLILayers = 0;

        [KSPField(isPersistant = true)]
        protected double totalTankArea;

        // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
        [NonSerialized]
        public Dictionary<string, bool> pressurizedFuels = new Dictionary<string, bool>();

        [KSPField(guiActiveEditor = true, guiName = "#RF_FuelTankRF_HighlyPressurized")] // Highly Pressurized?
        public bool highlyPressurized = false;

        [KSPField(guiName = "#RF_FuelTankRF_WallTemp", groupName = BoiloffGroupName, groupDisplayName = BoiloffGroupDisplayName, groupStartCollapsed = true)] // Wall Temp
        public string sWallTemp;

        [KSPField(guiName = "#RF_FuelTankRF_HeatPenetration", groupName = BoiloffGroupName)] // Heat Penetration
        public string sHeatPenetration;

        [KSPField(guiName = "#RF_FuelTankRF_BoiloffLoss", groupName = BoiloffGroupName)] // Boil-off Loss
        public string sBoiloffLoss;

        [KSPField(guiName = "#RF_FuelTankRF_AnalyticCooling", groupName = BoiloffGroupName)] // Analytic Cooling
        public string sAnalyticCooling;

        private double cooling = 0;

        // Thermal data captured while loaded, used for processing boiloff BackgroundUpdate on unloaded vessels.
        // Format per entry: "resourceName,coldTempK,conductInternalWPerK,dewarAreaM2"
        //   coldTempK            — boiling point of the propellant (K)
        //   conductInternalWPerK — part-interior→liquid thermal conductance in W/K (0 for Dewar tanks)
        //   dewarAreaM2          — tank area for Stefan-Boltzmann Dewar formula (−1 for non-Dewar tanks)
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

        private static double ConductionFactors => RFSettings.Instance.globalConductionCompensation ? Math.Max(1.0d, PhysicsGlobals.ConductionFactor) : 1d;

        public double BoiloffMassRate => boiloffMass;
        private double boiloffMass = 0d;
        private readonly List<FuelTank> cryoTanks = new List<FuelTank>();
        private readonly List<double> lossInfo = new List<double>();
        private readonly List<double> fluxInfo = new List<double>();

        private FlightIntegrator _flightIntegrator;

        double lowestTankTemperature = 300d;

        partial void OnLoadRF(ConfigNode _) {}

        partial void OnSaveRF(ConfigNode _)
        {
            if (!HighLogic.LoadedSceneIsFlight || cryoTanks.Count == 0)
                return;

            var bgEntries = new Dictionary<string, string>();
            foreach (var tank in cryoTanks)
            {
                if (tank.amount <= 0) continue;

                if (tank.vsp > 0)
                {
                    double conductInternalWPerK, dewarAreaM2;
                    if (tank.isDewar)
                    {
                        conductInternalWPerK = 0;
                        dewarAreaM2 = tank.totalArea;
                    }
                    else
                    {
                        double wallF = tank.wallConduction > 0 ? tank.wallThickness / tank.wallConduction : 0;
                        double insulF = tank.insulationConduction > 0 ? tank.insulationThickness / tank.insulationConduction : 0;
                        double resF = tank.resourceConductivity > 0 ? 0.01 / tank.resourceConductivity : 0;
                        conductInternalWPerK = tank.totalArea / Math.Max(double.Epsilon, wallF + insulF + resF);
                        dewarAreaM2 = -1;
                    }
                    bgEntries[tank.name] = string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R}",
                        tank.name, tank.temperature, conductInternalWPerK, dewarAreaM2);
                }
            }

            bgBoiloffData = bgEntries.Count > 0 ? string.Join(";", bgEntries.Values) : "";
        }

        partial void OnStartRF(StartState _)
        {
            if (HighLogic.LoadedSceneIsFlight)
                _flightIntegrator = vessel.vesselModules.Find(x => x is FlightIntegrator) as FlightIntegrator;

            foreach (var tank in tanksDict.Values)
            {
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
            Fields[nameof(sAnalyticCooling)].guiActive = debugBoilActive;

            GameEvents.onPartResourceListChange.Add(OnPartResourceListChange);
            GameEvents.onPartDestroyed.Add(OnPartDestroyed);
        }

        private bool IsProcedural => part.Modules.Contains("SSTUModularPart") || part.Modules.Contains("WingProcedural");

        private void CalculateInsulation()
        {
            Profiler.BeginSample("CalculateInsulation");
            // TODO tie this into insulation configuration GUI! Also, we should handle MLI separately and as part skin-internal conduction. (DONE)
            // Dewars and SOFI should be handled separately as part of the boiloff code on a per-tank basis (DONE)
            // Current SOFI configuration system should be left in place with players able to add to tanks that don't have it.
            if (totalMLILayers > 0 && totalVolume > 0 && !(double.IsNaN(part.temperature) || double.IsNaN(part.skinTemperature)))
            {
                double normalizationFactor = 1 / (PhysicsGlobals.SkinInternalConductionFactor * PhysicsGlobals.ConductionFactor * PhysicsGlobals.ThermalConvergenceFactor * 10 * 0.5);
                double tDelta = part.skinTemperature - part.temperature;
                if (tDelta == 0d)
                    tDelta = 0.00000000001d;
                double insulationFactor = Math.Abs(GetMLITransferRate(part.skinTemperature, part.temperature) / tDelta) * 0.001;
                double condRecip = part.partInfo.partPrefab.skinInternalConductionMult == 0d ? double.MaxValue : (1d / part.partInfo.partPrefab.skinInternalConductionMult);
                part.heatConductivity = normalizationFactor * 1 / ((1 / insulationFactor) + condRecip);
                CalculateAnalyticInsulationFactor(insulationFactor);
            }
            Profiler.EndSample();
        }

        private void CalculateAnalyticInsulationFactor(double insulationFactor)
        {
            double tMassRecip = part.thermalMass == 0d ? 1d : 1d / part.thermalMass;
            part.analyticInternalInsulationFactor = _flightIntegrator is FlightIntegrator
                ? (1d / PhysicsGlobals.AnalyticLerpRateInternal) * (insulationFactor * totalTankArea * tMassRecip) * RFSettings.Instance.analyticInsulationMultiplier * part.partInfo.partPrefab.analyticInternalInsulationFactor
                : 0;
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
                string MLIText = totalMLILayers > 0 ? $"{GetMLITransferRate(part.skinTemperature, part.temperature):F4}" : Localizer.GetStringByTag("#RF_FuelTankRF_NoMLI"); // "No MLI"
                sWallTemp = $"{part.temperature:F4} ({MLIText} * {part.radiativeArea:F2} m2)"; // 
                sAnalyticCooling = Utilities.FormatFlux(cooling);

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
            //print ("[Real Fuels]" + Time.time.ToString ());
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready)
            {
                // MLI performance varies by temperature delta
                CalculateInsulation();

                if(!_flightIntegrator.isAnalytical && SupportsBoiloff)
                    CalculateTankBoiloff(_flightIntegrator.timeSinceLastUpdate, _flightIntegrator.isAnalytical);
            }
        }

        private void HandleCooling(ref double cooling, double deltaTime, bool analyticalMode)
        {
            cooling = 0;
            if (analyticalMode)
            {
                if (part.thermalInternalFlux < 0)
                    cooling = part.thermalInternalFlux;
                else if (part.thermalInternalFluxPrevious < 0)
                    cooling = part.thermalInternalFluxPrevious;

                if (cooling < 0)
                {
                    // in analytic mode, MFTRF interprets this as an attempt to cool the tanks
                    // Questionable since the thermalInternalFlux is already tracking it??
                    if (part.thermalMassReciprocal > 0d)
                        analyticInternalTemp += cooling * part.thermalMassReciprocal * deltaTime;
                }
            }
        }

        private double GetBoiloffTransferRate(double deltaTemp, double wettedArea, in FuelTank tank)
        {
            double wallFactor = tank.wallConduction > 0 ? tank.wallThickness / tank.wallConduction : 0;
            double insulationFactor = tank.insulationConduction > 0 ? tank.insulationThickness / tank.insulationConduction : 0;
            double resourceFactor = tank.resourceConductivity > 0 ? 0.01 / tank.resourceConductivity : 0;
            double divisor = Math.Max(double.Epsilon, wallFactor + insulationFactor + resourceFactor);

            return deltaTemp * wettedArea / divisor;
        }

        private void CalculateTankBoiloff(double deltaTime, bool analyticalMode = false, double unclampedIntScalar = 0, double unclampedSkinScalar = 0)
        {
            Profiler.BeginSample("CalculateTankBoiloff");
            if (totalTankArea <= 0)
            {
                Debug.LogError("RF: CalculateTankBoiloff ran without calculating tank data!");
                CalculateTankArea();
            }

            if (double.IsNaN(part.temperature))
            {
                Debug.LogError($"RF: CalculateTankBoiloff found NaN part.temperature on {part}");
                Profiler.EndSample();
                return;
            }

            boiloffMass = 0d;
            lossInfo.Clear();
            fluxInfo.Clear();

            bool hasCryoFuels = CalculateLowestTankTemperature();
            if (hasCryoFuels && MFSSettings.radiatorMinTempMult >= 0d)
                part.radiatorMax = lowestTankTemperature * MFSSettings.radiatorMinTempMult / part.maxTemp;

            if (fueledByLaunchClamp)
            {
                if (hasCryoFuels)
                {
                    if (analyticalMode)
                        analyticInternalTemp = lowestTankTemperature;
                    else
                        part.temperature = lowestTankTemperature;
                    // part.skinTemperature or analyticSkinTemp ? Nah.
                }
                fueledByLaunchClamp = false;
                Profiler.EndSample();
                return;
            }

            if (deltaTime > 0 && !CheatOptions.InfinitePropellant)
            {
                //Debug.Log($"internalFlux = {part.thermalInternalFlux}, thermalInternalFluxPrevious = {part.thermalInternalFluxPrevious}, analytic internal flux = {previewInternalFluxAdjust}");
                HandleCooling(ref cooling, deltaTime, analyticalMode);

                foreach (var tank in cryoTanks)
                {
                    double tankAmount = tank.amount;
                    if (tankAmount > 0 && tank.vsp > 0)
                    {
                        double massLost = 0;
                        double hotTemp = part.temperature;

                        // We might be in analytic mode, and have a target temperature = analyticInternalTemp/analyticSkinTemp, and "progress" towards it reprsented by the scalar params
                        if (analyticalMode)
                        {
                            hotTemp = UtilMath.Lerp(part.temperature, analyticInternalTemp, Math.Min(1, unclampedIntScalar / 2));
                            DebugLog($"[MFTRF] CalculateBoiloff.Analytic using adjusted temp {hotTemp:F1} from {part.temperature:F1} towards {analyticInternalTemp:F1} based on scalar {unclampedIntScalar:F2}");
                        }

                        double deltaTemp = hotTemp - tank.temperature;
                        double Q = 0;
                        if (deltaTemp > 0)
                        {
                            double wettedArea = tank.totalArea; // disabled until proper wetted vs ullage conduction can be done (tank.amount / tank.maxAmount);
                            Q = tank.isDewar ? GetDewarTransferRate(hotTemp, tank.temperature, tank.totalArea)
                                             : GetBoiloffTransferRate(deltaTemp, wettedArea, tank);

                            Q *= 0.001d; // convert to kilowatts
                            massLost = Q / tank.vsp;

                            lossInfo.Add(massLost * 1000 * 3600);
                            fluxInfo.Add(Q);
                            massLost *= deltaTime; // Frame scaling
                        }

                        double d = tank.density > 0 ? tank.density : 1;
                        double lossAmount = massLost / d;
                        lossAmount = Math.Min(lossAmount, tankAmount);

                        if (lossAmount > 0)
                        {
                            // operate directly with the PartResource because FuelTank.amount isn't really meant for in-flight resource consumption
                            tank.resource.amount -= lossAmount;

                            // See if there is boiloff byproduct and see if any other parts want to accept it.
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

                        boiloffMass += massLost;

                        // subtract heat from boiloff
                        // subtracting heat in analytic mode is tricky: Analytic flux handling is 'cheaty' and tricky to predict.

                        if (Q > 0)
                        {
                            double heatLost = -Q;
                            if (!analyticalMode)
                                part.AddThermalFlux(heatLost);
                            else
                            {
                                analyticInternalTemp += heatLost * part.thermalMassReciprocal * deltaTime;
                                DebugLog($"{part.name} deltaTime = {deltaTime:F2}s, heat lost = {heatLost:F4}, thermalMassReciprocal = {part.thermalMassReciprocal:F6}");
                            }
                        }
                    }
                }
            }
            Profiler.EndSample();
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

        // This is how you update drag cubes, we shouldn't be the service for this, but left-over code.
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
            // TODO: Codify a more accurate tank area calculator.
            // Thought: cube YN/YP can be used to find the part diameter / circumference... X or Z finds the length
            // Also should try to determine if tank has a common bulkhead - and adjust heat flux into individual tanks accordingly
            SetTankAreaInfo(volume);

            // This allows a rough guess as to individual tank surface area based on ratio of tank volume to total volume but it breaks down at very small fractions
            // So use greater of spherical calculation and tank ratio of total area.
            // if for any reason our totalTankArea is still 0 (no drag cubes available yet or analytic temp routines executed first)
            // then we're going to be defaulting to spherical calculation
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
        // Analytic Interface
        public void SetAnalyticTemperature(FlightIntegrator fi, double analyticTemp, double predictedInternalTemp, double predictedSkinTemp)
        {
            analyticSkinTemp = predictedSkinTemp;
            analyticInternalTemp = predictedInternalTemp;

            if (SupportsBoiloff)
            {
                DebugLog($"{part.name} Analytic Temp = {analyticTemp:F2}, Analytic Internal = {predictedInternalTemp:F2}, Analytic Skin = {predictedSkinTemp:F2}");
                double lerpScalarInt = PhysicsGlobals.AnalyticLerpRateInternal * fi.timeSinceLastUpdate;
                double lerpScalarSkin = PhysicsGlobals.AnalyticLerpRateSkin * fi.timeSinceLastUpdate;
                double skinScalar = lerpScalarSkin * part.analyticSkinInsulationFactor;
                double intScalar = lerpScalarInt * part.analyticInternalInsulationFactor;
                // A value of 1.0 (unclamped) indicates the time that has passed == the expected time to equalize temperatures
                // For values <= 1-ish, we may consider trying to scale the temp progress down by accounting for boiloff.
                // Alternatively, just adjust the analytic output using the boiloff calculation anyway.

                if (fi.timeSinceLastUpdate < double.MaxValue)
                {
                    double remainingTime;
                    if (bgBoiloffLastUpdate > 0d)
                    {
                        remainingTime = Math.Max(0d, Planetarium.GetUniversalTime() - bgBoiloffLastUpdate);
                        analyticInternalTemp = ComputeMLIEquilibriumTemp(analyticSkinTemp);
                        bgBoiloffLastUpdate = 0d;
                    }
                    else
                    {
                        remainingTime = fi.timeSinceLastUpdate;
                    }

                    if (remainingTime > 0d)
                        CalculateTankBoiloff(remainingTime, fi.isAnalytical, intScalar, skinScalar);
                }
                else if (CalculateLowestTankTemperature())
                {
                    // Vessel is freshly spawned and has cryogenic tanks, set temperatures appropriately
                    analyticSkinTemp = lowestTankTemperature;
                    analyticInternalTemp = lowestTankTemperature;
                }
            }
        }

        public double GetSkinTemperature(out bool lerp)
        {
            lerp = false;
            return Math.Max(analyticSkinTemp, PhysicsGlobals.SpaceTemperature);
        }

        public double GetInternalTemperature(out bool lerp)
        {
            lerp = false;
            return Math.Max(analyticInternalTemp, PhysicsGlobals.SpaceTemperature);
        }
        #endregion

        #region Analytic Preview Interface

        // We don't really implement this interface anymore.
        // Boiloff should not be a significant portion of the flux generation that it will actively keep
        // the vessel cool and will change the steady-state temperature that is being calculated/previewed here.
        // Diff-Eq problem: this calculates the steady-state temp, of which boiloff result is an input.
        // However, boiloff as a resource can be consumed, so the amount of time to target this steady state changes.
        //
        // Normally called every FixedUpdate by FlightIntegrator in Analytic Mode
        // May be called outside of Analytic Mode if part.temp/part.skinTemp were out of bounds
        public void AnalyticInfo(FlightIntegrator fi, double sunAndBodyIn, double backgroundRadiation, double radArea, double absEmissRatio, double internalFlux, double convCoeff, double ambientTemp, double maxPartTemp)
        {
            if (!fi.isAnalytical)
                Debug.Log($"[MFTRF] AnalyticInfo called in non-analytic mode for {vessel}.  dT: {fi.timeSinceLastUpdate:F2}s");
            /*
            if (TimeWarp.CurrentRate == 1)
                DebugLog("AnalyticInfo being called with: sunAndBodyIn = " + sunAndBodyIn.ToString()
                         + ", backgroundRadiation = " + backgroundRadiation.ToString()
                         + ", radArea = "+ radArea.ToString()
                         + ", absEmissRatio = " + absEmissRatio.ToString()
                         + ", internalFlux = " + internalFlux.ToString()
                         + ", convCoeff = " + convCoeff.ToString()
                         + ", ambientTemp = " + ambientTemp.ToString()
                         + ", maxPartTemp = " + maxPartTemp.ToString()
                        );
            //float deltaTime = (float)(Planetarium.GetUniversalTime() - vessel.lastUT);
            //if (this.supportsBoiloff)
            //    CalculateTankBoiloff(TimeWarp.fixedDeltaTime, true);
            */
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
        /// Default hot and cold values of 300 / 70. Can be called in real time substituting skin temp and internal temp for hot and cold.
        /// </summary>
        private double GetMLITransferRate(double outerTemperature = 300, double innerTemperature = 70)
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
        /// Transfer rate through Dewar walls
        /// This is simplified down to basic radiation formula using corrected emissivity values for concentric walls for sake of performance
        /// </summary>
        private static double GetDewarTransferRate(double hot, double cold, double area)
        {
            // TODO Just radiation now; need to calculate conduction through piping/lid, etc
            double emissivity = 0.005074871897; // corrected and rounded value for concentric surfaces, actual emissivity of each surface is assumed to be 0.01 for silvered or aluminized coating
            return PhysicsGlobals.StefanBoltzmanConstant * emissivity * area * (Math.Pow(hot,4) - Math.Pow(cold,4));
        }

        /// <summary>
        /// Returns the steady-state interior temperature for MLI-insulated cryo tanks at the given skin temperature,
        /// i.e. the point where heat conducted inward through the MLI blanket equals heat absorbed by boiloff.
        /// </summary>
        /// <param name="skinTemp"></param>
        /// <returns></returns>
        private double ComputeMLIEquilibriumTemp(double skinTemp)
        {
            if (totalMLILayers <= 0 || totalTankArea <= 0)
                return skinTemp;

            var tankParams = new List<(double conductW, double coldK)>(cryoTanks.Count);
            var dewarParams = new List<(double area, double coldK)>();
            foreach (var tank in cryoTanks)
            {
                if (tank.vsp <= 0) continue;

                if (tank.isDewar)
                {
                    dewarParams.Add((tank.totalArea, tank.temperature));
                }
                else
                {
                    double wallF = tank.wallConduction > 0 ? tank.wallThickness / tank.wallConduction : 0;
                    double insulF = tank.insulationConduction > 0 ? tank.insulationThickness / tank.insulationConduction : 0;
                    double resF = tank.resourceConductivity > 0 ? 0.01 / tank.resourceConductivity : 0;
                    double conductW = tank.totalArea / Math.Max(double.Epsilon, wallF + insulF + resF);
                    tankParams.Add((conductW, tank.temperature));
                }
            }

            return tankParams.Count > 0 || dewarParams.Count > 0
                ? SolveMLIEquilibrium(skinTemp, totalTankArea, totalMLILayers, tankParams, dewarParams)
                : skinTemp;
        }

        /// <summary>
        /// Finds the shared part-interior equilibrium temperature given skin temperature and all cryo heat sinks.
        /// Solves: GetMLITransferRate(skinTemp, T) × tankAreaM2 = Σ conductW_i × max(0, T − coldK_i) via bisection. 
        /// Left side is monotonically decreasing in T; right side increasing → unique root.
        /// </summary>
        private static double SolveMLIEquilibrium(
            double skinTemp, double tankAreaM2, int mliLayers,
            List<(double conductW, double coldK)> tanks,
            List<(double area, double coldK)> dewarTanks)
        {
            double lo = double.MaxValue;
            foreach (var t in tanks)
            {
                if (t.coldK < lo) lo = t.coldK;
            }

            foreach (var d in dewarTanks)
            {
                if (d.coldK < lo) lo = d.coldK;
            }

            double hi = skinTemp;
            if (lo >= hi) return hi;

            const double tolerance = 1e-3; // 1 mK — well below any physical significance
            while (hi - lo > tolerance)
            {
                double mid = (lo + hi) * 0.5;
                double mliFlux = GetMLITransferRate(skinTemp, mid, mliLayers) * tankAreaM2;  // TODO: currently assumes pressureKPa is always 0 for unloaded vessels (space vacuum, no convective contribution).
                double internalFlux = 0;
                foreach (var t in tanks)
                {
                    internalFlux += t.conductW * Math.Max(0, mid - t.coldK);
                }

                foreach (var d in dewarTanks)
                {
                    if (mid > d.coldK)
                        internalFlux += GetDewarTransferRate(mid, d.coldK, d.area);
                }

                if (mliFlux > internalFlux) lo = mid;
                else hi = mid;
            }
            return (lo + hi) * 0.5;
        }

        #endregion

        #region Kerbalism

        /// <summary>
        /// Called by Kerbalism for unloaded (background) vessels via reflection.
        /// Solves the MLI thermal equilibrium jointly for all non-Dewar cryo propellants in the part,
        /// using Kerbalism's geometry+orientation-corrected VesselTemperature as the skin hot-side.
        /// Solving jointly is essential: in a LH2+LOX tank, LH2's heat sink keeps T_interior below
        /// LOX's boiling point, correctly suppressing LOX boiloff without any special-casing.
        /// Falls back to the stored rate per-tank when geometry data is unavailable.
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

            // Read MLI geometry needed for the equilibrium solver.
            int.TryParse(proto_module.moduleValues.GetValue(nameof(totalMLILayers)), out int mliLayers);
            double.TryParse(proto_module.moduleValues.GetValue(nameof(totalTankArea)),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double tankAreaM2);

            // Parse all entries. Separate Dewar tanks (handled individually) from vsp tanks
            // (handled via joint MLI equilibrium).
            var vspTanks   = new List<(string name, double coldK, double conductW, PartResourceDefinition resDef)>();
            var dewarTanks = new List<(string name, double coldK, double dewarArea, PartResourceDefinition resDef)>();

            foreach (string entry in data.Split(';'))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                string[] f = entry.Split(',');
                if (f.Length != 4) continue;
                string resourceName = f[0];
                if (!double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double coldK)) continue;
                if (!double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double conductW)) continue;
                if (!double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double dewarArea)) continue;

                PartResourceDefinition resDef = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (resDef == null || resDef.density <= 0d) continue;

                if (dewarArea >= 0)
                    dewarTanks.Add((resourceName, coldK, dewarArea, resDef));
                else if (conductW > 0 && MFSSettings.resourceVsps.ContainsKey(resourceName))
                    vspTanks.Add((resourceName, coldK, conductW, resDef));
            }

            bool anyRequest = false;

            // Solve the shared part-interior temperature, accounting for MLI and all cryo heat sinks.
            // Requires Kerbalism VesselTemperature; boiloff is skipped entirely if geometry data is unavailable.
            if (hasGeometry)
            {
                double interiorTemp;
                if (mliLayers > 0 && tankAreaM2 > 0 && (vspTanks.Count > 0 || dewarTanks.Count > 0))
                {
                    var tankParams = new List<(double conductW, double coldK)>(vspTanks.Count);
                    foreach (var t in vspTanks)
                    {
                        tankParams.Add((t.conductW, t.coldK));
                    }

                    var dewarParams = new List<(double area, double coldK)>(dewarTanks.Count);
                    foreach (var d in dewarTanks)
                    {
                        dewarParams.Add((d.dewarArea, d.coldK));
                    }

                    interiorTemp = SolveMLIEquilibrium(vesselTemp, tankAreaM2, mliLayers, tankParams, dewarParams);
                }
                else
                {
                    interiorTemp = vesselTemp; // no MLI: interior equilibrates to skin temperature
                }

                foreach (var (name, coldK, conductW, resDef) in vspTanks)
                {
                    if (!MFSSettings.resourceVsps.TryGetValue(name, out double vsp) || vsp <= 0) continue;
                    double deltaTemp = interiorTemp - coldK;
                    if (deltaTemp <= 0) continue;
                    double rateKgS = conductW * deltaTemp * 0.001 / vsp;
                    if (rateKgS <= 0) continue;
                    resourceChangeRequest.Add(new KeyValuePair<string, double>(name, -rateKgS / resDef.density));
                    anyRequest = true;
                }

                foreach (var (name, coldK, dewarArea, resDef) in dewarTanks)
                {
                    if (interiorTemp <= coldK || !MFSSettings.resourceVsps.TryGetValue(name, out double vsp) || vsp <= 0) continue;
                    double Q_kW = GetDewarTransferRate(interiorTemp, coldK, dewarArea) * 0.001;
                    double rateKgS = Math.Max(0, Q_kW / vsp);
                    if (rateKgS <= 0) continue;
                    resourceChangeRequest.Add(new KeyValuePair<string, double>(name, -rateKgS / resDef.density));
                    anyRequest = true;
                }
            }

            proto_module.moduleValues.SetValue("bgBoiloffLastUpdate",
                Planetarium.GetUniversalTime().ToString(CultureInfo.InvariantCulture));

            return anyRequest ? Localizer.GetStringByTag("#RF_FuelTankRF_kerbalismtips") : string.Empty;
        }

        /// <summary>
        /// Called by Kerbalism every frame for loaded vessels. Uses their resource system when Kerbalism is installed.
        /// </summary>
        public virtual string ResourceUpdate(Dictionary<string, double> availableResources, List<KeyValuePair<string, double>> resourceChangeRequest)
        {
            //resourceChangeRequest.Clear();

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
