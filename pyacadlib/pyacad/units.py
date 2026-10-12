"""Drawing units: the INSUNITS names, conversions through metres, and length
formatting (feet and inches for 'Inches' and 'Feet').

Everything but current() is pure. Unit names are the UnitsValue enum names
('Millimeters', 'Feet', ...) in any case.
"""

# UnitsValue enum value: (name, metres per unit, short label)
UNITS = {
    0: ('Undefined', None, ''),
    1: ('Inches', 0.0254, 'in'),
    2: ('Feet', 0.3048, 'ft'),
    3: ('Miles', 1609.344, 'mi'),
    4: ('Millimeters', 0.001, 'mm'),
    5: ('Centimeters', 0.01, 'cm'),
    6: ('Meters', 1.0, 'm'),
    7: ('Kilometers', 1000.0, 'km'),
    8: ('MicroInches', 2.54e-8, 'uin'),
    9: ('Mils', 2.54e-5, 'mil'),
    10: ('Yards', 0.9144, 'yd'),
    11: ('Angstroms', 1e-10, 'A'),
    12: ('Nanometers', 1e-9, 'nm'),
    13: ('Microns', 1e-6, 'um'),
    14: ('Decimeters', 0.1, 'dm'),
    15: ('Dekameters', 10.0, 'dam'),
    16: ('Hectometers', 100.0, 'hm'),
    17: ('Gigameters', 1e9, 'Gm'),
    18: ('Astronomical', 149597870700.0, 'au'),
    19: ('LightYears', 9460730472580800.0, 'ly'),
    20: ('Parsecs', 3.0856775814913673e16, 'pc'),
    21: ('USSurveyFeet', 1200.0 / 3937.0, 'ft'),
    22: ('USSurveyInch', 100.0 / 3937.0, 'in'),
    23: ('USSurveyYard', 3600.0 / 3937.0, 'yd'),
    24: ('USSurveyMile', 6336000.0 / 3937.0, 'mi'),
}


def name_of(value):
    """The unit name for an INSUNITS number."""
    if int(value) not in UNITS:
        raise ValueError('%r is not an INSUNITS value.' % (value,))
    return UNITS[int(value)][0]


def to_metres(value, unit_name):
    """value in the named unit, as metres."""
    return float(value) * _row(unit_name)[1]


def from_metres(value, unit_name):
    """value in metres, as the named unit."""
    return float(value) / _row(unit_name)[1]


def format_length(value, unit_name, precision=2):
    """A length for display: 2'-6.50" for Inches and Feet, '12.35 mm' for the
    rest, the bare number for Undefined."""
    name, metres, label = _row(unit_name, allow_undefined=True)
    precision = int(precision)
    if metres is None:
        return '%.*f' % (precision, float(value))
    if name not in ('Inches', 'Feet'):
        return '%.*f %s' % (precision, float(value), label)
    total = float(value) * (12.0 if name == 'Feet' else 1.0)
    sign = '-' if total < 0 and round(abs(total), precision) > 0 else ''
    total = round(abs(total), precision)
    feet = int(total // 12)
    inches = round(total - feet * 12, precision)
    if inches >= 12:
        feet += 1
        inches = round(inches - 12, precision)
    return '%s%d\'-%.*f"' % (sign, feet, precision, inches)


def current():
    """The active drawing's insertion units name."""
    from pyacad import doc
    return doc.units()


def _row(unit_name, allow_undefined=False):
    wanted = str(unit_name).strip().lower()
    for value in sorted(UNITS):
        row = UNITS[value]
        if row[0].lower() == wanted:
            if row[1] is None and not allow_undefined:
                raise ValueError('Undefined units have no length in metres.')
            return row
    raise ValueError('%r is not a unit name; use one of %s.'
                     % (unit_name, ', '.join(UNITS[v][0] for v in sorted(UNITS))))
