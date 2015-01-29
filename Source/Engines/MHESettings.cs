using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace RealFuels
{
    // ReSharper disable once InconsistentNaming
    public class MHESettings : MonoBehaviour
    {
        [Persistent]
        public float engineMassMultiplier = 1;
        [Persistent]
        public float heatMultiplier = 1;

        public Tanks.TankDefinitionList tankDefinitions = new Tanks.TankDefinitionList();

        private static MHESettings _instance;
        public static MHESettings Instance
        {
            get
            {
                // Will get destroyed on scene load, which is what we want 
                // because this means the DB will get reloaded.
                if (_instance != null && _instance)
                    return _instance;

                //Debug.Log("*MHE* Loading settings");

                GameObject gameObject = new GameObject(typeof(MHESettings).FullName);
                _instance = gameObject.AddComponent<MHESettings>();
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
            var title = SystemUtils.GetAssemblyTitle (asm);
            version = title + " " + SystemUtils.GetAssemblyVersionString (asm);

            return version;
        }

        internal void Awake()
        {
            ConfigNode node = GameDatabase.Instance.GetConfigNodes("MHESETTINGS").Last();
            Debug.Log("*MHE* Loading global settings");

            ConfigNode.LoadObjectFromConfig(this, node);
        }
    }

}
