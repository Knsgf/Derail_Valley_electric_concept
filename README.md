### 2WE3-981 - an electric locomotive proof of concept for Derail Valley
This is an experimental heavy freight electric locomotive mod for Derail Valley.
### Installation
1. Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) (UMM),
2. Install [Custom Car Loader](https://www.nexusmods.com/derailvalley/mods/324) (CCL) and its prerequisites,
3. Uninstall the following mods in UMM, if they're present: **WE6-981** and **WE6SIM**,
4. Drag and drop both [Catenary-DC.zip](https://github.com/Knsgf/Derail_Valley_electric_concept/releases/download/v2.2.1/Catenary-DC.zip) and [2WE3CCL.zip](https://github.com/Knsgf/Derail_Valley_electric_concept/releases/download/v2.2.1/2WE3CCL.zip) into UMM.
 The first file contains simulation code and catenary, the second is locomotive model itself. Both are required.
5. It's strongly recommended to read the [operating manual](https://github.com/Knsgf/Derail_Valley_electric_concept/releases/download/v2.2.1/2WE3_manual.pdf). The control scheme and operation of the locomotive are different from vanilla diesel and battery electric.

### Usage and spawning
The locomotive is currently set to appear at FF, SM, IME and CME. The parking sidings at SM and FF are unelectrified, so using another locomotive or the jogging mode is required there.

Electricity price in career is set to $10/kWh by default. It can be changed in mod options.

When spawning the locomotive in sandbox via the radio, make sure that units A and B are facing in the same direction and the arrow "front" of unit B is touching the arrows "back" of unit A. The order in which units are spawned doesn't matter: it's possible to bring in unit B first, then A and vice versa.

The debug version [Catenary-DC-debug.zip](https://github.com/Knsgf/Derail_Valley_electric_concept/releases/download/v2.2.1/Catenary-DC-debug.zip) includes an infinite power cheat, toggleable in mod settings. When enabled it allows the locomotive to run anywhere on the map. However it doesn't disable electricity costs.

### Other limitations
This mod is **incompatible** with VR and multiplayer, as the author doesn't have means to do proper tests in either. It also won't work properly with Double-Tracked Valley, as double tracked mainlines have to be redone manually.

Only 2 lines are electrified at the moment: SM-HB with a short FM-SM branch and CP-FF-IME/CME with a short downhill branch towards SM. The flat section between OWN and SM **is not electrified.** Most of HB, excluding C2-I and thorough lines in E and G yards is also **not electrified yet.**

The height of the locomotive causes it to clip through water standpipes and chutes at SM and IME. The collision with them is not enforced and players are free to ignore these.

The catenary has no hitboxes to both help with performance and permit players to throw switches obscured by poles via radio.

There are interior pop-ins when crossing units.

Gadgets which directly control a locomotive, such as amp limiter or overheat protector, are **not compatible**. Neither is "Start-up" function of the comms radio.

An installed inclinometer gadget shows 1-2° lean forward on a level track.

Some details, like physical horns, sand pipes, bogie centering devices and underframe air ducts, are missing.

Whistle levers can only be operated with a mouse.

For debugging purposes pantograph wear is tracked separately as a "mechanical powertrain". It'll be merged into electrical later.
