// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityModManagerNet;

using WE6SIM.catenary_editor;

namespace WE6SIM;

internal class test_settings: UnityModManager.ModSettings, IDrawable
{
	[Draw("Pole vertical offset")]
	private float _height_offset = 0.0f;

	[Draw("Pole placement")]
	private editor.placement _pole_placement = editor.placement.left;

	[Draw("Automatic poles")]
	private bool _automatic_pole_placement = false;

	[Draw("Distance")]
	private float _pole_distance = 40.0f;

	[Draw("Sweep")]
	private float _sweep = 0.3f;

	public test_settings()
	{
		Main.log("Settings set");
		OnChange();
	}
	
	public void OnChange()
	{
		editor.pole_height_offset     = _height_offset;
		editor.pole_placement         = _pole_placement;
		editor.auto_pole_placement    = _automatic_pole_placement;
		editor.distance_between_poles = _pole_distance;
		editor.maximum_sweep          = _sweep;
	}
}
