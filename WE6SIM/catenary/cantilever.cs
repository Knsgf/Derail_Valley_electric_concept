// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using UnityEngine;

namespace electric_sim.catenary;

interface cantilever_user: catenary_object_user
{
    bool dual_wire { get; }
    bool wire_attached { get; set; }
    Vector3 relative_wire_attachment_point();
}

public partial class overhead_equipment
{
    [JsonObject]
    private class cantilever: catenary_object, cantilever_user
    {
        const float sweep = 0.3f;

        private struct cantilever_internal
        {
            public string template;
            public float  wire_offset;
        }

        [JsonIgnore]
        private static readonly Dictionary<cantilever_kind, cantilever_internal> _cantilevers = new()
        {
            [cantilever_kind.Inner        ] = new() { template = "InnerCantilever"       , wire_offset =  sweep },
            [cantilever_kind.OutwardsInner] = new() { template = "InnerOutwardCantilever", wire_offset =  sweep },
            [cantilever_kind.MiddleInner  ] = new() { template = "MiddleInwardCantilever", wire_offset =   0.0f },
            [cantilever_kind.Middle       ] = new() { template = "MiddleCantilever"      , wire_offset =   0.0f },
            [cantilever_kind.InwardsOuter ] = new() { template = "OuterInwardCantilever" , wire_offset = -sweep },
            [cantilever_kind.Outer        ] = new() { template = "OuterCantilever"       , wire_offset = -sweep }
        };
        [JsonIgnore]
        private static readonly Dictionary<cantilever_kind, cantilever_internal> _gantry_registration_arms = new()
        {
            [cantilever_kind.Inner        ] = new() { template = "RegistrationArmInner"       , wire_offset =  sweep },
            [cantilever_kind.OutwardsInner] = new() { template = "RegistrationArmInnerOutward", wire_offset =  sweep },
            [cantilever_kind.MiddleInner  ] = new() { template = "RegistrationArmMiddleInner" , wire_offset =   0.0f },
            [cantilever_kind.Middle       ] = new() { template = "RegistrationArmMiddle"      , wire_offset =   0.0f },
            [cantilever_kind.Outer        ] = new() { template = "RegistrationArmOuter"       , wire_offset = -sweep }
        };
        [JsonIgnore]
        private static readonly Dictionary<cantilever_kind, cantilever_internal> _truss_gantry_registration_arms = new()
        {
            [cantilever_kind.Inner        ] = new() { template = "TrussArmInner"       , wire_offset = -sweep },
            [cantilever_kind.OutwardsInner] = new() { template = "TrussArmInnerOutward", wire_offset = -sweep },
            [cantilever_kind.MiddleInner  ] = new() { template = "TrussArmMiddleInner" , wire_offset =   0.0f },
            [cantilever_kind.Middle       ] = new() { template = "TrussArmMiddle"      , wire_offset =   0.0f },
            [cantilever_kind.InwardsOuter ] = new() { template = "TrussArmInwardsOuter", wire_offset = +sweep },
            [cantilever_kind.Outer        ] = new() { template = "TrussArmOuter"       , wire_offset = +sweep }
        };
        [JsonIgnore]
        private static readonly Dictionary<cantilever_kind, cantilever_internal> _tunnel_registration_arms = new()
        {
            [cantilever_kind.Inner        ] = new() { template = "TunnelInner"        , wire_offset =  sweep },
            [cantilever_kind.OutwardsInner] = new() { template = "TunnelOutwardsInner", wire_offset =  sweep },
            [cantilever_kind.MiddleInner  ] = new() { template = "TunnelMiddleInner"  , wire_offset =   0.0f },
            [cantilever_kind.Middle       ] = new() { template = "TunnelMiddle"       , wire_offset =   0.0f },
            [cantilever_kind.InwardsOuter ] = new() { template = "TunnelInwardsOuter" , wire_offset = -sweep },
            [cantilever_kind.Outer        ] = new() { template = "TunnelOuter"        , wire_offset = -sweep }
        };
        [JsonIgnore]
        private static readonly Dictionary<cantilever_kind, cantilever_internal> _trolley_cantilevers = new()
        {
            [cantilever_kind.Inner ] = new() { template = "TrolleyInner" , wire_offset =  sweep },
            [cantilever_kind.Middle] = new() { template = "TrolleyMiddle", wire_offset =   0.0f },
            [cantilever_kind.Outer ] = new() { template = "TrolleyOuter" , wire_offset = -sweep }
        };

        [JsonIgnore]
        private readonly Vector3 _wire_attachment_offset;
        
        [JsonProperty]
        public cantilever_kind cantilever_type;
        [JsonProperty]
        public steady_arm_kind steady_arm_type;

        [JsonProperty]
        public bool dual_wire { get; private set; }
        [JsonProperty]
        public bool wire_attached { get; set; }

        private static Dictionary<cantilever_kind, cantilever_internal> get_cantilever_types(steady_arm_kind steady_arm_type)
        {
            return steady_arm_type switch
            {
                steady_arm_kind.cantilever   => _cantilevers,
                steady_arm_kind.gantry       => _gantry_registration_arms,
                steady_arm_kind.truss_gantry => _truss_gantry_registration_arms,
                steady_arm_kind.tunnel       => _tunnel_registration_arms,
                steady_arm_kind.trolley      => _trolley_cantilevers,
                _ => throw new ArgumentException($"Unknown cantilever type {steady_arm_type}")
            };
        }

        private static string get_template(cantilever_kind cantilever_type, steady_arm_kind steady_arm_type, bool dual_wire)
        {
            if (!get_cantilever_types(steady_arm_type).TryGetValue(cantilever_type, out cantilever_internal cantilever_info))
                throw new ArgumentOutOfRangeException($"Invalid cantilever type {cantilever_type}");
            return cantilever_info.template + (dual_wire ? "Dual" : "Single");
        }
        
        [JsonConstructor]
        public cantilever(cantilever_kind cantilever_type, steady_arm_kind steady_arm_type,
            bool dual_wire, int x, int z, float y, Quaternion orientation)
            : base(get_template(cantilever_type, steady_arm_type, dual_wire), x, z, y, orientation)
        {
            if (!get_cantilever_types(steady_arm_type).TryGetValue(cantilever_type, out cantilever_internal cantilever_info))
                throw new ArgumentOutOfRangeException($"Invalid cantilever type {cantilever_type}");

            this.cantilever_type = cantilever_type;
            this.dual_wire       = dual_wire;
            this.steady_arm_type = steady_arm_type;

            _wire_attachment_offset = orientation * ((steady_arm_type == steady_arm_kind.gantry) ? Vector3.left : Vector3.right) * cantilever_info.wire_offset;
        }

        public Vector3 relative_wire_attachment_point() => get_relative_position() + _wire_attachment_offset;
    }
}
