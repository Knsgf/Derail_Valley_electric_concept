// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json;
using UnityEngine;

using electric_sim.catenary_editor;
using electric_sim.utilities;

using static electric_sim.utilities.world_position;

namespace electric_sim.catenary;

#if DEBUG
internal partial class overhead_equipment
{
    private static readonly string? _local_repository_path = null;

    private bool _poles_sunk = false, _store_scenery = false;
    
    private _type_ add_scenery_object<_type_>(Func<int, int, float, Quaternion, _type_> constructor, 
        Vector3 relative_position, Quaternion orientation)
        where _type_: catenary_object
    {
        _store_scenery = true;
        (int x, int z) = get_absolute_position(relative_position);
        return add_scenery_object(constructor, x, z, relative_position.y, orientation);
    }

    public catenary_object_user add_miscellaneous_object(string template_name, Vector3 relative_position, Quaternion orientation)
    {
        return add_scenery_object(miscellaneous_object.build_generic(template_name), relative_position, orientation);
    }

    public pole_user add_pole(pole_kind pole_type, Vector3 relative_position, Quaternion orientation, 
        bool is_siding_anchor_pole = false)
    {
        if (pole_type == pole_kind.SideRail)
        {
            return add_scenery_object((int x, int z, float y, Quaternion orientation) => 
                new side_rail_pole(x, z, y, orientation), relative_position, orientation);
        }
        return add_scenery_object((int x, int z, float y, Quaternion orientation) => 
            new pole(pole_type, is_siding_anchor_pole, x, z, y, orientation), relative_position, orientation);
    }

    public void add_gantry(int tracks, Vector3 relative_position, Quaternion orientation)
    {
        add_scenery_object((int x, int z, float y, Quaternion orientation) => new gantry(tracks, x, z, y, orientation),
            relative_position, orientation);
    }

    public void add_cantilever(cantilever_kind cantilever_type, bool is_gantry_registration_arm, bool on_truss_gantry,
        bool is_tunnel_registration_arm, bool dual_wire, Vector3 relative_position, Quaternion orientation)
    {
        add_scenery_object(
            delegate (int x, int z, float y, Quaternion orientation) 
            { 
                steady_arm_kind steady_arm_type;
                if (is_gantry_registration_arm)
                    steady_arm_type = on_truss_gantry ? steady_arm_kind.truss_gantry : steady_arm_kind.gantry;
                else if (is_tunnel_registration_arm)
                    steady_arm_type = steady_arm_kind.tunnel;
                else
                    steady_arm_type = steady_arm_kind.cantilever;
                return new cantilever(cantilever_type, steady_arm_type, dual_wire, x, z, y, orientation);
            }, 
            relative_position, orientation);
    }

    public wire_user add_wire(wire_kind wire_type, string substation, float length, float previous_pole_vertical_offset,
        Vector3 relative_position, Quaternion orientation)
    {
        return add_scenery_object((int x, int z, float y, Quaternion orientation) 
            => new wire(wire_type, substation, length, previous_pole_vertical_offset, x, z, y, orientation),
            relative_position, orientation);
    }

    public void add_substation(string map_location, float supply_voltage, float maximum_load, bool has_inverter, Vector3 relative_position)
    {
        add_scenery_object((int x, int z, float y, Quaternion orientation)
            => new substation(map_location, supply_voltage, maximum_load, has_inverter, x, z, y, orientation),
            relative_position, Quaternion.AngleAxis(90.0f, Vector3.back));
    }

    public void erase_nearby_objects(Vector3 relative_position, float erase_reach)
    {
        int erase_region_half_width = float_to_fixed(erase_reach);
        (int x, int z) = get_absolute_position(relative_position);
        List<catenary_object> objects_to_remove = [];
        find_objects_within_region(objects_to_remove, _object_tree, do_bounds_check: true, 
            x - erase_region_half_width, z - erase_region_half_width, x + erase_region_half_width, z + erase_region_half_width);
        bool rebuild_tree = false;
        foreach (catenary_object current_object in objects_to_remove)
        {
            if (current_object is pole catenary_pole)
                catenary_pole.erased = true;
            if (current_object.entity is not null)
            {
                current_object.is_visible = false;
                current_object.hide_when_out_of_view();
            }
            rebuild_tree |= !_freshly_added_objects.Remove(current_object);
            _all_objects.Remove(current_object);
            _scenery_changed = _store_scenery = true;
        }
        if (rebuild_tree)
            reconstruct_tree();
    }

    public void get_objects_in_area(List<catenary_object_user> objects, Vector3 relative_position, float area_half_size)
    {
        (int x, int z) = get_absolute_position(relative_position);
        List<catenary_object> found_objects = [];
        int area_half_size_fixed = float_to_fixed(area_half_size);
        find_objects_within_region(found_objects, _object_tree, do_bounds_check: true,
            x - area_half_size_fixed, z - area_half_size_fixed, x + area_half_size_fixed, z + area_half_size_fixed);
        objects.AddRange(found_objects);
    }

    private void reconstruct_tree_after_moving_object(catenary_object entity)
    {
        _scenery_changed = _store_scenery = true;
        if (!_freshly_added_objects.Contains(entity))
        {
            int index;
            for (index = _all_objects.Count - 1; index >= 0; --index)
            {
                if (_all_objects[index] == entity)
                {
                    _all_objects.FastRemoveAt(index);
                    break;
                }
            }
            assert.test(index >= 0);
            reconstruct_tree();
            _all_objects.Add(entity);
            _freshly_added_objects.Add(entity);
        }
    }

