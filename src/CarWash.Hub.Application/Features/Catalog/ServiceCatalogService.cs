using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Common.Validation;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Admin;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using FluentValidation;

namespace CarWash.Hub.Application.Features.Catalog;

/// <summary>A service as the catalog page shows it.</summary>
/// <param name="Id">The service's key.</param>
/// <param name="Code">The code machines send; not editable.</param>
/// <param name="Name">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="Icon">The icon key.</param>
/// <param name="TracksRefill">Whether refills are tracked.</param>
/// <param name="DefaultCapacity">The capacity a first refill suggests.</param>
/// <param name="LowThresholdPercent">The share left at which it counts as low.</param>
/// <param name="SortOrder">The position in lists and charts.</param>
/// <param name="IsActive">Whether it is shown and tracked.</param>
/// <param name="IsConfigured">False until an admin has reviewed a code the machines introduced.</param>
/// <param name="RowVersion">The version an edit must start from.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ServiceDefinitionDto(
    Guid Id, string Code, string Name, string NameAr, string? Icon, bool TracksRefill, int? DefaultCapacity,
    int LowThresholdPercent, int SortOrder, bool IsActive, bool IsConfigured, string RowVersion);

/// <summary>A change to a service.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record UpdateServiceRequest
{
    /// <summary>Gets the service.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the English name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the Arabic name.</summary>
    public string NameAr { get; init; } = string.Empty;

    /// <summary>Gets the icon key: <c>soap</c>, <c>water</c>, <c>vacuum</c> or any other for the generic icon.</summary>
    public string? Icon { get; init; }

    /// <summary>Gets a value indicating whether refills are tracked.</summary>
    public bool TracksRefill { get; init; }

    /// <summary>Gets the capacity a first refill suggests, in uses.</summary>
    public int? DefaultCapacity { get; init; }

    /// <summary>Gets the share left, in percent, at which the service counts as low.</summary>
    public int LowThresholdPercent { get; init; } = ServiceDefinition.DefaultLowThresholdPercent;

    /// <summary>Gets the position in lists and charts.</summary>
    public int SortOrder { get; init; }

    /// <summary>Gets a value indicating whether the service is shown and tracked.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>Gets the version the edit started from.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>Reviews and edits the services machines report.</summary>
/// <remarks>Layer: Application. Super Admins only. Services come from the machines; they are never created here.</remarks>
public interface IServiceCatalogService
{
    /// <summary>Lists every service, retired ones included, in display order.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalog.</returns>
    Task<IReadOnlyList<ServiceDefinitionDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Changes a service; saving marks it reviewed.</summary>
    /// <param name="request">The change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The service as saved.</returns>
    Task<ServiceDefinitionDto> UpdateAsync(UpdateServiceRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IServiceCatalogService"/>.</summary>
/// <remarks>Layer: Application.</remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="currentUser">Must be a Super Admin.</param>
/// <param name="validator">Validates the change.</param>
/// <param name="localizer">Messages.</param>
/// <param name="clock">The rebuild request's time.</param>
/// <param name="processingTrigger">Wakes the worker to recompute the levels.</param>
public sealed class ServiceCatalogService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IValidator<UpdateServiceRequest> validator,
    ILocalizationService localizer,
    IDateTimeProvider clock,
    IMessageProcessingTrigger processingTrigger) : IServiceCatalogService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceDefinitionDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        AdminRules.EnsureSuperAdmin(currentUser);

        var services = await unitOfWork.Repository<ServiceDefinition, Guid>()
            .ListAsync(new CatalogSpecification(), cancellationToken)
            .ConfigureAwait(false);

        return [.. services.Select(ToDto)];
    }

    /// <inheritdoc />
    public async Task<ServiceDefinitionDto> UpdateAsync(UpdateServiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminRules.EnsureSuperAdmin(currentUser);
        await validator.ValidateAndThrowLocalizedAsync(request, cancellationToken).ConfigureAwait(false);

        var service = await unitOfWork.Repository<ServiceDefinition, Guid>().GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
                      ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Service), request.Id);
        AdminRules.EnsureUnchanged(service.RowVersion, request.RowVersion);

        service.Name = request.Name.Trim();
        service.NameAr = request.NameAr.Trim();
        service.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim().ToLowerInvariant();
        service.TracksRefill = request.TracksRefill;
        service.DefaultCapacity = request.DefaultCapacity;
        service.LowThresholdPercent = request.LowThresholdPercent;
        service.SortOrder = request.SortOrder;
        service.IsActive = request.IsActive;
        service.IsConfigured = true;

        // Tracking or a threshold may have changed: every machine's levels are recomputed.
        await unitOfWork.Repository<StateRebuildRequest, long>()
            .AddAsync(new StateRebuildRequest { MachineId = null, Reason = RebuildReason.CatalogChanged, RequestedAt = clock.UtcNow }, cancellationToken)
            .ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        processingTrigger.Signal();
        return ToDto(service);
    }

    private static ServiceDefinitionDto ToDto(ServiceDefinition service) => new(
        service.Id, service.Code, service.Name, service.NameAr, service.Icon, service.TracksRefill, service.DefaultCapacity,
        service.LowThresholdPercent, service.SortOrder, service.IsActive, service.IsConfigured, AdminRules.Token(service.RowVersion));
}

/// <summary>Validates a change to a service.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class UpdateServiceRequestValidator : AbstractValidator<UpdateServiceRequest>
{
    /// <summary>The largest capacity accepted, in uses.</summary>
    public const int MaxCapacity = 100_000;

    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public UpdateServiceRequestValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(request => request.Name).Required(localizer, ResourceKeys.Fields.Name, 100);
        RuleFor(request => request.NameAr).Required(localizer, ResourceKeys.Fields.NameAr, 100);
        RuleFor(request => request.Icon).Optional(localizer, ResourceKeys.Fields.Name, 64);

        RuleFor(request => request.DefaultCapacity)
            .InclusiveBetween(1, MaxCapacity).When(request => request.DefaultCapacity is not null)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Range, localizer.GetString(ResourceKeys.Fields.DefaultCapacity), 1, MaxCapacity));

        RuleFor(request => request.LowThresholdPercent)
            .InclusiveBetween(1, 99)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Range, localizer.GetString(ResourceKeys.Fields.LowThresholdPercent), 1, 99));

        RuleFor(request => request.SortOrder)
            .InclusiveBetween(0, 10_000)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Range, localizer.GetString(ResourceKeys.Fields.SortOrder), 0, 10_000));
    }
}

/// <summary>Every service, retired ones included, in display order.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class CatalogSpecification : BaseSpecification<ServiceDefinition>
{
    /// <summary>Builds the specification.</summary>
    public CatalogSpecification()
    {
        AddOrderBy(service => service.SortOrder);
        AddOrderBy(service => service.Code);
    }
}
