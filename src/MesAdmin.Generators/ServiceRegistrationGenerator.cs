using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MesAdmin.Generators;

[Generator]
public sealed class ServiceRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classes = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => (INamedTypeSymbol?)ctx.SemanticModel.GetDeclaredSymbol(ctx.Node))
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => symbol!);

        context.RegisterSourceOutput(classes.Collect(), static (ctx, symbols) =>
        {
            var handlers = symbols
                .Where(static s => !s.IsAbstract && s.TypeKind == TypeKind.Class)
                .Select(static s => new ServiceRegistration(s, GetCommandHandlerInterface(s)))
                .Where(static h => h.ServiceInterface is not null)
                .Distinct(ServiceRegistrationComparer.Instance)
                .OrderBy(static h => h.Implementation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .ToImmutableArray();

            if (handlers.Length != 0)
                ctx.AddSource("MesGeneratedServiceRegistration.g.cs", SourceText.From(RenderHandlers(handlers), Encoding.UTF8));

            var infrastructureServices = symbols
                .Where(static s =>
                    !s.IsAbstract &&
                    s.TypeKind == TypeKind.Class &&
                    s.ContainingType is null &&
                    s.ContainingNamespace.ToDisplayString().StartsWith("MesAdmin.Infrastructure.Data", StringComparison.Ordinal))
                .SelectMany(static s => GetApplicationInterfaces(s).Select(i => new ServiceRegistration(s, i)))
                .Distinct(ServiceRegistrationComparer.Instance)
                .OrderBy(static h => h.Implementation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .ToImmutableArray();

            if (infrastructureServices.Length != 0)
                ctx.AddSource("MesGeneratedInfrastructureServiceRegistration.g.cs", SourceText.From(RenderInfrastructureServices(infrastructureServices), Encoding.UTF8));
        });
    }

    private static INamedTypeSymbol? GetCommandHandlerInterface(INamedTypeSymbol type)
        => type.AllInterfaces.FirstOrDefault(static i =>
            i.Name == "ICommandHandler" &&
            i.TypeArguments.Length == 2 &&
            i.ContainingNamespace.ToDisplayString() == "FastEndpoints");

    private static ImmutableArray<INamedTypeSymbol> GetApplicationInterfaces(INamedTypeSymbol type)
        => type.AllInterfaces
            .Where(static i =>
                i.Name != "IUnitOfWorkTransaction" &&
                i.ContainingNamespace.ToDisplayString() == "MesAdmin.Application.Interfaces")
            .ToImmutableArray();

    private static string RenderHandlers(ImmutableArray<ServiceRegistration> handlers)
    {
        var sb = new StringBuilder();
        AppendHeader(sb);
        sb.AppendLine("namespace MesAdmin.Application.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("public static partial class MesGeneratedServiceRegistration");
        sb.AppendLine("{");
        sb.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddMesGeneratedServices(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (var handler in handlers)
        {
            var service = handler.ServiceInterface!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var implementation = handler.Implementation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            sb.Append("        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped(services, typeof(")
                .Append(service)
                .Append("), typeof(")
                .Append(implementation)
                .AppendLine("));");
            sb.Append("        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped(services, typeof(")
                .Append(implementation)
                .AppendLine("));");
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string RenderInfrastructureServices(ImmutableArray<ServiceRegistration> services)
    {
        var sb = new StringBuilder();
        AppendHeader(sb);
        sb.AppendLine("namespace MesAdmin.Infrastructure.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("public static partial class MesGeneratedInfrastructureServiceRegistration");
        sb.AppendLine("{");
        sb.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddMesGeneratedInfrastructureServices(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (var service in services)
        {
            var serviceType = service.ServiceInterface!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var implementation = service.Implementation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            sb.Append("        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped(services, typeof(")
                .Append(serviceType)
                .Append("), typeof(")
                .Append(implementation)
                .AppendLine("));");
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8631");
        sb.AppendLine();
    }

    private readonly struct ServiceRegistration
    {
        public ServiceRegistration(INamedTypeSymbol implementation, INamedTypeSymbol? serviceInterface)
        {
            Implementation = implementation;
            ServiceInterface = serviceInterface;
        }

        public INamedTypeSymbol Implementation { get; }
        public INamedTypeSymbol? ServiceInterface { get; }
    }

    private sealed class ServiceRegistrationComparer : IEqualityComparer<ServiceRegistration>
    {
        public static readonly ServiceRegistrationComparer Instance = new();

        public bool Equals(ServiceRegistration x, ServiceRegistration y)
            => SymbolEqualityComparer.Default.Equals(x.Implementation, y.Implementation) &&
               SymbolEqualityComparer.Default.Equals(x.ServiceInterface, y.ServiceInterface);

        public int GetHashCode(ServiceRegistration obj)
        {
            unchecked
            {
                var hash = SymbolEqualityComparer.Default.GetHashCode(obj.Implementation);
                hash = (hash * 397) ^ SymbolEqualityComparer.Default.GetHashCode(obj.ServiceInterface);
                return hash;
            }
        }
    }
}
