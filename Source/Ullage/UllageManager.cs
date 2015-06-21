using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealFuels.Ullage
{
    public class UllageManager
    {
        private UllageManager _instance = null;
        Dictionary<Vessel, UllageModule> vesselLookup;
        public UllageManager Instance
        {
            get
            {
                if(HighLogic.LoadedSceneIsFlight)
                {
                    if (_instance == null)
                        _instance = new UllageManager();
                }
                else
                {
                    _instance = null;
                }
                return _instance;
            }
        }

        public UllageManager()
        {
            vesselLookup = new Dictionary<Vessel, UllageModule>();
        }
    }
}
