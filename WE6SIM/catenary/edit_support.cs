// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json;
using UnityEngine;

using WE6SIM.catenary_editor;
using WE6SIM.utilities;

using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary;

internal partial class overhead_equipment
{
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

    public void add_cantilever(cantilever_kind cantilever_type, bool is_gantry_registration_arm, bool is_tunnel_registration_arm,
        bool dual_wire, Vector3 relative_position, Quaternion orientation)
    {
        add_scenery_object((int x, int z, float y, Quaternion orientation) 
            => new cantilever(cantilever_type, is_gantry_registration_arm, is_tunnel_registration_arm, dual_wire, x, z, y, orientation),
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

    public void store_scenery_now()
    {
        _store_scenery = true;
        store_scenery();
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
                File.WriteAllText(@"C:\Users\Kf177\source\repos\we6\WE6SIM\catenary\scenery.json", formatted_scenery);

                string compact_scenery = JsonConvert.SerializeObject(objects_to_store, Formatting.None, write_types);
                File.WriteAllText(Path.Combine(_file_path, "compacted_scenery.json"), compact_scenery);
            }

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
            
            _store_scenery = false;
        }
    }
}
