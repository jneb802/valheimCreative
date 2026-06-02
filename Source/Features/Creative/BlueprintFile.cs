using System.Collections.Generic;

namespace ValheimCreative.Features.Creative
{
    internal sealed class BlueprintFile
    {
        internal string Name { get; set; } = "";
        internal string Creator { get; set; } = "";
        internal string Description { get; set; } = "";
        internal string Category { get; set; } = "Blueprints";
        internal List<BlueprintPieceEntry> Pieces { get; } = new();
    }
}
