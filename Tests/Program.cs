using System;
using System.Collections.Generic;
using System.Threading;

using electric_sim.circuit_sim;

using static electric_sim.circuit_sim.circuit;

namespace Tests
{
    internal class Program
    {
        const string test_diagram = 
            "\n"
            +  "       *----------------#C#-----*         \n"
            + @"      /                          \   " + "\n"
            + @"     /    *----*                  \  " + "\n"
            + @"    /     |    |                   \ " + "\n"
            +  "   *      |    |                    *     \n"
            + @"   |      *----+----*               |" + "\n"
            + @"   |           |     \     *--<R4@--*" + "\n"
            + @"   |           |      \    |         " + "\n"
            +  "*--+--@R1>--*--+--<R2@-+---*              \n"
            + @"|  |           |        \  |         " + "\n"
            +  "|  *--@R6>-----*         * *--@R3>--*     \n"
            +  "|                        |          |     \n"
            +  "*------<B@---------------*----[R5]--*     \n";
        static readonly Dictionary<string, float> _resistances = new Dictionary<string, float>()
        {
            ["B" ] = 1.0f,
            ["R1"] = 1.0f,
            ["R2"] = 2.0f,
            ["R3"] = 1.0f,
            ["R4"] = 1.5f,
            ["R5"] = 1.0f,
            ["R6"] = 0.5f
        };

        static circuit _test_circuit;
        static Dictionary<string, branch_user> _named_branches, _contactor_locations;
        static object _dummy = new object();

        static void print_currents()
        {
            _test_circuit.simulate();
            Thread.Sleep(500);
            _test_circuit.simulate();
            Thread.Sleep(500);
            _test_circuit.simulate();
            Thread.Sleep(500);
            foreach (KeyValuePair<string, branch_user> current_branch in _named_branches)
                Console.WriteLine($"{current_branch.Key}={current_branch.Value.current}");
            Console.WriteLine();
        }

        static void Main(string[] args)
        {
#if !CIRCUIT_TEST_OFF
            _test_circuit = circuit_compiler.trace(_resistances, test_diagram).set_up_simulation(out _named_branches, out _contactor_locations, _dummy);
            _named_branches["B"].EMF = 6.0f;
            _contactor_locations["C"].toggle_contactor("C", false);
            print_currents();
            _named_branches["B"].EMF = 5.0f;
            _contactor_locations["C"].toggle_contactor("C", true);
            print_currents();
#endif

#if !MOTOR_TEST_OFF
            var test_rig = new TM_rig(250.0f, 6.0f, 0.0f, 0, false);
            test_rig.run();
#endif
        }
    }
}
