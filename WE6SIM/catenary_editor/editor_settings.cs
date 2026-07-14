// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.IO;

using Newtonsoft.Json;

using UnityModManagerNet;

using electric_sim.catenary;
using electric_sim.devices;

using static UnityModManagerNet.UnityModManager;

namespace electric_sim.catenary_editor;

public struct stored_settings
{
    [JsonProperty]
    public float load_limit_factor, voltage_drop_factor, kWh_price;
}

public class editor_settings: ModSettings, IDrawable
{
    const float default_kWh_price = 10.0f;
    
    [Draw("Substation load limit multiplier (requires reload)")]
    private float _load_limit_factor = 1.0f;
    [Draw("Voltage drop multiplier (requires reload)")]
    private float _voltage_drop_factor = 1.0f;
    [Draw("Electricity price, $/kWh (requires reload)")]
    private float _kWh_price = default_kWh_price;

    public static editor_settings? instance { get; private set; }
    public static float load_limit_factor   { get; private set; } = 1.0f;
    public static float voltage_drop_factor { get; private set; } = 1.0f;
    public static float kWh_price           { get; private set; } = default_kWh_price;

#if DEBUG
    private bool _side_rail_placement_enabled = false;
    
    [Draw("Infinite power cheat")]
    private bool _infinite_power = false;

    [Draw("Part placement")]
    private editor.placement _part_placement = editor.placement.Disabled;

    [Draw("Pole type")]
    private overhead_equipment.pole_kind _pole_type = overhead_equipment.pole_kind.Ground;

    [Draw("Skip first pole")]
    private bool _skip_first = false;

    [Draw("Pole horizontal offset (mm)")]
    private int _horizontal_offset = 0;

    [Draw("Pole vertical offset (mm)")]
    private int _height_offset = 0;

    [Draw("Distance (m)")]
    private int _pole_distance = 40;

    [Draw("Sweep (mm)")]
    private int _sweep = 500;

    [Draw("Gantry stretch %")]
    private int _gantry_stretch = 100;

    [Draw("Registration arm type")]
    private overhead_equipment.cantilever_kind _cantilever_type = overhead_equipment.cantilever_kind.Middle;

    [Draw("Zigzag")]
    private bool _zigzag_arms = false;
    
    [Draw("Dual arm")]
    private bool _dual_arm = false;
    
    [Draw("Automatic termination")]
    private bool _stop_cantilever_placement_after_distance = false;

    [Draw("Maximum distance (m)")]
    private int _stop_cantilever_placement_distance = 1600;

    [Draw("Suspend distance measurement")]
    private bool _suspend_cantilever_distance_measurement = false;

    [Draw("Suspend wire placement")]
    private bool _suspend_wire_placement = false;
    
    [Draw("Terminate wire at next pole")]
    private bool _terminate_wire_placement = false;

    [Draw("Substation")]
    private string _substation = "";

    [Draw("Eraser")]
    private bool _erase_scenery = false;

    [Draw("Eraser reach (mm)")]
    private int _eraser_radius = 2500;
#endif

    public editor_settings()
    {
        OnChange();
    }
    
    public void OnChange()
    {
#if DEBUG
        if (_erase_scenery && _part_placement != editor.placement.Disabled)
        {
            _erase_scenery  = false;
            _part_placement = editor.placement.Disabled;
        }
        if (_part_placement is editor.placement.Bracket or editor.placement.FlippedBracket)
            _pole_type = overhead_equipment.pole_kind.Bracket;
        else if (_pole_type == overhead_equipment.pole_kind.Bracket)
            _pole_type = overhead_equipment.pole_kind.Ground;
        if (!_side_rail_placement_enabled && _pole_type == overhead_equipment.pole_kind.SideRail)
        {
            _side_rail_placement_enabled = true;
            _pole_distance               = 10;
        }
        else if (_side_rail_placement_enabled && _pole_type != overhead_equipment.pole_kind.SideRail)
        {
            _side_rail_placement_enabled = false;
            _pole_distance               = 40;
        }

        editor.pole_height_offset				= _height_offset     / 1000.0f;
        editor.pole_horizontal_offset			= _horizontal_offset / 1000.0f;
        editor.pole_type						= _pole_type;
        editor.part_placement					= _part_placement;
        editor.cantilever_type					= _cantilever_type;
        editor.skip_first						= _skip_first;
        editor.distance_between_poles			= _pole_distance;
        editor.maximum_sweep					= _sweep / 1000.0f;
        editor.erase_scenery					= _erase_scenery;
        editor.eraser_area_half                 = _eraser_radius / 1000.0f;
        editor.gantry_stretch					= _gantry_stretch / 100.0f;
        editor.zigzag							= _zigzag_arms;
        editor.dual_wire						= _dual_arm;
        editor.automatic_cantilever_termination = _stop_cantilever_placement_after_distance;
        editor.cantilever_termination_distance  = _stop_cantilever_placement_distance;
        editor.suspend_cantilever_distance      = _suspend_cantilever_distance_measurement;
        editor.suspend_wire_placement			= _suspend_wire_placement;
        editor.terminate_wire_at_next_pole      = _terminate_wire_placement;
        editor.designated_substation            = (_substation.Length >= 2) ? _substation : null;

        pantograph.infinite_power = _infinite_power;
#endif
    }

    public override void Save(ModEntry mod)
    {
        string raw_settings = JsonConvert.SerializeObject(
            new stored_settings 
            { 
                load_limit_factor   = _load_limit_factor,
                voltage_drop_factor = _voltage_drop_factor,
                kWh_price           = _kWh_price
            }, Formatting.Indented
        );
        File.WriteAllText(Path.Combine(mod.Path, "settings.json"), raw_settings);
    }

#if DEBUG
    internal void update_cantilever_type(overhead_equipment.cantilever_kind cantilever_type)
    {
        _cantilever_type = cantilever_type;
    }

    internal void reset_placement_mode()
    {
        _part_placement           = editor.placement.Disabled;
        _terminate_wire_placement = _suspend_wire_placement = false;
        _substation               = "";
    }
#endif
    
    public static void set_up(ModEntry mod)
    {
        if (instance == null)
        {
            stored_settings current_settings;
            try
            {
                string raw_settings = File.ReadAllText(Path.Combine(mod.Path, "settings.json"));
                current_settings    = JsonConvert.DeserializeObject<stored_settings>(raw_settings);
            }
            catch (Exception _)
            {
                current_settings = new stored_settings { load_limit_factor = 1.0f, voltage_drop_factor = 1.0f, kWh_price = default_kWh_price };
            }
			instance = new()
			{
				_load_limit_factor   = (current_settings.load_limit_factor   > 0.0f) ? current_settings.load_limit_factor   : 1.0f,
				_voltage_drop_factor = (current_settings.voltage_drop_factor > 0.0f) ? current_settings.voltage_drop_factor : 1.0f,
                _kWh_price           = (current_settings.kWh_price           > 0.0f) ? current_settings.kWh_price           : default_kWh_price
			};
            load_limit_factor   = instance._load_limit_factor;
            voltage_drop_factor = instance._voltage_drop_factor;
            kWh_price           = instance._kWh_price;
			mod.OnGUI           = instance.Draw;
            mod.OnSaveGUI       = instance.Save;
        }
    }
}
