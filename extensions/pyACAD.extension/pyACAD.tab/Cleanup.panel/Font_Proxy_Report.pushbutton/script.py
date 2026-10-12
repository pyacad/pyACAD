# -*- coding: utf-8 -*-
"""Reports the proxies in the drawing and the fonts its text styles name that
AutoCAD cannot find, then offers an Excel export and a font remap.

A proxy is an object made by software this AutoCAD does not have (an object
enabler is missing), so the report groups them by the class and application
that made them. Fonts are looked up the way AutoCAD looks for them: the
support paths, the drawing's folder and, for TrueType, the Windows fonts.
Big fonts and shape files are checked too. The remap points a style's own
font at a replacement (simplex.shx or arial.ttf unless you type another);
big fonts and shape files are reported but left alone.
"""

import os

from pyacad import audit, doc, forms, script, toast, xl

__title__ = 'Font and Proxy Report'

TITLE = 'Font and Proxy Report'
EXPORT = 'Export to Excel'
REMAP = 'Remap missing fonts'
DONE = 'Done'


def has_drawing():
    try:
        doc.active()
    except RuntimeError:
        return False
    return True


def default_name():
    path = script.get_document_path() or 'untitled'
    stem = os.path.splitext(os.path.basename(path))[0] or 'untitled'
    return stem + ' font and proxy report.xlsx'


def export(details, missing):
    """Writes the report to a workbook the person picks; True once written."""
    path = forms.save_file('Excel workbooks (*.xlsx)|*.xlsx', default_name(),
                           'Export the font and proxy report')
    if path is None:
        return False                                    # cancelled: say nothing
    try:
        # The save dialog already asked about replacing the file, and xl.write
        # would otherwise add a sheet to the old workbook instead.
        if os.path.exists(path):
            os.remove(path)
        xl.write(path, audit.font_report_rows(details, missing),
                 headers=audit.FONT_REPORT_HEADERS, sheet='Fonts and proxies')
    except Exception as error:
        script.get_logger().error('export to %s failed: %s' % (path, error))
        toast.error('Could not write the workbook', str(error))
        return False
    toast.success('Report exported', path)
    return True


def styles_text(styles):
    """'2 styles (Notes, Title)', at most five names."""
    names = ', '.join(styles[:5])
    if len(styles) > 5:
        names += ' and %d more' % (len(styles) - 5)
    return '%d style%s (%s)' % (len(styles), '' if len(styles) == 1 else 's', names)


def remap(missing):
    """Asks for a replacement per kind of font (SHX, TrueType) and applies
    it; returns the styles changed."""
    found = audit.font_finder()
    changed = []
    for kind, default, styles in audit.remap_plan(missing):
        answer = forms.ask_string(
            'Replace the missing %s font of %s with:'
            % ('SHX' if kind == 'shx' else 'TrueType', styles_text(styles)),
            default, TITLE)
        if answer is None or not answer.strip():
            continue                                    # cancelled: leave this kind alone
        answer = answer.strip()
        if not found(answer) and not forms.confirm(
                '%s was not found in the support paths either, so the text would '
                'still show a substitute font.\n\nUse it anyway?' % answer, TITLE):
            continue
        for style in styles:
            try:
                audit.set_text_style_font(style, answer)
            except Exception as error:
                script.get_logger().error('remapping %s failed: %s' % (style, error))
                toast.error('Could not remap %s' % style, str(error))
            else:
                changed.append(style)
    return changed


def run():
    if not has_drawing():
        toast.warning('No drawing is open', 'Open a drawing, then run Font and Proxy Report')
        return

    details = audit.proxy_details()
    missing = audit.missing_fonts(audit.text_style_fonts(), audit.font_finder())
    if not details and not missing:
        toast.success('No proxies and no missing fonts',
                      'Every font the text styles use was found')
        return

    report = audit.format_font_report(details, missing)
    forms.alert(report, TITLE, copy=report)

    options = [EXPORT]
    if audit.remap_plan(missing):
        options.append(REMAP)
    while options:
        choice = forms.ask_options('What next?', options + [DONE], TITLE)
        if choice is None or choice == DONE:
            break
        if choice == EXPORT:
            if export(details, missing):
                options.remove(EXPORT)
        elif choice == REMAP:
            options.remove(REMAP)
            changed = remap(missing)
            if changed:
                doc.send_command('_.REGENALL')          # redraw text in the new font
                toast.success('%d text style%s remapped' % (len(changed), '' if len(changed) == 1 else 's'),
                              ', '.join(changed))


if '__commandpath__' in globals():
    run()
