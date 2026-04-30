using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using UnityEngine;

namespace WE6SIM.catenary;

interface power_supply: catenary_object_user
{ }

internal partial class overhead_equipment
{
    [JsonObject]
    private class substation: catenary_object, power_supply
    {
        [JsonProperty]
        public string map_location;
        [JsonProperty]
        public float  supply_voltage;
        [JsonProperty]
        public float  maximum_load;
        [JsonProperty]
        public bool   has_inverter;
        
        public substation(string map_location, float supply_voltage, float maximum_load, bool has_inverter, 
            int x, int z, float y, Quaternion orientation): base("GantryArrow", x, z, y, orientation)
        { 
            this.map_location   = map_location;
            this.supply_voltage = supply_voltage;
            this.maximum_load   = maximum_load;
            this.has_inverter   = has_inverter;
        }

		public override void reveal()
		{
#if DEBUG
			base.reveal();
#endif
		}
    }
}
