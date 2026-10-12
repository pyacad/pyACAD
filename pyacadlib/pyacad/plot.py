"""Plotting a layout to PDF, plot devices and their paper sizes.

to_pdf() plots in the foreground (BACKGROUNDPLOT is 0 for the duration) and
returns when the file is written. output_name() and scale_type() are pure.
Everything here is in AcCoreMgd and AcDbMgd, so it also runs in Core Console.
The AutoCAD API is imported inside each function.
"""

import os as _os  # loads ntpath first; importing ntpath alone is circular under IronPython
import ntpath as _ntpath

_BAD_CHARS = '<>:"/\\|?*'
_RATIOS = ('1:1', '1:2', '1:4', '1:5', '1:8', '1:10', '1:16', '1:20', '1:30', '1:40',
           '1:50', '1:100', '2:1', '4:1', '8:1', '10:1', '100:1', '1000:1')


def output_name(pattern, drawing, layout):
    """pattern with {drawing} (the file name without folder or extension) and
    {layout} filled in; characters Windows forbids in a file name become '_'."""
    stem = _ntpath.splitext(_ntpath.basename(str(drawing)))[0]
    return str(pattern).replace('{drawing}', _clean(stem)).replace('{layout}', _clean(layout))


def scale_type(scale):
    """The StdScaleType member name for 'fit' or a ratio such as '1:50'."""
    text = str(scale).replace(' ', '').lower()
    if text == 'fit':
        return 'ScaleToFit'
    if text in _RATIOS:
        left, right = text.split(':')
        return 'StdScale%sTo%s' % (left, right)
    raise ValueError("scale is 'fit' or one of %s, not %r." % (', '.join(_RATIOS), scale))


def devices():
    """Names of every plot device (.pc3 files and system printers)."""
    from pyacad import _api, doc
    doc.active()
    validator = _api.DatabaseServices.PlotSettingsValidator.Current
    return [str(name) for name in validator.GetPlotDeviceList()]


def papers(device):
    """Canonical paper size names the device offers."""
    from pyacad import _api, doc
    doc.active()
    ds = _api.DatabaseServices
    validator = ds.PlotSettingsValidator.Current
    settings = ds.PlotSettings(False)
    try:
        validator.SetPlotConfigurationName(settings, str(device), None)
        validator.RefreshLists(settings)
        return [str(name) for name in validator.GetCanonicalMediaNameList(settings)]
    finally:
        settings.Dispose()


def to_pdf(layout, path, page_setup=None, device='DWG To PDF.pc3', paper=None, scale='fit'):
    """Plots one layout to a file and returns its path. page_setup names a
    saved page setup to start from (else the layout's own settings); paper is
    a canonical paper name (else the current one, when the device has it)."""
    from pyacad import _api, doc, ents
    ds = _api.DatabaseServices
    ps = _api.PlottingServices
    scale_name = scale_type(scale)
    if ps.PlotFactory.ProcessPlotState != ps.ProcessPlotState.NotPlotting:
        raise RuntimeError('Another plot is in progress.')
    db = doc.database()
    target = str(path)
    background = doc.sysvar('BACKGROUNDPLOT')
    doc.set_sysvar('BACKGROUNDPLOT', 0)
    try:
        with doc.transaction() as tr:
            layouts = ents._open_id(tr, db.LayoutDictionaryId)
            if not layouts.Contains(str(layout)):
                raise ValueError('No layout is named %s.' % layout)
            sheet = ents._open_id(tr, layouts.GetAt(str(layout)))
            settings = ds.PlotSettings(sheet.ModelType)
            try:
                settings.CopyFrom(_source(tr, db, sheet, page_setup))
                validator = ds.PlotSettingsValidator.Current
                media = settings.CanonicalMediaName
                validator.SetPlotConfigurationName(settings, str(device), None)
                validator.RefreshLists(settings)
                offered = [str(m) for m in validator.GetCanonicalMediaNameList(settings)]
                if paper is not None:
                    if str(paper) not in offered:
                        raise ValueError('%s has no paper named %s.' % (device, paper))
                    media = str(paper)
                if media in offered:
                    validator.SetPlotConfigurationName(settings, str(device), media)
                validator.SetPlotType(settings, ds.PlotType.Layout)
                validator.SetUseStandardScale(settings, True)
                validator.SetStdScaleType(settings, getattr(ds.StdScaleType, scale_name))
                validator.SetPlotCentered(settings, True)
                validator.SetPlotRotation(settings, ds.PlotRotation.Degrees000)
                info = ps.PlotInfo()
                info.Layout = sheet.ObjectId
                info.OverrideSettings = settings
                checker = ps.PlotInfoValidator()
                checker.MediaMatchingPolicy = ps.MatchingPolicy.MatchEnabled
                checker.Validate(info)
                engine = ps.PlotFactory.CreatePublishEngine()
                try:
                    engine.BeginPlot(None, None)
                    engine.BeginDocument(info, str(doc.active().Name), None, 1, True, target)
                    engine.BeginPage(ps.PlotPageInfo(), info, True, None)
                    engine.BeginGenerateGraphics(None)
                    engine.EndGenerateGraphics(None)
                    engine.EndPage(None)
                    engine.EndDocument(None)
                    engine.EndPlot(None)
                finally:
                    engine.Dispose()
            finally:
                settings.Dispose()
    finally:
        doc.set_sysvar('BACKGROUNDPLOT', int(background))
    return target


def _source(tr, db, sheet, page_setup):
    """The PlotSettings to copy: a named page setup, else the layout itself."""
    from pyacad import ents
    if page_setup is None:
        return sheet
    setups = ents._open_id(tr, db.PlotSettingsDictionaryId)
    if not setups.Contains(str(page_setup)):
        raise ValueError('No page setup is named %s.' % page_setup)
    return ents._open_id(tr, setups.GetAt(str(page_setup)))


def _clean(text):
    return ''.join('_' if c in _BAD_CHARS or ord(c) < 32 else c for c in str(text))
