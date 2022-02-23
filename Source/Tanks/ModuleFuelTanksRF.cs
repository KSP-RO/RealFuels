using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels.Tanks
{
    public partial class ModuleFuelTanks : IAnalyticTemperatureModifier, IAnalyticPreview
    {
        public const string BoiloffGroupName = "RFBoiloffDebug";
        public const string BoiloffGroupDisplayName = "RF Boiloff";
        protected double totalTankArea;
        private double analyticSkinTemp;
        private double analyticInternalTemp;
        public bool SupportsBoiloff => cryoTanks.Count > 0;
        private readonly Dictionary<string, double> boiloffProducts = new Dictionary<string, double>();

        public int numberOfMLILayers = 0; // base number of layers taken from TANK_DEFINITION configs

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "MLI Layers", guiUnits = "#", guiFormat = "F0"),
        UI_FloatRange(minValue = 0, maxValue = 100, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float _numberOfAddedMLILayers = 0; // This is the number of layers added by the player.
        public int numberOfAddedMLILayers { get => (int)_numberOfAddedMLILayers; }

        public int totalMLILayers => numberOfMLILayers + numberOfAddedMLILayers;

        // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
        [NonSerialized]
        public Dictionary<string, bool> pressurizedFuels = new Dictionary<string, bool>();

        [KSPField(guiActiveEditor = true, guiName = "Highly Pressurized?")]
        public bool highlyPressurized = false;

        [KSPField(guiName = "Wall Temp", groupName = BoiloffGroupName, groupDisplayName = BoiloffGroupDisplayName, groupStartCollapsed = true)]
        public string sWallTemp;

        [KSPField(guiName = "Heat Penetration", groupName = BoiloffGroupName)]
        public string sHeatPenetration;

        [KSPField(guiName = "Boil-off Loss", groupName = BoiloffGroupName)]
        public string sBoiloffLoss;

        [KSPField(guiName = "Analytic Cooling", groupName = BoiloffGroupName)]
        public string sAnalyticCooling;

        private double cooling = 0;

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

        partial void OnStartRF(StartState _)
        {
            if (HighLogic.LoadedSceneIsFlight)
                _flightIntegrator = vessel.vesselModules.Find(x => x is FlightIntegrator) as FlightIntegrator;

            foreach (var tank in tankList)
            {
                if (tank.maxAmount > 0 && (tank.vsp > 0 || tank.loss_rate > 0))
                    cryoTanks.Add(tank);
            }
            CalculateTankArea();

            if (HighLogic.LoadedSceneIsEditor)
            {
                Fields[nameof(_numberOfAddedMLILayers)].guiActiveEditor = maxMLILayers > 0;
                _numberOfAddedMLILayers = Mathf.Clamp(_numberOfAddedMLILayers, 0, maxMLILayers);
                ((UI_FloatRange)Fields[nameof(_numberOfAddedMLILayers)].uiControlEditor).maxValue = maxMLILayers;
                Fields[nameof(_numberOfAddedMLILayers)].uiControlEditor.onFieldChanged = delegate (BaseField field, object value)
                {
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
            // TODO tie this into insulation configuration GUI! Also, we should handle MLI separately and as part skin-internal conduction. (DONE)
            // Dewars and SOFI should be handled separately as part of the boiloff code on a per-tank basis (DONE)
            // Current SOFI configuration system should be left in place with players able to add to tanks that don't have it.
            if (totalMLILayers > 0 && totalVolume > 0 && !(double.IsNaN(part.temperature) || double.IsNaN(part.skinTemperature)))
            {
                double normalizationFactor = 1 / (PhysicsGlobals.SkinInternalConductionFactor * PhysicsGlobals.ConductionFactor * PhysicsGlobals.ThermalConvergenceFactor * 10 * 0.5);
                double insulationFactor = Math.Abs(GetMLITransferRate(part.skinTemperature, part.temperature) / (part.skinTemperature - part.temperature)) * 0.001;
                part.heatConductivity = normalizationFactor * 1 / ((1 / insulationFactor) + (1 / part.partInfo.partPrefab.skinInternalConductionMult));
                CalculateAnalyticInsulationFactor(insulationFactor);
            }
        }

        private void CalculateAnalyticInsulationFactor(double insulationFactor)
        {
            part.analyticInternalInsulationFactor = _flightIntegrator is FlightIntegrator
                ? (1d / PhysicsGlobals.AnalyticLerpRateInternal) * (insulationFactor * totalTankArea / part.thermalMass) * RFSettings.Instance.analyticInsulationMultiplier * part.partInfo.partPrefab.analyticInternalInsulationFactor
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
                string MLIText = totalMLILayers > 0 ? $"{GetMLITransferRate(part.skinTemperature, part.temperature):F4}" : "No MLI";
                sWallTemp = $"{part.temperature:F4} ({MLIText} * {part.radiativeArea:F2} m2)";
                sAnalyticCooling = Utilities.FormatFlux(cooling);

                sHeatPenetration = "";
                sBoiloffLoss = "";
                foreach (var m in lossInfo)
                    sBoiloffLoss += $"{m:F4} kg/hr | ";
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
            if (totalTankArea <= 0)
            {
                Debug.LogError("RF: CalculateTankBoiloff ran without calculating tank data!");
                CalculateTankArea();
            }
            if (double.IsNaN(part.temperature))
            {
                Debug.LogError($"RF: CalculateTankBoiloff found NaN part.temperature on {part}");
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
                return;
            }

            if (deltaTime > 0 && !CheatOptions.InfinitePropellant)
            {
                //Debug.Log($"internalFlux = {part.thermalInternalFlux}, thermalInternalFluxPrevious = {part.thermalInternalFluxPrevious}, analytic internal flux = {previewInternalFluxAdjust}");
                HandleCooling(ref cooling, deltaTime, analyticalMode);

                foreach (var tank in cryoTanks)
                {
                    if (tank.amount > 0 && tank.vsp > 0)
                    {
                        double massLost = 0;
                        double hotTemp = part.temperature;

                        if (RFSettings.Instance.ferociousBoilOff)
                            hotTemp = Math.Max(((hotTemp * part.thermalMass) - (tank.temperature * part.resourceThermalMass)) / (part.thermalMass - part.resourceThermalMass), part.temperature);

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
                        lossAmount = Math.Min(lossAmount, tank.amount);

                        if (lossAmount > 0)
                        {
                            tank.amount -= lossAmount;

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
                    else if (tank.amount > 0 && tank.loss_rate > 0)
                    {
                        double deltaTemp = part.temperature - tank.temperature;
                        if (deltaTemp > 0)
                        {
                            double lossAmount = tank.maxAmount * tank.loss_rate * deltaTemp * deltaTime;
                            lossAmount = Math.Min(lossAmount, tank.amount);
                            tank.amount -= lossAmount;
                            boiloffMass += lossAmount * tank.density;
                        }
                    }
                }
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

            InitUtilization();

            if (HighLogic.LoadedScene == GameScenes.LOADING)
                UpdateEngineIgnitor(def);
        }

        private void UpdateEngineIgnitor(TankDefinition def)
        {
            pressurizedFuels.Clear();
            foreach (var f in tankList)
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

            double areaCubes = CalculateTankAreaCubes();

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
                lowestTankTemperature = Math.Min(lowestTankTemperature, tank.temperature);
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
                    CalculateTankBoiloff(fi.timeSinceLastUpdate, fi.isAnalytical, intScalar, skinScalar);
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

        void DebugLog(string msg)
        {
#if DEBUG
            Debug.Log("[RealFuels.ModuleFuelTankRF] " + msg);
#endif
        }

#region Cryogenics
        // TODO MLI convective coefficient needs some research. I chose a value that would allow MLI in-atmo to provide better insulation than a naked tank.
        //      But it should probably be based on the gas composition of the planet involved?

        /// <summary>
        /// Transfer rate through multilayer insulation in watts/m2 via radiation, conduction and convection (conduction through gas in the layers).
        /// Default hot and cold values of 300 / 70. Can be called in real time substituting skin temp and internal temp for hot and cold.
        /// </summary>
        private double GetMLITransferRate(double outerTemperature = 300, double innerTemperature = 70)
        {
            //
            double QrCoefficient = 0.0000000004944; // typical MLI radiation flux coefficient
            double QcCoefficient = 0.0000000895; // typical MLI conductive flux coefficient. Possible tech upgrade target based on spacing mechanism between layers?
            //double QvCoefficient = 3.65; // 14.600; // 14600; // not even sure how this is right: convective contribution will be MURDEROUS.
            double emissivity = 0.03; // typical reflective mylar emissivity...?
            double layerDensity = 10.055; //14.99813f; // 8.51f; // layer density (layers/cm)

            double radiation = (QrCoefficient * emissivity * (Math.Pow(outerTemperature, 4.67) - Math.Pow(innerTemperature, 4.67))) / totalMLILayers;
            double conduction = ((QcCoefficient * Math.Pow(layerDensity, 2.63) * ((outerTemperature + innerTemperature) / 2)) / (totalMLILayers + 1)) * (outerTemperature - innerTemperature);
            double convection = RFSettings.Instance.QvCoefficient * ((vessel.staticPressurekPa * 7.500616851) * (Math.Pow(outerTemperature, 0.52) - Math.Pow(innerTemperature, 0.52))) / totalMLILayers;
            return radiation + conduction + convection;
        }

        /// <summary>
        /// Transfer rate through Dewar walls
        /// This is simplified down to basic radiation formula using corrected emissivity values for concentric walls for sake of performance
        /// </summary>
        private double GetDewarTransferRate(double hot, double cold, double area)
        {
            // TODO Just radiation now; need to calculate conduction through piping/lid, etc
            double emissivity = 0.005074871897; // corrected and rounded value for concentric surfaces, actual emissivity of each surface is assumed to be 0.01 for silvered or aluminized coating
            return PhysicsGlobals.StefanBoltzmanConstant * emissivity * area * (Math.Pow(hot,4) - Math.Pow(cold,4));
        }

        #endregion

        #region Kerbalism
        /// <summary>
        /// Called by Kerbalism every frame. Uses their resource system when Kerbalism is installed.
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

            return "boiloff product";
        }
        #endregion

        #region Tank Dimensions

        private void SetTankAreaInfo(double volume)
        {
            foreach (var tank in tankList)
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
            foreach (var tank in tankList)
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
