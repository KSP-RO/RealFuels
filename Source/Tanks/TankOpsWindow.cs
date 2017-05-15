using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Collections.ObjectModel;

using KSP.UI.Screens;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
	[KSPAddon (KSPAddon.Startup.Flight, false)]
	public class TankOpsWindow : MonoBehaviour
	{
		static bool GUIOpen = false;

		private GUIStyle _windowstyle, _labelstyle;
		private bool hasInitStyles = false;

		private FuelTank selecting = null;
		private Rect guiWindowRect = new Rect(20, 100, 1, 1);
		public Vector2 scrollPosition;

		private ModuleFuelTanks tankModule;
		private static TankOpsWindow instance;

		public void Awake() {
			//Debug.Log (/*debuggingClass.modName + */"Starting Awake()");
			//enabled = false;
			instance = this;
		}

		private void InitStyle()
		{
			_windowstyle = new GUIStyle(HighLogic.Skin.window);
			_labelstyle = new GUIStyle(HighLogic.Skin.label);
			hasInitStyles = true;
		}

		public void OnGUI()
		{//Executes code whenever screen refreshes.  Extension to enable use of button along main bar on top-right of screen.
			if (GUIOpen) {
				TanksGUI ();//If button is enabled, display rectangle.
			}//end if
		}//end OnGui extension

		public static void Open(ModuleFuelTanks tankModule) {
			if (instance != null) {
				instance.tankModule = tankModule;

				instance.InitStyle ();

				GUIOpen = true;		
				Debug.Log (/*debuggingClass.modName + */"Ending Open()");
			}
		}

		void OnDestroy ()
		{
			instance = null;
		}
		public void TanksGUI(){
			//Debug.Log (/*debuggingClass.modName + */"Starting TanksGUI()");

			guiWindowRect = GUILayout.Window (GetInstanceID (), guiWindowRect, GUIWindow, "Tank Operations",  GUILayout.Width( 500 ), GUILayout.ExpandHeight( true ));

			//GUI.BeginGroup(new Rect(Screen.width / 2 - 250, Screen.height / 2 - 250, 500, 500));

			//GUI.EndGroup();

		}

		public void GUIWindow(int windowID) {
			GUILayout.BeginVertical ();
			GUILayout.Label("Empty the propellants from any following tank, tanks which are empty can have their volume repurposed for another propellant type.");

			//Debug.Log ("TankOpsWindow: " + this.tankModule);

			foreach (FuelTank tank in this.tankModule.tankList) {
				if (tank.maxAmount == 0) {
					continue;
				}
				GUILayout.BeginHorizontal ("box");

				//Debug.Log ("TankOpsWindow: loop " + tank);
				GUILayout.Label (string.Format("{0} {1:P2} full.", tank.resource.resourceName, (tank.amount/tank.maxAmount)));
				if (tank.amount > 0) {
					if (GUILayout.Button ("Drain tank", GUILayout.Width (150f))) {
						tank.amount = 0;
					}
				}
				if (selecting == null && tank.amount == 0) {
					if (GUILayout.Button ("Switch tank type", GUILayout.Width (150f))) {
						if (tank.amount == 0) {
							selecting = tank;
							guiWindowRect.height += 200;
						}
					}
				}

				if (selecting == tank) {
					//GUILayout.BeginVertical ("box");
					scrollPosition = GUILayout.BeginScrollView(scrollPosition);
					foreach (FuelTank possibleTank in this.tankModule.tankList) {
						if (tank == possibleTank) {
							continue;
						}
						if (GUILayout.Button (possibleTank + "", GUILayout.Width (200f))) {
							double oldAmount = tank.maxAmount;
							FuelTank outTank = null;
							if (this.tankModule.tankList.TryGet(""+possibleTank,out outTank)) {
								if (outTank.amount > 0) {
									selecting = null;
									guiWindowRect.height -= 200;
									continue;
								}
								oldAmount += possibleTank.maxAmount;
							}
							if (tank.amount == 0) {
								tank.maxAmount = 0;

								possibleTank.maxAmount = oldAmount;
								possibleTank.amount = 0;

								selecting = null;
								guiWindowRect.height -= 200;
							}
						}

					}
					GUILayout.EndScrollView();
					//GUILayout.EndVertical ();
				}

				GUILayout.EndHorizontal ();
			}
			if (GUILayout.Button ("Close this Window"))
				GUIOpen = false;

			GUILayout.EndVertical ();
			GUI.DragWindow ();

		}
	}
}
