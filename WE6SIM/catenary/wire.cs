// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Collections.Generic;

using Newtonsoft.Json;
using UnityEngine;

using electric_sim.catenary_editor;
using electric_sim.utilities;

namespace electric_sim.catenary;

interface wire_user: catenary_object_user
{
    overhead_equipment.wire_kind wire_type { get; set; }
}

public partial class overhead_equipment
{
    [JsonObject]
    private class wire: catenary_object, wire_user
    {
        const float default_section_length = 40.0f, default_wire_height = 6.0f, end_anchor_dead_length = 4.0f, end_anchor_raise = 0.2f, end_achor_fixed_part_length = default_section_length - 35.0053f;
        const float default_side_rail_length = 10.0f, default_side_rail_height = 4.5f;
        const float single_wire_1m_resistance = 3.3E-5f, dual_wire_1m_resistance = 2.2E-5f, quad_wire_1m_resistance = 1.6E-5f, side_rail_1m_resistance = 2.314E-5f, trolley_wire_1m_resistance = 6.6E-5f;

        private struct wire_internal
        {
            public string  template;
            public string? fixed_template;
            public float   section_length, contact_height, resistance_per_metre, fixed_part_length;
            public bool    end_anchor;
        }

        [JsonIgnore]
        private static readonly Dictionary<wire_kind, wire_internal> _wire_sections = new()
        {
            [wire_kind.plain_dual          ] = new() { template = "WireDual"                , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = dual_wire_1m_resistance   },
            [wire_kind.plain_single        ] = new() { template = "WireSingle"              , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = single_wire_1m_resistance },
            [wire_kind.plain_quad          ] = new() { template = "WireQuad"                , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = quad_wire_1m_resistance   },
            [wire_kind.middle_anchor_dual  ] = new() { template = "WireMidpointAnchorDual"  , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = dual_wire_1m_resistance   },
            [wire_kind.middle_anchor_single] = new() { template = "WireMidpointAnchorSingle", section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = single_wire_1m_resistance },
            [wire_kind.middle_anchor_quad  ] = new() { template = "WireMidpointAnchorQuad"  , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = quad_wire_1m_resistance   },
            [wire_kind.end_anchor_dual     ] = new() { template = "WireDualEnd"             , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = dual_wire_1m_resistance  , end_anchor = true, fixed_template = "WireDualFixedEnd"  , fixed_part_length = end_achor_fixed_part_length },
            [wire_kind.end_anchor_single   ] = new() { template = "WireSingleEnd"           , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = single_wire_1m_resistance, end_anchor = true, fixed_template = "WireSingleFixedEnd", fixed_part_length = end_achor_fixed_part_length },
            [wire_kind.end_anchor_quad     ] = new() { template = "WireQuadEnd"             , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = quad_wire_1m_resistance  , end_anchor = true, fixed_template = "WireQuadFixedEnd"  , fixed_part_length = end_achor_fixed_part_length },
            [wire_kind.wall_anchor_single  ] = new() { template = "WireSingleWallEnd"       , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = single_wire_1m_resistance, end_anchor = true },
            [wire_kind.side_rail           ] = new() { template = "SideRail"                , section_length = default_side_rail_length, contact_height = default_side_rail_height, resistance_per_metre = side_rail_1m_resistance    },
            [wire_kind.termination_rail    ] = new() { template = "SideRailEnd"             , section_length = default_side_rail_length, contact_height = default_side_rail_height, resistance_per_metre = side_rail_1m_resistance    },
            [wire_kind.trolley             ] = new() { template = "TrolleyWire"             , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = trolley_wire_1m_resistance },
            [wire_kind.trolley_anchor      ] = new() { template = "TrolleyWireEnd"          , section_length = default_section_length  , contact_height = default_wire_height     , resistance_per_metre = trolley_wire_1m_resistance, end_anchor = true }
        };
        
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

        private static string wire_template(wire_kind wire_type) => _wire_sections[wire_type].template;

