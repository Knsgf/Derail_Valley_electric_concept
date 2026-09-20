// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using Newtonsoft.Json;
using UnityEngine;

using electric_sim.utilities;

namespace electric_sim.catenary;

public partial class overhead_equipment
{
    private struct miscellaneous_object_definition: catenary_object_definition
    {
        public readonly string template_name => template;
        public readonly string asset_path    => asset;
        
        [CSV_column(0)]
        public string template;
        [CSV_column(1)]
        public string asset;
    }
    
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
