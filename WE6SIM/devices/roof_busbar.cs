// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Collections.Generic;

using LocoSim.Implementations;

using UnityEngine;

using WE6SIM.utilities;

namespace WE6SIM.devices;

internal class roof_busbar: electric_device
{
    private readonly Port _inter_unit_cable_supplier, _inter_unit_cable_receiver;

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
    
    public roof_busbar(Dictionary<string, Port> ports, bool is_unit_A): base("Roof busbar")
    {
        if (is_unit_A)
        {
            _inter_unit_cable_supplier = sensor_grabber.grab_port(ports, "[internal_MU].SUPPLY_TO_B");
            _inter_unit_cable_receiver = sensor_grabber.grab_port(ports, "[internal_MU].SUPPLY_FROM_B");
        }
        else
        {
            _inter_unit_cable_supplier = sensor_grabber.grab_port(ports, "[internal_MU].SUPPLY_TO_A");
            _inter_unit_cable_receiver = sensor_grabber.grab_port(ports, "[internal_MU].SUPPLY_FROM_A");
        }
        _inter_unit_cable_receiver.ValueUpdatedInternally += voltage_from_other_unit_changed;
    }

    private void voltage_from_other_unit_changed(float voltage)
    {
        _inter_unit_cable_voltage = voltage;
        set_supply_voltage();
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
        float pantographs_voltage;
        if (!short_circuited)
        {
            pantographs_voltage = Mathf.Max(_pantograph_voltage,          _sidepan_voltage);
            voltage             = Mathf.Max(pantographs_voltage, _inter_unit_cable_voltage);
        }
        else
        {
            pantographs_voltage = voltage = 0.0f;
        }
        _inter_unit_cable_supplier.Value = pantographs_voltage;
    }

    public override void Dispose()
    {
        base.Dispose();
        _inter_unit_cable_receiver.ValueUpdatedInternally -= voltage_from_other_unit_changed;
    }
}
