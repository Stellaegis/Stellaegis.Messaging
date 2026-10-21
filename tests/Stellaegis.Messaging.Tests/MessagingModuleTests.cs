using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Messaging.Tests;

public sealed class MessagingModuleTests
{
    private static readonly Compartment Owner = Compartment.Parse("acme");

    private static ServiceProvider Build(ApplicationFrame? frame, bool inheritSharedSpace = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        if (frame is not null)
            services.AddStellaegisFrame(frame);
        services.AddStellaegisEmail(new ConfigurationBuilder().Build());
        if (inheritSharedSpace)
            services.AddStellaegisModuleParameter<MessagingModule>(MessagingModule.InheritSharedSpace);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Engine_declares_the_module_with_compartments_the_shared_space_and_its_capabilities()
    {
        using var provider = Build(ApplicationFrame.SingleCompartment(Owner));

        var declaration = provider.GetStellaegisModule<MessagingModule>();

        Assert.NotNull(declaration);
        Assert.Equal(ModuleCompartments.Required, declaration.Compartments);
        Assert.True(declaration.AcceptsSharedSpace);
        Assert.Contains(MessagingModule.InheritSharedSpace, declaration.Capabilities);
        Assert.Contains(ModuleCapability.OwnerIsolation, declaration.Capabilities);
        Assert.Empty(declaration.Operations);
        Assert.Empty(declaration.Parameters);
        Assert.True(MessagingModule.InheritSharedSpace.RequiresSharedSpace);
    }

    [Fact]
    public void Application_with_a_compartment_validates_isolated_by_owner_as_well()
    {
        using (var provider = Build(ApplicationFrame.SingleCompartment(Owner)))
            provider.ValidateStellaegisModules();
        using (var provider = Build(ApplicationFrame.SingleCompartment(Owner).WithOwnerIsolation()))
            provider.ValidateStellaegisModules();
    }

    [Fact]
    public void Inheritance_supposes_a_shared_space_in_the_frame()
    {
        using (var provider = Build(ApplicationFrame.ManyCompartments().WithSharedSpace(), inheritSharedSpace: true))
            provider.ValidateStellaegisModules();

        using var without = Build(ApplicationFrame.ManyCompartments(), inheritSharedSpace: true);
        var exception = Assert.Throws<InvalidOperationException>(without.ValidateStellaegisModules);
        Assert.Contains("inherit_shared_space", exception.Message);
        Assert.Contains("WithSharedSpace()", exception.Message);
    }

    [Fact]
    public void Application_without_compartments_is_refused()
    {
        using var provider = Build(frame: null);

        var exception = Assert.Throws<InvalidOperationException>(provider.ValidateStellaegisModules);
        Assert.Contains(MessagingModule.Name, exception.Message);
    }

    [Fact]
    public void Outbox_options_are_validated_when_the_host_starts()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
        [
            KeyValuePair.Create<string, string?>("Stellaegis:Messaging:Outbox:ClaimBatchSize", "0"),
        ]).Build();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddStellaegisFrame(ApplicationFrame.SingleCompartment(Owner)).AddStellaegisEmail(configuration);
        using var host = builder.Build();

        var exception = Assert.Throws<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<MessagingOutboxOptions>>().Value);

        Assert.Contains("ClaimBatchSize is 0", exception.Message);
        Assert.Null(new MessagingOutboxOptions().FindError());
        Assert.Null(new MessagingOutboxOptions { RetryBackoffBase = TimeSpan.Zero }.FindError());
    }
}
