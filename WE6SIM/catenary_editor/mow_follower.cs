// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

#if DEBUG

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using DV.ThingTypes;
using WE6SIM.catenary;

using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary_editor;

internal class mow_follower: IDisposable
{
    
    private readonly TrainCar _mow_vehicle;

    public mow_follower(TrainCar mow_vehicle)
    {
        _mow_vehicle = mow_vehicle;
        editor.use_DM1U = mow_vehicle.carType == TrainCarType.LocoDM1U;
        mow_vehicle.SimController.SimulationFlow.TickEvent += track_movement;
        catenary_visual.set_up();
    }

    private void track_movement()
    {
        Transform front_location = _mow_vehicle.FrontCouplerAnchor;
        Vector3 relative_position = front_location.position;
        if (PlayerManager.Car == _mow_vehicle)
            catenary_visual.handle_scenery_visibility(relative_position);
        editor.process_location(relative_position, front_location.forward);
    }

    public void Dispose()
    {
        _mow_vehicle.SimController.SimulationFlow.TickEvent -= track_movement;
        catenary_visual.store_scenery();
    }
}

#endif