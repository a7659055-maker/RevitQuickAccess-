using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitQuickAccess.Settings;
using RevitEx = Autodesk.Revit.Exceptions;

namespace RevitQuickAccess.Commands
{
    /// <summary>
    /// «Трубопровод по коннектору» — click a fitting exactly at the outlet you want the pipe to grow
    /// from; a pipe of a fixed length (settings: «Величина по коннектору») is built along THAT
    /// connector's own direction. Since it runs in line with the connector, no elbow is needed —
    /// it connects straight through. Clicking a pipe end simply extends that pipe.
    /// Pipe type / system are taken from the connected piping (walks through fittings).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConnectorPipeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;

            Reference r;
            try { r = uidoc.Selection.PickObject(ObjectType.Element, new PipeTools.MepFilter(), "По коннектору: кликни фитинг/трубу у нужного выхода"); }
            catch (RevitEx.OperationCanceledException) { return Result.Cancelled; }

            var el = doc.GetElement(r);
            XYZ pick = r?.GlobalPoint;
            if (el == null || pick == null) return Result.Cancelled;

            Connector start = PipeTools.FindConnector(el, pick);
            if (start == null) { message = "Не найден свободный коннектор рядом с точкой клика."; return Result.Failed; }

            XYZ dir = start.CoordinateSystem?.BasisZ;      // the direction this connector points
            if (dir == null) { message = "У коннектора не определено направление."; return Result.Failed; }

            double len = PluginSettings.ConnectorPipeMm / 304.8;

            string err = PipeTools.Grow(doc, uidoc.ActiveView, el, start, dir, Math.Abs(len),
                                        "Quick Access — трубопровод по коннектору");
            if (err != null) { message = err; return Result.Failed; }
            return Result.Succeeded;
        }
    }
}
