using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace RealFuels.TechLevels
{
    public class TechLevel
    {
        protected FloatCurve atmosphereCurve;
        protected FloatCurve velocityCurve;
        protected double TWR;
        protected double thrustMultiplier;
        protected double massMultiplier;
        protected double minThrottleMultiplier;
        protected float gimbalRange;
        protected string techRequired;
        protected float costMult;

        public static ConfigNode globalTechLevels = null;

        // CONSTRUCTORS
        public TechLevel()
        {
            atmosphereCurve = new FloatCurve();
            velocityCurve = new FloatCurve();
            TWR = -1;
            thrustMultiplier = -1;
            massMultiplier = -1;
            minThrottleMultiplier = -1;
            gimbalRange = -1f;
            techRequired = "";
            costMult = 1f;

            LoadGlobals();
        }
        public TechLevel(TechLevel t)
        {
            atmosphereCurve = t.atmosphereCurve;
            velocityCurve = t.velocityCurve;
            TWR = t.TWR;
            thrustMultiplier = t.thrustMultiplier;
            massMultiplier = t.massMultiplier;
            gimbalRange = t.gimbalRange;
            techRequired = t.techRequired;
            minThrottleMultiplier = t.minThrottleMultiplier;
            costMult = t.costMult;

            LoadGlobals();
        }
        public TechLevel(ConfigNode node)
        {
            Load(node);

            LoadGlobals();
        }

        // LOADERS
        // Gets global node
        protected bool LoadGlobals()
        {
            if (globalTechLevels == null)
            {
                globalTechLevels = RFSettings.Instance.techLevels;
            }

            return globalTechLevels != null;
        }

        // loads from an override node
        public bool Load(ConfigNode node)
        {
            if (node.HasNode("atmosphereCurve"))
                atmosphereCurve.Load(node.GetNode("atmosphereCurve"));
            else
            {
                atmosphereCurve = null;
                return false;
            }

            if (node.HasNode("velocityCurve"))
                velocityCurve.Load(node.GetNode("velocityCurve"));
            else
                velocityCurve = null;

            if (node.HasValue("TWR"))
                TWR = double.Parse(node.GetValue("TWR"));
            else
                TWR = -1;

            if (node.HasValue("thrustMultiplier"))
                thrustMultiplier = double.Parse(node.GetValue("thrustMultiplier"));
            else
                thrustMultiplier = -1;

            if (node.HasValue("massMultiplier"))
                massMultiplier = double.Parse(node.GetValue("massMultiplier"));
            else
                massMultiplier = -1;

            if (node.HasValue("minThrottleMultiplier"))
                minThrottleMultiplier = double.Parse(node.GetValue("minThrottleMultiplier"));
            else
                minThrottleMultiplier = -1;

            if (node.HasValue("gimbalRange"))
                gimbalRange = float.Parse(node.GetValue("gimbalRange"));
            else
                gimbalRange = -1;

            if (node.HasValue("costMult"))
                costMult = float.Parse(node.GetValue("costMult"));
            else
                costMult = 1f;

            if (node.HasValue("techRequired"))
                techRequired = node.GetValue("techRequired");
            else
                techRequired = "";

            return true;
        }

        // loads a given techlevel from global techlevels-style node
        public bool Load(ConfigNode node, int level)
        {
            var tLs = node.GetNodes("TECHLEVEL");
            if (tLs.Count() > 0)
            {
                foreach (ConfigNode n in tLs)
                    if (n.HasValue("name") && n.GetValue("name").Trim().Equals(level.ToString()))
                        return Load(n);
                return false;
            }

            if (node.HasValue("techLevelType"))
                return Load(node.GetValue("techLevelType"), level);

            if (node.HasNode("TLISP" + level))
                atmosphereCurve.Load(node.GetNode("TLISP" + level));
            else
            {
                atmosphereCurve = null;
                return false;
            }

            if (node.HasNode("TLVC" + level))
                velocityCurve.Load(node.GetNode("TLVC" + level));
            else
                velocityCurve = null;

            if (node.HasValue("TLTWR" + level))
                TWR = double.Parse(node.GetValue("TLTWR" + level));
            else
                TWR = 60;

            if (node.HasValue("TLTHROTTLE" + level))
                minThrottleMultiplier = double.Parse(node.GetValue("TLTHROTTLE" + level));
            else
                minThrottleMultiplier = 0.0;

            if (node.HasValue("TLGIMBAL" + level))
                gimbalRange = float.Parse(node.GetValue("TLGIMBAL" + level));
            else
                gimbalRange = -1;

            if (node.HasValue("TLCOST" + level))
                costMult = float.Parse(node.GetValue("TLCOST" + level));
            else
                costMult = 1;

            if (node.HasValue("TLTECH" + level))
                techRequired = node.GetValue("TLTECH" + level);
            else
                techRequired = "";

            return true;
        }

        // loads from global techlevels
        public bool Load(string type, int level)
        {

            if (globalTechLevels == null)
                return false;
            foreach (ConfigNode node in globalTechLevels.GetNodes("ENGINETYPE"))
            {
                if (node.HasValue("name") && node.GetValue("name").Equals(type))
                    return Load(node, level);
            }
            return false;
        }

        // loads from anything
        public bool Load(ConfigNode cfg, ConfigNode mod, string type, int level)
        {
            // check local techlevel configs
            if (cfg != null)
            {
                var tLs = cfg.GetNodes("TECHLEVEL");
                if (tLs.Count() > 0)
                {
                    foreach (ConfigNode n in tLs)
                        if (n.HasValue("name") && n.GetValue("name").Equals(level.ToString()))
                            return Load(n);
                    return false;
                }
                if (cfg.HasValue("techLevelType"))
                    return Load(cfg.GetValue("techLevelType"), level);
            }

            // check module techlevel configs
            if (mod != null)
            {
                var tLs = mod.GetNodes("TECHLEVEL");
                if (tLs.Count() > 0)
                {
                    foreach (ConfigNode n in tLs)
                        if (n.HasValue("name") && n.GetValue("name").Equals(level.ToString()))
                            return Load(n);
                    return false;
                }
            }

            // check global
            //Debug.Log("*RFEng* Fallback to global for type " + type + ", TL " + level);
            return Load(type, level);
        }

        // MULTIPLIERS
        public double Thrust(TechLevel oldTL, bool constantMass = false)
        {
            if (oldTL.thrustMultiplier > 0 && thrustMultiplier > 0)
                return thrustMultiplier / oldTL.thrustMultiplier;

            if (constantMass)
                return TWR / oldTL.TWR;
            else
                return TWR / oldTL.TWR * oldTL.atmosphereCurve.Evaluate(0) / atmosphereCurve.Evaluate(0);
        }

        public double Mass(TechLevel oldTL, bool constantThrust = false)
        {
            if (oldTL.massMultiplier > 0 && massMultiplier > 0)
                return massMultiplier / oldTL.massMultiplier;

            if (constantThrust)
                return oldTL.TWR / TWR;
            else
                return oldTL.atmosphereCurve.Evaluate(0) / atmosphereCurve.Evaluate(0);
        }

        public double Throttle()
        {
            if (minThrottleMultiplier < 0)
                return 0.0;
            if (minThrottleMultiplier > 1.0)
                return 1.0;
            return minThrottleMultiplier;
        }

        public float GimbalRange
        {
            get
            {
                return gimbalRange;
            }
        }

        public float CostMult
        {
            get
            {
                return costMult;
            }
        }

        public FloatCurve AtmosphereCurve
        {
            get
            {
                return atmosphereCurve;
            }
        }

        // looks up in global techlevels
        public static int MaxTL(string type)
        {
            int max = -1;
            if (globalTechLevels == null)
                return max;
            foreach (ConfigNode node in globalTechLevels.GetNodes("ENGINETYPE"))
            {
                if (node.HasValue("name") && node.GetValue("name").Equals(type))
                {
                    var tLs = node.GetNodes("TECHLEVEL");
                    if (tLs.Count() > 0)
                    {
                        return MaxTL(node);
                    }
                    foreach (ConfigNode.Value val in node.values)
                    {
                        string stmp = val.name;
                        stmp = stmp.Replace("TLTWR", "");
                        int itmp;
                        if (int.TryParse(stmp.Trim(), out itmp))
                            if (itmp > max)
                                max = itmp;
                    }
                }
            }
            return max;
        }

        // looks up in global techlevels
        public static int MinTL(string type)
        {
            int min = int.MaxValue;
            if (globalTechLevels == null)
                return min;
            foreach (ConfigNode node in globalTechLevels.GetNodes("ENGINETYPE"))
            {
                if (node.HasValue("name") && node.GetValue("name").Equals(type))
                {
                    var tLs = node.GetNodes("TECHLEVEL");
                    if (tLs.Count() > 0)
                    {
                        return MinTL(node);
                    }
                    foreach (ConfigNode.Value val in node.values)
                    {
                        string stmp = val.name;
                        stmp = stmp.Replace("TLTWR", "");
                        int itmp;
                        if (int.TryParse(stmp.Trim(), out itmp))
                            if (itmp < min)
                                min = itmp;
                    }
                }
            }
            return min;
        }

        // local check, with optional fallback to global
        public static int MaxTL(ConfigNode node, string type = "")
        {
            int max = -1;
            if (node != null)
            {
                foreach (ConfigNode n in node.GetNodes("TECHLEVEL"))
                {
                    int itmp;
                    if (n.HasValue("name") && int.TryParse(n.GetValue("name").Trim(), out itmp))
                        if (itmp > max)
                            max = itmp;
                }
            }
            if (max < 0 && !type.Equals(""))
                max = MaxTL(type);
            return max;
        }

        // local check, with optional fallback to global
        public static int MinTL(ConfigNode node, string type = "")
        {
            int min = int.MaxValue;
            if (node != null)
            {
                foreach (ConfigNode n in node.GetNodes("TECHLEVEL"))
                {
                    int itmp;
                    if (n.HasValue("name") && int.TryParse(n.GetValue("name").Trim(), out itmp))
                        if (itmp < min)
                            min = itmp;
                }
            }
            if (min >= int.MaxValue && !type.Equals(""))
                min = MinTL(type);
            return min;
        }

        // full check
        public static int MaxTL(ConfigNode cfg, ConfigNode mod, string type)
        {
            if (cfg.GetNodes("TECHLEVEL").Count() > 0)
                return MaxTL(cfg, type);
            else if (cfg.HasValue("techLevelType"))
                return MaxTL(cfg.GetValue("techLevelType"));
            else
                return MaxTL(mod, type);
        }

        // full check
        public static int MinTL(ConfigNode cfg, ConfigNode mod, string type)
        {
            if (cfg.GetNodes("TECHLEVEL").Count() > 0)
                return MinTL(cfg, type);
            else if (cfg.HasValue("techLevelType"))
                return MinTL(cfg.GetValue("techLevelType"));
            else
                return MinTL(mod, type);
        }

        // Check if can switch to TL
        public static bool CanTL(ConfigNode cfg, ConfigNode mod, string type, int level)
        {
            TechLevel nTL = new TechLevel();
            if (!nTL.Load(cfg, mod, type, level))
                return false;
            return HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX || nTL.techRequired.Equals("") || ResearchAndDevelopment.GetTechnologyState(nTL.techRequired) == RDTech.State.Available;
        }
    }
}
