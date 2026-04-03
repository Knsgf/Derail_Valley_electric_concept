// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace WE6SIM.circuit_sim;

internal partial class sparse_matrix
{
    internal class linear_solver
    {
        private static float[]? __intermediate_vector;

        private readonly sparse_matrix _L = new(), _U = new();
        private float[]? _U_diagonal;
        private int[]? _row_map;

        public static void compute(sparse_matrix lower, float[] upper_diagonal, sparse_matrix upper, float[] results,
            int[] row_map, sparse_matrix right_side)
        {
            int size = lower._num_rows;
            Debug.Assert(size == lower._num_columns && size == upper._num_rows && size == upper._num_columns);
            Debug.Assert(size == upper_diagonal.Length && size == right_side._num_rows && right_side._num_columns == 1);
            Debug.Assert(size == results.Length && __intermediate_vector != null && size <= __intermediate_vector.Length);

            float[] temporary = __intermediate_vector!;
            temporary[0] = right_side[row_map[0], 0];
            Dictionary<int, float>[] lower_contents = lower._contents;
            for (int row = 1; row < size; ++row)
            {
                float result = right_side[row_map[row], 0];
                foreach (KeyValuePair<int, float> row_item in lower_contents[row])
                {
                    Debug.Assert(row_item.Key >= 0 && row_item.Key < row);
                    result -= row_item.Value * temporary[row_item.Key];
                }
                temporary[row] = result;
            }

            int last_row = size - 1;
            results[last_row] = (upper_diagonal[last_row] == 0.0f) ? 0.0f : (temporary[last_row] / upper_diagonal[last_row]);
            Dictionary<int, float>[] upper_contents = upper._contents;
            for (int row = last_row - 1; row >= 0; --row)
            {
                if (upper_diagonal[row] == 0.0f)
                    results[row] = 0.0f;
                else
                {
                    float result = temporary[row];
                    foreach (KeyValuePair<int, float> row_item in upper_contents[row])
                    {
                        Debug.Assert(row_item.Key > row && row_item.Key < size);
                        result -= row_item.Value * results[row_item.Key];
                    }
                    results[row] = result / upper_diagonal[row];
                }
            }
        }

        public linear_solver(sparse_matrix coeffs)
        {
            change_coeff_matrix(coeffs);
        }

        public void change_coeff_matrix(sparse_matrix coeffs)
        {
            coeffs.decompose_to(_L, ref _U_diagonal, _U, ref _row_map);
            if (__intermediate_vector == null || __intermediate_vector.Length < _U_diagonal.Length)
                __intermediate_vector = new float[_U_diagonal.Length];
        }

        public void solve(float[] results, sparse_matrix right_hand_side)
        {
            Debug.Assert(_U_diagonal != null && _row_map != null && __intermediate_vector != null);
            Debug.Assert(_U_diagonal!.Length <= __intermediate_vector!.Length);
            Debug.Assert(results.Length == _U_diagonal.Length);
            Debug.Assert(right_hand_side._num_rows == _U_diagonal.Length && right_hand_side._num_columns == 1);
            compute(_L, _U_diagonal, _U, results, _row_map, right_hand_side);
        }
    }
}
