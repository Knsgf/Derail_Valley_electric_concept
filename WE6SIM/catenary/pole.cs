// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using UnityEngine;

using electric_sim.utilities;

using static electric_sim.catenary.overhead_equipment;

namespace electric_sim.catenary;

interface pole_user: catenary_object_user
{
    float offset { get; }

    bool cantilever_on_near_side { get; set; }
    bool cantilever_on_far_side  { get; set; }
    bool anchored                { get; set; }
    bool siding_anchor           { get; set; }

    bool is_ground   { get; }
    bool is_tunnel   { get; }
    bool is_bridge   { get; }
    bool is_bracket  { get; }
    bool is_siderail { get; }

    Vector3 get_pole_true_position();
    public (string?, string?) matching_cantilever(cantilever_kind direction);
}

public partial class overhead_equipment
{
    private struct pole_template: catenary_object_template
    {
        public readonly string template_name => template;
        public readonly string asset_path    => asset;
        
        [CSV_column(0)]
        public string template;
        [CSV_column(1)]
        public string asset;
        [CSV_column(2)]
        public string kind;
        [CSV_column(3)]
        public string type;
        [CSV_column(4)]
        public string foundation_template;
        [CSV_column(5)]
        public float offset;
        [CSV_column(6)]
        public bool is_ground;
        [CSV_column(7)]
        public bool is_bridge;
        [CSV_column(8)]
        public bool is_tunnel;
        [CSV_column(9)]
        public bool is_bracket;
        [CSV_column(10)]
        public string matching_cantilever;
        [CSV_column(11)]
        public string inner_direction;
        [CSV_column(12)]
        public string outwards_inner_direction;
        [CSV_column(13)]
        public string middle_inner_direction;
        [CSV_column(14)]
        public string middle_direction;
        [CSV_column(15)]
        public string inwards_outer_direction;
        [CSV_column(16)]
        public string outer_direction;
    }
    
    [JsonObject]
    private class pole: catenary_object, pole_user
    {
        const string default_kind = "default";
        
        private static readonly Dictionary<string, Dictionary<string, pole_template>> _pole_definitions = [];
        
        [JsonIgnore]
        public bool erased = false;
        
        [JsonProperty]
        public string kind      { get; private set; }
        [JsonProperty]
        public string pole_type { get; private set; }
        [JsonProperty]
        public bool cantilever_on_near_side { get; set; }
        [JsonProperty]
        public bool cantilever_on_far_side  { get; set; }
        [JsonProperty]
        public bool anchored { get; set; }
        [JsonProperty]
        public bool siding_anchor { get; set; }

        [JsonIgnore]
        public float offset     => _pole_definitions[kind][pole_type].offset;
        [JsonIgnore]
        public bool is_ground   => _pole_definitions[kind][pole_type].is_ground;
        [JsonIgnore]
        public bool is_tunnel   => _pole_definitions[kind][pole_type].is_tunnel;
        [JsonIgnore]
        public bool is_bridge   => _pole_definitions[kind][pole_type].is_bridge;
        [JsonIgnore]
        public bool is_bracket  => _pole_definitions[kind][pole_type].is_bracket;
        [JsonIgnore]
        public bool is_siderail => false;
        
        public static void set_up_templates(CSV_struct<pole_template> templates)
        {
            for (int row = templates.row_count; row > 0; --row)
            {
                pole_template current_definition = templates.get_row(row);
                if (!_pole_definitions.TryGetValue(current_definition.kind, out Dictionary<string, pole_template> poles_of_kind))
                    _pole_definitions[current_definition.kind] = poles_of_kind = [];
                poles_of_kind[current_definition.type] = current_definition;
            }
        }

        private static string pole_template(string? kind, string? type)
        {
            kind ??= default_kind;
            if (!_pole_definitions.TryGetValue(kind, out Dictionary<string, pole_template> poles_of_kind))
                throw new ArgumentException($"Unknown pole kind {kind}");
            if (type == null || !poles_of_kind.TryGetValue(type, out pole_template pole_definition))
                throw new ArgumentException($"Unknown pole type {type ?? "<null>"}");
            return pole_definition.template_name;
        }

        [JsonConstructor]
        public pole(string? kind, string? pole_type, bool is_siding_anchor_pole, int x, int z, float y, Quaternion orientation)
            : base(pole_template(kind, pole_type), x, z, y, orientation)
        {
            this.kind          = kind      ?? default_kind;
            this.pole_type     = pole_type ?? throw new ArgumentException("Placement type cannot be null");
            this.siding_anchor = is_siding_anchor_pole;
            
            string foundation_template = _pole_definitions[this.kind][this.pole_type].foundation_template;
            if (!string.IsNullOrWhiteSpace(foundation_template))
            {
                catenary_object foundation     = system.add_scenery_object(miscellaneous_object.build_generic(foundation_template), x, z, y, orientation);
                foundation.placed_procedurally = true;
            }
        }

        public Vector3 get_pole_true_position() => get_relative_position() + orientation * Vector3.right * offset;

        public void sink_pole(float height_change)
        {
            y -= height_change;
        }

        public (string?, string?) matching_cantilever(cantilever_kind direction)
        {
            pole_template definition = _pole_definitions[kind][pole_type];
            string? arm_type         = string.IsNullOrWhiteSpace(definition.matching_cantilever) ? null : definition.matching_cantilever;
            string? real_direction   = direction switch
            {
                cantilever_kind.Inner         => definition.inner_direction,
                cantilever_kind.OutwardsInner => definition.outwards_inner_direction,
                cantilever_kind.MiddleInner   => definition.middle_inner_direction,
                cantilever_kind.Middle        => definition.middle_direction,
                cantilever_kind.InwardsOuter  => definition.inwards_outer_direction,
                cantilever_kind.Outer         => definition.outer_direction,
                _ => null
            };
            return (arm_type, string.IsNullOrWhiteSpace(real_direction) ? null : real_direction);
        }
    }
}
