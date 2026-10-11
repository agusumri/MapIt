using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class ConventionExclusionTests
    {
        public class Src { public string Name { get; set; } public string InternalCode { get; set; } public string Secret { get; set; } }
        public class Dst { public string Name { get; set; } public string InternalCode { get; set; } public string Secret { get; set; } }

        [Fact]
        public void Global_ignore_prefix_is_honored_when_mapping()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.AddGlobalIgnore("Internal");
                cfg.CreateMap<Src, Dst>();
            }).CreateMapper();

            var dto = mapper.Map<Dst>(new Src { Name = "n", InternalCode = "x" });
            Assert.Equal("n", dto.Name);
            Assert.Null(dto.InternalCode);
        }

        [Fact]
        public void ShouldMapProperty_filter_is_honored_when_mapping()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.ShouldMapProperty = p => p.Name != "Secret";
                cfg.CreateMap<Src, Dst>();
            }).CreateMapper();

            var dto = mapper.Map<Dst>(new Src { Name = "n", Secret = "s" });
            Assert.Equal("n", dto.Name);
            Assert.Null(dto.Secret);
        }

        [Fact]
        public void Explicit_configuration_wins_over_global_ignore()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.AddGlobalIgnore("Internal");
                cfg.CreateMap<Src, Dst>().ForMember(d => d.InternalCode, o => o.MapFrom(s => s.InternalCode));
            }).CreateMapper();

            Assert.Equal("x", mapper.Map<Dst>(new Src { InternalCode = "x" }).InternalCode);
        }
    }
}
