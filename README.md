### 2WE3-981 - an electric locomotive proof of concept for Derail Valley
This is an experimental heavy freight electric locomotive mod for Derail Valley.

### Installation
1. Install either [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) or [DVModManager](https://www.nexusmods.com/derailvalley/mods/1637),
2. Install [Custom Car Loader](https://www.nexusmods.com/derailvalley/mods/324) (CCL) and its prerequisites,
3. Uninstall the following mods, if they're present: **WE6-981** and **WE6SIM**,
4. Drag and drop [Catenary-DC.zip](https://github.com/Knsgf/Derail_Valley_electric_concept/releases/download/v2.9.0/Catenary-DC.zip) into mod manager. This file contains overhead catenary, required for electric vehicles to run,
5. There are two versions of the locomotive, one requires buying a licence in game and the other is usable immediately without a licence. Choose either:
    * [2WE3CCL.zip](https://github.com/Knsgf/Derail_Valley_electric_concept/releases/download/v2.9.0/2WE3CCL.zip), which is no-licence version for sandbox mode or,
    * [2WE3CCL-licence.zip](https://github.com/Knsgf/Derail_Valley_electric_concept/releases/download/v2.9.0/2WE3CCL-licence.zip), whose licence costs $300k. Make sure to install [DV Custom Licences](https://github.com/WhistleWiz/dv-custom-licenses/releases) first.
    **NB!** Do not install both variations at the same time.
6. It's strongly recommended to read the [operating manual](https://github.com/Knsgf/Derail_Valley_electric_concept/releases/download/v2.9.0/2WE3_manual.pdf). The control scheme and operation of the locomotive are different from vanilla diesel and battery electric.

### Usage and spawning
The locomotive is set to appear at HB, FF, SM, IME and CME. The parking sidings at SM and FF and the roundhouse at HB are unelectrified, so using another locomotive or the jogging mode is required there.

Electricity price in career is set to $10/kWh by default. It can be changed in mod options.

When spawning the locomotive in sandbox via the radio, make sure that units A and B are facing in the same direction and the arrow "front" of unit B is touching the arrows "back" of unit A. The order in which units are spawned doesn't matter: it's possible to bring in unit B first, then A and vice versa.

There are 2 cheats, accessible in Ctrl+F10 mod options. The **Omnipresent electricity** permits electric vehicles to run anywhere on the map, regardless of catenary. The **Supply voltage override** allows rolling stock using an incompatible system to function, while still requiring a physical wire contact. Neither cheat turns off electricity costs.

### Other limitations
This mod is **incompatible** with VR and multiplayer, as the author doesn't have means to do proper tests in either. It also won't work with Double-Tracked Valley, as catenary doesn't automatically conform to the changed trackage and needs to be manually rebuilt from ground up to support it. To avoid confusion, the overhead catenary is removed whenever Double Track layout is active. While enabling power cheat permits usage of electric vehicles on the double-tracked map, it doesn't reinstate physical OCS.

The height of the locomotive causes it to clip through water standpipes and chutes at SM and IME. The collision with them is not enforced and players are free to ignore these.

The catenary has no hitboxes to both help with performance and permit players to throw switches obscured by poles via radio.

Gadgets which directly control a locomotive, such as amp limiter or overheat protector, are **not compatible**. Neither is "Start-up" function of the comms radio.

Some details, like physical horns, sand pipes, bogie centering devices and underframe air ducts, are missing.

### Acknowledgements
Special thanks to Kuu Lightwing and laheugan on Discord and "Yama Sama vt vods", "Морум та Дякула" and Yosuke Nakajima on YouTube for extensive testing and feedback.

The licence graphics and door physics tweaks has been contributed by LainesNitro.
