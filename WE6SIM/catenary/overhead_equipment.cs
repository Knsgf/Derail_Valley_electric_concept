// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using DV.Utils;
using WE6SIM.utilities;

using static UnityModManagerNet.UnityModManager;
using static WE6SIM.utilities.world_position;
using WE6SIM.catenary_editor;

namespace WE6SIM.catenary;

internal partial class overhead_equipment
{
    public enum pole_kind { Ground, Bridge, Tunnel, Bracket };
    public enum cantilever_kind { Inner, Middle, Outer };

    private static readonly string[] _template_names =
    {
        "EndPoleAnchor",
        "Gantry2Tracks",
        "Gantry3Tracks",
        "Gantry4Tracks",
        "InnerCantileverDual",
        "InnerCantileverSingle",
        "MiddleCantileverDual",
        "MiddleCantileverSingle",
        "OuterCantileverDual",
        "OuterCantileverSingle",
        "Pole",
        "PoleFoundation",
        "RegistrationBracket",
        "RegistrationArmInnerDual",
        "RegistrationArmInnerSingle",
        "RegistrationArmMiddleDual",
        "RegistrationArmMiddleSingle",
        "RegistrationArmOuterDual",
        "RegistrationArmOuterSingle",
        "WireDual",
        "WireDualEnd",
        "WireSingle",
        "WireSingleEnd"
    };
    private static overhead_equipment? _system;

    private int  _last_x, _last_z;
    //private float _remaining_time = 1.0f;
    private bool _scenery_changed = true, _store_scenery = false;

    private readonly Dictionary<string, GameObject> _templates = [];
    private readonly string _file_path;
    private readonly List<catenary_object> _all_objects = [], _freshly_added_objects = [];
    private List<catenary_object> _previously_visible_objects = [], _currently_visible_objects = [];
    private quad_tree _object_tree = new([]);

    public static overhead_equipment system => _system ?? throw new InvalidOperationException("Catenary not present");

    private overhead_equipment(ModEntry mod)
    {
        _file_path = mod.Path;
        AssetBundle catenary_assets = AssetBundle.LoadFromFile(Path.Combine(_file_path, "catenary"))
                ?? throw new FileNotFoundException("Not found " + Path.Combine(_file_path, "catenary"));
        string[] all_assets = catenary_assets.GetAllAssetNames();
        foreach (string name in all_assets)
            Main.log(name);
        foreach (string template_name in _template_names)
        {
            _templates[template_name] = catenary_assets.LoadAsset<GameObject>($"Assets/Catenary/{template_name}.prefab")
                ?? throw new FileNotFoundException($"No {template_name} prefab");
        }

        WorldMover floating_origin = SingletonBehaviour<WorldMover>.Instance;
        assert.test(floating_origin != null);
        floating_origin.WorldMoved           += floating_origin_shift;
        UnloadWatcher.UnloadRequested        += dispose;
        PlayerManager.PlayerTeleportFinished += track_player_movement;
    }

