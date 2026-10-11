using System;
using System.Collections.Generic;
using System.Linq;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class BasicMappingTests
    {
        public class Person
        {
            public string Name { get; set; }
            public int Age { get; set; }
            public string Email { get; set; }
        }

        public class PersonDto
        {
            public string Name { get; set; }
            public int Age { get; set; }
            public string Email { get; set; }
        }

        [Fact]
        public void Maps_matching_properties_by_convention()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>());
            var mapper = config.CreateMapper();

            var source = new Person { Name = "Alice", Age = 30, Email = "a@b.com" };
            var result = mapper.Map<PersonDto>(source);

            Assert.Equal("Alice", result.Name);
            Assert.Equal(30, result.Age);
            Assert.Equal("a@b.com", result.Email);
        }

        [Fact]
        public void Maps_nested_object_recursively()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Address, AddressDto>();
                cfg.CreateMap<PersonWithAddress, PersonWithAddressDto>();
            });
            var mapper = config.CreateMapper();

            var source = new PersonWithAddress
            {
                Name = "Bob",
                Address = new Address { Street = "1 Main", City = "Springfield" }
            };
            var result = mapper.Map<PersonWithAddressDto>(source);

            Assert.Equal("Bob", result.Name);
            Assert.NotNull(result.Address);
            Assert.Equal("1 Main", result.Address.Street);
            Assert.Equal("Springfield", result.Address.City);
        }

        [Fact]
        public void Maps_collections()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>());
            var mapper = config.CreateMapper();

            var source = new List<Person>
            {
                new Person { Name = "Alice", Age = 30 },
                new Person { Name = "Bob", Age = 25 }
            };
            var result = mapper.Map<List<PersonDto>>(source);

            Assert.Equal(2, result.Count);
            Assert.Equal("Alice", result[0].Name);
            Assert.Equal("Bob", result[1].Name);
        }

        [Fact]
        public void Maps_arrays()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>());
            var mapper = config.CreateMapper();

            var source = new[] { new Person { Name = "Alice" }, new Person { Name = "Bob" } };
            var result = mapper.Map<PersonDto[]>(source);

            Assert.Equal(2, result.Length);
            Assert.Equal("Alice", result[0].Name);
        }

        [Fact]
        public void Maps_enums_by_name()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Status, StatusDto>());
            var mapper = config.CreateMapper();

            var result = mapper.Map<StatusDto>(Status.Active);
            Assert.Equal(StatusDto.Active, result);
        }

        [Fact]
        public void Maps_enum_to_underlying_type()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Status, int>());
            var mapper = config.CreateMapper();

            var result = mapper.Map<int>(Status.Inactive);
            Assert.Equal(1, result);
        }

        [Fact]
        public void Maps_null_source_returns_null()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, PersonDto>());
            var mapper = config.CreateMapper();

            var result = mapper.Map<Person, PersonDto>((Person)null);
            Assert.Null(result);
        }

        [Fact]
        public void Maps_dictionary()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<string, string>());
            var mapper = config.CreateMapper();

            var source = new Dictionary<string, string> { { "a", "1" }, { "b", "2" } };
            var result = mapper.Map<Dictionary<string, string>>(source);

            Assert.Equal("1", result["a"]);
            Assert.Equal("2", result["b"]);
        }

        [Fact]
        public void Maps_primitives_with_conversion()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<int, long>());
            var mapper = config.CreateMapper();

            var result = mapper.Map<long>(42);
            Assert.Equal(42L, result);
        }

        [Fact]
        public void Maps_string_to_guid()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<string, Guid>());
            var mapper = config.CreateMapper();

            var id = Guid.NewGuid();
            var result = mapper.Map<Guid>(id.ToString());
            Assert.Equal(id, result);
        }

        [Fact]
        public void Ignore_member_not_mapped()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Person, PersonDto>()
                    .ForMember(d => d.Email, opt => opt.Ignore());
            });
            var mapper = config.CreateMapper();

            var source = new Person { Name = "Alice", Age = 30, Email = "a@b.com" };
            var result = mapper.Map<PersonDto>(source);

            Assert.Equal("Alice", result.Name);
            Assert.Null(result.Email);
        }

        [Fact]
        public void MapFrom_source_member_by_name()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.Username, opt => opt.MapFrom(s => s.FirstName + "." + s.LastName));
            });
            var mapper = config.CreateMapper();

            var source = new User { FirstName = "John", LastName = "Doe" };
            var result = mapper.Map<UserDto>(source);

            Assert.Equal("John.Doe", result.Username);
        }

        [Fact]
        public void Condition_prevents_mapping_when_false()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Source, Dest>()
                    .ForMember(d => d.Name, opt =>
                        opt.Condition(s => s.Name.Length > 3));
            });
            var mapper = config.CreateMapper();

            var shortSource = new Source { Name = "ab" };
            var shortResult = mapper.Map<Dest>(shortSource);
            Assert.Null(shortResult.Name);

            var longSource = new Source { Name = "abcd" };
            var longResult = mapper.Map<Dest>(longSource);
            Assert.Equal("abcd", longResult.Name);
        }

        public class User
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }

        public class UserDto
        {
            public string Username { get; set; }
        }

        public class Address
        {
            public string Street { get; set; }
            public string City { get; set; }
        }

        public class AddressDto
        {
            public string Street { get; set; }
            public string City { get; set; }
        }

        public class PersonWithAddress
        {
            public string Name { get; set; }
            public Address Address { get; set; }
        }

        public class PersonWithAddressDto
        {
            public string Name { get; set; }
            public AddressDto Address { get; set; }
        }

        public enum Status { Active = 0, Inactive = 1, Banned = 2 }
        public enum StatusDto { Active = 0, Inactive = 1, Banned = 2 }

        public class Source
        {
            public string Name { get; set; }
        }

        public class Dest
        {
            public string Name { get; set; }
        }
    }
}
