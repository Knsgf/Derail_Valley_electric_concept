using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

using Mono.Csv;

namespace electric_sim.utilities;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class CSV_column(int column_index): Attribute
{
    public int column_index = (column_index >= 0) ? column_index : throw new ArgumentOutOfRangeException("Column must be non-negative");
}

internal class CSV_struct<_type_> where _type_: struct
{
    private readonly _type_[] _row_structs;

    public int row_count => _row_structs.Length;
    
    private static object convert_field_type(string literal, Type field_type)
    {
        if (field_type == typeof(string))
            return literal.Trim();
        if (field_type == typeof(bool))
        {
            if (string.Equals(literal, "false", StringComparison.InvariantCultureIgnoreCase))
                return false;
            if (string.Equals(literal, "true", StringComparison.InvariantCultureIgnoreCase))
                return true;
            throw new ArgumentException($"Boolean value must be either true or false, got '{literal}'");
        }
        if (field_type == typeof(int))
        {
            if (int.TryParse(literal, NumberStyles.Integer, CultureInfo.InvariantCulture.NumberFormat, out int result))
                return result;
            throw new ArgumentException($"Integer value expected, got '{literal}'");
        }
        if (field_type == typeof(float))
        {
            if (float.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out float result))
                return result;
            throw new ArgumentException($"Decimal value expected, got '{literal}'");
        }
        throw new ArgumentException($"Unsupported type <{field_type}>");
    }
    
    public CSV_struct(string CSV_text)
    {
        FieldInfo[]                all_fields        = typeof(_type_).GetFields();
        int                        highest_index     = 0;
        Dictionary<int, FieldInfo> _CSV_column_types = [];
        foreach (FieldInfo current_field in all_fields)
        {
            if (Attribute.GetCustomAttribute(current_field, typeof(CSV_column), inherit: false) is CSV_column column_info)
            { 
                _CSV_column_types[column_info.column_index] = current_field; 
                highest_index = Math.Max(highest_index, column_info.column_index);
            }
        }

        using MemoryStream CSV_stream   = new(Encoding.UTF8.GetBytes(CSV_text));
        CsvFileReader      CSV_contents = new(CSV_stream);
        List<List<string>> table        = [];
        CSV_contents.ReadAll(table);
        int rows     = table.Count, column_count = highest_index + 1;
        _row_structs = new _type_[rows];
        for (int row = rows - 1; row > 0; --row)
        {
            if (table[row].Count != column_count)
                throw new ArgumentException($"Number of CSV columns ({table[row].Count}) in row {row} doesn't match number of fields ({column_count})");
            object new_struct = new _type_();
            foreach (KeyValuePair<int, FieldInfo> current_field in _CSV_column_types)
            {
                int column = current_field.Key;
                FieldInfo field_into = current_field.Value;
                try
                {
                    field_into.SetValue(new_struct, convert_field_type(table[row][column], field_into.FieldType));
                }
                catch (Exception error)
                {
                    Main.log($"Error parsing CSV at row {row}, column {column}\n{error.Message}\n{error.StackTrace}");
                    throw;
                }
            }
            _row_structs[row - 1] = (_type_) new_struct;
        }
    }

    public _type_ get_row(int row_index)
    {
        return (row_index <= _row_structs.Length) 
            ? _row_structs[row_index - 1] 
            : throw new ArgumentOutOfRangeException($"Attempt to get CSV row {row_index} past end {_row_structs.Length}");
    }
}
