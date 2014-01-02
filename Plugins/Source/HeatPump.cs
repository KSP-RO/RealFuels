//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace ModularFuelTanks
{
	public class ModuleHeatPump: PartModule
	{
		public class ResourceRate
		{
			public string name;
			public float rate;
			public int id {
				get {
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
			base.Events ["Shutdown"].active = true;
			base.Events ["Activate"].active = false;
		}

		[KSPEvent(guiName = "Shutdown Heat Pump", guiActive = true)]
		public void Shutdown ()
		{
			isActive = false;
			base.Events ["Shutdown"].active = false;
			base.Events ["Activate"].active = true;
		}

		[KSPField(isPersistant = true)]
		public bool isActive = false;

		[KSPField(isPersistant = true)]
		public float heatDissipation = 0.0f;

		[KSPField(isPersistant = true)]
		public float heatTransfer = 0.0f;

		public List<ResourceRate> resources;

		public override string GetInfo ()
		{
			string s;
			s = "Heat Pump: " + heatTransfer + "/s\nRequirements:\n";
			foreach (ResourceRate resource in resources) {
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
		}

		public override void OnLoad (ConfigNode node)
		{
			base.OnLoad (node);
			foreach (ConfigNode n in node.GetNodes ("RESOURCE")) {
				if(n.HasValue ("name") && n.HasValue ("rate")) {
					float rate;
					float.TryParse (n.GetValue ("rate"), out rate);
					resources.Add (new ResourceRate(n.GetValue("name"), rate));
				}
			}
		}

		public override void OnStart (StartState state)
		{
			base.OnStart (state);
			if(resources.Count == 0 && part.partInfo != null) {
				if(part.partInfo.partPrefab.Modules.Contains ("ModuleHeatPump"))
					resources = ((ModuleHeatPump) part.partInfo.partPrefab.Modules["ModuleHeatPump"]).resources;
			}
		}

		void FixedUpdate()
		{
			if (HighLogic.LoadedSceneIsEditor || part.parent == null || !isActive)
				return;

			float efficiency = (part.parent.temperature + 273) / (part.parent.temperature + 300);
			if (part.parent.temperature < -273)
				efficiency = 0;
			if (heatTransfer < 0) {
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
			part.temperature += efficiency * heatTransfer * Time.deltaTime;
		}
	}
}
