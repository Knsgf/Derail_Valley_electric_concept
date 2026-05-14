// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using Newtonsoft.Json;
using UnityEngine;

using WE6SIM.utilities;

namespace WE6SIM.catenary;

interface wire_user: catenary_object_user
{
    overhead_equipment.wire_kind wire_type { get; set; }
}

internal partial class overhead_equipment
{
    [JsonObject]
    private class wire: catenary_object, wire_user
    {
        const float default_section_length = 40.0f, default_wire_height = 6.0f, end_anchor_dead_length = 4.0f, end_achor_fixed_part_length = default_section_length - 35.0053f;
        const float default_side_rail_length = 10.0f, default_side_rail_height = 4.5f;
        const float single_wire_1m_resistance = 3.3E-5f, dual_wire_1m_resistance = 2.2E-5f, side_rail_1m_resistance = 2.314E-5f;
        
        [JsonIgnore]
        private static readonly float y_scale = Mathf.Sqrt(2.0f);
        [JsonIgnore]
        private static readonly Quaternion secondary_vertical_orientation = Quaternion.Euler(45.0f, 0.0f, 0.0f);

        [JsonIgnore]
        private readonly Quaternion _primary_vertical_orientation, _fixed_part_primary_vertical_orientation;
        [JsonIgnore]
        private readonly Vector3 _primary_vertical_scale, _secondary_vertical_scale;
        [JsonIgnore]
        private readonly Vector3 _fixed_part_primary_vertical_scale, _fixed_part_secondary_vertical_scale, _fixed_part_offset;
        [JsonIgnore]
        private readonly line_cross _pantograph_strip_intersection;
        [JsonIgnore]
        private readonly float _other_end_y, _contact_height;
        
        private GameObject? _primary_transform, _secondary_transform, _fixed_part_template, _fixed_primary_transform, _fixed_secondary_transform;

        [JsonProperty]
        public float length;
        [JsonProperty]
        public float previous_pole_vertical_offset;
        [JsonProperty]
        public string substation;
        [JsonIgnore]
        public int substation_index = 0;
        [JsonIgnore]
        public float length_1m_resistance;

        [JsonProperty]
        public wire_kind wire_type { get; set; }

        private static string wire_template(wire_kind wire_type)
        {
            return wire_type switch
            {
                wire_kind.plain_dual           => "WireDual",
                wire_kind.plain_single         => "WireSingle",
                wire_kind.middle_anchor_dual   => "WireMidpointAnchorDual",
                wire_kind.middle_anchor_single => "WireMidpointAnchorSingle",
                wire_kind.end_anchor_dual      => "WireDualEnd",
                wire_kind.end_anchor_single    => "WireSingleEnd",
                wire_kind.wall_anchor_single   => "WireSingleWallEnd",
                wire_kind.side_rail            => "SideRail",
                wire_kind.termination_rail     => "SideRailEnd",
                _ => throw new ArgumentOutOfRangeException($"Invalid wire type {wire_type}")
            };
        }

        private (Quaternion primary_orientation, Vector3 primary_scale, Vector3 secondary_scale) 
            compute_shear_scale_transform(float shear_angle, float length, float template_section_length)
        {
            float primary_orientation_angle    = Mathf.Deg2Rad * 45.0f + shear_angle / 2.0f;
            var   primary_vertical_orientation = Quaternion.Euler(Mathf.Rad2Deg * (-primary_orientation_angle), 0.0f, 0.0f);
            var   primary_vertical_scale       = new Vector3(1.0f, Mathf.Cos(primary_orientation_angle), Mathf.Sin(primary_orientation_angle));
            var   secondary_vertical_scale     = new Vector3(1.0f, y_scale, length / template_section_length * y_scale / Mathf.Cos(shear_angle));
            Main.log($"wire {shear_angle} {primary_orientation_angle} {primary_vertical_scale} {secondary_vertical_scale}");
            return (primary_vertical_orientation, primary_vertical_scale, secondary_vertical_scale);
        }
        
