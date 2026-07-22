using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitQuickAccess.Transfer;

namespace RevitQuickAccess.Commands
{
    // Thin ribbon commands so the transfer actions are recordable (ribbon click) and bindable.
    // They raise the transfer ExternalEvent, which uses the options last set in the panel (or defaults).

    [Transaction(TransactionMode.Manual)]
    public class TransferCopyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { TransferManager.RequestCopy(); return Result.Succeeded; }
    }

    [Transaction(TransactionMode.Manual)]
    public class TransferPasteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { TransferManager.RequestPasteExact(); return Result.Succeeded; }
    }

    [Transaction(TransactionMode.Manual)]
    public class TransferCopyBaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { TransferManager.RequestCopyBase(); return Result.Succeeded; }
    }

    [Transaction(TransactionMode.Manual)]
    public class TransferPasteBaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { TransferManager.RequestPasteBase(); return Result.Succeeded; }
    }

    [Transaction(TransactionMode.Manual)]
    public class TransferInspectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { TransferManager.RequestInspect(); return Result.Succeeded; }
    }
}