    private void load_scenery_from_file()
    {
        try
        {
            string raw_scenery = File.ReadAllText(Path.Combine(_file_path, "scenery.json"));
            stuff_scenery(raw_scenery, no_saving: false);
        }
        catch (Exception error)
        {
            Main.log($"Exception occured when loading editable scenery file: {error}");
        }
    }

    public void store_scenery_now()
    {
        _store_scenery = true;
        store_scenery();
    }

    public void sink_tunnel_poles()
    {
        if (_poles_sunk)
            return;
        _poles_sunk = true;
        List<pole> all_poles =
        [..
            from   current_object in _all_objects
            where  !current_object.placed_procedurally
            let    current_pole = current_object as pole
            where  current_pole != null
            select current_pole
        ];
        List<(int start_index, int end_index)> tunnel_ranges = [];
        int tunnel_start = -1, tunnel_end = -1;
        Main.log($"TSNK {all_poles.Count}");
        for (int index = 0; index < all_poles.Count; ++index)
        {
            pole current_pole = all_poles[index];
            if (current_pole.pole_type == pole_kind.Tunnel && tunnel_start < 0)
                tunnel_start = index;
            else if (tunnel_start >= 0 && current_pole.pole_type != pole_kind.Tunnel)
                tunnel_end = index;
            if (tunnel_start >= 0 && tunnel_end > 0)
            {
                tunnel_ranges.Add((tunnel_start - 6, tunnel_end + 6));
                tunnel_start = tunnel_end = -1;
                Main.log($"TSNK {tunnel_ranges[tunnel_ranges.Count - 1]}");
            }
        }

        Main.log("TSNK");
        for (int range_index = tunnel_ranges.Count - 1; range_index > 0; --range_index)
        {
            (int range1_start, int range1_end) = tunnel_ranges[range_index - 1];
            (int range2_start, int range2_end) = tunnel_ranges[range_index    ];
            if (range2_start - range1_end < 10)
            {
                tunnel_ranges[range_index - 1] = (range1_start, range2_end);
                tunnel_ranges.RemoveAt(range_index);
            }
            else
                Main.log($"TSNK {tunnel_ranges[range_index]}");
        }
        Main.log($"TSNK {tunnel_ranges[0]}");

        foreach ((int range_start, int range_end) in tunnel_ranges)
        {
            int current_pole_index;
            float sink = 0.1f;
            for (current_pole_index = range_start; current_pole_index < range_start + 5; ++current_pole_index)
            {
                if (current_pole_index >= 0 && current_pole_index < all_poles.Count)
                    all_poles[current_pole_index].sink_pole(sink);
                sink += 0.1f;
            }
            for(; current_pole_index < range_end - 5; ++current_pole_index)
            {
                if (current_pole_index >= 0 && current_pole_index < all_poles.Count)
                    all_poles[current_pole_index].sink_pole(sink);
            }
            for (; current_pole_index < range_end; ++current_pole_index)
            {
                if (current_pole_index >= 0 && current_pole_index < all_poles.Count)
                    all_poles[current_pole_index].sink_pole(sink);
                sink -= 0.1f;
            }
        }
    }

    public void store_scenery()
    {
        editor.disable();
        if (_file_path != null)
        {
            List<catenary_object> objects_to_store;
            string                formatted_scenery;
            var                   write_types = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            if (_store_scenery)
            {
                objects_to_store =
                [..
                    from   current_object in _all_objects
                    where !current_object.placed_procedurally 
                        && !(current_object is gantry saving_gantry && saving_gantry._further_pole.erased)
                    select current_object
                ];
                formatted_scenery = JsonConvert.SerializeObject(objects_to_store, Formatting.Indented, write_types);
                File.WriteAllText(Path.Combine(_file_path, "scenery.json"), formatted_scenery);
                if (_local_repository_path != null)
                    File.WriteAllText(_local_repository_path, formatted_scenery);

                string compact_scenery = JsonConvert.SerializeObject(objects_to_store, Formatting.None, write_types);
                File.WriteAllText(Path.Combine(_file_path, "compacted_scenery.json"), compact_scenery);
            }

            if (PlayerManager.PlayerTransform != null)
            {
                Vector3 player_position = PlayerManager.PlayerTransform.position;
                (int player_x, int player_z) = get_absolute_position(player_position);
                var player_location = (catenary_object) add_miscellaneous_object("GantryArrow", PlayerManager.PlayerTransform.position, Quaternion.identity);
                player_location.placed_procedurally = true;
                objects_to_store =
                [..
                    from    current_object in _all_objects
                    let     x_offset = Math.Abs(current_object.x - player_x)
                    let     z_offset = Math.Abs(current_object.z - player_z)
                    where   x_offset <= 10 * fixed_divider && z_offset <= 10 * fixed_divider
                    orderby (long) x_offset * x_offset + (long) z_offset * z_offset
                    select  current_object
                ];
                formatted_scenery = JsonConvert.SerializeObject(objects_to_store, Formatting.Indented, write_types);
                File.WriteAllText(Path.Combine(_file_path, "nearby_objects.json"), formatted_scenery);
            }
            
            _store_scenery = false;
        }
    }
}
#endif