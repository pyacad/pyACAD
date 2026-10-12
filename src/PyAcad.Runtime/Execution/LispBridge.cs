using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.DatabaseServices;
using PyAcad.Shared;

namespace PyAcad.Runtime.Execution
{
    /// <summary>
    /// The host half of the AutoLISP bridge. The loader defines <c>(pyacad name-or-path
    /// args...)</c> and hands its ResultBuffer here; the first argument names a script
    /// file or a pyACAD command, the rest become __args__, and the script's __result__
    /// goes back as LISP data (nil when it set none or failed, with the failure logged).
    /// </summary>
    public static class LispBridge
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object Call(ResultBuffer arguments)
        {
            var typed = new List<LispValues.Typed>();
            if (arguments != null)
                foreach (TypedValue value in arguments)
                    typed.Add(new LispValues.Typed(value.TypeCode, value.Value));
            var decoded = LispValues.Decode(typed);
            if (decoded.Count == 0 || !(decoded[0] is string target))
            {
                Log.Error("(pyacad ...) needs a script path or command name as its first argument.");
                return null;
            }
            decoded.RemoveAt(0);

            var outcome = RuntimeHost.Invoke(target, decoded);
            if (!outcome.Succeeded)
            {
                Log.Error("(pyacad \"" + target + "\") failed: " + outcome.ErrorText);
                return null;
            }
            if (outcome.ResultJson == null) return null;

            object shape;
            try { shape = JsonShapes.Parse(outcome.ResultJson); }
            catch (Exception ex)
            {
                Log.Error("(pyacad \"" + target + "\"): __result__ is not JSON", ex);
                return null;
            }
            var encoded = LispValues.Encode(shape);
            // A single scalar goes back bare; anything else is a list the caller walks.
            if (encoded.Count == 1)
            {
                var only = encoded[0];
                if (only.Code == LispValues.Nil) return null;
                if (only.Code == LispValues.True) return true;
                return only.Value;
            }
            var buffer = new ResultBuffer();
            foreach (var item in encoded)
                buffer.Add(new TypedValue(item.Code, item.Value));
            return buffer;
        }
    }
}
