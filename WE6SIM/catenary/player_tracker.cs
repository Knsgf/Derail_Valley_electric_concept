// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using WE6SIM.utilities;

namespace WE6SIM.catenary;

internal partial class overhead_equipment
{
    private class player_tracker: MonoBehaviour
    {
        
        void Start()
        {
            //Main.log("Player tracker started");
            gameObject.SetActive(false);
        }
        
        void Update()
        {
            Transform? player_view = PlayerManager.ActiveCamera?.transform;
            if (player_view is null)
            {
                player_view = PlayerManager.PlayerTransform;
                if (player_view is null)
                    return;
            }
            //(int x, int z) = world_position.get_absolute_position(player_view.position);
            //Main.log($"x={x} z={z}");
            _system?.handle_scenery_visibility(player_view.position);
        }

        public void suspend_tracker()
        {
            gameObject.SetActive(false);
        }

        public void resume_tracker()
        {
            gameObject.SetActive(true);
        }
    }
}
