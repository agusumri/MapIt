using MapIt.Generated;
using Xunit;

namespace MapIt.Tests
{
    public class GenSource { public string Name { get; set; } public int Value { get; set; } public GenNested Child { get; set; } }
    public class GenDest { public string Name { get; set; } public int Value { get; set; } public GenNestedDto Child { get; set; } }
    public class GenNested { public string Label { get; set; } }
    public class GenNestedDto { public string Label { get; set; } }

    // The generator implements these partial methods at compile time (no reflection).
    [Mapper]
    public partial class GeneratedMapper
    {
        public partial GenDest ToDest(GenSource src);
        public partial GenNestedDto ToNested(GenNested src);
    }

    public class SourceGeneratorTests
    {
        [Fact]
        public void Generated_mapper_maps_scalar_members()
        {
            var mapper = new GeneratedMapper();
            var dest = mapper.ToDest(new GenSource { Name = "n", Value = 42, Child = new GenNested { Label = "L" } });

            Assert.Equal("n", dest.Name);
            Assert.Equal(42, dest.Value);
        }

        [Fact]
        public void Generated_mapper_maps_nested_via_sibling_method()
        {
            var mapper = new GeneratedMapper();
            var dest = mapper.ToDest(new GenSource { Name = "n", Value = 1, Child = new GenNested { Label = "deep" } });

            Assert.NotNull(dest.Child);
            Assert.Equal("deep", dest.Child.Label);
        }

        [Fact]
        public void Generated_mapper_handles_null_source()
        {
            var mapper = new GeneratedMapper();
            Assert.Null(mapper.ToDest(null));
        }
    }
}
