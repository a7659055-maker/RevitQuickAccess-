using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitQuickAccess.Settings;
using RevitEx = Autodesk.Revit.Exceptions;

namespace RevitQuickAccess.Commands
{
    /// <summary>
    /// Click any point on a pipe → the pipe is broken there, a tee (from the pipe type's routing
    /// preferences) is inserted, and a branch pipe of the configured length grows in the chosen
    /// direction. Direction is fixed per command (up / down / left / right, and the 45° variants).
    /// </summary>
    public abstract class TeeCommandBase : IExternalCommand
    {
        protected abstract string DirKey { get; }
        protected virtual string Prompt => "Тройник: кликни точку на трубе";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;

            Reference r;
            try { r = uidoc.Selection.PickObject(ObjectType.Element, new PipeOnlyFilter(), Prompt); }
            catch (RevitEx.OperationCanceledException) { return Result.Cancelled; }

            var pipe = doc.GetElement(r) as Pipe;
            XYZ pt = r?.GlobalPoint;
            if (pipe == null || pt == null) { message = "Нужно кликнуть по трубе."; return Result.Failed; }

            XYZ dir = PipeTools.BranchDirection(pipe, DirKey);
            double len = PluginSettings.TeeBranchMm / 304.8;

            string err = PipeTools.MakeTee(doc, uidoc.ActiveView, pipe, pt, dir, len, "Quick Access — тройник");
            if (err != null) { message = err; return Result.Failed; }
            return Result.Succeeded;
        }

        private class PipeOnlyFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Pipe;
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }

    [Transaction(TransactionMode.Manual)] public class TeeUpCommand : TeeCommandBase { protected override string DirKey => "up"; }
    [Transaction(TransactionMode.Manual)] public class TeeDownCommand : TeeCommandBase { protected override string DirKey => "down"; }
    [Transaction(TransactionMode.Manual)] public class TeeLeftCommand : TeeCommandBase { protected override string DirKey => "left"; }
    [Transaction(TransactionMode.Manual)] public class TeeRightCommand : TeeCommandBase { protected override string DirKey => "right"; }

    [Transaction(TransactionMode.Manual)] public class Tee45UpCommand : TeeCommandBase { protected override string DirKey => "up45"; }
    [Transaction(TransactionMode.Manual)] public class Tee45DownCommand : TeeCommandBase { protected override string DirKey => "down45"; }
    [Transaction(TransactionMode.Manual)] public class Tee45LeftCommand : TeeCommandBase { protected override string DirKey => "left45"; }
    [Transaction(TransactionMode.Manual)] public class Tee45RightCommand : TeeCommandBase { protected override string DirKey => "right45"; }
}
