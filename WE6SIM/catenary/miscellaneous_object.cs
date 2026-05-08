// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using Newtonsoft.Json;
using UnityEngine;

namespace WE6SIM.catenary;

internal partial class overhead_equipment
{
    [JsonObject]
    private class miscellaneous_object: catenary_object
    {
        [JsonProperty]
        public string template_name;

        public miscellaneous_object(string template_name, int x, int z, float y, Quaternion orientation)
            : base(template_name, x, z, y, orientation)
        {
            this.template_name = template_name;
        }
        public static Func<int, int, float, Quaternion, catenary_object> build_generic(string template_name)
        {
            return (int x, int z, float y, Quaternion orientation)
                => new miscellaneous_object(template_name, x, z, y, orientation);
        }
    }
}
