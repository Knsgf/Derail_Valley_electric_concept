using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WE6SIM;

internal partial class unit_a_sim: IDisposable
{
	private static readonly string circuit_diagram =
"""
             *--#CP4#-------------------------*
             |                                |
   *--#CP1#--*--@PSR1>--*--@PSR2>--*--@PSR3>--*
   |                    |          |          |
   *--#CP2#-------------*          |          |
   |                               |          |
*--*--#CP3#------------------------*          |
|                                             |
|  *------------------------------------------*
|  |
|  *--#RR1.1#------------*
|  |                     |
|  *--#RF1.1#--*--@MA1>--*--#RF1.2#--*--@MF1>--*
|              |                     |         |
|              *------------#RR1.2#--*         |
|                                              |
*--<EPS@---------------------------------------*
""";

	private readonly Dictionary<string, float> _element_resistances = new()
	{
		["PSR1"] = 0.44f,
		["PSR2"] = 0.45f,
		["PSR3"] = 1.33f,
		["MA1"] = 0.033f * 0.65f,
		["MF1"] = 0.033f * 0.35f,
		["EPS"] = 0.3f,

		["CP1"] = 0.0f,
		["CP2"] = 0.0f,
		["CP3"] = 0.0f,
		["CP4"] = 0.0f,

		["RF1.1"] = 0.0f,
		["RF1.2"] = 0.0f,
		["RR1.1"] = 0.0f,
		["RR1.2"] = 0.0f,
	};
}
