// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using UnityEngine;

using electric_sim.utilities;

namespace electric_sim.catenary;

interface cantilever_user: catenary_object_user
{
    bool dual_wire { get; }
    bool wire_attached { get; set; }
#if DEBUG
    Vector3 relative_wire_attachment_point();
#endif
}

public partial class overhead_equipment
{
    private struct cantilever_template: catenary_object_template
    {
        public readonly string template_name => template;
        public readonly string asset_path    => asset;
        
        [CSV_column(0)]
        public string template;
        [CSV_column(1)]
        public string asset;
        [CSV_column(2)]
        public string arm_type;
        [CSV_column(3)]
        public string cantilever_type;
        [CSV_column(4)]
        public bool   dual_wire;
        [CSV_column(5)]
        public float  wire_offset;
    }
    
    [JsonObject]
    private class cantilever: catenary_object, cantilever_user
    {
        private struct cantilever_definition
        {
            public string? template;
            public bool    dual_wire;
            public float   wire_offset;
        }

        [JsonIgnore]
        private static readonly Dictionary<string, Dictionary<string, cantilever_definition[]>> _all_cantilevers = [];

#if DEBUG
        [JsonIgnore]
        private readonly Vector3 _wire_attachment_offset;
#endif
        
        [JsonProperty]
        public string cantilever_type;
        [JsonProperty]
        public string steady_arm_type;

        [JsonProperty]
        public bool dual_wire { get; private set; }
        [JsonProperty]
        public bool wire_attached { get; set; }

        public static void set_up_templates(CSV_struct<cantilever_template> templates)
        {
            for (int row = templates.row_count; row > 0; --row)
            { 
                cantilever_template current_definition = templates.get_row(row);
                if (!_all_cantilevers.TryGetValue(current_definition.arm_type, 
                    out Dictionary<string, cantilever_definition[]> cantilevers_of_type))
                {
                    _all_cantilevers[current_definition.arm_type] = cantilevers_of_type = [];
                }
                if (!cantilevers_of_type.TryGetValue(current_definition.cantilever_type, 
                    out cantilever_definition[] cantilevers))
                {
                    cantilevers_of_type[current_definition.cantilever_type] = cantilevers = new cantilever_definition[2];
                }
                cantilevers[current_definition.dual_wire ? 1 : 0] = new cantilever_definition 
                { 
                    template    = current_definition.template_name,
                    dual_wire   = current_definition.dual_wire, 
                    wire_offset = current_definition.wire_offset 
                };
            }
        }

        private static cantilever_definition get_definition(string cantilever_type, string steady_arm_type, bool dual_wire)
        {
            if (!_all_cantilevers.TryGetValue(steady_arm_type, 
                out Dictionary<string, cantilever_definition[]> cantilevers_of_type))
            {
                throw new ArgumentException($"Invalid cantilever type {steady_arm_type}");
            }
            int cantilever_index = dual_wire ? 1 : 0;
            if (!cantilevers_of_type.TryGetValue(cantilever_type, out cantilever_definition[] cantilevers)
                || cantilevers[cantilever_index].template == null)
            {
                throw new ArgumentException($"Invalid cantilever type {cantilever_type}");
            }
            return cantilevers[cantilever_index];
        }

        private static string get_template(string cantilever_type, string steady_arm_type, bool dual_wire)
        {
            return get_definition(cantilever_type, steady_arm_type, dual_wire).template!;
        }

        [JsonConstructor]
        public cantilever(string cantilever_type, string steady_arm_type,
            bool dual_wire, int x, int z, float y, Quaternion orientation)
            : base(get_template(cantilever_type, steady_arm_type, dual_wire), x, z, y, orientation)
        {
            //if (!get_cantilever_types(steady_arm_type).TryGetValue(cantilever_type, out cantilever_internal cantilever_info))
            //    throw new ArgumentOutOfRangeException($"Invalid cantilever type {cantilever_type}");

            this.cantilever_type = cantilever_type;
            this.dual_wire       = dual_wire;
            this.steady_arm_type = steady_arm_type;

#if DEBUG
            _wire_attachment_offset = orientation * Vector3.right * get_definition(cantilever_type, steady_arm_type, dual_wire).wire_offset;
#endif
        }

#if DEBUG
        public Vector3 relative_wire_attachment_point() => get_relative_position() + _wire_attachment_offset;
#endif
    }
}
