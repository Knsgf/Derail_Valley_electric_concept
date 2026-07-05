// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Threading;
using System.Threading.Tasks;

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
        private static double _energy_consumed = 0.0, _energy_returned = 0.0;
        
        [JsonIgnore]
        private float _current_voltage, _current_load = 0.0f, _new_load;
        [JsonIgnore]
        private bool _shutdown = false;
        [JsonIgnore]
        private CancellationTokenSource? _restoration_sequence = null;
        
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
            this.supply_voltage = _current_voltage = supply_voltage;
            this.maximum_load   = maximum_load;
            this.has_inverter   = has_inverter;
        }

        public float wire_voltage(int wire_x, int wire_z, float wire_y, float load_current, float wire_1m_resistance)
        {
            int   x_offset = x - wire_x, z_offset = z - wire_z;
            float y_offset = y - wire_y;
            float distance = Mathf.Sqrt(((long) x_offset * x_offset + (long) z_offset + z_offset) 
                / (world_position.fixed_multiplier * world_position.fixed_multiplier) + y_offset * y_offset);
            _new_load += load_current;
            return _current_voltage - _current_load * wire_1m_resistance * distance;
        }

        private async void shutdown()
        {
            if (_shutdown)
                return;
            _shutdown        = true;
            _current_voltage = 0.0f;
            if (_restoration_sequence == null)
            {
                Main.log("10 s shutdown");
                await Task.Delay(10_000);
            }
            else
            {
                Main.log("30 s shutdown");
                _restoration_sequence.Cancel();
                await Task.Delay(30_000);
            }
            Main.background_log("Power restoration started");
            _current_voltage      = supply_voltage;
            _restoration_sequence = new();
            _shutdown             = false;
            
            try
            {
                await Task.Delay(5 * 60_000, _restoration_sequence.Token);
            }
            catch (TaskCanceledException _)
            {
                Main.background_log("Power restoration terminated");
                return;
            }

            Main.background_log("Power restoration complete");
            _restoration_sequence = null;
        }

        public void simulate_load()
        {
            float current_load = _current_load = _new_load;
            _new_load          = 0.0f;
            float voltage      = supply_voltage;
            if (current_load > maximum_load && UnityEngine.Random.value <= (current_load / maximum_load - 1) * (2.0f / Time.deltaTime))
                shutdown();
            else if (current_load < -100.0f)
            {
                if (has_inverter)
                    voltage += current_load / 3000.0f * 350.0f;
            }
            if (!_shutdown)
                _current_voltage = voltage * 0.01f + _current_voltage * 0.99f;
            if (current_load >= 0.0f)
                _energy_consumed += (1.0 / (1000.0 * 3600.0)) * _current_voltage * current_load * Time.deltaTime;
            else
                _energy_returned += (1.0 / (1000.0 * 3600.0)) * _current_voltage * (-current_load) * Time.deltaTime;
            Main.diagnostics?.Value = (float) _energy_consumed;
            Main.diagnostics2?.Value = (float) _energy_returned;
            /*
            if (string.Equals(map_location, "SM1500", System.StringComparison.OrdinalIgnoreCase))
            {
                Main.diagnostics?.Value = _current_load;
                //Main.diagnostics2?.Value = _current_voltage;
            }
            */
        }
        
        public override void reveal()
        {
#if DEBUG
            base.reveal();
#endif
        }
    }
}
