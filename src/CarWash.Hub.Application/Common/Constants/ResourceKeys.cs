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
}
