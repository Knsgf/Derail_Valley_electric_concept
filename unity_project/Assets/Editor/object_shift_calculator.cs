using UnityEngine;
using UnityEditor;

public class ObjectShiftCalculator: EditorWindow
{
    float   _quaternion_y;
    int     _x, _z;
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
        _x     = EditorGUILayout.IntField("Initial x", _x);
        _z     = EditorGUILayout.IntField("Initial z", _z);
        _shift = EditorGUILayout.Vector3Field("Shift along object axes", _shift);
        
        Quaternion orientation    = new Quaternion(0.0f, _quaternion_y, 0.0f, Mathf.Sqrt(1.0f - _quaternion_y * _quaternion_y)).normalized;
        Vector3    absolute_shift = orientation * _shift;
        EditorGUILayout.LabelField("New position");
        EditorGUILayout.SelectableLabel($"x: {Mathf.RoundToInt(_x + absolute_shift.x * 1000.0f)}");
        EditorGUILayout.SelectableLabel($"z: {Mathf.RoundToInt(_z + absolute_shift.z * 1000.0f)}");
    }
}
