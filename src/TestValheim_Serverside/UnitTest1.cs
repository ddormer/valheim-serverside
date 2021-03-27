using Microsoft.VisualStudio.TestTools.UnitTesting;
using Valheim_Serverside;
using System;
using System.Collections.Generic;
using HarmonyLib;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace TestValheim_Serverside
{
    [TestClass]
    public class SequentialItemsTest
    {
        [TestMethod]
        public void TestSequential()
        {
            List<CodeInstruction> sequence = new List<CodeInstruction>();
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_0));
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_1, "A"));
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_2));
            var seq = new SequentialInstructions(sequence);
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_0)));
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_1, "A")));
            Assert.IsTrue(seq.Check(new CodeInstruction(OpCodes.Ldarg_2)));
        }
        [TestMethod]
        public void TestNotSequential()
        {
            List<CodeInstruction> sequence = new List<CodeInstruction>();
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_0));
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_1, "A"));
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_2));
            var seq = new SequentialInstructions(sequence);
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_0)));
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_1, "A")));
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_3)));
        }

        [TestMethod]
        public void TestNotSequentialThenSequential()
        {
            List<CodeInstruction> sequence = new List<CodeInstruction>();
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_0));
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_1));
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_2));
            var seq = new SequentialInstructions(sequence);
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_0)));
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_1)));
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_3)));

            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_0)));
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_1, "A")));
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_3)));
        }
        [TestMethod]
        public void TestSingleItem()
        {
            List<CodeInstruction> sequence = new List<CodeInstruction>();
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_0));
            var seq = new SequentialInstructions(sequence);
            Assert.IsTrue(seq.Check(new CodeInstruction(OpCodes.Ldarg_0)));
        }
        [TestMethod]
        public void TestMissingOperandInCheck()
        {
            List<CodeInstruction> sequence = new List<CodeInstruction>();
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_0));
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_1, "A"));
            sequence.Add(new CodeInstruction(OpCodes.Ldarg_2));
            var seq = new SequentialInstructions(sequence);
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_0)));
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_1)));
            Assert.IsFalse(seq.Check(new CodeInstruction(OpCodes.Ldarg_2)));
        }
    }
}
