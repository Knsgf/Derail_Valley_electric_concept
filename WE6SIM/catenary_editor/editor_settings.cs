// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using UnityModManagerNet;

using WE6SIM.catenary;

namespace WE6SIM.catenary_editor;

internal class editor_settings: UnityModManager.ModSettings, IDrawable
{
    private bool _side_rail_placement_enabled = false;
    
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
    private int _sweep = 300;

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

    public editor_settings()
    {
        Main.log("Settings set");
        OnChange();
    }
    
    public void OnChange()
    {
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
        editor.designated_substation                       = (_substation.Length >= 2) ? _substation : null;
    }

    public void update_cantilever_type(overhead_equipment.cantilever_kind cantilever_type)
    {
        _cantilever_type = cantilever_type;
    }

    public void reset_placement_mode()
    {
        _part_placement           = editor.placement.Disabled;
        _terminate_wire_placement = _suspend_wire_placement = false;
        _substation               = "";
    }
}
