using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RealFuels.Ullage
{
    public class UllageModule : VesselModule
    {

        /*localAcceleration = engine.transform.InverseTransformDirection(vessel.acceleration - FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()));
        rotation = engine.transform.InverseTransformDirection(engine.rigidbody.angularVelocity);

        if (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > TimeWarp.MaxPhysicsRate)
            {
                // Time warping... (5x -> 100000x)
                //Debug.Log("Vessel state: " + vessel.Landed.ToString() + " " + vessel.Splashed.ToString() + " " + vessel.LandedOrSplashed.ToString());
                if (vessel.LandedOrSplashed == true)
                {
                    localAcceleration = engine.transform.InverseTransformDirection(-FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()));
                    localAccelerationAmount = localAcceleration * deltaTime;
                    rotation.Set(0.0f, 0.0f, 0.0f);
                    rotationAmount.Set(0.0f, 0.0f, 0.0f);
                }
                else
                {
                    localAcceleration.Set(0.0f, ventingAcc, 0.0f);
                    localAccelerationAmount.Set(0.0f, ventingAcc * deltaTime, 0.0f);
                    rotation.Set(0.0f, 0.0f, 0.0f);
                    rotationAmount.Set(0.0f, 0.0f, 0.0f);
                }
            }
            else
            {
                localAcceleration.y += ventingAcc;
                localAccelerationAmount.y += ventingAcc * deltaTime;
            }*/
    }
}
