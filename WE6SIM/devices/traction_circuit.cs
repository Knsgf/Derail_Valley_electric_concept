// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

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
                        *--#CR4#----------------------------*
                        |                                   |
            *--#CR1#----*--@SR1>----*--@SR2>----*--@SR3>----*
            |                       |           |           |
            *--#CR2#----------------*           |           |
            |                                   |           |
*--#LC1#----*--#CR3#----------------------------*           |
|                                                           |
|  *--------------------------------------------------------*
|  |
|  *--#RR1.1#---------------*
|  |                        |
|  *--#RF1.1#---*--@MA1>----*--#RF1.2#------*--@MF1>----*
|               |                           |           |
|               *--#RR1.2#------------------*           |
|                                                       |
*--<EPS@------------------------------------------------*
""";

	const int nrb = 3;
	private readonly Dictionary<string, float> _element_resistances = new()
	{
		["SR1"] = 1.1f / nrb,
		["SR2"] = 1.1f / nrb,
		["SR3"] = 3.6f / nrb,
		["MA1"] = 0.21f * 0.65f,
		["MF1"] = 0.21f * 0.35f,
		["EPS"] = 0.1f,

		["LC1"] = 0.0f,

		["CR1"] = 0.0f,
		["CR2"] = 0.0f,
		["CR3"] = 0.0f,
		["CR4"] = 0.0f,

		["RF1.1"] = 0.0f,
		["RF1.2"] = 0.0f,
		["RR1.1"] = 0.0f,
		["RR1.2"] = 0.0f,
	};

	private static readonly string _reverser_toggles =
"""
#  RF1.1 RF1.2 RR1.1 RR1.2
1 |===========|     |     |
2 |     |     |===========|
""";

	private static readonly string _primary_contactor_toggles =
"""
#  CR1 CR2 CR3 CR4
1 |===|   |   |   |
2 |=======|   |   |
3 |===========|   |
4 |   |   |===|   |
5 |   |   |=======|
6 |   |===========|
7 |===============|
""";
}
