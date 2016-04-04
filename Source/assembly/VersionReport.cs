using System;
using UnityEngine;
using System.Reflection;

using KSP.IO;

namespace RealFuels {

	[KSPAddon(KSPAddon.Startup.Instantly, true)]
	public class MFSVersionReport : MonoBehaviour
	{
		static string version = null;

        public static string GetAssemblyVersionString (Assembly assembly)
        {
            string version = assembly.GetName().Version.ToString ();

            var cattrs = assembly.GetCustomAttributes(true);
            foreach (var attr in cattrs) {
                if (attr is AssemblyInformationalVersionAttribute) {
                    var ver = attr as AssemblyInformationalVersionAttribute;
                    version = ver.InformationalVersion;
                    break;
                }
            }

            return version;
        }

        public static string GetAssemblyTitle (Assembly assembly)
        {
            string title = assembly.GetName().Name;

            var cattrs = assembly.GetCustomAttributes(true);
            foreach (var attr in cattrs) {
                if (attr is AssemblyTitleAttribute) {
                    var ver = attr as AssemblyTitleAttribute;
                    title = ver.Title;
                    break;
                }
            }

            return title;
        }

		public static string GetVersion ()
		{
			if (version != null) {
				return version;
			}
			var asm = Assembly.GetCallingAssembly ();
			var title = GetAssemblyTitle (asm);
			version = title + " " + GetAssemblyVersionString (asm);
			return version;
		}

		void Start ()
		{
			Debug.Log (GetVersion ());
			Destroy (this);
		}
	}
}
