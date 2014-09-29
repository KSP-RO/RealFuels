//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace RealFuels
{
    public class ModuleHeatPump: PartModule
    {
        public class ResourceRate
        {
            public string name;
            public float rate;
            public int id
            {
                get 
                {
                    return name.GetHashCode ();
                }
            }
            public ResourceRate(string name, float rate)
            {
                this.name = name;
                this.rate = rate;
            }

        }

        [KSPAction ("Activate Heat Pump")]
        public void ActivateAction (KSPActionParam param)
        {
            Activate ();
        }

        [KSPAction ("Shutdown Heat Pump")]
        public void ShutdownAction (KSPActionParam param)
        {
            Shutdown ();
        }

        [KSPAction ("Toggle Heat Pump")]
        public void ToggleAction (KSPActionParam param)
        {
            if(isActive)
                Shutdown ();
            else
                Activate ();
        }

        [KSPEvent(guiName = "Activate Heat Pump", guiActive = true)]
        public void Activate ()
        {
            isActive = true;
            Events ["Shutdown"].active = true;
            Events ["Activate"].active = false;
            //Events ["Shutdown"].guiActive = true;
            //Events ["Activate"].guiActive = false;
        }

        [KSPEvent(guiName = "Shutdown Heat Pump", guiActive = true)]
        public void Shutdown ()
        {
            isActive = false;
            Events ["Shutdown"].active = false;
            Events ["Activate"].active = true;
            //Events ["Shutdown"].guiActive = false;
            //Events ["Activate"].guiActive = true;
        }

        [KSPField(isPersistant = true)]
        public bool isActive = false;

        [KSPField(isPersistant = false)]
        public float heatDissipation = 0.12f;

        [KSPField(isPersistant = false)]
        public float heatConductivity = 0.12f;

        [KSPField(isPersistant = false)]
        public float heatTransfer = 1.0f;

        [KSPField(isPersistant = false)]
        public float heatGain = 0.0f;

        public List<ResourceRate> resources;

        public List<AttachNode> attachNodes = new List<AttachNode>();
        public List<string> attachNodeNames = new List<string>(); 

        public override string GetInfo ()
        {
            string s;
            s = "Heat Pump: " + heatTransfer + "/s\nRequirements:\n";
            foreach (ResourceRate resource in resources)
            {
                if(resource.rate > 1)
                    s += "  " + resource.name + ": " + resource.rate.ToString ("2F") + "/s\n";
                else if(resource.rate > 0.01666667f)
                    s += "  " + resource.name + ": " + (resource.rate * 60).ToString ("2F") + "/m\n";
                else
                    s += "  " + resource.name + ": " + (resource.rate * 3600).ToString ("2F") + "/h\n";
            }

            return s;
        }

        public override void OnAwake ()
        {
            base.OnAwake ();
            resources = new List<ResourceRate> ();
            attachNodes = new List<AttachNode>();
            part.heatConductivity = heatConductivity;
            part.heatDissipation = heatDissipation;
            print("*RF* ModuleHeatPump.Awake(): heatConductivity / heatDissipation = " + part.heatConductivity.ToString() + " / " + part.heatDissipation.ToString());
        }

        public override void OnLoad (ConfigNode node)
        {
            base.OnLoad (node);
            foreach (ConfigNode n in node.GetNodes ("RESOURCE")) 
            {
                if(n.HasValue ("name") && n.HasValue ("rate")) 
                {
                    float rate;
                    float.TryParse (n.GetValue ("rate"), out rate);
                    resources.Add (new ResourceRate(n.GetValue("name"), rate));
                }
            }
            foreach (ConfigNode c in node.GetNodes("HEATPUMP_NODE"))
            {
                // It would be easier to do this by just reading multiple names from one node
                // Doing it this way allows for expansion later such as other attributes in each HEATPUMP_NODE
                print("*RF* Heatpump searching HEATPUMP_NODE");
                if (c.HasValue("name"))
                {
                    string nodeName = c.GetValue("name");
                    print("*RF* Heatpump adding " + nodeName);
                    attachNodeNames.Add(nodeName);
                }

            }

        }

        public override void OnStart (StartState state)
        {
            base.OnStart (state);
            part.heatConductivity = heatConductivity;
            part.heatDissipation = heatDissipation;

            Events ["Shutdown"].active = false;
            Events ["Activate"].active = true;

            if(resources.Count == 0 && part.partInfo != null) 
            {
                if(part.partInfo.partPrefab.Modules.Contains ("ModuleHeatPump"))
                    resources = ((ModuleHeatPump) part.partInfo.partPrefab.Modules["ModuleHeatPump"]).resources;
            }
            if (attachNodes.Count == 0)
            {
                foreach (string nodeName in attachNodeNames)
                {
                    AttachNode node = this.part.findAttachNode(nodeName);
                    if ((object)node != null)
                        attachNodes.Add(node);
                }
            }
            print("*RF* ModuleHeatPump.OnStart(): heatConductivity / heatDissipation = " + part.heatConductivity.ToString() + " / " + part.heatDissipation.ToString());
        }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || !isActive)
            {
                return;
            }

            foreach (AttachNode attachNode in attachNodes)
            {
                Part targetPart = attachNode.attachedPart;

                if ((object)targetPart == null)
                    continue;

                ProcessCooling(targetPart);
            }
            ProcessCooling(this.part.parent);

            /*
            float efficiency = (part.parent.temperature + 273) / (part.parent.temperature + 300);
            if (part.parent.temperature < -273)
                efficiency = 0;
            if (heatTransfer < 0) 
            {
                efficiency = (part.temperature + 273) / (part.temperature + 300);
                if(part.temperature < -273)
                    efficiency = 0;
            }
            foreach (ResourceRate resource in resources) {
                if(resource.rate > 0) {
                    float available = part.RequestResource(resource.id, resource.rate);
                    if(efficiency > available / resource.rate)
                        efficiency = available / resource.rate;
                }
            }
            // this really should be linear, but KSP's current heat model is weird.
            part.parent.temperature -= efficiency * heatTransfer * Time.deltaTime / part.parent.mass;
            part.temperature += efficiency * heatTransfer * heatGain * Time.deltaTime;
            */
        }

        public void ProcessCooling(Part targetPart)
        {
            print("*RF* ModuleHeatPump processing part temperature and efficiency: " + targetPart.name);
            float efficiency = (targetPart.temperature + 273) / (targetPart.temperature + 300);
            if (targetPart.temperature < -273)
                efficiency = 0;
            if (heatTransfer < 0) 
            {
                efficiency = (part.temperature + 273) / (part.temperature + 300);
                if(part.temperature < -273)
                    efficiency = 0;
            }
            print("*RF* ModuleHeatPump processing resources");
            foreach (ResourceRate resource in resources)
            {
                if(resource.rate > 0)
                {
                    float available = part.RequestResource(resource.id, resource.rate);
                    if(efficiency > available / resource.rate)
                        efficiency = available / resource.rate;
                }
            }
            // this really should be linear, but KSP's current heat model is weird.
            print("*RF* ModuleHeatPump setting targetPart temperature");
            targetPart.temperature -= efficiency * heatTransfer * Time.deltaTime / targetPart.mass;
            // target part ought to take into consideration resource mass too - Starwaster
            print("*RF* ModuleHeatPump setting radiator temperature");
            part.temperature += efficiency * heatTransfer * heatGain * Time.deltaTime;
        }
    }
}
