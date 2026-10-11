using System;
using System.Linq;
using System.Linq.Expressions;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class ValidationTests
    {
        public class Src { public string Name { get; set; } public int Age { get; set; } }
        public class DestExtra { public string Name { get; set; } public string Extra { get; set; } }

        [Fact]
        public void Multiple_unmapped_members_across_maps_throw_one_exception_listing_all()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Src, DestExtra>();
                cfg.CreateMap<Src, AnotherDest>();
            });
            var ex = Assert.Throws<MapItConfigurationException>(() => config.AssertConfigurationIsValid());
            Assert.True(ex.Errors.Length >= 2);
        }

        public class AnotherDest { public string Name { get; set; } public string Missing { get; set; } }

        [Fact]
        public void MemberList_none_produces_no_member_errors()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Src, DestExtra>(MemberList.None));
            var ex = Record.Exception(() => config.AssertConfigurationIsValid());
            Assert.Null(ex);
        }

        public class NoCtor
        {
            public string Name { get; set; }
            private NoCtor() { }
            public static NoCtor Make() => new NoCtor();
        }

        [Fact]
        public void Non_constructible_destination_reports_cannot_construct()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, NoCtor>());
            var ex = Assert.Throws<MapItConfigurationException>(() => config.AssertConfigurationIsValid());
            Assert.Contains(ex.Errors, e => e.CanConstruct == false);
        }

        [Fact]
        public void DisableCtorValidation_suppresses_constructibility_error()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Src, NoCtor>().DisableCtorValidation().ForMember("Name", o => o.MapFrom(s => s.Name)));
            // Name is mapped; ctor validation disabled -> valid.
            var ex = Record.Exception(() => config.AssertConfigurationIsValid());
            Assert.Null(ex);
        }

        [Fact]
        public void Constructibility_only_error_has_no_unmapped_lead_line()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Src, NoCtor>().ForMember("Name", o => o.MapFrom(s => s.Name)));
            var ex = Assert.Throws<MapItConfigurationException>(() => config.AssertConfigurationIsValid());
            Assert.DoesNotContain("Unmapped members", ex.Message);
        }

        public class Outer { public Inner Child { get; set; } }
        public class Inner { public string V { get; set; } }
        public class OuterDto { public InnerDto Child { get; set; } }
        public class InnerDto { public string V { get; set; } }

        [Fact]
        public void Nested_member_pair_without_map_is_reported()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Outer, OuterDto>());
            var ex = Assert.Throws<MapItConfigurationException>(() => config.AssertConfigurationIsValid());
            Assert.Contains(ex.Errors, e => e.UnmappableNestedPairs != null);
        }

        [Fact]
        public void BuildExecutionPlan_returns_nonnull_lambda_with_correct_signature()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, DestExtra>()
                .ForMember("Extra", o => o.MapFrom(s => s.Name)));
            var plan = ((IGlobalConfiguration)config).BuildExecutionPlan(typeof(Src), typeof(DestExtra));
            Assert.NotNull(plan);
            Assert.Equal(3, plan.Parameters.Count);
            Assert.Equal(typeof(Src), plan.Parameters[0].Type);
            Assert.Equal(typeof(DestExtra), plan.Parameters[1].Type);
            Assert.Equal(typeof(ResolutionContext), plan.Parameters[2].Type);

            // Compiled delegate maps correctly with a null context.
            var del = plan.Compile();
            var result = (DestExtra)del.DynamicInvoke(new Src { Name = "n" }, null, null);
            Assert.Equal("n", result.Name);
        }

        [Fact]
        public void AssertConfigurationIsValid_for_missing_profile_throws_argument_exception()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, DestExtra>().ForMember("Extra", o => o.MapFrom(s => s.Name)));
            Assert.Throws<ArgumentException>(() => config.AssertConfigurationIsValid<EmptyProfile>());
        }

        public class EmptyProfile : Profile { }
    }
}
