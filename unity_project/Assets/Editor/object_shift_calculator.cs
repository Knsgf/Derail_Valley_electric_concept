using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

public class ObjectShiftCalculator: EditorWindow
{
    static readonly Dictionary<int, float> _preset_offsets = new Dictionary<int, float>()
    {
        [1] =  4.40f,
        [2] =  8.86f,
        [3] = 13.26f,
        [4] = 17.56f
    };
    
    float   _quaternion_y, _rotation_angle, _preset_multiplier = 1.0f;
    int     _x, _z, _preset;
    Vector3 _shift;
    
    [MenuItem("Catenary/Object shift calculator")]
    static void init()
    {
        GetWindow<ObjectShiftCalculator>().Show();
    }

    void OnGUI()
    {
        _quaternion_y = Mathf.Clamp(EditorGUILayout.FloatField("Quaternion Y", _quaternion_y), -1.0f, 1.0f);
        EditorGUILayout.HelpBox("The calculator assumes quaternion W to be positive.\n" 
                              + "If W is negative, flip the sign of Y.", MessageType.Info);
        _x = EditorGUILayout.IntField("Initial x", _x);
        _z = EditorGUILayout.IntField("Initial z", _z);
        _preset            = EditorGUILayout.IntField  ("Offset preset (1-4, 0 off)", _preset           );
        _preset_multiplier = EditorGUILayout.FloatField(         "Preset multiplier", _preset_multiplier);
        if (_preset_offsets.TryGetValue(_preset, out float preset_offset))
            _shift = new Vector3(preset_offset * _preset_multiplier, 0.0f, 0.0f);
        else
        { 
            _preset            = 0; 
            _preset_multiplier = 1.0f;
        }
        _shift = EditorGUILayout.Vector3Field("Shift along object axes", _shift);
        
        Quaternion orientation    = new Quaternion(0.0f, _quaternion_y, 0.0f, Mathf.Sqrt(1.0f - _quaternion_y * _quaternion_y)).normalized;
        Vector3    absolute_shift = orientation * _shift;
        EditorGUILayout.LabelField("New position");
        EditorGUILayout.SelectableLabel($"x: {Mathf.RoundToInt(_x + absolute_shift.x * 1000.0f)}");
        EditorGUILayout.SelectableLabel($"z: {Mathf.RoundToInt(_z + absolute_shift.z * 1000.0f)}");

        _rotation_angle = EditorGUILayout.FloatField("Rotation Y", _rotation_angle);
        Quaternion new_orientation = orientation * Quaternion.Euler(0.0f, _rotation_angle, 0.0f);
        EditorGUILayout.LabelField("New orientation");
        EditorGUILayout.SelectableLabel($"y: {new_orientation.y}");
        EditorGUILayout.SelectableLabel($"w: {new_orientation.w}");
    }
}
