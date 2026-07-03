// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using LocoSim.Implementations;

using UnityEngine;

using WE6SIM.catenary;
using WE6SIM.utilities;

namespace WE6SIM.devices;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class pantograph_part(string part_name): Attribute
{
	public string name { get; private set; } = part_name;
}

internal class pantograph: electric_device
{
    const string pantograph_tag = "PantographBase", sidepan_tag = "SidepanBase";

    const float maximum_head_height = 6.6f, frame_thickness = 0.085f, head_movement_speed = 0.5f;
    const float sidepan_relative_movement_speed = 0.5f;
    const int   max_iterations_before_sleep = 6;
    const float powertrain_points = 2000.0f, nominal_current = 2200.0f;
    const float wear_per_metre = 5.0E-6f * powertrain_points, wear_per_second_at_full_current = 9.0E-5f * powertrain_points;

    private static readonly Dictionary<string, FieldInfo> _pantograph_parts = [];
    private static readonly Quaternion _sidepan_pivot_deployed_orientation = Quaternion.AngleAxis(90.0f  , Vector3.up   );
    private static readonly Quaternion   _sidepan_arm_deployed_orientation = Quaternion.AngleAxis(15.456f, Vector3.right);

    private readonly roof_busbar _roof_bus;

    [pantograph_part(pantograph_tag)]
    private Transform _base; 
    [pantograph_part("LowerFrontFrame")]
    private Transform _front_lower_frame; 
    [pantograph_part("UpperFrontFrame")]
    private Transform _front_upper_frame; 
    [pantograph_part("LowerBackFrame")]
    private Transform _back_lower_frame; 
    [pantograph_part("UpperBackFrame")]
    private Transform _back_upper_frame; 
    [pantograph_part("Head")]
    private Transform _head;

    [pantograph_part("FrontPiston")]
    private Transform _front_piston; 
    [pantograph_part("BackPiston")]
    private Transform  _back_piston;

    [pantograph_part("RollersOffset")]
    private Transform _head_rollers; 
    [pantograph_part("UpperFrameTopPivot")]
    private Transform _top_pivot; 
    [pantograph_part("LeverTip")]
    private Transform _piston_lever_tip;
    
    [pantograph_part("StripEnd1")]
    private Transform _contact_strip_end1; 
    [pantograph_part("StripEnd2")]
    private Transform _contact_strip_end2; 

    [pantograph_part(sidepan_tag)]
    private Transform _sidepan_base; 
    [pantograph_part("SidepanPivot")]
    private Transform _sidepan_pivot; 
    [pantograph_part("SidepanArm")]
    private Transform _sidepan_arm; 
    [pantograph_part("SidepanInner")]
    private Transform _sidepan_inner_contact; 
    [pantograph_part("SidepanOuter")]
    private Transform _sidepan_outer_contact;

    private readonly Port _arcing_damage, _dropper_hit_damage, _regular_damage;

    private readonly Quaternion _initial_front_lower_frame_orientation, _initial_front_upper_frame_orientation;
    private readonly Quaternion _initial_back_lower_frame_orientation, _initial_back_upper_frame_orientation;
    private readonly GameObject _unit;

    private readonly float _initial_head_height, _head_offset_from_base, _upper_frame_length;
    private readonly float _lower_frame_length, _lower_frame_length2, _double_lower_frame_length;
    private readonly float _lower_frame_horizontal_offset, _lower_frame_horizontal_roller_offset, _piston_height;

    private Vector2 _head_rollers_offset;
    private float   _current_height, _target_height, _current_upper_frame_angle;
    private int     _interations_left = max_iterations_before_sleep, _last_x, _last_z;
    private bool    _stowed = true;

    private float _side_pivot_relative_position = 0.0f, _side_arm_relative_position = 0.0f;
    private bool  _sidepan_stowed = true, _at_either_end = false;

    private float _last_pantograph_voltage = 0.0f;

    public bool stowed         => _stowed;
    public bool sidepan_stowed => _sidepan_stowed;

    public event Action? toggled, sidepan_toggled;

