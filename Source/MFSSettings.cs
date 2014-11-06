using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace RealFuels
{
    // ReSharper disable once InconsistentNaming
    public class MFSSettings : MonoBehaviour
    {
        [Persistent]
        public bool useRealisticMass;
        [Persistent]
        public float tankMassMultiplier = 1;

        [Persistent]
        public float engineMassMultiplier = 1;
        [Persistent]
        public float heatMultiplier = 1;

        [Persistent]
        public float baseCostPV = 0.01f; // 0.01 * 1000 USD per liter

        [Persistent]
        public float partUtilizationDefault = 86;
        [Persistent]
        public bool partUtilizationTweakable = false;


        public HashSet<string> ignoreFuelsForFill = new HashSet<string>();

        public TankDefinitionList tankDefinitions = new TankDefinitionList();

        // TODO: Move engine tech levels into here too.

        #region Initialization
        private static MFSSettings _instance;
        public static MFSSettings Instance
        {
            get
            {
                // Will get destroyed on scene load, which is what we want 
                // because this means the DB will get reloaded.
                if (_instance != null && _instance)
                    return _instance;

                //Debug.Log("*MFS* Loading settings");

                GameObject gameObject = new GameObject(typeof(MFSSettings).FullName);
                _instance = gameObject.AddComponent<MFSSettings>();
                return _instance;
            }
        }

        static string version;
        public static string GetVersion()
        {
            if (version != null) {
                return version;
            }

            var asm = Assembly.GetCallingAssembly ();
            // ReSharper disable once PossibleNullReferenceException
            var title = (asm.GetCustomAttributes(typeof(AssemblyTitleAttribute), false)[0] as AssemblyTitleAttribute).Title;
            version = title + " " + SystemUtils.GetAssemblyVersionString (asm);

            return version;
        }

        internal void Awake()
        {
            ConfigNode node = GameDatabase.Instance.GetConfigNodes("MFSSETTINGS").Last();
            Debug.Log("*MFS* Loading global settings");

            ConfigNode.LoadObjectFromConfig(this, node);

            ConfigNode ignoreNode = node.GetNode("IgnoreFuelsForFill");
            if (ignoreNode != null)
                foreach (ConfigNode.Value v in ignoreNode.values)
                    ignoreFuelsForFill.Add(v.name);
            

            foreach (ConfigNode defNode in GameDatabase.Instance.GetConfigNodes("TANK_DEFINITION"))
            {
                if(tankDefinitions.Contains(defNode.GetValue("name")))
                    Debug.LogWarning("[MFS] Ignored duplicate definition of tank type " + defNode.GetValue("name"));
                else
                    tankDefinitions.Add(new TankDefinition(defNode));
            }
        }

        #endregion

        #region TankDefinition

        public class TankDefinition : IConfigNode
        {
            [Persistent]
            public string name;

            [Persistent]
            public string basemass;

            [Persistent]
            public string baseCost;

            public Tanks.ModuleFuelTanks.FuelTankList tankList = new Tanks.ModuleFuelTanks.FuelTankList();

            public TankDefinition() { }

            public TankDefinition(ConfigNode node)
            {
                Load(node);
            }

            public void Load(ConfigNode node)
            {
                if (!(node.name.Equals("TANK_DEFINITION") && node.HasValue("name")))
                    return;

                ConfigNode.LoadObjectFromConfig(this, node);
                tankList.Load(node);

                for (int i = tankList.Count - 1; i >= 0; --i)
                {
                    var tank = tankList[i];
                    if (!tank.resourceAvailable)
                    {
                        //Debug.LogWarning("[MFT] Unable to initialize tank definition for resource \"" + tank.name + "\" in tank definition \"" + name + "\" as this resource is not defined.");
                        tankList.RemoveAt(i);
                    }
                }
            }

            public void Save(ConfigNode node)
            {
                ConfigNode.CreateConfigFromObject(this, node);
                tankList.Save(node);
            }
        }

        public class TankDefinitionList : KeyedCollection<string, TankDefinition>, IConfigNode
        {
            protected override string GetKeyForItem(TankDefinition item)
            {
                return item.name;
            }

            public void Load(ConfigNode node)
            {
                foreach (ConfigNode tankNode in node.GetNodes("TANK_DEFINITION"))
                    Add(new TankDefinition(tankNode));
            }

            public void Save(ConfigNode node)
            {
                foreach (TankDefinition tank in this)
                {
                    ConfigNode tankNode = new ConfigNode("TANK");
                    tank.Save(tankNode);
                    node.AddNode(tankNode);
                }
            }
        }

        #endregion
    }

}
