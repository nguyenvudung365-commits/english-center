using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using Xunit;

namespace EnglishCenter.ArchitectureTests;

internal static class Modules
{
    public static readonly string[] Names = { "Identity", "Academic", "Enrollment", "Finance", "MediaNotification" };
    public static readonly string[] Layers = { "Domain", "Contracts", "Application", "Infrastructure", "Api" };

    public static string Ns(string module, string layer) => $"EnglishCenter.Modules.{module}.{layer}";

    public static Assembly Load(string module, string layer) => Assembly.Load(Ns(module, layer));

    public static IEnumerable<(string Module, string Layer, Assembly Assembly)> All()
    {
        foreach (var m in Names)
            foreach (var l in Layers)
                yield return (m, l, Load(m, l));
    }

    public static IEnumerable<object[]> ModuleData() => Names.Select(n => new object[] { n });
    public static IEnumerable<object[]> ModuleLayerData() =>
        Names.SelectMany(m => Layers.Select(l => new object[] { m, l }));

    public static string FormatFailures(TestResult r) =>
        r.FailingTypeNames is null ? "" : string.Join(", ", r.FailingTypeNames);
}

public class ModuleArchitectureTests
{
    // C9
    [Fact]
    public void C9_Exactly25ModuleAssembliesAreLoaded_AndContainTypes()
    {
        var all = Modules.All().ToList();
        Assert.Equal(25, all.Count);
        Assert.Equal(25, all.Select(a => a.Assembly.GetName().Name).Distinct().Count());
        foreach (var (m, l, asm) in all)
        {
            Assert.NotEmpty(Types.InAssembly(asm).GetTypes());
            Assert.StartsWith(Modules.Ns(m, l), asm.GetName().Name);
        }
    }

    // C1 + C2 (Application duoc phep phu thuoc Contracts cua module khac)
    [Theory]
    [MemberData(nameof(Modules.ModuleLayerData), MemberType = typeof(Modules))]
    public void C1_C2_Module_DoesNotDependOnOtherModules(string module, string layer)
    {
        var asm = Modules.Load(module, layer);
        foreach (var other in Modules.Names.Where(n => n != module))
        {
            var forbidden = new List<string>
            {
                Modules.Ns(other, "Domain"),
                Modules.Ns(other, "Application"),
                Modules.Ns(other, "Infrastructure"),
                Modules.Ns(other, "Api"),
            };
            // C2: chi Application duoc dung Contracts cua module khac.
            if (layer != "Application")
                forbidden.Add(Modules.Ns(other, "Contracts"));

            var result = Types.InAssembly(asm).That().ResideInNamespaceStartingWith("EnglishCenter")
                .ShouldNot().HaveDependencyOnAny(forbidden.ToArray()).GetResult();
            Assert.True(result.IsSuccessful,
                $"{module}.{layer} phu thuoc module {other}: {Modules.FormatFailures(result)}");
        }
    }

