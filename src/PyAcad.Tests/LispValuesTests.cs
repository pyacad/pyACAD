using System.Collections.Generic;
using System.Linq;
using PyAcad.Runtime.Execution;
using Xunit;

namespace PyAcad.Tests
{
    public class LispValuesTests
    {
        private static string Flat(IEnumerable<LispValues.Typed> values) =>
            string.Join(" ", values.Select(v => v.ToString()));

        [Fact]
        public void Scalars_encode_to_their_lisp_types()
        {
            Assert.Equal("5005:hi", Flat(LispValues.Encode("hi")));
            Assert.Equal("5010:7", Flat(LispValues.Encode(7)));
            Assert.Equal("5010:7", Flat(LispValues.Encode(7L)));
            Assert.Equal("5001:2.5", Flat(LispValues.Encode(2.5)));
            Assert.Equal("5021:", Flat(LispValues.Encode(true)));
            Assert.Equal("5019:", Flat(LispValues.Encode(false)));
            Assert.Equal("5019:", Flat(LispValues.Encode(null)));
        }

        [Fact]
        public void A_list_is_bracketed_and_nested_lists_nest()
        {
            var shape = new List<object> { 1, "a", new List<object> { 2.0, null } };
            Assert.Equal("5016: 5010:1 5005:a 5016: 5001:2 5019: 5017: 5017:", Flat(LispValues.Encode(shape)));
        }

        [Fact]
        public void A_dictionary_becomes_an_association_list()
        {
            var shape = new Dictionary<string, object> { ["count"] = 3, ["name"] = "x" };
            Assert.Equal("5016: 5016: 5005:count 5010:3 5017: 5016: 5005:name 5005:x 5017: 5017:",
                Flat(LispValues.Encode(shape)));
        }

        [Fact]
        public void Decode_turns_lisp_arguments_into_script_values()
        {
            var args = new[]
            {
                new LispValues.Typed(LispValues.Text, "layer"),
                new LispValues.Typed(LispValues.Int16, (short)4),
                new LispValues.Typed(LispValues.Double, 1.5),
                new LispValues.Typed(LispValues.Nil, null),
                new LispValues.Typed(LispValues.ListBegin, null),
                new LispValues.Typed(LispValues.Int32, 1),
                new LispValues.Typed(LispValues.Int32, 2),
                new LispValues.Typed(LispValues.ListEnd, null),
            };
            var decoded = LispValues.Decode(args);
            Assert.Equal("layer", decoded[0]);
            Assert.Equal(4, decoded[1]);
            Assert.Equal(1.5, decoded[2]);
            Assert.Null(decoded[3]);
            Assert.Equal(new object[] { 1, 2 }, Assert.IsType<List<object>>(decoded[4]));
        }
    }
}
