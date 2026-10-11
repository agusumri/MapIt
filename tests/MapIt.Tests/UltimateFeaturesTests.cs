using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class UltimateFeaturesTests
    {
        // ---- Diff / patch mapping ----
        public class Src { public string Name { get; set; } public int Age { get; set; } public string City { get; set; } }
        public class Dst { public string Name { get; set; } public int Age { get; set; } public string City { get; set; } }

        [Fact]
        public void MapChanges_reports_only_changed_members()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, Dst>());
            var mapper = config.CreateMapper();

            var dest = new Dst { Name = "Alice", Age = 30, City = "Oslo" };
            var src = new Src { Name = "Alice", Age = 31, City = "Bergen" }; // Name same, Age+City changed

            var result = mapper.MapChanges(src, dest);

            Assert.True(result.HasChanges);
            Assert.Equal(2, result.Changes.Count);
            Assert.Contains("Age", result.ChangedMemberNames);
            Assert.Contains("City", result.ChangedMemberNames);
            Assert.DoesNotContain("Name", result.ChangedMemberNames);
            Assert.Equal(31, dest.Age);
            Assert.Equal("Bergen", dest.City);

            var ageChange = result.Changes.First(c => c.MemberName == "Age");
            Assert.Equal(30, ageChange.OldValue);
            Assert.Equal(31, ageChange.NewValue);
        }

        [Fact]
        public void MapChanges_reports_no_changes_when_identical()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, Dst>());
            var mapper = config.CreateMapper();

            var dest = new Dst { Name = "A", Age = 1, City = "C" };
            var result = mapper.MapChanges(new Src { Name = "A", Age = 1, City = "C" }, dest);
            Assert.False(result.HasChanges);
        }

        // ---- Async resolvers ----
        public class Entity { public int Id { get; set; } public string Name { get; set; } }
        public class EnrichedDto { public int Id { get; set; } public string Name { get; set; } public string Enriched { get; set; } }

        private sealed class AsyncEnricher : IAsyncValueResolver<Entity, EnrichedDto, string>
        {
            public async Task<string> ResolveAsync(Entity source, EnrichedDto destination, string destMember, ResolutionContext context, CancellationToken ct)
            {
                await Task.Delay(1, ct);
                return "enriched-" + source.Id;
            }
        }

        [Fact]
        public async Task MapAsync_awaits_async_resolver_members()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Entity, EnrichedDto>()
                   .ForMember(d => d.Enriched, opt => opt.MapFromAsync(new AsyncEnricher())));
            var mapper = config.CreateMapper();

            var dto = await mapper.MapAsync<Entity, EnrichedDto>(new Entity { Id = 7, Name = "x" });
            Assert.Equal("enriched-7", dto.Enriched);
            Assert.Equal("x", dto.Name);   // sync members still mapped
            Assert.Equal(7, dto.Id);
        }

        [Fact]
        public async Task MapAsync_from_func()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Entity, EnrichedDto>()
                   .ForMember(d => d.Enriched, opt => opt.MapFromAsync(e => Task.FromResult("f-" + e.Name))));
            var mapper = config.CreateMapper();

            var dto = await mapper.MapAsync<Entity, EnrichedDto>(new Entity { Id = 1, Name = "n" });
            Assert.Equal("f-n", dto.Enriched);
        }

        // ---- UseAsDataSource / expression translation ----
        public class Person { public string FirstName { get; set; } public int Years { get; set; } }
        public class PersonView { public string Name { get; set; } public int Age { get; set; } }

        private static IQueryable<Person> People() => new[]
        {
            new Person { FirstName = "Ann", Years = 20 },
            new Person { FirstName = "Bob", Years = 40 },
        }.AsQueryable();

        [Fact]
        public void TranslateExpression_rewrites_destination_predicate_to_source()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Person, PersonView>()
                   .ForMember(d => d.Name, o => o.MapFrom(s => s.FirstName))
                   .ForMember(d => d.Age, o => o.MapFrom(s => s.Years)));
            var mapper = config.CreateMapper();

            var sourcePredicate = mapper.TranslateExpression<PersonView, Person>(v => v.Age >= 30);
            var filtered = People().Where(sourcePredicate).ToList();

            Assert.Single(filtered);
            Assert.Equal("Bob", filtered[0].FirstName);
        }

        [Fact]
        public void UseAsDataSource_filters_by_destination_then_projects()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Person, PersonView>()
                   .ForMember(d => d.Name, o => o.MapFrom(s => s.FirstName))
                   .ForMember(d => d.Age, o => o.MapFrom(s => s.Years)));
            var mapper = config.CreateMapper();

            var views = People()
                .UseAsDataSource(mapper)
                .For<PersonView>()
                .Where(v => v.Age < 30)
                .ToQueryable()
                .ToList();

            Assert.Single(views);
            Assert.Equal("Ann", views[0].Name);
            Assert.Equal(20, views[0].Age);
        }

        // ---- ProjectTo collection projection ----
        public class Blog { public string Title { get; set; } public List<Post> Posts { get; set; } }
        public class Post { public string Heading { get; set; } }
        public class BlogDto { public string Title { get; set; } public List<PostDto> Posts { get; set; } }
        public class PostDto { public string Heading { get; set; } }

        [Fact]
        public void ProjectTo_projects_nested_collection()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Blog, BlogDto>();
                cfg.CreateMap<Post, PostDto>();
            });
            var mapper = config.CreateMapper();

            var blogs = new[]
            {
                new Blog { Title = "b1", Posts = new List<Post> { new Post { Heading = "p1" }, new Post { Heading = "p2" } } }
            }.AsQueryable();

            var dtos = mapper.ProjectTo<BlogDto>(blogs).ToList();
            Assert.Single(dtos);
            Assert.Equal("b1", dtos[0].Title);
            Assert.Equal(2, dtos[0].Posts.Count);
            Assert.Equal("p1", dtos[0].Posts[0].Heading);
        }
    }
}
