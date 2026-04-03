using HarmonyLib;
using LocoSim.Implementations;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;
using static UnityModManagerNet.UnityModManager;

namespace WE6SIM;

public struct catenary_objects
{
	public GameObject pole;
}

public static class Main
{
	// Unity Mod Manage Wiki: https://wiki.nexusmods.com/index.php/Category:Unity_Mod_Manager

	private static ModEntry.ModLogger? _logger;
	private static test_settings?      _settings;
	private static catenary_objects _catenary_parts;

	public static Port? diagnostics { get; set; }
	public static Port? diagnostics2 { get; set; }
	public static catenary_objects catenary_parts => _catenary_parts;
	public static float pole_height_offset { get; set; }

	public static void log(string message)
	{
		_logger?.Log(message);
	}

	private static bool Load(ModEntry mod)
	{
		Harmony? code_injector = null;

		_logger = mod.Logger;
		try
		{
			code_injector = new Harmony(mod.Info.Id);
			code_injector.PatchAll(Assembly.GetExecutingAssembly());

			// Other plugin startup logic

			AssetBundle catenary_assets = AssetBundle.LoadFromFile(Path.Combine(mod.Path, "catenary"))
				?? throw new FileNotFoundException("Not found " + Path.Combine(mod.Path, "catenary"));
			string[] all_assets = catenary_assets.GetAllAssetNames();
			foreach (string name in all_assets)
				log(name);
			_catenary_parts.pole = catenary_assets.LoadAsset<GameObject>("assets/_ccl_cars/catenary/poleinner.prefab")
				?? throw new FileNotFoundException("No pole prefab");

			_settings = test_settings.Load<test_settings>(mod);
			mod.OnGUI = show_test_configuration;

			log("WE6SIM started");
		}
		catch (Exception ex)
		{
			_logger.LogException($"Failed to load {mod.Info.DisplayName}:", ex);
			code_injector?.UnpatchAll(mod.Info.Id);
			return false;
		}

		return true;
	}

	private static void show_test_configuration(ModEntry mod)
	{
		_settings.Draw(mod);
	}
}
