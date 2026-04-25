using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using UnityEngine;


namespace WE6SIM.catenary;

interface cantilever_user: catenary_object_user
{}

internal static partial class catenary_visual
{
    [JsonObject]
    private class cantilever: catenary_object, cantilever_user
    {
        [JsonProperty]
        public cantilever_kind cantilever_type { get; private set; }
        [JsonProperty]
        public bool is_gantry_registration_arm { get; private set; }
        [JsonProperty]
        public bool dual_wire { get; private set; }
        
        private static int get_template(cantilever_kind cantilever_type, bool is_gantry_registration_arm, bool dual_wire)
        {
            if (is_gantry_registration_arm)
            {
                return cantilever_type switch
                {
                    cantilever_kind.Inner  => dual_wire ? 13 : 14,
                    cantilever_kind.Middle => dual_wire ? 15 : 16,
                    cantilever_kind.Outer  => dual_wire ? 17 : 18,
                    _ => throw new ArgumentOutOfRangeException($"Invalid registration arm type {cantilever_type}")
                };
            }
            return cantilever_type switch
            {
                cantilever_kind.Inner  => dual_wire ? 4 : 5,
                cantilever_kind.Middle => dual_wire ? 6 : 7,
                cantilever_kind.Outer  => dual_wire ? 8 : 9,
                _ => throw new ArgumentOutOfRangeException($"Invalid cantilever type {cantilever_type}")
            };
        }
        
        [JsonConstructor]
        public cantilever(cantilever_kind cantilever_type, bool is_gantry_registration_arm, bool dual_wire, int x, int z, float y, Quaternion orientation)
            : base(get_template(cantilever_type, is_gantry_registration_arm, dual_wire), x, z, y, orientation)
        {
            this.cantilever_type            = cantilever_type;
            this.is_gantry_registration_arm = is_gantry_registration_arm;
            this.dual_wire                  = dual_wire;
        }
    }
}
