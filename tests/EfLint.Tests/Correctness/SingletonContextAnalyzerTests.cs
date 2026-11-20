using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Correctness;

public class SingletonContextAnalyzerTests
{
    [Fact]
    public async Task AddSingleton_GenericContext_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.{|EFL2005:AddSingleton<BlogContext>|}();
                }
            }
            """);
    }

    [Fact]
    public async Task AddSingleton_WithFactory_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.{|EFL2005:AddSingleton<BlogContext>|}(sp => new BlogContext());
                }
            }
            """);
    }

    [Fact]
    public async Task AddDbContext_OptionsAndSingleton_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.{|EFL2005:AddDbContext<BlogContext>|}(options => { }, ServiceLifetime.Singleton);
                }
            }
            """);
    }

    [Fact]
    public async Task AddDbContext_BothLifetimesSingleton_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.{|EFL2005:AddDbContext<BlogContext>|}(ServiceLifetime.Singleton, ServiceLifetime.Singleton);
                }
            }
            """);
    }

    [Fact]
    public async Task AddSingleton_InstanceOverload_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.{|EFL2005:AddSingleton<BlogContext>|}(new BlogContext());
                }
            }
            """);
    }

    [Fact]
    public async Task AddSingleton_DerivedContext_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class DerivedContext : BlogContext { }

            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.{|EFL2005:AddSingleton<DerivedContext>|}();
                }
            }
            """);
    }

    [Fact]
    public async Task AddScoped_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.AddScoped<BlogContext>();
                }
            }
            """);
    }

    [Fact]
    public async Task AddTransient_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.AddTransient<BlogContext>();
                }
            }
            """);
    }

    [Fact]
    public async Task AddDbContext_ScopedLifetime_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.AddDbContext<BlogContext>(options => { }, ServiceLifetime.Scoped);
                }
            }
            """);
    }

    [Fact]
    public async Task AddDbContext_TransientLifetime_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.AddDbContext<BlogContext>(ServiceLifetime.Transient, ServiceLifetime.Transient);
                }
            }
            """);
    }

    [Fact]
    public async Task AddSingleton_NonContextType_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class SomeService { }

            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.AddSingleton<SomeService>();
                }
            }
            """);
    }

    [Fact]
    public async Task AddSingleton_TypeofString_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class Sample
            {
                public void M(IServiceCollection services)
                {
                    services.AddSingleton(typeof(string));
                }
            }
            """);
    }

    [Fact]
    public async Task LookAlikeAddSingleton_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SingletonContextAnalyzer>("""
            public class FakeServices
            {
                public void AddSingleton<T>() { }
            }

            public class Sample
            {
                public void M(FakeServices services)
                {
                    services.AddSingleton<BlogContext>();
                }
            }
            """);
    }
}
