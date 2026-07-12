// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json;
using UnityEngine;

using DV.Utils;

using WE6SIM.catenary_editor;
using WE6SIM.utilities;

using static UnityModManagerNet.UnityModManager;
using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary;

internal partial class overhead_equipment
{
    const int scenery_tree_objects_per_node = 64, wires_tree_objects_per_node = 4;
    
    public enum pole_kind { Ground, Bridge, Tunnel, Bracket, SideRail };
    public enum cantilever_kind { Inner, OutwardsInner, MiddleInner, Middle, InwardsOuter, Outer };
    public enum wire_kind { plain_dual, plain_single, plain_quad, end_anchor_dual, end_anchor_single, end_anchor_quad, 
        wall_anchor_single, middle_anchor_dual, middle_anchor_single, middle_anchor_quad, side_rail, termination_rail };

    public const float default_pole_offset = 2.2f;

    private static readonly string[] _template_names =
    {
#if DEBUG
        "GantryArrow",
#endif
        "Gantry2Tracks",
        "Gantry3Tracks",
        "Gantry4Tracks",
        "RegistrationBracket",
        "RegistrationArmInnerDual",
        "RegistrationArmInnerSingle",
        "RegistrationArmInnerOutwardDual",
        "RegistrationArmInnerOutwardSingle",
        "RegistrationArmMiddleDual",
        "RegistrationArmMiddleSingle",
        "RegistrationArmMiddleInnerDual",
        "RegistrationArmMiddleInnerSingle",
        "RegistrationArmOuterDual",
        "RegistrationArmOuterSingle",

        "Pole",
        "PoleAnchor",
        "PoleFoundation",
        "InnerCantileverDual",
        "InnerCantileverSingle",
        "InnerOutwardCantileverDual",
        "InnerOutwardCantileverSingle",
        "MiddleCantileverDual",
        "MiddleCantileverSingle",
        "MiddleInwardCantileverDual",
        "MiddleInwardCantileverSingle",
        "OuterCantileverDual",
        "OuterCantileverSingle",
        "OuterInwardCantileverDual",
        "OuterInwardCantileverSingle",

        "SideRail",
        "SideRailEnd",
        "SideRailPole",

        "BridgePortal",
        "TunnelPole",
        "TunnelInnerDual",
        "TunnelInnerSingle",
        "TunnelOutwardsInnerDual",
        "TunnelOutwardsInnerSingle",
        "TunnelMiddleInnerDual",
        "TunnelMiddleInnerSingle",
        "TunnelMiddleDual",
        "TunnelMiddleSingle",
        "TunnelInwardsOuterDual",
        "TunnelInwardsOuterSingle",
        "TunnelOuterDual",
        "TunnelOuterSingle",

        "WireDual",
        "WireDualEnd",
        "WireDualFixedEnd",
        "WireMidpointAnchorDual",
        "WireSingle",
        "WireSingleEnd",
        "WireSingleFixedEnd",
        "WireMidpointAnchorSingle",
        "WireSingleWallEnd",
        "WireQuad",
        "WireQuadEnd",
        "WireQuadFixedEnd",
        "WireMidpointAnchorQuad",

        "Signs/DropPantographs",
        "Signs/DropPantographsOtherSide",
        "Signs/DropPantographsWarning",
        "Signs/DropPantographsWarningOtherSide",
        "Signs/RaisePantographs",
        "Signs/RaisePantographsOtherSide",
        "Signs/NeutralBegin",
        "Signs/NeutralBeginOtherSide",
        "Signs/NeutralEnd",
        "Signs/NeutralEndOtherSide",
        "Signs/NeutralEndNoRegen",
        "Signs/NeutralEndNoRegenOtherSide",
        "Signs/NeutralWarning",
        "Signs/NeutralWarningOtherSide",
    };
    private static overhead_equipment? _system;

    private int  _last_x, _last_z;
    //private float _remaining_time = 1.0f;
    private bool _scenery_changed = true, _store_scenery = false;