        private (Quaternion primary_orientation, Vector3 primary_scale, Vector3 secondary_scale) 
            compute_shear_scale_transform(float shear_angle, float length, float template_section_length)
        {
            float primary_orientation_angle    = Mathf.Deg2Rad * 45.0f + shear_angle / 2.0f;
            var   primary_vertical_orientation = Quaternion.Euler(Mathf.Rad2Deg * (-primary_orientation_angle), 0.0f, 0.0f);
            var   primary_vertical_scale       = new Vector3(1.0f, Mathf.Cos(primary_orientation_angle), Mathf.Sin(primary_orientation_angle));
            var   secondary_vertical_scale     = new Vector3(1.0f, y_scale, length / template_section_length * y_scale / Mathf.Cos(shear_angle));
            //Main.log($"wire {shear_angle} {primary_orientation_angle} {primary_vertical_scale} {secondary_vertical_scale}");
            return (primary_vertical_orientation, primary_vertical_scale, secondary_vertical_scale);
        }
        
        [JsonConstructor]
        public wire(wire_kind wire_type, string substation, float length, float previous_pole_vertical_offset, 
            int x, int z, float y, Quaternion orientation): base(wire_template(wire_type), x, z, y, orientation)
        {
            assert.test(length > 0.0f);
            wire_internal wire_info            = _wire_sections[wire_type];
            this.wire_type                     = wire_type;
            this.substation                    = substation;
            this.length                        = length;
            this.previous_pole_vertical_offset = previous_pole_vertical_offset;
            this.length_1m_resistance          = wire_info.resistance_per_metre * editor_settings.voltage_drop_factor;

            if (wire_info.fixed_template == null)
            {
                float shear_angle = Mathf.Atan(previous_pole_vertical_offset / length);
                (_primary_vertical_orientation, _primary_vertical_scale, _secondary_vertical_scale) 
                    = compute_shear_scale_transform(shear_angle, length, wire_info.section_length);
            }
            else
            {
                previous_pole_vertical_offset += end_anchor_raise;
                float shear_angle = Mathf.Atan(previous_pole_vertical_offset / length);
                _fixed_part_template = system._templates[wire_info.fixed_template];
                (_fixed_part_primary_vertical_orientation, _fixed_part_primary_vertical_scale, _fixed_part_secondary_vertical_scale)
                    = compute_shear_scale_transform(shear_angle, wire_info.fixed_part_length, wire_info.fixed_part_length);
                float lengthwise_offset = length - wire_info.section_length;
                _fixed_part_offset = new Vector3(0.0f, previous_pole_vertical_offset / length * lengthwise_offset, 
                    lengthwise_offset);
                float stretch_part_length = length - wire_info.fixed_part_length;
                assert.test(stretch_part_length > 0.0f);
                (_primary_vertical_orientation, _primary_vertical_scale, _secondary_vertical_scale) 
                    = compute_shear_scale_transform(shear_angle, stretch_part_length, wire_info.section_length - wire_info.fixed_part_length);
            }

            Vector3 wire_top_view = orientation * Vector3.forward * (wire_info.end_anchor ? (length - end_anchor_dead_length) : length);
            _pantograph_strip_intersection = new line_cross(x, z, 
                x + world_position.float_to_fixed(wire_top_view.x), z + world_position.float_to_fixed(wire_top_view.z), 0.1f);
            _other_end_y = y + previous_pole_vertical_offset;
            //Main.log($"WTV = {wire_top_view} h0 = {y} h1 = {_other_end_y}");

            _contact_height = wire_info.contact_height;

#if DEBUG            
            Vector3         arrow_offset = orientation * Vector3.forward;
            int             x_offset     = world_position.float_to_fixed(arrow_offset.x), z_offset = world_position.float_to_fixed(arrow_offset.z);
            catenary_object arrow        = system.add_scenery_object(miscellaneous_object.build_generic("GantryArrow"),
                                           x + x_offset, z + z_offset, y, 
                                           orientation * new Quaternion(0.0f, 1.0f / Mathf.Sqrt(2.0f), 0.0f, 1.0f / Mathf.Sqrt(2.0f)));
            arrow.placed_procedurally = true;
#endif
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
        
        public override bool reveal()
        {
            is_visible = true;
            if (entity is null)
            {
                entity = new GameObject("Wire");
                Transform entity_location = entity.transform;
                entity_location.position  = get_relative_position();
                entity_location.rotation  = orientation;

                assert.test(template is not null);
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
            return true;
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
