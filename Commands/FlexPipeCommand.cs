using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitEx = Autodesk.Revit.Exceptions;

namespace RevitQuickAccess.Commands
{
    /// <summary>
    /// «Соединить гибким трубопроводом» — click a connector on one element, then a connector on
    /// another; a flexible pipe is created between them and connected to both.
    /// System / level / diameter are taken from the connected piping.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FlexPipeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;

            Connector a = Pick(uidoc, "Гибкая труба: кликни ПЕРВЫЙ коннектор");
            if (a == null) return Result.Cancelled;
            Connector b = Pick(uidoc, "Гибкая труба: кликни ВТОРОЙ коннектор");
            if (b == null) return Result.Cancelled;

            if (a.Origin.DistanceTo(b.Origin) < 1e-6)
            { message = "Это один и тот же коннектор."; return Result.Failed; }

            ElementId flexTypeId = PipeTools.FirstId(doc, typeof(FlexPipeType));
            if (flexTypeId == ElementId.InvalidElementId)
            { message = "В проекте нет типа гибкого трубопровода (FlexPipeType)."; return Result.Failed; }

            ElementId systemTypeId = a.MEPSystem?.GetTypeId()
                                     ?? b.MEPSystem?.GetTypeId()
                                     ?? PipeTools.FirstId(doc, typeof(PipingSystemType));
            ElementId levelId = PipeTools.ResolveLevel(doc, a.Owner ?? b.Owner, uidoc.ActiveView);
            double dia = a.Radius > 0 ? a.Radius * 2 : (b.Radius > 0 ? b.Radius * 2 : 0);

            using (var t = new Transaction(doc, "Quick Access — гибкий трубопровод"))
            {
                t.Start();

                var pts = new List<XYZ> { a.Origin, b.Origin };
                FlexPipe flex;
                try { flex = FlexPipe.Create(doc, systemTypeId, flexTypeId, levelId, pts); }
                catch (System.Exception ex) { t.RollBack(); message = "Не удалось создать гибкую трубу: " + ex.Message; return Result.Failed; }
                if (flex == null) { t.RollBack(); message = "Не удалось создать гибкую трубу."; return Result.Failed; }

                if (dia > 0) flex.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(dia);
                doc.Regenerate();

                Connector fa = PipeTools.Nearest(flex.ConnectorManager, a.Origin);
                Connector fb = PipeTools.Nearest(flex.ConnectorManager, b.Origin);
                try { if (fa != null && !fa.IsConnectedTo(a)) fa.ConnectTo(a); } catch { }
                try { if (fb != null && !fb.IsConnectedTo(b)) fb.ConnectTo(b); } catch { }

                t.Commit();
            }
            return Result.Succeeded;
        }

        private static Connector Pick(UIDocument uidoc, string prompt)
        {
            try
            {
                var r = uidoc.Selection.PickObject(ObjectType.Element, new PipeTools.MepFilter(), prompt);
                var el = uidoc.Document.GetElement(r);
                if (el == null || r.GlobalPoint == null) return null;
                return PipeTools.FindConnector(el, r.GlobalPoint);
            }
            catch (RevitEx.OperationCanceledException) { return null; }
        }
    }
}
