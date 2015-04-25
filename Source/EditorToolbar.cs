using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using RUI.Icons.Selectable;

using KSP.IO;

namespace ExtraplanetaryLaunchpads {
	[KSPAddon (KSPAddon.Startup.EditorAny, false) ]
	public class MFTEditorToolbar : MonoBehaviour
	{
		//static Texture texture;
		static Icon icon;

		void onGUIEditorToolbarReady ()
		{
			if (icon == null) {
				//texture = GameDatabase.Instance.GetTexture ("ExtraplanetaryLaunchpads/Textures/icon_button", false);
				var iconloader = PartCategorizer.Instance.iconLoader;
				icon = iconloader.GetIcon("R&D_node_icon_fuelsystems");
			}
			var cat = PartCategorizer.Instance.filters.Find (c => c.button.categoryName == "Filter by Module");
			var subcat = cat.subcategories.Find (c => c.button.categoryName == "Modular Fuel Tank");
			subcat.button.SetIcon (icon);
		}

		void Awake ()
		{
			GameEvents.onGUIEditorToolbarReady.Add (onGUIEditorToolbarReady);
		}

		void OnDestroy ()
		{
			GameEvents.onGUIEditorToolbarReady.Remove (onGUIEditorToolbarReady);
		}
	}
}