    static pantograph()
    {
        FieldInfo[] all_fields = typeof(pantograph).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (FieldInfo field in all_fields)
        {
            var part = (pantograph_part?) Attribute.GetCustomAttribute(field, typeof(pantograph_part), inherit: false);
            if (part is not null)
                _pantograph_parts[part.name] = field;
        }
    }

    private static GameObject? find_pantograph_base(bool is_sidepan, GameObject entity)
    {
        foreach (GameObject current_object in entity.AllChildren())
        {
            if (string.Equals(current_object.name, is_sidepan ? sidepan_tag : pantograph_tag, StringComparison.OrdinalIgnoreCase))
                return current_object;
        }
        return null;
    }

    private void assign_part(Dictionary<string, FieldInfo> tagged_anchors, GameObject entity)
    {
        if (tagged_anchors.TryGetValue(entity.name, out FieldInfo field))
        {
            //Main.log($"Anchor {entity.name} <=> {field.Name}");
            field.SetValue(this, entity.transform);
        }
    }

    private void assign_parts(Dictionary<string, FieldInfo> tagged_anchors, GameObject pantograph_base)
    {
        assign_part(tagged_anchors, pantograph_base);
        foreach (GameObject current_part in pantograph_base.AllChildren())
            assign_part(tagged_anchors, current_part);
    }

    public pantograph(GameObject unit, roof_busbar roof_bus, Fuse electric_supply, Fuse air_supply, Dictionary<string, Port> ports)
        : base("pantograph", electric_supply, air_supply)
    {
        _roof_bus = roof_bus;
        
        GameObject pantograph_base = find_pantograph_base(is_sidepan: false, unit) ?? throw new Exception("Missing pantograph");
        assign_parts(_pantograph_parts, pantograph_base);
        if (_base == null || _head == null || _front_lower_frame == null || _front_upper_frame == null
            || _back_lower_frame == null || _back_upper_frame == null || _front_piston == null || _back_piston == null
            || _head_rollers == null || _top_pivot == null || _piston_lever_tip == null
            || _contact_strip_end1 == null || _contact_strip_end2 == null)
        {
            throw new Exception("Incomplete pantograph");
        }

        _unit = unit;
        _head_offset_from_base = _head.localPosition.y;
        _initial_head_height = _current_height = _target_height = unit.transform.InverseTransformPoint(_head.TransformPoint(Vector3.zero)).y;
        //Main.log($"_initial_head_height = {_initial_head_height} _head_offset_from_base = {_head_offset_from_base}");
        if (_initial_head_height >= maximum_head_height)
            throw new Exception("Pantograph located too high");

        _piston_height = _front_piston.localPosition.y;

        _lower_frame_length                   = _front_upper_frame.localPosition.magnitude;
        _lower_frame_length2                  = _lower_frame_length * _lower_frame_length;
        _double_lower_frame_length            = 2.0f * _lower_frame_length;
        _upper_frame_length                   = _top_pivot.localPosition.magnitude;
        _head_rollers_offset                  = new Vector2(_head_rollers.localPosition.z, _head_rollers.localPosition.y);
        _lower_frame_horizontal_offset        = _front_lower_frame.localPosition.z;
        _lower_frame_horizontal_roller_offset = _lower_frame_horizontal_offset - _head_rollers_offset.x;
        //Main.log($"_lower_frame_horizontal_offset = {_lower_frame_horizontal_roller_offset} _lower_frame_length = {_lower_frame_length} _upper_frame_length = {_upper_frame_length} _head_rollers_offset = ({_head_rollers_offset.x}, {_head_rollers_offset.y})");

        float lower_frame_rest_angle = Mathf.Rad2Deg * Mathf.Asin(_front_upper_frame.localPosition.y / _lower_frame_length);
        float upper_frame_rest_angle = Mathf.Rad2Deg * Mathf.Asin(        _top_pivot.localPosition.y / _upper_frame_length) + lower_frame_rest_angle;
        //Main.log($"lower_frame_rest_angle = {lower_frame_rest_angle} upper_frame_rest_angle = {upper_frame_rest_angle}");
        _initial_front_lower_frame_orientation = _front_lower_frame.localRotation * Quaternion.AngleAxis(lower_frame_rest_angle, Vector3.right);
        _initial_front_upper_frame_orientation = _front_upper_frame.localRotation * Quaternion.AngleAxis(upper_frame_rest_angle, Vector3.left );
        _initial_back_lower_frame_orientation  =  _back_lower_frame.localRotation * Quaternion.AngleAxis(lower_frame_rest_angle, Vector3.right);
        _initial_back_upper_frame_orientation  =  _back_upper_frame.localRotation * Quaternion.AngleAxis(upper_frame_rest_angle, Vector3.right);

        GameObject sidepan_base = find_pantograph_base(is_sidepan: true, unit) ?? throw new Exception("Missing sidepan");
        assign_parts(_pantograph_parts, sidepan_base);
        if (_sidepan_base == null || _sidepan_pivot == null || _sidepan_arm == null 
            || _sidepan_inner_contact == null || _sidepan_outer_contact == null)
        {
            throw new Exception("Incomplete sidepan");
        }

        _arcing_damage      = sensor_grabber.grab_port(ports, "[Pantograph].ARCING"        );
        _dropper_hit_damage = sensor_grabber.grab_port(ports, "[Pantograph].DROPPER_HIT"   );
        _regular_damage     = sensor_grabber.grab_port(ports, "[Pantograph].REGULAR_DAMAGE");
    }

