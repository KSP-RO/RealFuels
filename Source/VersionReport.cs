using System;
using UnityEngine;
using KSPAPIExtensions;

using KSP.IO;

namespace RealFuels {

	[KSPAddon(KSPAddon.Startup.Instantly, true)]
	public class MFSVersionReport : MonoBehaviour
	{

		void Start ()
		{
			Debug.Log (MFSSettings.GetVersion ());
			Destroy (this);
		}
	}
}
