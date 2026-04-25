// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityModManagerNet;
using WE6SIM.catenary;
using WE6SIM.catenary_editor;

namespace WE6SIM;

internal class test_settings: UnityModManager.ModSettings, IDrawable
{
	[Draw("Pole vertical offset")]
	private float _height_offset = 0.0f;

    [Draw("Pole type")]
    private catenary_visual.pole_kind _pole_type = catenary_visual.pole_kind.Ground;

	[Draw("Registration arm type")]
	private catenary_visual.cantilever_kind _cantilever_type = catenary_visual.cantilever_kind.Middle;

    [Draw("Part placement")]
    private editor.placement _part_placement = editor.placement.Disabled;

    [Draw("Skip first pole")]
	private bool _skip_first = false;

	[Draw("Distance (m)")]
	private int _pole_distance = 40;

	[Draw("Sweep (mm)")]
	private int _sweep = 300;

	[Draw("Eraser")]
	private bool _erase_scenery = false;

	[Draw("Gantry stretch %")]
	private int _gantry_stretch = 100;

	public test_settings()
	{
		Main.log("Settings set");
		OnChange();
	}
	
	public void OnChange()
	{
		if (_erase_scenery && _part_placement != editor.placement.Disabled)
		{
			_erase_scenery  = false;
			_part_placement = editor.placement.Disabled;
		}
		if (_part_placement == editor.placement.Bracket)
			_pole_type = catenary_visual.pole_kind.Bracket;
		else if (_pole_type == catenary_visual.pole_kind.Bracket)
			_pole_type = catenary_visual.pole_kind.Ground;

		editor.pole_height_offset     = _height_offset;
		editor.pole_type			  = _pole_type;
		editor.part_placement         = _part_placement;
		editor.cantilever_type		  = _cantilever_type;
		editor.skip_first             = _skip_first;
		editor.distance_between_poles = _pole_distance;
		editor.maximum_sweep          = _sweep / 1000.0f;
		editor.erase_scenery          = _erase_scenery;
		editor.gantry_stretch         = _gantry_stretch / 100.0f;
	}

	public void reset_placement_mode()
	{
		_part_placement = editor.part_placement = editor.placement.Disabled;
	}
}
