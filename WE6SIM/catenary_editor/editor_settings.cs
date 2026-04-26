// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityModManagerNet;

using WE6SIM.catenary;

namespace WE6SIM.catenary_editor;

internal class editor_settings: UnityModManager.ModSettings, IDrawable
{
    [Draw("Part placement")]
    private editor.placement _part_placement = editor.placement.Disabled;

    [Draw("Pole type")]
    private overhead_equipment.pole_kind _pole_type = overhead_equipment.pole_kind.Ground;

    [Draw("Skip first pole")]
	private bool _skip_first = false;

	[Draw("Pole vertical offset")]
	private float _height_offset = 0.0f;

	[Draw("Distance (m)")]
	private int _pole_distance = 40;

	[Draw("Sweep (mm)")]
	private int _sweep = 300;

	[Draw("Gantry stretch %")]
	private int _gantry_stretch = 100;

	[Draw("Registration arm type")]
	private overhead_equipment.cantilever_kind _cantilever_type = overhead_equipment.cantilever_kind.Middle;

	[Draw("Zigzag")]
	private bool _zigzag_arms = false;

	[Draw("Eraser")]
	private bool _erase_scenery = false;

	public editor_settings()
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
			_pole_type = overhead_equipment.pole_kind.Bracket;
		else if (_pole_type == overhead_equipment.pole_kind.Bracket)
			_pole_type = overhead_equipment.pole_kind.Ground;

		editor.pole_height_offset     = _height_offset;
		editor.pole_type			  = _pole_type;
		editor.part_placement         = _part_placement;
		editor.cantilever_type		  = _cantilever_type;
		editor.skip_first             = _skip_first;
		editor.distance_between_poles = _pole_distance;
		editor.maximum_sweep          = _sweep / 1000.0f;
		editor.erase_scenery          = _erase_scenery;
		editor.gantry_stretch         = _gantry_stretch / 100.0f;
		editor.zigzag                 = _zigzag_arms;
	}

	public void update_cantilever_type(overhead_equipment.cantilever_kind cantilever_type)
	{
		_cantilever_type = cantilever_type;
	}
}