    private void move()
    {
        float height_difference = _target_height - _current_height;
        float movement_speed    = Mathf.Min(head_movement_speed, Mathf.Abs(height_difference) / 0.5f);
        if (height_difference > 0.006f)
        {
            _current_height   = Mathf.Min(_current_height + movement_speed * Time.deltaTime, maximum_head_height);
            _interations_left = max_iterations_before_sleep;
        }
        else if (height_difference < -0.006f)
        {
            _current_height   = Mathf.Max(_current_height - movement_speed * Time.deltaTime, _initial_head_height);
            _interations_left = max_iterations_before_sleep;
        }
        else if (_interations_left <= 0)
            return;

        --_interations_left;
        float current_head_offset = _current_height - _initial_head_height + _head_offset_from_base;
        float upper_frame_angle_cosine = Mathf.Cos(_current_upper_frame_angle);
        float height_to_roller = current_head_offset + _head_rollers_offset.y - frame_thickness / (2.0f * upper_frame_angle_cosine);
        float upper_frame_to_roller_length = _upper_frame_length - _head_rollers_offset.x / upper_frame_angle_cosine;
        float head_roller_distance_from_bottom_pivot = new Vector2(_lower_frame_horizontal_roller_offset, height_to_roller).magnitude;
        float angle_to_head_roller = Mathf.Acos(_lower_frame_horizontal_roller_offset / head_roller_distance_from_bottom_pivot);
        float upper_frame_to_roller_length2 = upper_frame_to_roller_length * upper_frame_to_roller_length;
        float head_roller_distance_from_bottom_pivot2 = head_roller_distance_from_bottom_pivot * head_roller_distance_from_bottom_pivot;
        float angle_between_frames = Mathf.Acos((_lower_frame_length2 + upper_frame_to_roller_length2
            - head_roller_distance_from_bottom_pivot2) / (_double_lower_frame_length * upper_frame_to_roller_length));
        float angle_between_lower_frame_and_distance_to_head_roller = Mathf.Acos((_lower_frame_length2
            + head_roller_distance_from_bottom_pivot2 - upper_frame_to_roller_length2)
            / (_double_lower_frame_length * head_roller_distance_from_bottom_pivot));
        float lower_frame_angle = Mathf.PI - (angle_to_head_roller + angle_between_lower_frame_and_distance_to_head_roller);
        _current_upper_frame_angle = angle_between_frames - lower_frame_angle;
        //Main.log((Mathf.Rad2Deg * _current_upper_frame_angle).ToString());

        Vector3 frame_lever_tip_position_relative_to_base = _base.InverseTransformPoint(_piston_lever_tip.TransformPoint(Vector3.zero));
        frame_lever_tip_position_relative_to_base.z -= _lower_frame_horizontal_offset;
        float piston_extension = _piston_height / frame_lever_tip_position_relative_to_base.y
            * frame_lever_tip_position_relative_to_base.z + _lower_frame_horizontal_offset;

        _head.localPosition      = new Vector3(0.0f, current_head_offset, 0.0f);
        angle_between_frames    *= Mathf.Rad2Deg;
        var lower_frame_rotation = Quaternion.AngleAxis(Mathf.Rad2Deg * lower_frame_angle, Vector3.left);
        var front_upper_frame_rotation = Quaternion.AngleAxis(angle_between_frames, Vector3.right);
        _front_lower_frame.localRotation = _initial_front_lower_frame_orientation * lower_frame_rotation;
        _back_lower_frame.localRotation  =  _initial_back_lower_frame_orientation * lower_frame_rotation;
        _front_upper_frame.localRotation = _initial_front_upper_frame_orientation * front_upper_frame_rotation;
        _back_upper_frame.localRotation  = _initial_back_upper_frame_orientation
            * new Quaternion(-front_upper_frame_rotation.x, -front_upper_frame_rotation.y, -front_upper_frame_rotation.z,
                front_upper_frame_rotation.w);
        _front_piston.localPosition = new Vector3(0.0f, _piston_height,  piston_extension);
        _back_piston.localPosition  = new Vector3(0.0f, _piston_height, -piston_extension);
    }

