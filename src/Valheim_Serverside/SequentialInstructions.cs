using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Valheim_Serverside
{
	public class SequentialInstructions
	{
		private int num_found = 0;
		public List<CodeInstruction> Sequential;

		public SequentialInstructions(List<CodeInstruction> instructions)
		{
			this.Sequential = instructions;
		}

		public bool Check(CodeInstruction instruction)
		{
			for (int i = 0; i < Sequential.Count(); i += 1)
			{
				var expected = Sequential[i];
				var valid = false;

				if ((expected.opcode != null) && (expected.operand != null) && (expected.opcode == instruction.opcode) && (expected.operand == instruction.operand))
				{
					valid = true;
				}
				else if (expected.opcode != null && expected.operand == null && expected.opcode == instruction.opcode)
				{
					valid = true;
				}
				else if (expected.operand != null && expected.opcode == null && expected.operand == instruction.operand)
				{
					valid = true;
				}

				if (valid && i == num_found)
				{
					if (i == 0)
					{
						num_found = 1;
					}
					else
					{
						num_found = i + 1;
					}

					if (num_found == Sequential.Count())
					{
						return true;
					}
					return false;
				}
			}
			num_found = 0;
			return false;
		}
	}
}
