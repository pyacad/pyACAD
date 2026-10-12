using System;
using System.Collections;
using System.Collections.Generic;

namespace PyAcad.Runtime.Execution
{
    /// <summary>
    /// The pure half of the AutoLISP bridge: a script's __result__ (as the JSON shapes
    /// the engines hand back) encoded as a flat list of typed LISP values, and LISP
    /// arguments decoded into plain .NET values for __args__. Type codes are AutoCAD's
    /// LispDataType numbers, written as ints so this file needs no AutoCAD assembly.
    /// </summary>
    public static class LispValues
    {
        public const short Double = 5001;
        public const short Int16 = 5003;
        public const short Text = 5005;
        public const short Point3d = 5009;
        public const short Int32 = 5010;
        public const short ListBegin = 5016;
        public const short ListEnd = 5017;
        public const short Nil = 5019;
        public const short True = 5021;

        /// <summary>One typed LISP value; Value is null for list markers, nil and T.</summary>
        public struct Typed
        {
            public short Code;
            public object Value;
            public Typed(short code, object value) { Code = code; Value = value; }
            public override string ToString() => Code + ":" + (Value ?? "");
        }

        /// <summary>
        /// Encodes a JSON shape (string, number, bool, null, list, dictionary). A
        /// dictionary becomes an association list of (key . value) pairs written as
        /// two-element lists, which LISP reads with assoc and cadr.
        /// </summary>
        public static List<Typed> Encode(object shape)
        {
            var output = new List<Typed>();
            Append(output, shape);
            return output;
        }

        private static void Append(List<Typed> output, object value)
        {
            switch (value)
            {
                case null:
                    output.Add(new Typed(Nil, null));
                    break;
                case bool b:
                    output.Add(new Typed(b ? True : Nil, null));
                    break;
                case string s:
                    output.Add(new Typed(Text, s));
                    break;
                case int i:
                    output.Add(new Typed(Int32, i));
                    break;
                case long l when l >= int.MinValue && l <= int.MaxValue:
                    output.Add(new Typed(Int32, (int)l));
                    break;
                case long l:
                    output.Add(new Typed(Double, (double)l));
                    break;
                case short sh:
                    output.Add(new Typed(Int32, (int)sh));
                    break;
                case float f:
                    output.Add(new Typed(Double, (double)f));
                    break;
                case double d:
                    output.Add(new Typed(Double, d));
                    break;
                case decimal m:
                    output.Add(new Typed(Double, (double)m));
                    break;
                case IDictionary dict:
                    output.Add(new Typed(ListBegin, null));
                    foreach (DictionaryEntry entry in dict)
                    {
                        output.Add(new Typed(ListBegin, null));
                        output.Add(new Typed(Text, entry.Key?.ToString() ?? ""));
                        Append(output, entry.Value);
                        output.Add(new Typed(ListEnd, null));
                    }
                    output.Add(new Typed(ListEnd, null));
                    break;
                case IEnumerable items:
                    output.Add(new Typed(ListBegin, null));
                    foreach (var item in items) Append(output, item);
                    output.Add(new Typed(ListEnd, null));
                    break;
                default:
                    output.Add(new Typed(Text, value.ToString()));
                    break;
            }
        }

        /// <summary>
        /// Decodes LISP arguments into the values a script sees in __args__: text stays
        /// text, integers become int, reals double, points a list of doubles, nil None.
        /// Nested lists are flattened one level into a list; deeper nesting is rare in
        /// a call and not worth the reader's time.
        /// </summary>
        public static List<object> Decode(IEnumerable<Typed> values)
        {
            var result = new List<object>();
            var stack = new Stack<List<object>>();
            var current = result;
            foreach (var typed in values)
            {
                switch (typed.Code)
                {
                    case ListBegin:
                        var list = new List<object>();
                        current.Add(list);
                        stack.Push(current);
                        current = list;
                        break;
                    case ListEnd:
                        if (stack.Count > 0) current = stack.Pop();
                        break;
                    case Nil:
                        current.Add(null);
                        break;
                    case True:
                        current.Add(true);
                        break;
                    case Int16:
                    case Int32:
                        current.Add(Convert.ToInt32(typed.Value));
                        break;
                    case Double:
                        current.Add(Convert.ToDouble(typed.Value));
                        break;
                    case Text:
                        current.Add(typed.Value?.ToString() ?? "");
                        break;
                    default:
                        current.Add(typed.Value);
                        break;
                }
            }
            return result;
        }
    }
}
