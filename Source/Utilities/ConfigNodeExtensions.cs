using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/* RealChute was made by Christophe Savard (stupid_chris) and is licensed under CC-BY-NC-SA. You can remix, modify and
 * redistribute the work, but you must give attribution to the original author (me) and you cannot sell your derivatives.
 * For more information contact me on the forum. */

// This is the ConfigNodeExtensions class, featuring trygets for nodes.

namespace RealFuels
{
    public static class ConfigNodeExtensions
    {
        #region Methods
        /// <summary>
        /// Sees if the ConfigNode has a named node and stores it in the ref value
        /// </summary>
        /// <param name="name">Name of the node to find</param>
        /// <param name="result">Value to store the result in</param>
        public static bool TryGetNode(this ConfigNode node, string name, ref ConfigNode result)
        {
            if (node.HasNode(name))
            {
                result = node.GetNode(name);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the node has a value that equals the specified output
        /// </summary>
        /// <param name="name">Name of the value to check</param>
        /// <param name="value">Value to check</param>
        public static bool HasValue(this ConfigNode node, string name, string value)
        {
            return node.HasValue(name) && node.GetValue(name) == value;
        }

        /// <summary>
        /// Returns true if the ConfigNode has the specified value and stores it within the ref. Ref is untouched if value not present.
        /// </summary>
        /// <param name="name">Name of the value searched for</param>
        /// <param name="value">Value to assign</param>
        public static bool TryGetValue(this ConfigNode node, string name, ref string value)
        {
            if (node.HasValue(name))
            {
                value = node.GetValue(name);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the ConfigNode has a value of the given name and stores it as an array within the given ref value. Does not touch the ref if not.
        /// </summary>
        /// <param name="name">Name of the value to look for</param>
        /// <param name="value">Value to store the result in</param>
        /*public static bool TryGetValue(this ConfigNode node, string name, ref string[] value)
        {
            if (node.HasValue(name))
            {
                value = RCUtils.ParseArray(node.GetValue(name));
                return true;
            }
            return false;
        }*/

        /// <summary>
        /// Returns true if the ConfigNode has the specified value and stores it in the ref value if it is parsable. Value is left unchanged if not.
        /// </summary>
        /// <param name="name">Name of the value to searched for</param>
        /// <param name="value">Value to assign</param>
        public static bool TryGetValue(this ConfigNode node, string name, ref float value)
        {
            float result = 0;
            if (node.HasValue(name) && float.TryParse(node.GetValue(name), out result))
            {
                value = result;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the ConfigNode has the specified value and stores it in the ref value if it is parsable. Value is left unchanged if not.
        /// </summary>
        /// <param name="name">Name of the value to searched for</param>
        /// <param name="value">Value to assign</param>
        public static bool TryGetValue(this ConfigNode node, string name, ref Color value)
        {
            Color result;
            if (node.HasValue(name))
            {
                try
                {
                    Vector4 col = KSPUtil.ParseVector4(node.GetValue(name));
                    result = new Color(col.x, col.y, col.z, col.w);
                    value = result;
                    return true;
                }
                catch (Exception e)
                {
                    MonoBehaviour.print("Exception: Error parsing as color4: original text: " + node.GetValue("name") + " --- exception " + e.Message);
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true if the ConfigNode contains the value and sotres it in the ref if it is parsable. Value is left unchanged if not.
        /// </summary>
        /// <param name="name">Name of the value to look for</param>
        /// <param name="value">Value to store the result in</param>
        public static bool TryGetValue(this ConfigNode node, string name, ref int value)
        {
            int result = 0;
            if (node.HasValue(name) && int.TryParse(node.GetValue(name), out result))
            {
                value = result;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the ConfigNode has the specified value and stores it in the ref value if it is parsable. Value is left unchanged if not.
        /// </summary>
        /// <param name="name">Name of the value to searched for</param>
        /// <param name="value">Value to assign</param>
        public static bool TryGetValue(this ConfigNode node, string name, ref double value)
        {
            double result = 0;
            if (node.HasValue(name) && double.TryParse(node.GetValue(name), out result))
            {
                value = result;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sees if the ConfigNode has a given value, and tries to store it in the ref if it's parseable
        /// </summary>
        /// <param name="name">Name of the value to get</param>
        /// <param name="value">Value to store the result in</param>
        public static bool TryGetValue(this ConfigNode node, string name, ref bool value)
        {
            bool result = false;
            if (node.HasValue(name) && bool.TryParse(node.GetValue(name), out result))
            {
                value = result;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the ConfigNode has the given value, and stores it in the ref value
        /// </summary>
        /// <param name="name">Name of the value to find</param>
        /// <param name="value">Value to store the result in</param>
        /*public static bool TryGetValue(this ConfigNode node, string name, ref Vector3 value)
        {
            if (node.HasValue(name) && RCUtils.TryParseVector3(node.GetValue(name), ref value)) { return true; }
            return false;
        }*/

        /// <summary>
        /// Sees if the ConfigNode has the given value and sets it, else it creates a new value for it.
        /// </summary>
        /// <param name="name">Name of the value to look for</param>
        /// <param name="value">Value to set</param>
        public static void SetAddValue(this ConfigNode node, string name, string value)
        {
            if (node.HasValue(name)) { node.SetValue(name, value); }
            else { node.AddValue(name, value); }
        }

        /// <summary>
        /// Returns an array of values that correspond to the values present in each node
        /// </summary>
        /// <param name="name">Value to search for</param>
        public static string[] GetValues(this ConfigNode[] nodes, string name)
        {
            return nodes.Where(node => node.HasValue(name)).Select(node => node.GetValue("name")).ToArray();
        }
        #endregion
    }
}