    private readonly Dictionary<string, GameObject> _templates = [];
    private readonly string _file_path;
    private readonly List<catenary_object> _all_objects = [], _nearby_wires = [];
#if DEBUG
    private readonly List<catenary_object> _freshly_added_objects = [];
#endif

    private GameObject? _OCS_ticker;

    private List<catenary_object> _previously_visible_objects = [], _currently_visible_objects = [];
    private quad_tree _object_tree = new([], scenery_tree_objects_per_node), _wires_tree = new([], wires_tree_objects_per_node);

    private substation[]? _all_substations = null;

    public static overhead_equipment system => _system ?? throw new InvalidOperationException("Catenary not present");
    
    private overhead_equipment(ModEntry mod)
    {
        _file_path = mod.Path;
        AssetBundle catenary_assets = AssetBundle.LoadFromFile(Path.Combine(_file_path, "catenary_parts"))
                ?? throw new FileNotFoundException("Not found " + Path.Combine(_file_path, "catenary_parts"));
        string[] all_assets = catenary_assets.GetAllAssetNames();
        foreach (string template_name in _template_names)
        {
            _templates[template_name] = catenary_assets.LoadAsset<GameObject>($"Assets/Catenary/{template_name}.prefab")
                ?? throw new FileNotFoundException($"No {template_name} prefab");
        }

        WorldMover floating_origin = SingletonBehaviour<WorldMover>.Instance;
        assert.test(floating_origin != null);
        floating_origin.WorldMoved    += floating_origin_shift;
        UnloadWatcher.UnloadRequested += dispose;
    }

