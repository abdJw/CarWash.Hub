namespace CarWash.Hub.Application.Common.Constants;

/// <summary>
/// Keys into the shared string resources.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// Constants rather than inline strings, so a mistyped key is a compile error instead of a
/// response body containing the key itself - which is exactly how a missing translation reaches
/// production unnoticed, since <c>IStringLocalizer</c> returns the key when a lookup fails rather
/// than throwing. <c>ResourceCompletenessTests</c> walks these constants against both resource
/// files to catch the reverse case, a key with no translation.
/// </para>
/// <para>
/// A key also serves as the language-independent <c>code</c> in problem documents, so clients
/// branch on <c>Auth_ClientSecretKeyMissing</c> rather than on English text.
/// </para>
/// </remarks>
public static class ResourceKeys
{
    /// <summary>General error messages returned in problem documents.</summary>
    public static class Errors
    {
        /// <summary>The caller is not authenticated.</summary>
        public const string Unauthorized = "Error_Unauthorized";

        /// <summary>The caller is authenticated but not allowed to do this.</summary>
        public const string Forbidden = "Error_Forbidden";

        /// <summary>A resource does not exist. {0} = what was looked for, {1} = its identifier.</summary>
        public const string NotFound = "Error_NotFound";

        /// <summary>Input failed validation.</summary>
        public const string ValidationFailed = "Error_ValidationFailed";

        /// <summary>The request conflicts with current state.</summary>
        public const string Conflict = "Error_Conflict";

        /// <summary>An unhandled failure. {0} = the correlation id to quote.</summary>
        public const string Unexpected = "Error_Unexpected";

        /// <summary>The row was changed by someone else since it was read (rowversion mismatch).</summary>
        public const string ConcurrencyConflict = "Concurrency_Conflict";

        /// <summary>A unique value is already taken.</summary>
        public const string DuplicateValue = "Error_DuplicateValue";

        /// <summary>Too many requests in the current window.</summary>
        public const string TooManyRequests = "Error_TooManyRequests";

        /// <summary>The server refused the body as too large before the API read it.</summary>
        public const string RequestTooLarge = "Error_RequestTooLarge";

        /// <summary>The server could not read the request at the HTTP level.</summary>
        public const string BadRequest = "Error_BadRequest";
    }

    /// <summary>Authentication failures. Each is also the <c>code</c> of its 401.</summary>
    public static class Auth
    {
        /// <summary>No <c>client-secret-key</c> header was sent.</summary>
        public const string ClientSecretKeyMissing = "Auth_ClientSecretKeyMissing";

        /// <summary>The <c>client-secret-key</c> header matched no configured key.</summary>
        public const string ClientSecretKeyInvalid = "Auth_ClientSecretKeyInvalid";

        /// <summary>No <c>client-read-key</c> header was sent.</summary>
        public const string ClientReadKeyMissing = "Auth_ClientReadKeyMissing";

        /// <summary>The <c>client-read-key</c> header matched no configured key.</summary>
        public const string ClientReadKeyInvalid = "Auth_ClientReadKeyInvalid";
    }

    /// <summary>
    /// Field validation templates.
    /// </summary>
    /// <remarks>
    /// Each takes the localized field name as {0}, so one message serves every field.
    /// </remarks>
    public static class Validation
    {
        /// <summary>A required field was empty. {0} = field.</summary>
        public const string Required = "Validation_Required";

        /// <summary>A value exceeded the maximum length. {0} = field, {1} = maximum.</summary>
        public const string MaxLength = "Validation_MaxLength";

        /// <summary>A query parameter or header could not be converted to its type.</summary>
        public const string MalformedRequest = "Validation_MalformedRequest";

        /// <summary>A range was given backwards. {0} = the start field, {1} = the end field.</summary>
        public const string DateRange = "Validation_DateRange";

        /// <summary>A range was longer than allowed. {0} = the start field, {1} = the end field, {2} = the maximum in days.</summary>
        public const string RangeTooLong = "Validation_RangeTooLong";

        /// <summary>{0} must be at least {1} characters.</summary>
        public const string MinLength = "Validation_MinLength";

        /// <summary>{0} must be between {1} and {2}.</summary>
        public const string Range = "Validation_Range";

        /// <summary>{0} is not a valid e-mail address.</summary>
        public const string InvalidEmail = "Validation_InvalidEmail";

        /// <summary>{0} may contain only letters, digits, '-' and '_'.</summary>
        public const string InvalidCode = "Validation_InvalidCode";

        /// <summary>A station user must belong to a station.</summary>
        public const string StationRequired = "Validation_StationRequired";

        /// <summary>A Super Admin does not belong to a station.</summary>
        public const string StationNotAllowed = "Validation_StationNotAllowed";

        /// <summary>Choose an active station.</summary>
        public const string StationInactive = "Validation_StationInactive";

        /// <summary>Choose at least one service to refill.</summary>
        public const string RefillNoItems = "Validation_RefillNoItems";

