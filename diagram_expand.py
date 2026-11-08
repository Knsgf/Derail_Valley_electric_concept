import sys

expansion_start  = int(sys.argv[1])
expansion_length = int(sys.argv[2])
blank_fill  = " " * expansion_length
branch_fill = "-" * expansion_length
branch_sumbols = { "-", "*", "+", "@", ">", "<" , "#", "[", "]" }
circuit_file_name = r"C:\Users\Kf177\source\repos\we6\WE6SIM\unit_A\traction_circuit.cs"

lines      = []
definition = False
start_line = end_line = None
with open(circuit_file_name, "rt", encoding = "utf-8") as circuit_file:
    for line in circuit_file:
        lines.append(line)
        if line.find("circuit_diagram") >= 0:
            definition = True
        elif definition and line.find('"""') >= 0:
            if start_line is None:
                start_line = len(lines)
            else:
                end_line   = len(lines) - 1
                definition = False

for line_index in range(start_line, end_line):
    line = lines[line_index]
    if expansion_start >= len(line):
        continue
    if line[expansion_start] in branch_sumbols:
        lines[line_index] = line[:expansion_start] + branch_fill + line[expansion_start:]
    else:
        lines[line_index] = line[:expansion_start] +  blank_fill + line[expansion_start:]

circuit_file = open(circuit_file_name, "wt")

with open(circuit_file_name, "wt", encoding = "utf-8") as circuit_file:
    for line in lines:
        circuit_file.write(line)
