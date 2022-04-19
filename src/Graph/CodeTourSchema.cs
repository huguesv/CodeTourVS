using Microsoft.VisualStudio.GraphModel;
using Microsoft.VisualStudio.GraphModel.CodeSchema;
using Microsoft.VisualStudio.GraphModel.Schemas;

namespace CodeTourVS
{
    internal class CodeTourSchema
    {
        static CodeTourSchema()
        {
            TourToStepLink.BasedOnCategory = CodeLinkCategories.Contains;
        }

        public static GraphSchema Schema = new GraphSchema("TourSchema");
        public static GraphCategory Tour = Schema.Categories.AddNewCategory("TourFile");
        public static GraphCategory TourToStepLink = Schema.Categories.AddNewCategory("TourToStepLink");
        public static GraphCategory Step = Schema.Categories.AddNewCategory("TourStep");
        public static GraphNodeIdName StepValueName = GraphNodeIdName.Get("TourStepValueName", null, typeof(string), true);

        // We use this instead of CodeNodeProperties.SourceLocation because we
        // want sorting to be done according to the label only.
        // When CodeNodeProperties.SourceLocation is used, sorting puts all the
        // nodes from the same source file together.
        public static GraphProperty StepLocation = Schema.Properties.AddNewProperty("StepLocation", typeof(SourceLocation));
    }
}
