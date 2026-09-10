using MesXray.AI;
using MesXray.AI.Evidence;
using MesXray.AI.Investigators;
using MesXray.AI.Llm;
using MesXray.AI.Validation;
using MesXray.Api.Configuration;
using MesXray.Domain.Abstractions;
using MesXray.Graph.Queries;
using MesXray.Graph.Store;
using MesXray.Runtime;
using MesXray.Runtime.Fixtures;
using MesXray.Runtime.Security;
using MesXray.Runtime.Store;

namespace MesXray.Api.Bootstrap;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the graph store, query services, runtime adapter with its whitelist gateway, and the AI investigator.</summary>
    public static IServiceCollection AddXRay(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(XRayOptions.SectionName).Get<XRayOptions>() ?? new XRayOptions();
        var runtimeOptions = configuration.GetSection(RuntimeOptions.SectionName).Get<RuntimeOptions>() ?? new RuntimeOptions();
        var aiOptions = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

        services.AddSingleton(options);
        services.AddSingleton(runtimeOptions);
        services.AddSingleton(aiOptions);
        services.AddSingleton(TimeProvider.System);

        // Graph
        services.AddSingleton<InMemoryGraphStore>();
        services.AddSingleton<IGraphStore>(sp => sp.GetRequiredService<InMemoryGraphStore>());
        services.AddSingleton<IGraphRepository>(sp => sp.GetRequiredService<InMemoryGraphStore>());
        services.AddSingleton<GraphBootstrapper>();
        services.AddSingleton<GraphQueryService>();
        services.AddSingleton<FieldTraceService>();
        services.AddSingleton<ImpactService>();

        // Runtime (read-only, whitelisted)
        services.AddSingleton<IRedactor, Redactor>();
        services.AddSingleton<ITraceStore, InMemoryTraceStore>();
        services.AddSingleton<IToolAuditLog, InMemoryToolAuditLog>();
        services.AddSingleton<FixtureRuntimeAdapter>(sp =>
        {
            var bootstrapper = sp.GetRequiredService<GraphBootstrapper>();
            var runtime = sp.GetRequiredService<RuntimeOptions>();
            if (string.IsNullOrWhiteSpace(runtime.FixtureRoot))
            {
                runtime.FixtureRoot = Path.Combine(bootstrapper.FixtureRoot.Length > 0 ? bootstrapper.FixtureRoot : FixturePaths.Resolve(options.Fixtures.Root), "runtime");
            }

            return new FixtureRuntimeAdapter(runtime, sp.GetRequiredService<IRedactor>());
        });
        services.AddSingleton<IRuntimeAdapter>(sp => sp.GetRequiredService<FixtureRuntimeAdapter>());
        services.AddSingleton<RuntimeToolGateway>();

        // AI
        services.AddSingleton<EvidenceBundleBuilder>();
        services.AddSingleton<EvidenceBindingValidator>();
        services.AddSingleton<RuleBasedInvestigator>();
        services.AddHttpClient<OpenAiCompatibleLlmClient>();
        services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<OpenAiCompatibleLlmClient>());
        services.AddSingleton<IAiInvestigator>(sp => string.Equals(aiOptions.Provider, AiOptions.OpenAiCompatibleProvider, StringComparison.OrdinalIgnoreCase)
            ? new LlmInvestigator(sp.GetRequiredService<ILlmClient>(), sp.GetRequiredService<RuleBasedInvestigator>(), sp.GetRequiredService<EvidenceBindingValidator>(), aiOptions, sp.GetRequiredService<ILogger<LlmInvestigator>>())
            : sp.GetRequiredService<RuleBasedInvestigator>());

        return services;
    }

    /// <summary>Builds the graph and preloads fixture traces. Called once at startup.</summary>
    public static WebApplication BootstrapXRay(this WebApplication app)
    {
        var bootstrapper = app.Services.GetRequiredService<GraphBootstrapper>();
        bootstrapper.Build();

        var adapter = app.Services.GetRequiredService<FixtureRuntimeAdapter>();
        var traces = app.Services.GetRequiredService<ITraceStore>();
        foreach (var trace in adapter.PreloadedTraces())
        {
            traces.Save(trace);
        }

        return app;
    }
}
