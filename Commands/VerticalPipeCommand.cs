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
    /// «Стояк» — grows a VERTICAL pipe of a fixed length (settings: «Величина стояка») from the
    /// connector you click near. A pipe that already runs vertically is simply extended.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VerticalPipeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;

            Reference r;
            try { r = uidoc.Selection.PickObject(ObjectType.Element, new PipeTools.MepFilter(), "Стояк: кликни трубу/фитинг возле нужного конца"); }
            catch (RevitEx.OperationCanceledException) { return Result.Cancelled; }

            var el = doc.GetElement(r);
            XYZ pick = r?.GlobalPoint;
            if (el == null || pick == null) return Result.Cancelled;

            Connector start = PipeTools.FindConnector(el, pick);
            if (start == null) { message = "Не найден свободный коннектор рядом с точкой клика."; return Result.Failed; }

            double len = PluginSettings.VerticalPipeMm / 304.8;
            XYZ dir = new XYZ(0, 0, PluginSettings.VerticalPipeUp ? 1 : -1);

            string err = PipeTools.Grow(doc, uidoc.ActiveView, el, start, dir, Math.Abs(len),
                                        "Quick Access — вертикальный трубопровод");
            if (err != null) { message = err; return Result.Failed; }
            return Result.Succeeded;
        }
    }
}
