using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Stellaegis.Messaging.MailKit;

/// <summary>
/// Dependency injection helpers for the MailKit SMTP transport.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SMTP <see cref="IEmailTransport"/> on MailKit (Basic and client-certificate
    /// authentication). Combine with <c>AddStellaegisEmail</c> to run the full messaging engine.
    /// </summary>
    public static IServiceCollection AddStellaegisMailKitTransport(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmailTransport, MailKitEmailTransport>();
        return services;
    }
}