    private void sidepan_move()
    {
        if (!is_powered)
            return;
        if (_sidepan_stowed)
        {
            if (_side_arm_relative_position > 0.0f)
            {
                _side_arm_relative_position -= sidepan_relative_movement_speed * Time.deltaTime;
                _at_either_end               = false;
            }
            else if (_side_pivot_relative_position > 0.0f)
            {
                _side_pivot_relative_position -= sidepan_relative_movement_speed * Time.deltaTime;
                _at_either_end                 = false;
            }
            else if (_at_either_end)
                return;
            _at_either_end = true;
        }
        else
        {
            if (_side_pivot_relative_position < 1.0f)
            {
                _side_pivot_relative_position += sidepan_relative_movement_speed * Time.deltaTime;
                _at_either_end                 = false;
            }
            else if (_side_arm_relative_position < 1.0f)
            {
                _side_arm_relative_position += sidepan_relative_movement_speed * Time.deltaTime;
                _at_either_end               = false;
            }
            else if (_at_either_end)
                return;
            _at_either_end = true;
        }
        _sidepan_pivot.localRotation = Quaternion.Slerp(Quaternion.identity, _sidepan_pivot_deployed_orientation, _side_pivot_relative_position);
        _sidepan_arm.localRotation   = Quaternion.Slerp(Quaternion.identity,   _sidepan_arm_deployed_orientation,   _side_arm_relative_position);
    }

    private (float?, float) get_wire_height_and_voltage(Transform pantograph_base, Transform strip_end1, Transform strip_end2,
        float load_current) 
    { 
        (int strip_end1_x, int stripe_end1_z) = world_position.get_absolute_position(strip_end1.position);
        (int strip_end2_x, int stripe_end2_z) = world_position.get_absolute_position(strip_end2.position);
        //Main.log($"Contact ({strip_end1_x}, {stripe_end1_z})-({strip_end2_x}, {stripe_end2_z}) {pantograph_base.position.y}");
        return overhead_equipment.system.wire_height_and_voltage(strip_end1_x, stripe_end1_z, 
                                                     strip_end2_x, stripe_end2_z, pantograph_base.position.y, load_current);
    }
    
    private async void explode(Port trigger)
    {
        if (trigger.Value != 1.0f)
        {
            trigger.Value = 1.0f;
            await Task.Delay(2000);
            trigger.Value = 0.0f;
        }
    }
    
