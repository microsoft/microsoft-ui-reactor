using System;
using System.Collections.Generic;
using Reactor.Controls;

namespace Reactor.TestApp.TableViewGallery;

/// <summary>Shared data + column sets for the first-class Reactor TableView gallery pages.</summary>
static class TableViewSampleData
{
    public sealed record Person(
        string FirstName, string LastName, string Email, string Department,
        string Role, DateTimeOffset JoinDate, double Salary, bool IsActive)
    {
        public string JoinDateText => JoinDate.ToString("yyyy-MM-dd");
        public string Status => IsActive ? "Active" : "Inactive";
    }

    public static readonly List<Person> People = new()
    {
        new("Ava","Chen","ava.chen@contoso.com","Engineering","Software Engineer", new(2021,3,15,0,0,0,TimeSpan.Zero), 112500, true),
        new("Noah","Patel","noah.patel@contoso.com","Design","Designer", new(2022,1,22,0,0,0,TimeSpan.Zero), 84000, true),
        new("Mia","Garcia","mia.garcia@contoso.com","Product","Product Manager", new(2020,11,9,0,0,0,TimeSpan.Zero), 167800, true),
        new("Ethan","Nguyen","ethan.nguyen@contoso.com","Sales","Account Executive", new(2023,5,2,0,0,0,TimeSpan.Zero), 73500, false),
        new("Sophia","Jones","sophia.jones@contoso.com","Marketing","Brand Manager", new(2019,8,18,0,0,0,TimeSpan.Zero), 124200, true),
        new("Liam","Wright","liam.wright@contoso.com","Operations","Ops Manager", new(2018,4,11,0,0,0,TimeSpan.Zero), 158600, true),
        new("Olivia","Smith","olivia.smith@contoso.com","Finance","Financial Analyst", new(2021,9,27,0,0,0,TimeSpan.Zero), 98200, true),
        new("James","Lopez","james.lopez@contoso.com","HR","Recruiter", new(2024,2,14,0,0,0,TimeSpan.Zero), 68100, true),
        new("Emma","Brown","emma.brown@contoso.com","Engineering","Senior Engineer", new(2017,6,5,0,0,0,TimeSpan.Zero), 198400, true),
        new("Lucas","Taylor","lucas.taylor@contoso.com","Design","Researcher", new(2022,10,31,0,0,0,TimeSpan.Zero), 62000, false),
        new("Zoe","Hernandez","zoe.hernandez@contoso.com","Product","Group PM", new(2016,12,20,0,0,0,TimeSpan.Zero), 221700, true),
        new("Henry","Wilson","henry.wilson@contoso.com","Sales","Sales Manager", new(2020,7,13,0,0,0,TimeSpan.Zero), 171300, false),
    };

    /// <summary>Showcase columns with colored pill / chip / tint template cells.</summary>
    public static List<TableColumn> VibrantColumns() => new()
    {
        new("First name", nameof(Person.FirstName), Width: 110),
        new("Department", nameof(Person.Department), CellStyle.Pill, Width: 150),
        new("Status", nameof(Person.IsActive), CellStyle.Chip, Width: 100),
        new("Salary", nameof(Person.Salary), CellStyle.Tint, Width: 120),
        new("Join date", nameof(Person.JoinDateText), Width: 110),
        new("Role", nameof(Person.Role), Width: 170),
        new("Email", nameof(Person.Email)),
    };

    /// <summary>Plain-text equivalents (for the "vibrant off" state + feature-focused pages).</summary>
    public static List<TableColumn> TextColumns() => new()
    {
        new("First name", nameof(Person.FirstName), Width: 110),
        new("Department", nameof(Person.Department), Width: 130),
        new("Status", nameof(Person.Status), Width: 90),
        new("Salary", nameof(Person.Salary), Width: 110),
        new("Join date", nameof(Person.JoinDateText), Width: 110),
        new("Role", nameof(Person.Role), Width: 170),
        new("Email", nameof(Person.Email)),
    };

    private static readonly string[] s_first =
        { "Ava","Noah","Mia","Ethan","Sophia","Liam","Olivia","James","Emma","Lucas","Zoe","Henry","Aria","Leo","Nora","Owen","Isla","Max","Ruby","Eli" };
    private static readonly string[] s_last =
        { "Chen","Patel","Garcia","Nguyen","Jones","Wright","Smith","Lopez","Brown","Taylor","Hernandez","Wilson","Kim","Singh","Rossi","Khan","Davis","Cohen","Ali","Park" };
    private static readonly (string Dept, string Role)[] s_deptRole =
    {
        ("Engineering","Software Engineer"), ("Design","Product Designer"), ("Product","Product Manager"),
        ("Sales","Account Executive"), ("Marketing","Brand Manager"), ("Operations","Ops Manager"),
        ("Finance","Financial Analyst"), ("HR","Recruiter"),
    };

    /// <summary>Synthesizes <paramref name="n"/> deterministic rows (for pagination / perf / virtualization).</summary>
    public static List<Person> ManyPeople(int n)
    {
        var list = new List<Person>(n);
        for (int i = 0; i < n; i++)
        {
            var first = s_first[i % s_first.Length];
            var last = s_last[(i / s_first.Length) % s_last.Length];
            var (dept, role) = s_deptRole[i % s_deptRole.Length];
            var join = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays((i * 37) % 3200);
            double salary = 60000 + (i * 1373) % 180000;
            list.Add(new Person(first, $"{last}", $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}{i}@contoso.com",
                dept, role, join, salary, i % 4 != 0));
        }
        return list;
    }
}
