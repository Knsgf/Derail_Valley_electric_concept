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
	private editor.placement _pole_placement = editor.placement.Disabled;

	[Draw("Skip first pole")]
	private bool _skip_first = false;

	[Draw("Distance")]
	private float _pole_distance = 40.0f;

	[Draw("Sweep")]
	private float _sweep = 0.3f;

	[Draw("Eraser")]
	private bool _erase_scenery = false;

	public test_settings()
	{
		Main.log("Settings set");
		OnChange();
	}
	
	public void OnChange()
	{
		if (_erase_scenery && _pole_placement != editor.placement.Disabled)
		{
			_erase_scenery  = false;
			_pole_placement = editor.placement.Disabled;
		}
		editor.pole_height_offset     = _height_offset;
		editor.pole_placement         = _pole_placement;
		editor.skip_first             = _skip_first;
		editor.distance_between_poles = _pole_distance;
		editor.maximum_sweep          = _sweep;
		editor.erase_scenery          = _erase_scenery;
	}

	public void reset_placement_mode()
	{
		_pole_placement = editor.pole_placement = editor.placement.Disabled;
	}
}
