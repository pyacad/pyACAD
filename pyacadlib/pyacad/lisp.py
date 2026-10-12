"""AutoLISP from python: call a LISP function, queue an expression, read the
arguments a (pyacad ...) call passed in.

call() goes through Application.Invoke (acedInvoke), which reaches only
functions LISP has exposed: commands defined as C:NAME and functions
registered with (vl-acad-defun 'name). Arbitrary expressions cannot be
evaluated synchronously from .NET; evaluate() queues one on the command line
and returns None. Python functions cannot be registered as LISP functions at
run time: a script is reachable from LISP as (pyacad "PYNAME" args...), which
hands the arguments to the script as args() and returns its __result__.
encode() and decode() are pure.
"""

NIL = 5019
T = 5021
TEXT = 5005
INT16 = 5003
INT32 = 5010
DOUBLE = 5001
POINT2D = 5002
POINT3D = 5009
LIST_BEGIN = 5016
LIST_END = 5017
DOTTED_PAIR = 5018
VOID = 5014
ENAME = 5006


def encode(value):
    """A python value as (LISP type code, value) pairs: None and False are nil,
    True is T, lists and tuples are lists, a dict is a list of (key value)."""
    output = []
    _append(output, value)
    return output


def decode(pairs):
    """(type code, value) pairs back into python values: a list with one item
    per top-level LISP value. Points arrive as tuples, nil as None, T as True."""
    result = []
    stack = []
    current = result
    for code, value in pairs:
        code = int(code)
        if code == LIST_BEGIN:
            nested = []
            current.append(nested)
            stack.append(current)
            current = nested
        elif code in (LIST_END, DOTTED_PAIR):
            if stack:
                current = stack.pop()
        elif code == NIL:
            current.append(None)
        elif code == T:
            current.append(True)
        elif code in (INT16, INT32):
            current.append(int(value))
        elif code == DOUBLE:
            current.append(float(value))
        elif code == TEXT:
            current.append('' if value is None else str(value))
        elif code == VOID:
            continue
        else:
            current.append(value)
    return result


def call(function, *args):
    """Calls an exposed LISP function (C:NAME or one registered with
    vl-acad-defun) and returns its result as python values."""
    from pyacad import _api, doc
    import System
    ds = _api.DatabaseServices
    doc.active()
    values = [ds.TypedValue(TEXT, str(function))]
    for code, value in [p for a in args for p in encode(a)]:
        if value is None:
            values.append(ds.TypedValue(code))
        elif code == INT32:
            values.append(ds.TypedValue(code, System.Int32(value)))
        elif code == DOUBLE:
            values.append(ds.TypedValue(code, System.Double(value)))
        else:
            values.append(ds.TypedValue(code, value))
    buffer = ds.ResultBuffer(System.Array[ds.TypedValue](values))
    try:
        reply = _api.Application.Invoke(buffer)
    finally:
        buffer.Dispose()
    if reply is None:
        return None
    try:
        pairs = [(int(v.TypeCode), _plain(v.TypeCode, v.Value)) for v in reply]
    finally:
        reply.Dispose()
    decoded = decode(pairs)
    if not decoded:
        return None
    return decoded[0] if len(decoded) == 1 else decoded


def evaluate(expression):
    """Queues a LISP expression on the command line; it runs after the script
    returns, so the result is not available and this returns None."""
    from pyacad import doc
    text = str(expression).strip()
    if not text.startswith('('):
        text = '(' + text + ')'
    doc.send_command(text)
    return None


def args():
    """The arguments of the (pyacad "PYNAME" args...) call that ran this script."""
    from pyacad import script
    return script.get_args()


def _append(output, value):
    if value is None or value is False:
        output.append((NIL, None))
    elif value is True:
        output.append((T, None))
    elif isinstance(value, str):
        output.append((TEXT, value))
    elif isinstance(value, int):
        if -2147483648 <= value <= 2147483647:
            output.append((INT32, value))
        else:
            output.append((DOUBLE, float(value)))
    elif isinstance(value, float):
        output.append((DOUBLE, value))
    elif isinstance(value, dict):
        output.append((LIST_BEGIN, None))
        for key in value:
            output.append((LIST_BEGIN, None))
            output.append((TEXT, str(key)))
            _append(output, value[key])
            output.append((LIST_END, None))
        output.append((LIST_END, None))
    elif isinstance(value, (list, tuple)):
        output.append((LIST_BEGIN, None))
        for item in value:
            _append(output, item)
        output.append((LIST_END, None))
    else:
        output.append((TEXT, str(value)))


def _plain(code, value):
    """A LISP reply value as python: points as tuples, entity names as handles."""
    code = int(code)
    if code == POINT3D:
        return (float(value.X), float(value.Y), float(value.Z))
    if code == POINT2D:
        return (float(value.X), float(value.Y))
    if code == ENAME:
        return str(value.Handle.ToString())
    return value
