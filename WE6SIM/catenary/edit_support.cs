// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using WE6SIM.catenary_editor;
using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary;

internal static partial class catenary_visual
{
    private static void add_scenery_object<_type_>(Func<int, int, float, Quaternion, _type_> constructor, 
        Vector3 relative_position, Quaternion orientation)
        where _type_: catenary_object
    {
        (int x, int z) = get_absolute_position(relative_position);
        add_scenery_object(constructor, x, z, relative_position.y, orientation);
    }

    public static void add_pole(pole_kind pole_type, Vector3 relative_position, Quaternion orientation)
    {
		add_scenery_object((int x, int z, float y, Quaternion orientation) => new pole(pole_type, x, z, y, orientation),
            relative_position, orientation);
    }

    public static void add_gantry(int tracks, Vector3 relative_position, Quaternion orientation)
    {
        add_scenery_object((int x, int z, float y, Quaternion orientation) => new gantry(tracks, x, z, y, orientation),
            relative_position, orientation);
    }

    public static void erase_nearby_objects(Vector3 relative_position)
    {
        const int erase_region_half_width = (int) (2.5f * fixed_multiplier);

        (int x, int z) = get_absolute_position(relative_position);
        List<catenary_object> objects_to_remove = [];
        find_objects_within_region(objects_to_remove, object_tree, do_bounds_check: true, 
            x - erase_region_half_width, z - erase_region_half_width, x + erase_region_half_width, z + erase_region_half_width);
        foreach (catenary_object current_object in objects_to_remove)
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

    public static void get_objects_in_area(List<catenary_object_user> objects, Vector3 relative_position, float area_half_size)
    {
        (int x, int z) = get_absolute_position(relative_position);
        List<catenary_object> found_objects = [];
        int area_half_size_fixed = float_to_fixed(area_half_size);
        find_objects_within_region(found_objects, object_tree, do_bounds_check: true,
            x - area_half_size_fixed, z - area_half_size_fixed, x + area_half_size_fixed, z + area_half_size_fixed);
        objects.AddRange(found_objects);
    }

    public static void store_scenery()
    {
        if (_store_scenery && _file_path != null)
        {
            List<catenary_object> objects_to_store =
			[..
                from   current_object in _all_objects
                where !current_object.placed_procedurally
                select current_object
            ];
            string raw_scenery = JsonConvert.SerializeObject(objects_to_store, 
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
            File.WriteAllText(Path.Combine(_file_path, "scenery.json"), raw_scenery);
            _store_scenery = false;
        }
    }
}
