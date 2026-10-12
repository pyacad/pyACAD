"""Change the drawing inside the command (the orchestrator checks one-step undo)."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    from pyacad import doc
    from Autodesk.AutoCAD.DatabaseServices import Circle, BlockTable, BlockTableRecord, OpenMode
    from Autodesk.AutoCAD.Geometry import Point3d, Vector3d
    db = doc.database()
    with doc.transaction() as tr:
        bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead)
        ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite)
        for i in range(2):
            c = Circle(Point3d(100 + i * 50, 100, 0), Vector3d.ZAxis, 10)
            ms.AppendEntity(c)
            tr.AddNewlyCreatedDBObject(c, True)

    p.check('two circles appended under the command', True)

S.Probe('p04_modify', globals()).run(body)