    private void stuff_scenery(string raw_scenery, bool no_saving)
    {
        List<catenary_object>? loaded_objects = JsonConvert.DeserializeObject<List<catenary_object>>(raw_scenery, 
            new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
        if (loaded_objects != null)
        {
            //Main.log($"Added objects: {loaded_objects.Count}");
            if (no_saving)
            {
                foreach (catenary_object current_object in loaded_objects)
                    current_object.placed_procedurally = true;
            }
            _all_objects.AddRange(loaded_objects);
        }

        /*
        for (int index1 = _all_objects.Count - 1; index1 > 0; --index1)
        {
            for (int index2 = index1 - 1; index2 >= 0; --index2)
            {
                if (_all_objects[index1] is pole pole1 && _all_objects[index2] is pole pole2
                    && pole1.pole_type == pole_kind.Bracket && pole2.pole_type == pole_kind.Bracket
                    && Math.Abs(pole1.x - pole2.x) < fixed_divider / 10 && Math.Abs(pole1.z - pole2.z) < fixed_divider / 10)
                { 
                    _all_objects.RemoveAt(index1);
                    _store_scenery = true;
                    break;
                }
            }
        }
        */
        _scenery_changed = _all_objects.Count > 0;
    }

    private void load_scenery_from_bundle(AssetBundle scenery, string location_name)
    {
        string raw_scenery = scenery.LoadAsset<TextAsset>($"Assets/Catenary/Scenery/scenery_{location_name}.json").text
            ?? throw new FileNotFoundException($"No {location_name} location");
        stuff_scenery(raw_scenery, no_saving: true);
    }

    public static void set_up(ModEntry mod)
    {
        if (_system != null)
            throw new InvalidOperationException("Attempt to create a duplicate catenary in the world");
        _system = new overhead_equipment(mod);
        
        // "catenary_object" and its derivaties require "system" property to be initialised,
        // which precludes doing loading inside constructor
        AssetBundle catenary = AssetBundle.LoadFromFile(Path.Combine(system._file_path, "catenary"))
            ?? throw new FileNotFoundException("Not found " + Path.Combine(system._file_path, "catenary"));
        string[] all_locations =
        [
            // Yards
            "CME",
            "FM",
            "FF",
            "IME",
            "SM",

            // Mainlines
            "CME-IME",
            "CP-IMW[IMW]",
            "FF-IME[FF]",
            "FF-SM[FF]",
            "FM-SM[SM]",
            "IME-FF[CME-IME]",

            // Neutral sections
            "[FF]![CME-IME]"
        ];
        foreach (string location in all_locations)
            _system.load_scenery_from_bundle(catenary, location);
#if DEBUG        
        _system.load_scenery_from_file();   
#endif
        if (_system._scenery_changed)
            _system.reconstruct_tree();

        List<substation> all_substations =
        [..
            from   current_object in _system._all_objects
            let    current_substation = current_object as substation
            where  current_substation != null
            select current_substation
        ];
        substation neutral_connection = new("NEUTRAL", 0.0f, 100000.0f, false, 0, 0, 0.0f, Quaternion.identity);
        all_substations.Add(neutral_connection);
        int last_substation = all_substations.Count - 1;
        (all_substations[0], all_substations[last_substation]) = (all_substations[last_substation], all_substations[0]);
        _system._all_substations                   = new substation[last_substation + 1];
        Dictionary<string, int> substation_indices = [];
        for (int index = 0; index <= last_substation; ++index)
        {
            _system._all_substations[index] = all_substations[index];
            substation_indices[all_substations[index].map_location] = index;
        }

        List<wire> all_wires = 
        [..
            from   current_object in _system._all_objects
            let    current_wire = current_object as wire
            where  current_wire != null
            select current_wire
        ];
        foreach (wire current_wire in all_wires)
        {
            if (!substation_indices.TryGetValue(current_wire.substation, out int substation_index))
                throw new Exception($"Non-existent substation connection {current_wire.substation}");
            current_wire.substation_index = substation_index;
        }
        _system._wires_tree = new(
            [..
                from   current_wire in all_wires
                select current_wire
            ], 
            wires_tree_objects_per_node
        );

        _system._OCS_ticker = new GameObject("WE6SIM.catenary.overhead_equpment._player_tracker", typeof(OCS_ticker));
        var tracker = _system._OCS_ticker.GetComponent<OCS_ticker>();
        PlayerManager.PlayerTeleportStarted  += tracker.suspend_tracker;
        PlayerManager.PlayerTeleportFinished += tracker.resume_tracker;
        PlayerManager.PlayerChanged          += _system.restart_tracker;
    }

    private void restart_tracker()
    {
        _OCS_ticker?.GetComponent<OCS_ticker>().resume_tracker();
    }

    public static void dispose()
    {
#if DEBUG
        editor.disable();
#endif
        if (_system == null)
            return;
        if (_system._OCS_ticker is not null)
        {
            var tracker = _system._OCS_ticker.GetComponent<OCS_ticker>();
            PlayerManager.PlayerTeleportStarted  -= tracker.suspend_tracker;
            PlayerManager.PlayerTeleportFinished -= tracker.resume_tracker;
            PlayerManager.PlayerChanged          -= _system.restart_tracker;
            GameObject.Destroy(_system._OCS_ticker);
            _system._OCS_ticker = null;
        }
        WorldMover floating_origin     = SingletonBehaviour<WorldMover>.Instance;
        floating_origin?.WorldMoved   -= _system.floating_origin_shift;
        UnloadWatcher.UnloadRequested -= dispose;
        for (int index = _system._currently_visible_objects.Count - 1; index >= 0; --index)
        {
            catenary_object current_object = _system._currently_visible_objects[index];
            current_object.is_visible      = false;
            current_object.hide_when_out_of_view();
        }
        _system = null;
    }

    private void find_objects_within_region(List<catenary_object> found_objects, quad_tree all_objects,
        bool do_bounds_check, int left, int top, int right, int bottom, bool search_tree_only = false)
    {
        found_objects.Clear();
        all_objects.find_objects(found_objects, do_bounds_check, left, top, right, bottom);
    #if DEBUG
        if (!search_tree_only)
        {
            foreach (catenary_object current_object in _freshly_added_objects)
            {
                if (current_object.x >= left && current_object.x <= right && current_object.z >= top && current_object.z <= bottom)
                    found_objects.Add(current_object);
            }
        }
    #endif
    }

    public void handle_scenery_visibility(Vector3 relative_postion)
    {
        const float visible_distance       = 500.0f;
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

#if DEBUG        
        foreach (var current_object in _currently_visible_objects)
            assert.test(current_object.entity != null);
#endif
    }

    private void reconstruct_tree()
    {
        _object_tree = new quad_tree(_all_objects, scenery_tree_objects_per_node);
#if DEBUG
        _freshly_added_objects.Clear();
#endif
    }

    private void floating_origin_shift(WorldMover floating_origin, Vector3 shift)
    {
#if DEBUG
        if (_freshly_added_objects.Count >= _object_tree.node_objects_limit)
            reconstruct_tree();
#endif
        
        List<catenary_object> visible_objects = _currently_visible_objects;
        for (int index = visible_objects.Count - 1; index >= 0; --index)
            visible_objects[index].entity!.transform.position -= shift;
    }

    private _type_ add_scenery_object<_type_>(Func<int, int, float, Quaternion, _type_> constructor, 
        int x, int z, float y, Quaternion orientation)
        where _type_: catenary_object
    {
        _type_ new_object = constructor(x, z, y, orientation);
        _all_objects.Add(new_object);
#if DEBUG
        _freshly_added_objects.Add(new_object);
#endif
        //Main.log($"x={new_object.x / fixed_multiplier} z={new_object.z / fixed_multiplier} y={new_object.y} c={_all_objects.Count}");
        _scenery_changed = true;
        return new_object;
    }

    private void simulate_all_substations_load()
    {
        foreach (substation current_substation in _all_substations!)
            current_substation.simulate_load();
    }

    public (float? contact_height, float contact_voltage) wire_height_and_voltage(int strip_end1_x, int strip_end1_z, int strip_end2_x, int strip_end2_z, 
        float pantograph_base_y, float load_current)
    {
        const int wire_search_half_area = (int) (60.0f * fixed_multiplier);
        
        int strip_centre_x = (strip_end1_x + strip_end2_x) >> 1;
        int strip_centre_z = (strip_end1_z + strip_end2_z) >> 1;
        _nearby_wires.Clear();
        find_objects_within_region(_nearby_wires, _wires_tree, do_bounds_check: true, 
            strip_centre_x - wire_search_half_area, strip_centre_z - wire_search_half_area, 
            strip_centre_x + wire_search_half_area, strip_centre_z + wire_search_half_area, search_tree_only: true);
        float lowest_height = float.MaxValue, lowest_energised_height = float.MaxValue;
        //Main.log($"{_nearby_wires.Count} wires");
        wire? wire_in_contact = null, energised_wire = null;
        foreach (catenary_object current_object in _nearby_wires)
        {
            var    current_wire   = (wire) current_object;
            float? contact_height = current_wire.contact_height(strip_end1_x, strip_end1_z, strip_end2_x, strip_end2_z, pantograph_base_y);
            if (contact_height != null)
            {
                float wire_height = (float) contact_height;
                if (lowest_height > wire_height)
                {
                    lowest_height   = wire_height;
                    wire_in_contact = current_wire;
                }
                if (current_wire.substation_index > 0 && lowest_energised_height > wire_height)
                {
                    lowest_energised_height = wire_height;
                    energised_wire          = current_wire;
                }
            }
        }

        if (wire_in_contact == null)
            return (null, 0.0f);
        if (energised_wire == null || lowest_energised_height - lowest_height >= 0.2f)
            return (lowest_height, 0.0f);
        substation supplying_substation = _all_substations![energised_wire.substation_index];
        return (lowest_height, supplying_substation.wire_voltage(energised_wire.x, energised_wire.z, 
            energised_wire.y, load_current, energised_wire.length_1m_resistance));
    }
}
