from pyacad import doc
from Autodesk.AutoCAD.DatabaseServices import Circle, BlockTable, BlockTableRecord, OpenMode
from Autodesk.AutoCAD.Geometry import Point3d, Vector3d
db = doc.database()
with doc.transaction() as tr:
    bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead)
    ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite)
    c = Circle(Point3d(0, 0, 0), Vector3d.ZAxis, 5)
    ms.AppendEntity(c)
    tr.AddNewlyCreatedDBObject(c, True)

print("added one circle")