        [JsonConstructor]
        public wire(wire_kind wire_type, string substation, float length, float previous_pole_vertical_offset, 
            int x, int z, float y, Quaternion orientation): base(wire_template(wire_type), x, z, y, orientation)
        {
            assert.test(length > 0.0f);
            this.wire_type                     = wire_type;
            this.substation                    = substation;
            this.length                        = length;
            this.previous_pole_vertical_offset = previous_pole_vertical_offset;
            this.length_1m_resistance          = wire_type switch
            {
                wire_kind.plain_dual           =>   dual_wire_1m_resistance,
                wire_kind.plain_single         => single_wire_1m_resistance,
                wire_kind.middle_anchor_dual   =>   dual_wire_1m_resistance,
                wire_kind.middle_anchor_single => single_wire_1m_resistance,
                wire_kind.end_anchor_dual      =>   dual_wire_1m_resistance,
                wire_kind.end_anchor_single    => single_wire_1m_resistance,
                wire_kind.wall_anchor_single   => single_wire_1m_resistance,
                wire_kind.side_rail            =>   side_rail_1m_resistance,
                wire_kind.termination_rail     =>   side_rail_1m_resistance,
                _ => throw new ArgumentOutOfRangeException($"Invalid wire type {wire_type}")
            };
            
            bool  is_side_rail              = wire_type is wire_kind.side_rail or wire_kind.termination_rail;
            float template_section_length   = is_side_rail ? default_side_rail_length : default_section_length;
            float shear_angle               = Mathf.Atan(previous_pole_vertical_offset / length);
            if (wire_type != wire_kind.end_anchor_single)
            {
                (_primary_vertical_orientation, _primary_vertical_scale, _secondary_vertical_scale) 
                    = compute_shear_scale_transform(shear_angle, length, template_section_length);
            }
            else
            {
                _fixed_part_template = system._templates["WireSingleFixedEnd"];
                (_fixed_part_primary_vertical_orientation, _fixed_part_primary_vertical_scale, _fixed_part_secondary_vertical_scale)
                    = compute_shear_scale_transform(shear_angle, end_achor_fixed_part_length, end_achor_fixed_part_length);
                float lengthwise_offset = length - default_section_length;
                _fixed_part_offset = new Vector3(0.0f, previous_pole_vertical_offset / length * lengthwise_offset, 
                    lengthwise_offset);
                float stretch_part_length = length - end_achor_fixed_part_length;
                assert.test(stretch_part_length > 0.0f);
                (_primary_vertical_orientation, _primary_vertical_scale, _secondary_vertical_scale) 
                    = compute_shear_scale_transform(shear_angle, stretch_part_length, default_section_length - end_achor_fixed_part_length);
            }

            bool    end_achor     = wire_type is wire_kind.end_anchor_dual or wire_kind.end_anchor_single or wire_kind.wall_anchor_single;
            Vector3 wire_top_view = orientation * Vector3.forward * (end_achor ? (length - end_anchor_dead_length) : length);
            _pantograph_strip_intersection = new line_cross(x, z, 
                x + world_position.float_to_fixed(wire_top_view.x), z + world_position.float_to_fixed(wire_top_view.z), 0.01f);
            _other_end_y = y + previous_pole_vertical_offset;
            Main.log($"WTV = {wire_top_view} h0 = {y} h1 = {_other_end_y}");

            _contact_height = is_side_rail ? default_side_rail_height : default_wire_height;
        }

        private void reveal_part(string transform_name, Transform entity_location, Vector3 local_offset, GameObject template,
            Quaternion primary_vertical_orientation, Vector3 primary_vertical_scale, Vector3 secondary_vertical_scale,
            ref GameObject? primary_transform, ref GameObject? secondary_transform)
        {
            primary_transform = new GameObject(transform_name);
            Transform primary_location = primary_transform.transform;
            primary_location.SetParent(entity_location, worldPositionStays: false);
            primary_location.localPosition = local_offset;
            primary_location.localRotation = primary_vertical_orientation;
            primary_location.localScale    = primary_vertical_scale;

            secondary_transform = GameObject.Instantiate(template, primary_location);
            Transform secondary_location     = secondary_transform.transform;
            secondary_location.localRotation = secondary_vertical_orientation;
            secondary_location.localScale    = secondary_vertical_scale;
        }
        
        public override void reveal()
        {
            is_visible = true;
            if (entity is null)
            {
                entity = new GameObject("Wire");
                Transform entity_location = entity.transform;
                entity_location.position  = get_relative_position();
                entity_location.rotation  = orientation;

                reveal_part("ShearA", entity_location, Vector3.zero, template, 
                    _primary_vertical_orientation, _primary_vertical_scale, _secondary_vertical_scale,
                    ref _primary_transform, ref _secondary_transform);
                if (_fixed_part_template is not null)
                {
                    reveal_part("ShearB", entity_location, _fixed_part_offset, _fixed_part_template,
                        _fixed_part_primary_vertical_orientation, _fixed_part_primary_vertical_scale, _fixed_part_secondary_vertical_scale,
                        ref _fixed_primary_transform, ref _fixed_secondary_transform);
                }
            }
        }

        public override void hide_when_out_of_view()
        {
            if (!is_visible && entity is not null)
            {
                assert.test(_primary_transform is not null && _secondary_transform is not null);
                GameObject.Destroy(_secondary_transform);
                GameObject.Destroy(  _primary_transform);
                GameObject.Destroy(              entity);
                if (_fixed_part_template is not null)
                {
                    GameObject.Destroy(_fixed_secondary_transform);
                    GameObject.Destroy(  _fixed_primary_transform);
                    _fixed_primary_transform = _fixed_secondary_transform = null;
                }
                entity = _primary_transform = _secondary_transform = null;
            }
        }

        public float? contact_height(int strip_end1_x, int strip_end1_z, int strip_end2_x, int strip_end2_z, 
            float pantograph_base_y)
        {
            float? intersection_parameter = _pantograph_strip_intersection.crossed_line_parameter(strip_end1_x, strip_end1_z,
                                                                                                  strip_end2_x, strip_end2_z);
            if (intersection_parameter == null)
                return null;
            float contact_height = Mathf.LerpUnclamped(y, _other_end_y, (float) intersection_parameter) + _contact_height;
            //Main.log($"@{contact_height} -> {pantograph_base_y}");
            return (contact_height > pantograph_base_y) ? contact_height : null;
        }
    }
}
