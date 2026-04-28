using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using UnityEngine;


namespace WE6SIM.catenary;

interface cantilever_user: catenary_object_user
{
    bool dual_wire { get; }
    bool wire_attached { get; set; }
    Vector3 relative_wire_attachment_point();
}

internal partial class overhead_equipment
{
    [JsonObject]
    private class cantilever: catenary_object, cantilever_user
    {
        const float sweep = 0.3f;
        
        [JsonIgnore]
        private readonly Vector3 _wire_attachment_offset;
        
        [JsonProperty]
        public cantilever_kind cantilever_type { get; private set; }
        [JsonProperty]
        public bool is_gantry_registration_arm { get; private set; }
        [JsonProperty]
        public bool dual_wire { get; private set; }
        [JsonProperty]
        public bool wire_attached { get; set; }
        
        private static string get_template(cantilever_kind cantilever_type, bool is_gantry_registration_arm, bool dual_wire)
        {
            if (is_gantry_registration_arm)
            {
                return cantilever_type switch
                {
                    cantilever_kind.Inner  => dual_wire ?  "RegistrationArmInnerDual" :  "RegistrationArmInnerSingle",
                    cantilever_kind.Middle => dual_wire ? "RegistrationArmMiddleDual" : "RegistrationArmMiddleSingle",
                    cantilever_kind.Outer  => dual_wire ?  "RegistrationArmOuterDual" :  "RegistrationArmOuterSingle",
                    _ => throw new ArgumentOutOfRangeException($"Invalid registration arm type {cantilever_type}")
                };
            }
            return cantilever_type switch
            {
                cantilever_kind.Inner  => dual_wire ?  "InnerCantileverDual" :  "InnerCantileverSingle",
                cantilever_kind.Middle => dual_wire ? "MiddleCantileverDual" : "MiddleCantileverSingle",
                cantilever_kind.Outer  => dual_wire ?  "OuterCantileverDual" :  "OuterCantileverSingle",
                _ => throw new ArgumentOutOfRangeException($"Invalid cantilever type {cantilever_type}")
            };
        }
        
        [JsonConstructor]
        public cantilever(cantilever_kind cantilever_type, 
            bool is_gantry_registration_arm, bool dual_wire, int x, int z, float y, Quaternion orientation)
            : base(get_template(cantilever_type, is_gantry_registration_arm, dual_wire), x, z, y, orientation)
        {
            this.cantilever_type            = cantilever_type;
            this.is_gantry_registration_arm = is_gantry_registration_arm;
            this.dual_wire                  = dual_wire;

            float wire_attachment_offset = cantilever_type switch
            {
                cantilever_kind.Inner  => sweep,
                cantilever_kind.Middle => 0.0f,
                cantilever_kind.Outer  => -sweep,
                _ => throw new ArgumentOutOfRangeException($"Unknown cantilever type {cantilever_type}")
            };
            _wire_attachment_offset = orientation * (is_gantry_registration_arm ? Vector3.left : Vector3.right) * wire_attachment_offset;
        }

        public Vector3 relative_wire_attachment_point() => get_relative_position() + _wire_attachment_offset;
    }
}
