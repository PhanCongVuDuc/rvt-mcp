using Xunit;

namespace RvtMcp.Tests
{
    /// <summary>Tests that mutate the static <c>ToolCatalogStore</c> must not run in parallel with each other.</summary>
    [CollectionDefinition("ToolCatalogStore", DisableParallelization = true)]
    public class ToolCatalogStoreCollection { }
}
