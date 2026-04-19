// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using WE6SIM.catenary_editor;
using WE6SIM.utilities;

namespace WE6SIM.catenary;

interface gantry_user: catenary_object_user
{
    float stretch { get; set; }
}

internal static partial class catenary_visual
{
    [JsonObject]
    private class gantry: catenary_object, gantry_user
    {
        [JsonIgnore]
        private static readonly float[] _gantry_lengths = [8.86f, 13.26f, 17.56f];

        [JsonProperty]
        private readonly int tracks;
        [JsonIgnore]
        private float _stretch = 1.0f;
        [JsonIgnore]
        private readonly pole _further_pole;

        [JsonProperty]
        public float stretch
        {
            get => _stretch;
            set
            {
                _stretch = value;
                (int further_pole_x, int further_pole_z) = further_pole_position(x, z, tracks, value, orientation);
                _further_pole.x = further_pole_x;
                _further_pole.z = further_pole_z;
                _further_pole.entity?.transform.position = world_position.get_relative_position(further_pole_x, further_pole_z, y);
                reconstruct_tree();
            }
        }

        private static int get_template(int tracks)
        {
            return (tracks >= 2 && tracks <= 4) ? (tracks - 1)
                : throw new ArgumentOutOfRangeException("Gantries should cover 2, 3 or 4 tracks");
        }

        private static (int x, int z) further_pole_position(int x, int z, int tracks, float stretch, Quaternion orientation)
        {
            Vector3 offset_to_further_pole = orientation * Vector3.left * (_gantry_lengths[tracks - 2] * stretch);
            int further_pole_x = x + (int) (offset_to_further_pole.x * world_position.fixed_multiplier);
            int further_pole_z = z + (int) (offset_to_further_pole.z * world_position.fixed_multiplier);
            return (further_pole_x, further_pole_z);
        }

        [JsonConstructor]
        public gantry(int tracks, int x, int z, float y, Quaternion orientation, float stretch = 1.0f)
            : base(get_template(tracks), x, z, y, orientation)
        {
            this.tracks = tracks;
            (int further_pole_x, int further_pole_z) = further_pole_position(x, z, tracks, stretch, orientation);
            _further_pole = add_scenery_object((int x, int z, float y, Quaternion orientation) => new pole(pole_kind.Ground, x, z, y, orientation), 
                further_pole_x, further_pole_z, y, orientation);
            _further_pole.do_not_store = true;
        }
    }
}