    public void simulate(float load_current)
    {
        check_if_disposed();
        bool        raised   = false;
        roof_busbar roof_bus = _roof_bus;
        if (roof_bus.halved_current)
            load_current /= 2.0f;
        if (_stowed || !is_powered)
        {
            _target_height               = _initial_head_height;
            roof_bus.pantograph_voltage = _last_pantograph_voltage = 0.0f;
        }
        else
        {
            /*
            (int strip_end1_x, int stripe_end1_z) = world_position.get_absolute_position(_contact_strip_end1.position);
            (int strip_end2_x, int stripe_end2_z) = world_position.get_absolute_position(_contact_strip_end2.position);
            Vector3 base_world_position = _base.position;
            float?  wire_height         = overhead_equipment.system.wire_height(strip_end1_x, stripe_end1_z, 
                                                                                strip_end2_x, stripe_end2_z, base_world_position.y);
            */
            (float? wire_height, float supply_voltage) = get_wire_height_and_voltage(_base, _contact_strip_end1, _contact_strip_end2, load_current);
            float bus_voltage;
            if (wire_height == null)
            {
                _target_height              = maximum_head_height;
                roof_bus.pantograph_voltage = 0.0f;
                bus_voltage                 = roof_bus.voltage;
                if (bus_voltage < _last_pantograph_voltage && load_current >= 200.0f 
                    && _last_pantograph_voltage > 0.0f && bus_voltage / _last_pantograph_voltage < 0.1f
                    || roof_bus.short_circuited)
                {
                    explode(_arcing_damage);
                }
            }
            else
            {
                Vector3 target_head_world_position = _base.position;
                target_head_world_position.y       = (float) wire_height;
                _target_height                     = _unit.transform.InverseTransformPoint(target_head_world_position).y;
                raised                             = Mathf.Abs(_current_height - _target_height) < 0.2f;
                roof_bus.pantograph_voltage        = raised ? supply_voltage : 0.0f;
                bus_voltage                        = roof_bus.voltage;
                
                if (_current_height - _target_height > 0.3f)
                    explode(_dropper_hit_damage);
                else if (bus_voltage < _last_pantograph_voltage && load_current >= 200.0f 
                    && _last_pantograph_voltage > 0.0f && bus_voltage / _last_pantograph_voltage < 0.1f
                    || roof_bus.short_circuited)
                {
                    explode(_arcing_damage);
                }
            }
            _last_pantograph_voltage = Mathf.Min(roof_bus.pantograph_voltage, roof_bus.voltage);    // No arcing if the other pantograph is still live
        }
        move();

        if (_sidepan_stowed || _side_pivot_relative_position < 1.0f || _side_arm_relative_position < 1.0f)
            roof_bus.sidepan_voltage = 0.0f;
        else
        {
            (float? rail_height, roof_bus.sidepan_voltage) = get_wire_height_and_voltage(_sidepan_base, _sidepan_inner_contact, _sidepan_outer_contact, load_current);
            raised |= rail_height != null;
        }
        sidepan_move();

        /*
        if (_unit.name[6] == 'A')
            Main.diagnostics?.Value = raised ? load_current : 0.0f;
        else
            Main.diagnostics2?.Value = raised ? load_current : 0.0f;
        */
        (int x, int z) = world_position.get_absolute_position(_base.position);
        if (!raised)
            _regular_damage.Value = 0.0f;
        else
        {
            float current_ratio = load_current / nominal_current;
            _regular_damage.Value = wear_per_metre * Mathf.Sqrt(world_position.get_distance_squared(x, z, _last_x, _last_z))
                                  + wear_per_second_at_full_current * current_ratio * current_ratio * Time.deltaTime;
        }
        _last_x = x;
        _last_z = z;
    }

    public void toggle(bool stowed)
    {
        check_if_disposed();
        _stowed = stowed;
        toggled?.Invoke();
    }

    public void sidepan_toggle(bool stowed)
    {
        check_if_disposed();
        _sidepan_stowed = stowed;
        sidepan_toggled?.Invoke();
    }
}