        /// <summary>Each service can appear only once in a refill.</summary>
        public const string RefillDuplicateService = "Validation_RefillDuplicateService";

        /// <summary>{0} is not tracked for refills.</summary>
        public const string RefillServiceNotTracked = "Validation_RefillServiceNotTracked";

        /// <summary>The refill time must be within the last {0} days and not in the future.</summary>
        public const string RefillTime = "Validation_RefillTime";

        /// <summary>Set both the start and the end of quiet hours, at different times, or neither for a station open 24 hours.</summary>
        public const string QuietHours = "Validation_QuietHours";
    }

    /// <summary>Localized names of fields and things, substituted into other messages.</summary>
    public static class Fields
    {
        /// <summary>The machine identifier.</summary>
        public const string MachineId = "Field_MachineId";

        /// <summary>A message's data.</summary>
        public const string Data = "Field_Data";

        /// <summary>The start of a date range.</summary>
        public const string From = "Field_From";

        /// <summary>The end of a date range.</summary>
        public const string To = "Field_To";

        /// <summary>A machine, as named in "not found" messages.</summary>
        public const string Machine = "Field_Machine";

        /// <summary>A machine message, as named in "not found" messages.</summary>
        public const string MachineMessage = "Field_MachineMessage";

        /// <summary>Station code</summary>
        public const string StationCode = "Field_StationCode";

        /// <summary>Name</summary>
        public const string Name = "Field_Name";

        /// <summary>Arabic name</summary>
        public const string NameAr = "Field_NameAr";

        /// <summary>City</summary>
        public const string City = "Field_City";

        /// <summary>Address</summary>
        public const string Address = "Field_Address";

        /// <summary>Display name</summary>
        public const string DisplayName = "Field_DisplayName";

        /// <summary>User name</summary>
        public const string UserName = "Field_UserName";

        /// <summary>E-mail</summary>
        public const string Email = "Field_Email";

        /// <summary>Password</summary>
        public const string Password = "Field_Password";

        /// <summary>Station</summary>
        public const string Station = "Field_Station";

        /// <summary>User</summary>
        public const string User = "Field_User";

        /// <summary>Service</summary>
        public const string Service = "Field_Service";

        /// <summary>Default capacity</summary>
        public const string DefaultCapacity = "Field_DefaultCapacity";

        /// <summary>Low level threshold</summary>
        public const string LowThresholdPercent = "Field_LowThresholdPercent";

        /// <summary>Order</summary>
        public const string SortOrder = "Field_SortOrder";

        /// <summary>Capacity</summary>
        public const string Capacity = "Field_Capacity";

        /// <summary>Refill time</summary>
        public const string RefilledAt = "Field_RefilledAt";

        /// <summary>Notes</summary>
        public const string Notes = "Field_Notes";

        /// <summary>Reason</summary>
        public const string VoidReason = "Field_VoidReason";

        /// <summary>Refill</summary>
        public const string Refill = "Field_Refill";

        /// <summary>Alert</summary>
        public const string Alert = "Field_Alert";

        /// <summary>Hours without activity</summary>
        public const string NoActivityAfterHours = "Field_NoActivityAfterHours";
    }

    /// <summary>Success messages carried in the <c>ApiResponse</c> envelope.</summary>
    public static class Messages
    {
        /// <summary>Generic success, used by the envelope when an endpoint names none.</summary>
        public const string Success = "Message_Success";

        /// <summary>A machine message was stored.</summary>
        public const string MessageStored = "Message_MessageStored";
    }

    /// <summary>Why a pushed machine message was refused, beyond a missing field.</summary>
    public static class Ingestion
    {
        /// <summary>The machine identifier breaks the format rule. {0} = the value, {1} = the maximum length.</summary>
        public const string MachineIdInvalid = "Ingestion_MachineIdInvalid";

        /// <summary>The data is larger than accepted. {0} = the limit in bytes.</summary>
        public const string DataTooLarge = "Ingestion_DataTooLarge";
    }

    /// <summary>Why an administrative change was refused.</summary>
    public static class Admin
    {
        /// <summary>Only a Super Admin can do this.</summary>
        public const string SuperAdminOnly = "Admin_SuperAdminOnly";

        /// <summary>You cannot deactivate yourself or change your own role or station.</summary>
        public const string CannotChangeOwnAccess = "Admin_CannotChangeOwnAccess";

        /// <summary>The current password is not correct.</summary>
        public const string CurrentPasswordWrong = "Admin_CurrentPasswordWrong";

        /// <summary>Choose a password different from the current one.</summary>
        public const string PasswordUnchanged = "Admin_PasswordUnchanged";

        /// <summary>You can only void your own refills, within 24 hours of recording them.</summary>
        public const string VoidNotAllowed = "Admin_VoidNotAllowed";

        /// <summary>This refill is already voided.</summary>
        public const string RefillAlreadyVoided = "Admin_RefillAlreadyVoided";
    }
}