    private void load_scenery(ModEntry mod)
    {
        List<catenary_object>? loaded_objects = null;
        try
        {
            string raw_scenery = File.ReadAllText(Path.Combine(_file_path, "scenery.json"));
            loaded_objects = JsonConvert.DeserializeObject<List<catenary_object>>(raw_scenery, 
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
        }
        catch (Exception error)
        {
            Main.log($"Exception occured when loading scenery: {error}");
        }
        if (loaded_objects != null)
        {
            Main.log($"Loaded objects: {loaded_objects.Count}");
            _all_objects.AddRange(loaded_objects);
        }
        _scenery_changed = _all_objects.Count > 0;
        if (_scenery_changed)
            _object_tree = new quad_tree(_all_objects);
    }

    public static void set_up(ModEntry mod)
    {
        Main.log("catenary_visual.set_up()");
        if (_system != null)
            throw new InvalidOperationException("Attempt to create a duplicate catenary in the world");
        _system = new overhead_equipment(mod);
        _system.load_scenery(mod);
    }

    public static void dispose()
    {
        Main.log("catenary_visual.dispose()");
        if (_system == null)
            return;
        WorldMover floating_origin            = SingletonBehaviour<WorldMover>.Instance;
        floating_origin?.WorldMoved          -= _system.floating_origin_shift;
        UnloadWatcher.UnloadRequested        -= dispose;
        PlayerManager.PlayerTeleportFinished -= _system.track_player_movement;
        for (int index = _system._currently_visible_objects.Count - 1; index >= 0; --index)
        {
            catenary_object current_object = _system._currently_visible_objects[index];
            GameObject.Destroy(current_object.entity);
            current_object.entity = null;
        }
        _system = null;
    }

    private void find_objects_within_region(List<catenary_object> found_objects, quad_tree all_objects, 
        bool do_bounds_check, int left, int top, int right, int bottom)
    {
        found_objects.Clear();
        all_objects.find_objects(found_objects, do_bounds_check, left, top, right, bottom);
        foreach (catenary_object current_object in _freshly_added_objects)
        {
            if (current_object.x >= left && current_object.x <= right && current_object.z >= top && current_object.z <= bottom)
                found_objects.Add(current_object);
        }
    }

    public void handle_scenery_visibility(Vector3 relative_postion)
    {
        const float visible_distance       = 100.0f;
        const int   visible_distance_fixed = (int) (visible_distance * fixed_multiplier);

        /*
        _remaining_time -= Time.deltaTime;
        if (_remaining_time > 0.0f)
            return;
        _remaining_time = 1.0f;
        */

        (int x, int z) = get_absolute_position(relative_postion);
        if (!_scenery_changed && Math.Abs(x - _last_x) + Math.Abs(z - _last_z) <= visible_distance_fixed >> 3)
            return;
        _last_x = x;
        _last_z = z;
        _scenery_changed = false;

        (_previously_visible_objects, _currently_visible_objects) = (_currently_visible_objects, _previously_visible_objects);
        List<catenary_object> visible_objects = _previously_visible_objects;
        for (int object_index = visible_objects.Count - 1; object_index >= 0; --object_index)
            visible_objects[object_index].is_visible = false;
        visible_objects = _currently_visible_objects;
        find_objects_within_region(visible_objects, _object_tree, do_bounds_check: false,
            x - visible_distance_fixed, z - visible_distance_fixed,x + visible_distance_fixed, z + visible_distance_fixed);
        for (int object_index = visible_objects.Count - 1; object_index >= 0; --object_index)
            visible_objects[object_index].reveal();

        visible_objects = _previously_visible_objects;
        for (int object_index = visible_objects.Count - 1; object_index >= 0; --object_index)
            visible_objects[object_index].hide_when_out_of_view();
    }

    private void reconstruct_tree()
    {
        _object_tree = new quad_tree(_all_objects);
        _freshly_added_objects.Clear();
    }

    private void floating_origin_shift(WorldMover floating_origin, Vector3 shift)
    {
        if (_freshly_added_objects.Count >= quad_tree.node_objects_limit)
            reconstruct_tree();
        
        List<catenary_object> visible_objects = _currently_visible_objects;
        for (int index = visible_objects.Count - 1; index >= 0; --index)
            visible_objects[index].entity!.transform.position -= shift;
    }

    private void track_player_movement()
    {
        (int x, int z) = get_absolute_position(PlayerManager.PlayerTransform.position);
        Main.log($"x={x} z={z}");
        handle_scenery_visibility(PlayerManager.PlayerTransform.position);
    }


    private _type_ add_scenery_object<_type_>(Func<int, int, float, Quaternion, _type_> constructor, 
        int x, int z, float y, Quaternion orientation)
        where _type_: catenary_object
    {
        _type_ new_object = constructor(x, z, y, orientation);
        _all_objects.Add(new_object);
        _freshly_added_objects.Add(new_object);
        Main.log($"x={new_object.x / fixed_multiplier} z={new_object.z / fixed_multiplier} y={new_object.y} c={_all_objects.Count}");
        _scenery_changed = _store_scenery = true;
        return new_object;
    }

}
