// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace WE6SIM.catenary;

internal partial class overhead_equipment
{
    private class player_tracker: MonoBehaviour
    {
        private bool _scenery_refresh_suspended = false;
        
        void Start()
        {
            //Main.log("Player tracker started");
        }
        
        void Update()
        {
            if (!_scenery_refresh_suspended)
                refresh_scenery();
        }

        private void refresh_scenery()
        {
            Transform? player_view = PlayerManager.ActiveCamera?.transform;
            if (player_view is null)
            {
                player_view = PlayerManager.PlayerTransform;
                if (player_view is null)
                    return;
            }
            //(int x, int z) = get_absolute_position(player_view.position);
            //Main.log($"x={x} z={z}");
            _system?.handle_scenery_visibility(player_view.position);
        }

        public void suspend_tracker()
        {
            _scenery_refresh_suspended = true;
        }

        public void resume_tracker()
        {
            _scenery_refresh_suspended = false;
        }
    }
}
