using System;
using PyAcad.Runtime.Bundles;
using Xunit;

namespace PyAcad.Tests
{
    public class ContextRuleTests
    {
        private static bool Eval(string text, params string[] truths)
        {
            var set = new System.Collections.Generic.HashSet<string>(truths, StringComparer.OrdinalIgnoreCase);
            return ContextRule.Parse(text).Evaluate(set.Contains);
        }

        [Fact]
        public void Single_Condition()
        {
            Assert.True(Eval("doc", "doc"));
            Assert.False(Eval("doc"));
        }

        [Fact]
        public void And_Requires_Both()
        {
            Assert.True(Eval("doc & selection", "doc", "selection"));
            Assert.False(Eval("doc & selection", "doc"));
        }

        [Fact]
        public void Or_Requires_Either()
        {
            Assert.True(Eval("xrefs | layouts", "layouts"));
            Assert.False(Eval("xrefs | layouts"));
        }

        [Fact]
        public void Not_Negates()
        {
            Assert.True(Eval("doc & !selection", "doc"));
            Assert.False(Eval("doc & !selection", "doc", "selection"));
        }

        [Fact]
        public void And_Binds_Tighter_Than_Or()
        {
            // a | b & c  ==  a | (b & c)
            Assert.True(Eval("doc | selection & multi-doc", "doc"));
            Assert.False(Eval("doc | selection & multi-doc", "selection"));
            Assert.True(Eval("doc | selection & multi-doc", "selection", "multi-doc"));
        }

        [Fact]
        public void Whitespace_And_Glued_Operators_Both_Parse()
        {
            Assert.True(Eval("doc&selection", "doc", "selection"));
            Assert.True(Eval("  doc  |  selection  ", "selection"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("doc &")]
        [InlineData("& doc")]
        [InlineData("doc doc")]
        [InlineData("doc ? selection")]
        public void Malformed_Throws(string text) =>
            Assert.Throws<FormatException>(() => ContextRule.Parse(text));

        [Fact]
        public void KnownConditions_Lists_The_Eight()
        {
            Assert.Equal(
                new[] { "doc", "selection", "modelspace", "paperspace", "xrefs", "blocks", "layouts", "multi-doc" },
                ContextRule.KnownConditions);
        }

        [Fact]
        public void Every_Known_Condition_Has_An_Evaluator_And_None_Is_Live_Yet()
        {
            Assert.Equal(ContextRule.KnownConditions.Length, PyAcad.Runtime.Ribbon.ContextConditions.Evaluators.Count);
            foreach (var name in ContextRule.KnownConditions)
                Assert.True(PyAcad.Runtime.Ribbon.ContextConditions.Evaluators.ContainsKey(name), name);
            Assert.Empty(PyAcad.Runtime.Ribbon.ContextConditions.Snapshot());
        }
    }
}
