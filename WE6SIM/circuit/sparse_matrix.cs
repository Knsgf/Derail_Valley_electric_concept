// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using UnityEngine;

namespace electric_sim.circuit_sim;

internal partial class sparse_matrix
{
    const float EPSILON = 1.0E-5f;

    private readonly HashSet<int> _indices_set = [];

    private Dictionary<int, float>[] _contents = [];
    private int _num_rows = 0, _num_columns = 0, _last_row = -1;

    private void are_indices_in_range(int row, int column)
    {
        if (row < 0 || row >= _num_rows)
            throw new IndexOutOfRangeException((_num_rows > 0) ? $"Row {row} out of range (0..{_num_rows - 1})" : "Matrix has zero rows");
        if (column < 0 || column >= _num_columns)
            throw new IndexOutOfRangeException((_num_columns > 0) ? $"Column {column} out of range (0..{_num_columns - 1})" : "Matrix has zero columns");
    }

    public sparse_matrix(int rows = -1, int columns = -1)
    {
        clear(rows, columns);
    }

    public float this[int row, int column]
    {
        get
        {
            are_indices_in_range(row, column);
            _contents[row].TryGetValue(column, out float result);
            return result;
        }
        set
        {
            are_indices_in_range(row, column);
            if (value is <= -EPSILON or >= EPSILON)
                _contents[row][column] = value;
            else if (_contents[row].ContainsKey(column))
                    _contents[row].Remove(column);
        }
    }

    public void clear(int rows = -1, int columns = -1)
    {
        if (rows < 0)
            rows = _contents.Length;
        if (columns < 0)
            columns = rows;

        if (_contents.Length < rows)
        {
            _contents = new Dictionary<int, float>[rows];
            for (int index = rows - 1; index >= 0; --index)
                _contents[index] = [];
        }
        else
        {
            for (int index = Math.Max(_num_rows, rows) - 1; index >= 0; --index)
                _contents[index].Clear();
        }
        _num_rows    = rows;
        _last_row    = rows - 1;
        _num_columns = columns;
    }

    public void copy_from(sparse_matrix source)
    {
        clear(source._num_rows, source._num_columns);
        for (int row = _last_row;  row >= 0; --row)
        {
            Dictionary<int, float> row_ref = _contents[row];
            foreach (KeyValuePair<int, float> item in source._contents[row])
                row_ref[item.Key] = item.Value;
        }
    }

    public sparse_matrix negate_from(sparse_matrix source)
    {
        clear(source._num_rows, source._num_columns);
        for (int row = _last_row; row >= 0; --row)
        {
            Dictionary<int, float> row_ref = _contents[row];
            foreach (KeyValuePair<int, float> item in source._contents[row])
                row_ref[item.Key] = -item.Value;
        }
        return this;
    }

    public sparse_matrix transpose_from(sparse_matrix source)
    {
        clear(source._num_columns, source._num_rows);
        Dictionary<int, float>[] contents = _contents;
        for (int row = source._last_row; row >= 0; --row)
        {
            foreach (KeyValuePair<int, float> item in source._contents[row])
                contents[item.Key][row] = item.Value;
        }
        return this;
    }

    public void multiply(sparse_matrix left, sparse_matrix right)
    {
        if (left._num_columns != right._num_rows)
            throw new ArgumentException("Left operand's number of columns must match right operand's number of rows");

        clear(left._num_rows, right._num_columns);
        for (int row = _last_row; row >= 0; --row)
        {
            Dictionary<int, float> row_ref = _contents[row];
            foreach (KeyValuePair<int, float> vector_item in left._contents[row])
            {
                float left_multiplicand = vector_item.Value;
                foreach (KeyValuePair<int, float> right_item in right._contents[vector_item.Key])
                {
                    row_ref.TryGetValue(right_item.Key, out float value);
                    row_ref[right_item.Key] = value + left_multiplicand * right_item.Value;
                }
            }

            HashSet<int> indices = _indices_set;
            indices.Clear();
            foreach (KeyValuePair<int, float> row_item in row_ref)
            {
                if (row_item.Value is > -EPSILON and < EPSILON)
                    indices.Add(row_item.Key);
            }
            foreach (int index in indices)
                row_ref.Remove(index);
        }
    }

    // Performs LU deomposition with partial pivoting, splitting outputs into:
    // * lower triangular matrix, excluding main diagonal, which is implicitly all 1's,
    // * elements on upper triangular matrix diagonal,
    // * upper triangular matrix, excluding main diagonal, which is stored separately for perfomance reasons
    // * row permutation array
    //
    // Note that this is not a genuine LDU decompositon, as U matrix is not normalised to have identity diagonal
    public void decompose_to(sparse_matrix lower, [NotNull] ref float[]? upper_diagonal, sparse_matrix upper, [NotNull] ref int[]? row_permutation)
    {
        int size = _num_rows;
        if (size != _num_columns)
            throw new InvalidOperationException("Decomposition requires a square matrix");
        lower.clear(size);
        upper.copy_from(this);
        if (upper_diagonal == null || upper_diagonal.Length != size)
            upper_diagonal = new float[size];
        if (row_permutation == null || row_permutation.Length != size)
            row_permutation = new int[size];
        for (int row = _last_row; row >= 0; --row)
            row_permutation[row] = row;

        Dictionary<int, float>[] upper_contents = upper._contents, lower_contents = lower._contents;
        for (int row = 0; row < size; ++row)
        {
            int biggest_absolute_row = row;
            upper_contents[row].TryGetValue(row, out float element);
            float biggest_absolute_value = Mathf.Abs(element);
            for (int permute_row = row + 1; permute_row < size; ++permute_row)
            {
                upper_contents[permute_row].TryGetValue(row, out element);
                element = Mathf.Abs(element);
                if (biggest_absolute_value < element)
                {
                    biggest_absolute_value = element;
                    biggest_absolute_row   = permute_row;
                }
            }
            if (biggest_absolute_row != row)
            {
                ( upper_contents[row],  upper_contents[biggest_absolute_row]) = ( upper_contents[biggest_absolute_row],  upper_contents[row]);
                ( lower_contents[row],  lower_contents[biggest_absolute_row]) = ( lower_contents[biggest_absolute_row],  lower_contents[row]);
                (row_permutation[row], row_permutation[biggest_absolute_row]) = (row_permutation[biggest_absolute_row], row_permutation[row]);
            }

            Dictionary<int, float> upper_row_ref = upper_contents[row];
            bool non_zero_element = upper_row_ref.TryGetValue(row, out float diagonal_element);
            upper_diagonal[row] = diagonal_element;
            if (!non_zero_element)
                continue;
            upper_row_ref.Remove(row);
            for (int row_to_subtract_from = row + 1; row_to_subtract_from < size; ++row_to_subtract_from)
            {
                Dictionary<int, float> upper_sub_row_ref = upper_contents[row_to_subtract_from];
                non_zero_element = upper_sub_row_ref.TryGetValue(row, out float multiplier);
                if (!non_zero_element)
                    continue;
                multiplier /= diagonal_element;
                lower_contents[row_to_subtract_from][row] = multiplier;
                upper_sub_row_ref.Remove(row);
                foreach (KeyValuePair <int, float> row_item in upper_row_ref)
                {
                    int column = row_item.Key;
                    upper_sub_row_ref.TryGetValue(column, out float minuend);
                    upper_sub_row_ref[column] = minuend - multiplier * row_item.Value;
                    if (Mathf.Abs(upper_sub_row_ref[column]) < EPSILON)
                        upper_sub_row_ref.Remove(column);
                }
            }
        }
    }
}
