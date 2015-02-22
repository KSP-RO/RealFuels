using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;

using KSP.IO;

namespace RealFuels
{

	public class MFTSettings : ScenarioModule
	{
		static string version = null;
		public static string GetVersion ()
		{
			if (version != null) {
				return version;
			}

			var asm = Assembly.GetCallingAssembly ();
			version =  asm.GetName().Version.ToString ();

			var cattrs = asm.GetCustomAttributes(true);
			foreach (var attr in cattrs) {
				if (attr is AssemblyInformationalVersionAttribute) {
					var ver = attr as AssemblyInformationalVersionAttribute;
					version = ver.InformationalVersion;
					break;
				}
			}

			return version;
		}

		public static MFTSettings current
		{
			get {
				var game = HighLogic.CurrentGame;
				return game.scenarios.Select (s => s.moduleRef).OfType<MFTSettings> ().SingleOrDefault ();
				
			}
		}

		public static void CreateSettings (Game game)
		{
			if (!game.scenarios.Any (p => p.moduleName == typeof (MFTSettings).Name)) {
				Debug.Log (String.Format ("[MFT] Settings create"));
				var proto = game.AddProtoScenarioModule (typeof (MFTSettings), GameScenes.SPACECENTER, GameScenes.EDITOR, GameScenes.SPH, GameScenes.TRACKSTATION, GameScenes.FLIGHT);
				proto.Load (ScenarioRunner.fetch);
			}
		}

		public override void OnLoad (ConfigNode config)
		{
			var settings = config.GetNode ("Settings");
			if (settings == null) {
				settings = new ConfigNode ("Settings");
				if (HighLogic.LoadedScene == GameScenes.SPACECENTER) {
					enabled = true;
				}
			}
			//XXX fill in with config node stuff to load settings using the
			// settings config node
		}

		public override void OnSave(ConfigNode config)
		{
			var settings = new ConfigNode ("Settings");
			config.AddNode (settings);

			//XXX fill in with config node stuff to save settings to the
			// settings config node
		}
		
		public override void OnAwake ()
		{
			enabled = false;
		}

		void OnGUI ()
		{
			var rect = new Rect(Screen.width / 2 - 250, Screen.height / 2 - 30,
								500, 100);

			GUI.skin = HighLogic.Skin;

			string name = "Modular Fuel Tanks Settings: ";
			string ver = GetVersion ();
			GUILayout.BeginArea(rect, name + ver, GUI.skin.window);
			GUILayout.BeginVertical ();

			//XXX fill in with per-save settings buttons etc

			if (GUILayout.Button ("OK")) {
				enabled = false;
			}
			GUILayout.EndVertical ();
			GUILayout.EndArea();
		}
	}

	// Fun magic to get a custom scenario into a game automatically.

	public class MFTSettingsCreator
	{
		public static MFTSettingsCreator me;
		void onGameStateCreated (Game game)
		{
			MFTSettings.CreateSettings (game);
		}

		public MFTSettingsCreator ()
		{
			GameEvents.onGameStateCreated.Add (onGameStateCreated);
		}
	}

	[KSPAddon(KSPAddon.Startup.Instantly, false)]
	public class MFTSettingsCreatorSpawn : MonoBehaviour
	{

		void Start ()
		{
			//Debug.Log (String.Format ("MFTSettingsCreatorSpawn.Start"));
			MFTSettingsCreator.me = new MFTSettingsCreator ();
			enabled = false;
		}
	}
}
