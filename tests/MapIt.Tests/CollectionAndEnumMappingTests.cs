using System;
using System.Collections.Generic;
using System.Linq;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class CollectionAndEnumMappingTests
    {
        // ---- Collection update by key ----
        public class SrcItem { public int Id { get; set; } public string Name { get; set; } }
        public class DestItem { public int Id { get; set; } public string Name { get; set; } }

        [Fact]
        public void Update_existing_collection_matches_by_key()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<SrcItem, DestItem>().EqualityComparison((s, d) => s.Id == d.Id));
            var mapper = config.CreateMapper();

            var existing = new List<DestItem>
            {
                new DestItem { Id = 1, Name = "old-1" },
                new DestItem { Id = 2, Name = "old-2" },
            };
            var keep1 = existing[0];

            var source = new List<SrcItem>
            {
                new SrcItem { Id = 1, Name = "new-1" },  // update
                new SrcItem { Id = 3, Name = "new-3" },  // add
            };

            var result = mapper.Map(source, existing);

            Assert.Same(existing, result);        // same destination instance
            Assert.Equal(2, result.Count);        // id=2 removed, id=3 added
            var item1 = result.First(d => d.Id == 1);
            Assert.Same(keep1, item1);            // matched item updated in place
            Assert.Equal("new-1", item1.Name);
            Assert.Contains(result, d => d.Id == 3 && d.Name == "new-3");
            Assert.DoesNotContain(result, d => d.Id == 2);
        }

        // ---- Enum mapping with explicit overrides ----
        public enum SrcStatus { Active, Inactive, Pending }
        public enum DestStatus { Enabled, Disabled, Waiting }

        [Fact]
        public void Enum_mapping_with_explicit_value_overrides()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<SrcStatus, DestStatus>().ConvertUsingEnumMapping(opt => opt
                    .MapValue(SrcStatus.Active, DestStatus.Enabled)
                    .MapValue(SrcStatus.Inactive, DestStatus.Disabled)
                    .MapValue(SrcStatus.Pending, DestStatus.Waiting)));
            var mapper = config.CreateMapper();

            Assert.Equal(DestStatus.Enabled, mapper.Map<DestStatus>(SrcStatus.Active));
            Assert.Equal(DestStatus.Disabled, mapper.Map<DestStatus>(SrcStatus.Inactive));
            Assert.Equal(DestStatus.Waiting, mapper.Map<DestStatus>(SrcStatus.Pending));
        }

        [Fact]
        public void Enum_mapping_falls_back_to_name_for_unmapped_values()
        {
            // Only override one; the rest fall back to by-name.
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<SrcStatus, SameNameEnum>().ConvertUsingEnumMapping(opt => opt
                    .MapValue(SrcStatus.Active, SameNameEnum.Pending)));
            var mapper = config.CreateMapper();

            Assert.Equal(SameNameEnum.Pending, mapper.Map<SameNameEnum>(SrcStatus.Active));   // override
            Assert.Equal(SameNameEnum.Inactive, mapper.Map<SameNameEnum>(SrcStatus.Inactive)); // by name
        }

        public enum SameNameEnum { Active, Inactive, Pending }
    }
}
