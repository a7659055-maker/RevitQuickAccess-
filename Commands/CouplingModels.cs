using System.Collections.Generic;
using System.Windows.Media;
using Autodesk.Revit.DB;

namespace RevitQuickAccess.Commands
{
    /// <summary>A pipe-fitting family symbol that can act as a coupling (PartType = Union).</summary>
    public class UnionOption
    {
        public ElementId SymbolId { get; set; }
        public string Name { get; set; } = "";
        public ImageSource Preview { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>One block in the coupling dialog — all selected pipes of a single pipe type.</summary>
    public class CouplingGroup
    {
        public ElementId TypeId { get; set; }
        public string TypeName { get; set; } = "";
        public List<ElementId> PipeIds { get; set; } = new List<ElementId>();

        public int PipeCount => PipeIds.Count;
        public string Header => $"{TypeName} — труб: {PipeCount}";

        /// <summary>Distance between couplings (segment length), mm — edited in the dialog.</summary>
        public string StepMm { get; set; } = "4000";

        public bool HasUnionRule { get; set; }
        public string Warning => HasUnionRule
            ? ""
            : "⚠ В трассировке этого типа не задана муфта — выбери семейство ниже.";
        public System.Windows.Visibility WarningVisibility =>
            HasUnionRule ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        public List<UnionOption> Unions { get; set; } = new List<UnionOption>();
        public UnionOption Selected { get; set; }

        /// <summary>Write the chosen family into the type's routing preferences permanently.</summary>
        public bool AssignToType { get; set; }
    }
}
