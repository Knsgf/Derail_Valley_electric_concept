// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityModManagerNet;

namespace WE6SIM;

internal class test_settings: UnityModManager.ModSettings, IDrawable
{
	[Draw("Pole vertical offset")]
	private float height_offset = 0.0f;

	public void OnChange()
	{
		Main.log($"Height offset = {height_offset}");
		Main.pole_height_offset = height_offset;
	}
}