    // C3
    [Theory]
    [MemberData(nameof(Modules.ModuleData), MemberType = typeof(Modules))]
    public void C3_Domain_IsPure(string module)
    {
        var result = Types.InAssembly(Modules.Load(module, "Domain")).ShouldNot().HaveDependencyOnAny(
            Modules.Ns(module, "Application"), Modules.Ns(module, "Infrastructure"), Modules.Ns(module, "Api"),
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore").GetResult();
        Assert.True(result.IsSuccessful, Modules.FormatFailures(result));
    }

    // C4
    [Theory]
    [MemberData(nameof(Modules.ModuleData), MemberType = typeof(Modules))]
    public void C4_Application_DoesNotDependOnInfrastructureApiOrFrameworks(string module)
    {
        var result = Types.InAssembly(Modules.Load(module, "Application")).ShouldNot().HaveDependencyOnAny(
            Modules.Ns(module, "Infrastructure"), Modules.Ns(module, "Api"),
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore").GetResult();
        Assert.True(result.IsSuccessful, Modules.FormatFailures(result));
    }

    // C5
    [Theory]
    [MemberData(nameof(Modules.ModuleData), MemberType = typeof(Modules))]
    public void C5_Contracts_OnlyDependOnBuildingBlocksAndSystem(string module)
    {
        var asm = Modules.Load(module, "Contracts");
        var r1 = Types.InAssembly(asm).ShouldNot().HaveDependencyOn(Modules.Ns(module, "Domain")).GetResult();
        Assert.True(r1.IsSuccessful, Modules.FormatFailures(r1));

        var otherModules = Modules.Names.Select(n => $"EnglishCenter.Modules.{n}.").ToArray();
        var r2 = Types.InAssembly(asm).ShouldNot().HaveDependencyOnAny(
            Modules.Layers.SelectMany(l => Modules.Names.Select(n => Modules.Ns(n, l)))
                .Where(ns => ns != Modules.Ns(module, "Contracts")).ToArray()).GetResult();
        Assert.True(r2.IsSuccessful, Modules.FormatFailures(r2));

        // Khong dung Microsoft.EntityFrameworkCore / AspNetCore
        var r3 = Types.InAssembly(asm).ShouldNot().HaveDependencyOnAny(
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore").GetResult();
        Assert.True(r3.IsSuccessful, Modules.FormatFailures(r3));
    }

    [Fact]
    public void C5b_ContractEvents_UseOnlyPrimitiveLikePropertyTypes()
    {
        var allowed = new HashSet<Type>
        {
            typeof(Guid), typeof(string), typeof(decimal), typeof(DateTime), typeof(DateOnly),
            typeof(int), typeof(long), typeof(bool), typeof(double), typeof(DateTimeOffset),
        };
        foreach (var m in Modules.Names)
        {
            var asm = Modules.Load(m, "Contracts");
            foreach (var t in asm.GetTypes().Where(t => t.Namespace?.EndsWith("IntegrationEvents") == true && !t.IsNested))
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    Assert.True(allowed.Contains(p.PropertyType) && !p.PropertyType.IsEnum,
                        $"{t.FullName}.{p.Name} co kieu {p.PropertyType} khong phai kieu nguyen thuy");
                }
            }
        }
    }

    // C6
    [Fact]
    public void C6_BuildingBlocks_DoNotDependOnModules()
    {
        var asm = typeof(EnglishCenter.BuildingBlocks.AssemblyReference).Assembly;
        var result = Types.InAssembly(asm).ShouldNot().HaveDependencyOn("EnglishCenter.Modules").GetResult();
        Assert.True(result.IsSuccessful, Modules.FormatFailures(result));
    }

    // C7
    [Fact]
    public void C7_Controllers_DoNotDependOnInfrastructureOrEf()
    {
        var found = 0;
        foreach (var (m, l, asm) in Modules.All().Where(x => x.Layer == "Api"))
        {
            var types = Types.InAssembly(asm).That().HaveNameEndingWith("Controller");
            found += types.GetTypes().Count();
            var result = types.ShouldNot().HaveDependencyOnAny(
                Modules.Ns(m, "Infrastructure"), "Microsoft.EntityFrameworkCore").GetResult();
            Assert.True(result.IsSuccessful, $"{m}: {Modules.FormatFailures(result)}");
        }
        Assert.True(found >= 5, "Phai tim thay it nhat 5 controller (moi module mot ping controller)");

        // Ke ca controller o bat ky assembly nao: khong namespace *.Infrastructure
        var hostResult = Types.InAssembly(typeof(Program).Assembly).That().HaveNameEndingWith("Controller")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore").GetResult();
        Assert.True(hostResult.IsSuccessful);
    }

    // D1
    [Theory]
    [MemberData(nameof(Modules.ModuleData), MemberType = typeof(Modules))]
    public void D1_EachModule_HasExactlyOneDbContext_InInfrastructure(string module)
    {
        foreach (var (m, l, asm) in Modules.All().Where(x => x.Module == module))
        {
            var contexts = asm.GetTypes().Where(t => typeof(DbContext).IsAssignableFrom(t) && t != typeof(DbContext)).ToList();
            if (l == "Infrastructure")
            {
                var ctx = Assert.Single(contexts);
                Assert.StartsWith(Modules.Ns(module, "Infrastructure"), ctx.Namespace);
            }
            else
            {
                Assert.Empty(contexts);
            }
        }
    }

    // C8 - CHAN tham chieu project o muc csproj
    [Fact]
    public void C8_ProjectReferences_FollowAllowedMatrix()
    {
        var root = FindRepoRoot();
        var csprojs = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).ToList();
        Assert.Equal(27, csprojs.Count); // 1 BuildingBlocks + 25 module + 1 host

        var violations = new List<string>();
        foreach (var file in csprojs)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var refs = ReadProjectReferences(file);
            foreach (var r in refs)
            {
                if (!IsAllowed(name, r)) violations.Add($"{name} -> {r}");
            }
        }
        Assert.True(violations.Count == 0, "Tham chieu khong hop le: " + string.Join("; ", violations));
    }

    [Fact]
    public void C8b_SolutionContainsAll30Projects()
    {
        var root = FindRepoRoot();
        var sln = File.ReadAllText(Path.Combine(root, "EnglishCenter.sln"));
        var expected = new List<string> { "EnglishCenter.BuildingBlocks", "EnglishCenter.Api",
            "EnglishCenter.ArchitectureTests", "EnglishCenter.BuildingBlocks.Tests", "EnglishCenter.IntegrationTests" };
        expected.AddRange(Modules.Names.SelectMany(m => Modules.Layers.Select(l => Modules.Ns(m, l))));
        Assert.Equal(30, expected.Count);
        foreach (var p in expected)
            Assert.Contains($"\"{p}\"", sln);
    }

    [Fact]
    public void C8c_DomainAndApplicationCsproj_DoNotReferenceEfOrAspNetPackages()
    {
        var root = FindRepoRoot();
        foreach (var m in Modules.Names)
        foreach (var l in new[] { "Domain", "Application", "Contracts" })
        {
            var file = Path.Combine(root, "src", "Modules", m, Modules.Ns(m, l), Modules.Ns(m, l) + ".csproj");
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("EntityFrameworkCore", text);
            Assert.DoesNotContain("Microsoft.AspNetCore", text);
        }
    }

    [Fact]
    public void C8_Matrix_Detects_Violations_SelfCheck()
    {
        // Dam bao ham IsAllowed that su phat hien vi pham (tranh test xanh gia).
        Assert.False(IsAllowed("EnglishCenter.Modules.Finance.Application", "EnglishCenter.Modules.Identity.Application"));
        Assert.False(IsAllowed("EnglishCenter.Modules.Finance.Api", "EnglishCenter.Modules.Identity.Api"));
        Assert.False(IsAllowed("EnglishCenter.Modules.Finance.Contracts", "EnglishCenter.Modules.Finance.Domain"));
        Assert.False(IsAllowed("EnglishCenter.Modules.Finance.Domain", "EnglishCenter.Modules.Finance.Application"));
        Assert.False(IsAllowed("EnglishCenter.BuildingBlocks", "EnglishCenter.Modules.Finance.Domain"));
        Assert.False(IsAllowed("EnglishCenter.Modules.Finance.Infrastructure", "EnglishCenter.Modules.Identity.Contracts"));
        Assert.True(IsAllowed("EnglishCenter.Modules.Finance.Application", "EnglishCenter.Modules.Enrollment.Contracts"));
        Assert.True(IsAllowed("EnglishCenter.Api", "EnglishCenter.Modules.Finance.Api"));
    }

    // ---- helpers ----
    private static bool IsAllowed(string project, string target)
    {
        const string bb = "EnglishCenter.BuildingBlocks";
        const string prefix = "EnglishCenter.Modules.";

        if (project == bb) return false;
        if (project == "EnglishCenter.Api")
            return target == bb || (target.StartsWith(prefix) && target.EndsWith(".Api"));

        if (!project.StartsWith(prefix)) return false;
        var (pm, pl) = Split(project);
        if (target == bb) return true;
        if (!target.StartsWith(prefix)) return false;
        var (tm, tl) = Split(target);

        bool same = pm == tm;
        return pl switch
        {
            "Domain" => false,
            "Contracts" => false,
            "Application" => (same && (tl == "Domain" || tl == "Contracts")) || (!same && tl == "Contracts"),
            "Infrastructure" => same && (tl == "Application" || tl == "Domain"),
            "Api" => same && (tl == "Application" || tl == "Infrastructure"),
            _ => false,
        };
    }

    private static (string Module, string Layer) Split(string project)
    {
        var parts = project.Substring("EnglishCenter.Modules.".Length).Split('.');
        return (parts[0], parts[1]);
    }

    private static List<string> ReadProjectReferences(string csproj)
    {
        var doc = System.Xml.Linq.XDocument.Load(csproj);
        return doc.Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => Path.GetFileNameWithoutExtension(s!.Replace('\\', '/')))
            .ToList();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EnglishCenter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Khong tim thay EnglishCenter.sln");
    }
}
