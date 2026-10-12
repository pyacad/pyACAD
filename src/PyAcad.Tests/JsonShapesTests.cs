using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using PyAcad.Shared;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// JsonShapes turns System.Text.Json into the plain shapes JavaScriptSerializer used
    /// to hand back, which config, secrets, update state and the AI providers all walk.
    /// </summary>
    public class JsonShapesTests
    {
        [Fact]
        public void Objects_Become_StringObjectDictionaries_AndArrays_ArrayLists()
        {
            var map = JsonShapes.ParseObject("{\"a\": {\"b\": [1, \"two\", {\"c\": null}]}}");

            var inner = Assert.IsType<Dictionary<string, object>>(map["a"]);
            var list = Assert.IsType<ArrayList>(inner["b"]);
            Assert.Equal(3, list.Count);
            Assert.Equal(1, list[0]);
            Assert.Equal("two", list[1]);
            var last = Assert.IsType<Dictionary<string, object>>(list[2]);
            Assert.True(last.ContainsKey("c"));
            Assert.Null(last["c"]);
        }

        [Fact]
        public void Numbers_Keep_TheOldTypes_IntThenLongThenDecimal()
        {
            var map = JsonShapes.ParseObject(
                "{\"i\": 3, \"l\": 5000000000, \"d\": 1.50, \"e\": 1e300, \"huge\": 1e400}");

            Assert.IsType<int>(map["i"]);
            Assert.IsType<long>(map["l"]);
            Assert.Equal(1.50m, Assert.IsType<decimal>(map["d"]));
            Assert.Equal(1e300, Assert.IsType<double>(map["e"]));
            Assert.Equal("1e400", map["huge"]);
        }

        [Fact]
        public void Booleans_And_Strings_Are_Themselves()
        {
            var map = JsonShapes.ParseObject("{\"t\": true, \"f\": false, \"s\": \"caf\\u00e9\"}");

            Assert.Equal(true, map["t"]);
            Assert.Equal(false, map["f"]);
            Assert.Equal("café", map["s"]);
        }

        [Fact]
        public void ParseObject_OfNull_IsNull_AndOfAnArray_Throws()
        {
            Assert.Null(JsonShapes.ParseObject("null"));
            Assert.Throws<InvalidOperationException>(() => JsonShapes.ParseObject("[1]"));
        }

        [Fact]
        public void Malformed_Text_Throws_AJsonException()
        {
            Assert.ThrowsAny<JsonException>(() => JsonShapes.Parse("{\"a\": "));
        }

        [Fact]
        public void HandEditedConfig_WithACommentAndATrailingComma_StillParses()
        {
            var map = JsonShapes.ParseObject("{\n  // mine\n  \"theme\": \"dark\",\n}");

            Assert.Equal("dark", map["theme"]);
        }

        [Fact]
        public void Serialize_WritesOneLine_AndRoundTripsTheShapes()
        {
            var value = new Dictionary<string, object>
            {
                ["name"] = "Größe \"quoted\"",
                ["n"] = 42,
                ["off"] = null,
                ["list"] = new ArrayList { 1, "x", true },
                ["arr"] = new[] { "a", "b" },
                ["nested"] = new Dictionary<string, object> { ["k"] = 1.5m },
            };

            var json = JsonShapes.Serialize(value);

            Assert.DoesNotContain("\n", json);
            Assert.Contains("Größe", json);
            var back = JsonShapes.ParseObject(json);
            Assert.Equal("Größe \"quoted\"", back["name"]);
            Assert.Equal(42, back["n"]);
            Assert.Null(back["off"]);
            Assert.Equal(new object[] { 1, "x", true }, ((ArrayList)back["list"]).ToArray());
            Assert.Equal(new object[] { "a", "b" }, ((ArrayList)back["arr"]).ToArray());
            Assert.Equal(1.5m, ((Dictionary<string, object>)back["nested"])["k"]);
        }

        [Fact]
        public void Serialize_Null_IsTheJsonNull()
        {
            Assert.Equal("null", JsonShapes.Serialize(null));
        }
    }
}
