### WE6-981 - an electric locomotive proof of concept for Derail Valley
This is an experimental heavy freight electric locomotive mod for Derail Valley.
### Installation
1. Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) (UMM),
2. Install [Custom Car Loader](https://www.nexusmods.com/derailvalley/mods/324) (CCL) and its prerequisites,
3. Drag and drop both [WE6SIM.zip](https://github.com/Knsgf/WE6Concept/releases/download/Regular/WE6SIM.zip) and [WE6CCL.zip](https://github.com/Knsgf/WE6Concept/releases/download/Regular/WE6CCL.zip) into UMM.  
 The first file contains simulation code and catenary, the second is locomotive model itself. Both are required.
4. It's strongly recommended to read the [operating manual](https://github.com/Knsgf/WE6Concept/releases/download/Regular/WE6_manual.pdf). The control scheme and operation of the locomotive are different from vanilla diesel and battery electric.

### Usage and spawning
At the moment the locomotive is only usable in sandbox mode and can only be brought in via scenario editor or comms radio.

When spawning the locomotive via the radio, make sure that units A and B are facing in the same direction and the arrow "front" of unit B is touching the arrows "back" of unit A. The order in which units are spawned doesn't matter: it's possible to bring in unit B first, then A and vice versa.

The graph plotter window for unit A includes 2 electricity meters. The "diagnostics.DISPLAY" shows total energy consumed in kWh, and "diagnostics.DISPLAY2" is the amount recovered by regenerative braking. The current plan for electricity price is $10/kWh.
### Other limitations
Only 2 lines are electrified at the moment: a short FM-SM route and FF-IME/CME with a short downhill branch towards SM. THe flat section between OWN and SM **is not electrified.**

The height of the locomotive causes it to clip through water standpipes and chutes at SM and IME. The collision with them is not enforced and players are free to ignore these.

The catenary has no hitboxes to both help with performance and permit players to throw switches obscured by poles via radio.

Unit B has no windows, doors or working lights, excluding those in the machinery room. The walk mesh is also not yet set, making it possible to clip through the walls, and most handles have no sound.

There are interior pop-ins when crossing units.

Gadgets which directly control a locomotive, such as amp limiter or overheat protector, are **not compatible** with WE6. Neither is "Start-up" function of the comms radio.

An installed inclinometer gadget shows 1-2° lean forward on a level track.

There is no horn yet. Some details, like sand pipes, bogie centering devices and underframe air ducts, are also missing.

The "powertrain damage on idling" mechanic is not yet implemented.

