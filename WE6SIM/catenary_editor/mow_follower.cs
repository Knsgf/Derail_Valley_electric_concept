// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

#if DEBUG

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary_editor;

internal class mow_follower: IDisposable
{
    private readonly TrainCar _mow_vehicle;

    public mow_follower(TrainCar mow_vehicle)
    {
        _mow_vehicle = mow_vehicle;
        mow_vehicle.SimController.SimulationFlow.TickEvent += track_movement;
        editor.set_up_floating_origin();
    }

    private void track_movement()
    {
        Transform front_location = _mow_vehicle.FrontCouplerAnchor;
        Vector3 relative_position = front_location.position;
        (int front_x, int front_z) = get_absolute_position(relative_position);
        editor.process_location(front_x, front_z, relative_position.y, front_location.rotation);
    }

    public void Dispose()
    {
        _mow_vehicle.SimController.SimulationFlow.TickEvent -= track_movement;
        editor.remove_all_scenery();
    }
}

#endif