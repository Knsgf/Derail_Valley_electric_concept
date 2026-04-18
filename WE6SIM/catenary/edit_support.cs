// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary;

internal static partial class catenary_visual
{
    public static void add_scenery_object(int item_index, Vector3 relative_position, Quaternion orientation)
    {
        (int x, int z) = get_absolute_position(relative_position);
        scenery_object new_object = new(item_index, x, z, relative_position.y, orientation);
        _all_objects.Add(new_object);
        _freshly_added_objects.Add(new_object);
        Main.log($"x={x / fixed_multiplier} z={z / fixed_multiplier} y={relative_position.y} c={_all_objects.Count}");
        _scenery_changed = _store_scenery = true;
    }

    public static void erase_nearby_objects(Vector3 relative_position)
    {
        const int erase_region_half_width = (int) (2.5f * fixed_multiplier);

        (int x, int z) = get_absolute_position(relative_position);
        List<scenery_object> objects_to_remove = [];
        find_objects_within_region(objects_to_remove, object_tree, do_bounds_check: true, 
            x - erase_region_half_width, z - erase_region_half_width, x + erase_region_half_width, z + erase_region_half_width);
        foreach (scenery_object current_object in objects_to_remove)
        {
            if (current_object.entity is not null)
            {
                GameObject.Destroy(current_object.entity);
                current_object.entity = null;
            }
            _freshly_added_objects.Remove(current_object);
            _all_objects.Remove(current_object);
        }
        if (objects_to_remove.Count > 0)
        {
            object_tree = new quad_tree(_all_objects);
            _store_scenery = true;
        }
    }

    public static void get_objects_of_type(List<GameObject> objects, int type, Vector3 relative_position, int area_half_size)
    {
        (int x, int z) = get_absolute_position(relative_position);
        List<scenery_object> found_objects = [];
        find_objects_within_region(found_objects, object_tree, do_bounds_check: true,
            x - area_half_size, z - area_half_size, x + area_half_size, z + area_half_size);
        foreach (scenery_object current_object in found_objects)
        {
            if (current_object.template_index == type && current_object.entity is not null)
                objects.Add(current_object.entity);
        }
    }

    public static void store_scenery()
    {
        if (_store_scenery && file_path != null)
        {
            string raw_scenery = JsonConvert.SerializeObject(_all_objects);
            File.WriteAllText(Path.Combine(file_path, "scenery.json"), raw_scenery);
            _store_scenery = false;
        }
    }
}
