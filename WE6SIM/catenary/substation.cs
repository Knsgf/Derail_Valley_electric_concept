// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using Newtonsoft.Json;
using UnityEngine;

using WE6SIM.utilities;

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

        public float wire_voltage(int wire_x, int wire_z, float wire_y, float load_current, float wire_1m_resistance)
        {
            int   x_offset = x - wire_x, z_offset = z - wire_z;
            float y_offset = y - wire_y;
            float distance = Mathf.Sqrt(((long) x_offset * x_offset + (long) z_offset + z_offset) 
                / (world_position.fixed_multiplier * world_position.fixed_multiplier) + y_offset * y_offset);
            float voltage = supply_voltage;
            if (load_current < -100.0f)
            {
                if (!has_inverter)
                    voltage -= load_current * 10.0f;
            }
            Main.diagnostics?.Value = distance;
            Main.diagnostics2?.Value = supply_voltage - maximum_load * wire_1m_resistance * distance;
            return voltage - load_current * wire_1m_resistance * distance;
        }
        
        public override void reveal()
        {
#if DEBUG
            base.reveal();
#endif
        }
    }
}
