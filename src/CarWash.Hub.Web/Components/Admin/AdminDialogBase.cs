using CarWash.Hub.Web.Circuits;
using CarWash.Hub.Web.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace CarWash.Hub.Web.Components.Admin;

/// <summary>
/// An edit dialog: runs its save through the operation runner, shows each field's validation
/// error under that field and any other refusal (a conflict, a rule) above the form, and closes
/// with <c>true</c> once saved.
/// </summary>
/// <remarks>Layer: Web.</remarks>
public abstract class AdminDialogBase : ComponentBase
{
    private IReadOnlyDictionary<string, string[]>? _errors;

    /// <summary>Gets the dialog this component is shown in.</summary>
    [CascadingParameter]
    protected IMudDialogInstance Dialog { get; private set; } = default!;

    /// <summary>Gets the runner every command goes through.</summary>
    [Inject]
    protected DashboardOperationRunner Runner { get; private set; } = default!;

    /// <summary>Gets the dashboard's words.</summary>
    [Inject]
    protected IStringLocalizer<DashboardResource> L { get; private set; } = default!;

    /// <summary>Gets the snackbar for the "saved" confirmation.</summary>
    [Inject]
    protected ISnackbar Snackbar { get; private set; } = default!;

    /// <summary>Gets a value indicating whether a save is running.</summary>
    protected bool Saving { get; private set; }

    /// <summary>Gets the refusal that belongs to no single field, if any.</summary>
    protected string? Message { get; private set; }

    /// <summary>Returns a field's validation errors, or null.</summary>
    /// <param name="field">The request property name.</param>
    /// <returns>The errors, joined.</returns>
    protected string? ErrorOf(string field) =>
        _errors is not null && _errors.TryGetValue(field, out var messages) ? string.Join(' ', messages) : null;

    /// <summary>Returns whether a field has a validation error.</summary>
    /// <param name="field">The request property name.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    protected bool HasError(string field) => ErrorOf(field) is not null;

    /// <summary>Saves through a service, then closes - or shows why not.</summary>
    /// <typeparam name="TService">The service.</typeparam>
    /// <param name="save">The command.</param>
    /// <param name="confirmation">The snackbar text on success; "Saved." when null.</param>
    /// <returns>A task that completes when the save has finished.</returns>
    protected async Task SaveAsync<TService>(Func<TService, CancellationToken, Task> save, string? confirmation = null)
        where TService : notnull
    {
        Saving = true;
        Message = null;
        _errors = null;

        try
        {
            var result = await Runner.CommandAsync(save);
            if (result.Succeeded)
            {
                Snackbar.Add(confirmation ?? L[DashboardText.CommonSaved], Severity.Success);
                Dialog.Close(DialogResult.Ok(true));
                return;
            }

            _errors = result.Errors;
            Message = result.Errors is { Count: > 0 } ? null : result.Message;
        }
        finally
        {
            Saving = false;
        }
    }

    /// <summary>Closes without saving.</summary>
    protected void Cancel() => Dialog.Cancel();
}
