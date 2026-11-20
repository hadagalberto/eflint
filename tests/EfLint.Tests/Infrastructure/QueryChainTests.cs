using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EfLint.Infrastructure;
using EfLint.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace EfLint.Tests.Infrastructure;

public class QueryChainTests
{
    [Fact]
    public async Task DbSetAndQueryOperators_AreEfQueries_NonEfQueryIsNot()
    {
        const string source = """
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            public class Blog { public int Id { get; set; } }

            public class BlogContext : DbContext
            {
                public DbSet<Blog> Blogs => Set<Blog>();
            }

            public class Sample
            {
                public void M(BlogContext db, IQueryable<Blog> unrelated)
                {
                    var a = db.Blogs;
                    var b = db.Blogs.Where(x => x.Id > 1);
                    var c = db.Set<Blog>().OrderBy(x => x.Id);
                    var d = unrelated.Where(x => x.Id > 1);
                }
            }
            """;

        var (compilation, model) = await CreateModelAsync(source);
        var symbols = EfSymbols.Create(compilation);

        Assert.True(QueryChain.IsEfQuery(OperationFor(model, "db.Blogs"), symbols));
        Assert.True(QueryChain.IsEfQuery(OperationFor(model, "db.Blogs.Where(x => x.Id > 1)"), symbols));
        Assert.True(QueryChain.IsEfQuery(OperationFor(model, "db.Set<Blog>().OrderBy(x => x.Id)"), symbols));
        Assert.False(QueryChain.IsEfQuery(OperationFor(model, "unrelated.Where(x => x.Id > 1)"), symbols));
    }

    [Fact]
    public async Task Materializers_AreRecognized_QueryOperatorsAreNot()
    {
        const string source = """
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            public class Blog { public int Id { get; set; } }

            public class BlogContext : DbContext
            {
                public DbSet<Blog> Blogs => Set<Blog>();
            }

            public class Sample
            {
                public void M(BlogContext db)
                {
                    var a = db.Blogs.ToList();
                    var b = db.Blogs.Count();
                    var c = db.Blogs.Where(x => x.Id > 1);
                }
            }
            """;

        var (compilation, model) = await CreateModelAsync(source);
        var symbols = EfSymbols.Create(compilation);

        Assert.True(QueryChain.IsMaterializer(((IInvocationOperation)OperationFor(model, "db.Blogs.ToList()")).TargetMethod, symbols));
        Assert.True(QueryChain.IsMaterializer(((IInvocationOperation)OperationFor(model, "db.Blogs.Count()")).TargetMethod, symbols));
        Assert.False(QueryChain.IsMaterializer(((IInvocationOperation)OperationFor(model, "db.Blogs.Where(x => x.Id > 1)")).TargetMethod, symbols));
    }

    private static IOperation OperationFor(SemanticModel model, string expressionText)
    {
        var expression = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ExpressionSyntax>()
            .First(node => node.ToString() == expressionText);
        return model.GetOperation(expression)!;
    }

    private static async Task<(Compilation Compilation, SemanticModel Model)> CreateModelAsync(string source)
    {
        var references = await EfLintTestResources.EfCore.ResolveAsync(null, CancellationToken.None);
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "QueryChainTest",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (compilation, compilation.GetSemanticModel(tree));
    }
}
