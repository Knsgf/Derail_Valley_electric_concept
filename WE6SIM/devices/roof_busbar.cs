// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace WE6SIM.devices;

internal class roof_busbar
{
    private float _pantograph_voltage, _sidepan_voltage, _inter_unit_cable_voltage;

    public float voltage         { get; private set; }
    public bool  short_circuited { get; private set; }
    public float pantograph_voltage
    {
        get => _pantograph_voltage;
        set
        {
            _pantograph_voltage = value;
            set_supply_voltage();
        }
    }
    public float sidepan_voltage
    {
        get => _sidepan_voltage;
        set
        {
            _sidepan_voltage = value;
            set_supply_voltage();
        }
    }
    public float inter_unit_cable_voltage
    {
        get => _inter_unit_cable_voltage;
        set
        {
            _inter_unit_cable_voltage = value;
            set_supply_voltage();
        }
    }

    private static bool made_short_circuit(float voltage1, float voltage2)
    {
        return voltage1 > 1.0f && voltage2 > 1.0f && Mathf.Abs(voltage1 - voltage2) > 50.0f;
    }
    
    private void set_supply_voltage()
    {
        short_circuited = made_short_circuit(_pantograph_voltage,          _sidepan_voltage)
                       || made_short_circuit(_pantograph_voltage, _inter_unit_cable_voltage)
                       || made_short_circuit(   _sidepan_voltage, _inter_unit_cable_voltage);
        if (!short_circuited)
            voltage = Mathf.Max(_pantograph_voltage, Mathf.Max(_sidepan_voltage, _inter_unit_cable_voltage));
        else
        {
            voltage = 0.0f;
        }
    }
}
