using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stellaegis.Primitives;
using Stellaegis.Primitives.DependencyInjection;

namespace Stellaegis.Messaging.Tests;

public sealed class MessagingModuleTests
{
    private static readonly Compartment Owner = Compartment.Parse("acme");

    private static ServiceProvider Build(ApplicationFrame? frame)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        if (frame is not null)
            services.AddStellaegisFrame(frame);
        services.AddStellaegisEmail(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Engine_declares_the_module_with_compartments()
    {
        using var provider = Build(ApplicationFrame.SingleCompartment(Owner));

        var declaration = provider.GetStellaegisModule<MessagingModule>();

        Assert.NotNull(declaration);
        Assert.True(declaration.IsCompartmented);
    }

    [Fact]
    public void Application_with_a_compartment_validates()
    {
        using var provider = Build(ApplicationFrame.SingleCompartment(Owner));

        provider.ValidateStellaegisModules();
    }

    [Fact]
    public void Application_without_compartments_is_refused()
    {
        using var provider = Build(frame: null);

        var exception = Assert.Throws<InvalidOperationException>(provider.ValidateStellaegisModules);
        Assert.Contains(MessagingModule.Name, exception.Message);
    }
}
