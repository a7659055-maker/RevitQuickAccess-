using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitQuickAccess.Transfer
{
    /// <summary>Which coordinate system a paste is aligned to.</summary>
    public enum CoordBasis
    {
        Internal,   // Revit's "paste in place" — same internal coords (only correct if internal origins match)
        Shared,     // same real-world/shared (survey) coordinates — the cross-project fix
        BasePoint   // user-picked base point → picked target point (optionally with rotation)
    }

    public enum TransferMode
    {
        Copy, CopyBase, PasteExact, PasteBase, Inspect, MoveXyz, PasteCsv
    }

    /// <summary>What was copied — must reference the still-open source document for CopyElements.</summary>
    public sealed class TransferClipboard
    {
        public Document SourceDoc;
        public List<ElementId> Ids = new List<ElementId>();
        public CoordBasis Basis = CoordBasis.Shared;

        // base-point mode (all in source internal coords)
        public XYZ BasePoint;
        public XYZ BaseDir;      // null → translation only; set → rotation via direction

        public string SourceTitle = "";
        public string SourcePath = "";

        public bool HasElements => Ids != null && Ids.Count > 0;
        public bool IsBase => BasePoint != null;
    }

    /// <summary>Silently accept destination types when a copied type name already exists.</summary>
    public class KeepDestTypesHandler : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            => DuplicateTypeAction.UseDestinationTypes;
    }
}
