//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace RealFuels
{
    //Use this class to handle any functions needed by all ModularFuels parts
    public class ModularFuelPartModule : PartModule
    {
        [KSPField(isPersistant = true)]
        protected double timestamp = 0.0;

        protected double precisionDeltaTime = 0.0;

        public static ModularFuelPartModule controllerModule = null;
        public static double controllerTimeStamp = 0.0;
        public static double controllerPrecisionDeltaTime = 0.0;

        //This makes sure that every frame the timestep is determined only once; all other Modular Fuel parts then use that timestep for all operations
        //This function must occur AFTER part-specific functions that are dependent on it if you also want to use precisionDeltaTime for persistence purposes
        public override void OnUpdate()
        {
            base.OnUpdate();

            //If the controller module isn't assigned, assign it; we cast to object because that is faster than the standard == null function, which involves Unity checking if the object was destroyed in addition to being null
            if ((object)controllerModule == null)
                controllerModule = this;

            //Here the controller does the floating point math
            if (controllerModule == this)
            {
                double tempTimeStamp = Planetarium.GetUniversalTime();
                controllerPrecisionDeltaTime = tempTimeStamp - controllerTimeStamp;
                controllerTimeStamp = tempTimeStamp;
            }
            precisionDeltaTime = controllerPrecisionDeltaTime;      //Assign the deltaTime for each part from the static field, so that this field can also be used for handling persistence stuff
        }

        public void OnDestroy()
        {
            controllerModule = null;
        }

        public override void OnSave(ConfigNode node)
        {
            timestamp = controllerTimeStamp;        //This ensures that the timestamp is saved for when the thing initially loads, so that "persistence" effects can be handled; do it before things are saved
            base.OnSave(node);
        }

        public override void OnStart(PartModule.StartState state)
        {
            base.OnStart(state);
            precisionDeltaTime = Planetarium.GetUniversalTime() - timestamp;        //This allows us to handle persistence by calculating the deltaTime between "now" and the last time this part was loaded
        }
    }
}
