namespace CarWash.Hub.Web.Localization;

/// <summary>
/// Keys into the dashboard's string resources.
/// </summary>
/// <remarks>
/// Layer: Web. Constants, as in <c>ResourceKeys</c>, so a mistyped key is a compile error rather
/// than a key shown on screen. <c>DashboardResourceCompletenessTests</c> checks every constant
/// against both <c>DashboardResource</c> files. Messages that the Application layer returns - a
/// validation error, a "not found" - stay in <c>SharedResource</c>; these are the dashboard's own
/// words.
/// </remarks>
public static class DashboardText
{
    /// <summary>CarWash Hub</summary>
    public const string AppName = "App_Name";

    /// <summary>Loading the dashboard…</summary>
    public const string AppLoading = "App_Loading";

    /// <summary>Overview</summary>
    public const string NavOverview = "Nav_Overview";

    /// <summary>Machines</summary>
    public const string NavMachines = "Nav_Machines";

    /// <summary>Menu</summary>
    public const string NavMenu = "Nav_Menu";

    /// <summary>Sign out</summary>
    public const string NavSignOut = "Nav_SignOut";

    /// <summary>Dark mode</summary>
    public const string NavDarkMode = "Nav_DarkMode";

    /// <summary>Light mode</summary>
    public const string NavLightMode = "Nav_LightMode";

    /// <summary>Language</summary>
    public const string NavLanguage = "Nav_Language";

    /// <summary>Super Admin</summary>
    public const string RoleSuperAdmin = "Role_SuperAdmin";

    /// <summary>Station user</summary>
    public const string RoleStationUser = "Role_StationUser";

    /// <summary>Sign in</summary>
    public const string LoginTitle = "Login_Title";

    /// <summary>Machines, usage and refills in one place.</summary>
    public const string LoginSubtitle = "Login_Subtitle";

    /// <summary>User name or e-mail</summary>
    public const string LoginUserName = "Login_UserName";

    /// <summary>Password</summary>
    public const string LoginPassword = "Login_Password";

    /// <summary>Keep me signed in on this device</summary>
    public const string LoginRememberMe = "Login_RememberMe";

    /// <summary>Sign in</summary>
    public const string LoginSubmit = "Login_Submit";

    /// <summary>Enter your user name and password.</summary>
    public const string LoginRequired = "Login_Required";

    /// <summary>The user name or password is incorrect.</summary>
    public const string LoginFailed = "Login_Failed";

    /// <summary>Too many failed attempts. Try again in {0} minutes.</summary>
    public const string LoginLockedOut = "Login_LockedOut";

    /// <summary>Sign out of CarWash Hub on this device?</summary>
    public const string LogoutPrompt = "Logout_Prompt";

    /// <summary>Sign out</summary>
    public const string LogoutConfirm = "Logout_Confirm";

    /// <summary>You have signed out.</summary>
    public const string LogoutDone = "Logout_Done";

    /// <summary>Sign in again</summary>
    public const string LogoutSignInAgain = "Logout_SignInAgain";

    /// <summary>Cancel</summary>
    public const string CommonCancel = "Common_Cancel";

    /// <summary>Back</summary>
    public const string CommonBack = "Common_Back";

    /// <summary>Back to the overview</summary>
    public const string CommonBackToOverview = "Common_BackToOverview";

    /// <summary>No access</summary>
    public const string AccessDeniedTitle = "AccessDenied_Title";

    /// <summary>Your account cannot open this page.</summary>
    public const string AccessDeniedText = "AccessDenied_Text";

    /// <summary>Page not found</summary>
    public const string NotFoundTitle = "NotFound_Title";

    /// <summary>This page does not exist, or it is not available to you.</summary>
    public const string NotFoundText = "NotFound_Text";

    /// <summary>Something went wrong</summary>
    public const string ErrorTitle = "Error_Title";

    /// <summary>The page could not be shown. Reference: {0}</summary>
    public const string ErrorText = "Error_Text";

    /// <summary>This part of the page failed to load.</summary>
    public const string ErrorBoundary = "Error_Boundary";

    /// <summary>Reload</summary>
    public const string ErrorReload = "Error_Reload";

    /// <summary>Live</summary>
    public const string LiveLabel = "Live_Label";

    /// <summary>Updated {0}</summary>
    public const string LiveUpdated = "Live_Updated";

    /// <summary>24 hours</summary>
    public const string Range24Hours = "Range_24Hours";

    /// <summary>7 days</summary>
    public const string Range7Days = "Range_7Days";

    /// <summary>30 days</summary>
    public const string Range30Days = "Range_30Days";

    /// <summary>Uses today</summary>
    public const string KpiUsesToday = "Kpi_UsesToday";

    /// <summary>vs. yesterday at this time</summary>
    public const string KpiVsYesterday = "Kpi_VsYesterday";

    /// <summary>same as yesterday at this time</summary>
    public const string KpiSameAsYesterday = "Kpi_SameAsYesterday";

    /// <summary>Machines in service</summary>
    public const string KpiMachines = "Kpi_Machines";

    /// <summary>{0} in use now</summary>
    public const string KpiBusyNow = "Kpi_BusyNow";

    /// <summary>Silent machines</summary>
    public const string KpiSilent = "Kpi_Silent";

    /// <summary>No message for {0} hours or more</summary>
    public const string KpiSilentHint = "Kpi_SilentHint";

    /// <summary>Every machine is reporting</summary>
    public const string KpiAllReporting = "Kpi_AllReporting";

    /// <summary>Unassigned machines</summary>
    public const string KpiUnassigned = "Kpi_Unassigned";

    /// <summary>Waiting to be assigned to a station</summary>
    public const string KpiUnassignedHint = "Kpi_UnassignedHint";

    /// <summary>Uses per hour, last 12 hours</summary>
    public const string KpiLast12Hours = "Kpi_Last12Hours";

    /// <summary>Uses by service</summary>
    public const string ChartUsageTitle = "Chart_UsageTitle";

    /// <summary>Show as table</summary>
    public const string ChartShowTable = "Chart_ShowTable";

    /// <summary>Show as chart</summary>
    public const string ChartShowChart = "Chart_ShowChart";

    /// <summary>Total</summary>
    public const string ChartTotal = "Chart_Total";

    /// <summary>Time</summary>
    public const string ChartTime = "Chart_Time";

    /// <summary>No uses in this period.</summary>
    public const string ChartNoData = "Chart_NoData";

    /// <summary>{0} uses from {1} to {2}</summary>
    public const string ChartSummary = "Chart_Summary";

    /// <summary>{0} uses</summary>
    public const string ChartUses = "Chart_Uses";

    /// <summary>Overview</summary>
    public const string OverviewTitle = "Overview_Title";

    /// <summary>Today by service</summary>
    public const string OverviewTodayByService = "Overview_TodayByService";

    /// <summary>Busiest machines today</summary>
    public const string OverviewBusiest = "Overview_Busiest";

    /// <summary>All machines</summary>
    public const string OverviewAllMachines = "Overview_AllMachines";

    /// <summary>No uses yet today.</summary>
    public const string OverviewNoUsesToday = "Overview_NoUsesToday";

    /// <summary>Machines</summary>
    public const string MachinesTitle = "Machines_Title";

    /// <summary>Search by name, MAC address or station</summary>
    public const string MachinesSearch = "Machines_Search";

    /// <summary>No machine matches.</summary>
    public const string MachinesEmpty = "Machines_Empty";

    /// <summary>No machine has sent a message yet. Machines appear here on their own as soon as they report.</summary>
    public const string MachinesNoneYet = "Machines_NoneYet";

    /// <summary>{0} of {1} machines</summary>
    public const string MachinesCount = "Machines_Count";

    /// <summary>All</summary>
    public const string FilterAll = "Filter_All";

    /// <summary>In use</summary>
    public const string FilterBusy = "Filter_Busy";

    /// <summary>Idle</summary>
    public const string FilterIdle = "Filter_Idle";

    /// <summary>Silent</summary>
    public const string FilterSilent = "Filter_Silent";

    /// <summary>Unassigned</summary>
    public const string FilterUnassigned = "Filter_Unassigned";

    /// <summary>Sort by</summary>
    public const string SortLabel = "Sort_Label";

    /// <summary>Name</summary>
    public const string SortName = "Sort_Name";

    /// <summary>Last seen</summary>
    public const string SortLastSeen = "Sort_LastSeen";

    /// <summary>Uses today</summary>
    public const string SortUsesToday = "Sort_UsesToday";

    /// <summary>In use now</summary>
    public const string ActivityBusy = "Activity_Busy";

    /// <summary>Idle · {0}</summary>
    public const string ActivityIdle = "Activity_Idle";

    /// <summary>Silent · {0}</summary>
    public const string ActivitySilent = "Activity_Silent";

    /// <summary>Not heard from yet</summary>
    public const string ActivityUnknown = "Activity_Unknown";

    /// <summary>No station</summary>
    public const string MachineUnassigned = "Machine_Unassigned";

    /// <summary>Out of service</summary>
    public const string MachineInactive = "Machine_Inactive";

    /// <summary>Uses today</summary>
    public const string MachineUsesToday = "Machine_UsesToday";

    /// <summary>No uses today</summary>
    public const string MachineNoUsesToday = "Machine_NoUsesToday";

    /// <summary>Last use {0}</summary>
    public const string MachineLastUse = "Machine_LastUse";

    /// <summary>MAC address</summary>
    public const string MachineMac = "Machine_Mac";

    /// <summary>Station</summary>
    public const string MachineStation = "Machine_Station";

    /// <summary>First seen</summary>
    public const string MachineFirstSeen = "Machine_FirstSeen";

    /// <summary>Last message</summary>
    public const string MachineLastSeen = "Machine_LastSeen";

    /// <summary>Uses since first seen</summary>
    public const string MachineTotalUses = "Machine_TotalUses";

    /// <summary>Uses in this period</summary>
    public const string MachineUsesInRange = "Machine_UsesInRange";

    /// <summary>Latest uses</summary>
    public const string MachineRecentUses = "Machine_RecentUses";

    /// <summary>Latest messages</summary>
    public const string MachineRecentMessages = "Machine_RecentMessages";

    /// <summary>No uses yet.</summary>
    public const string MachineNoUses = "Machine_NoUses";

    /// <summary>No messages yet.</summary>
    public const string MachineNoMessages = "Machine_NoMessages";

    /// <summary>This machine does not exist, or it is not at your station.</summary>
    public const string MachineNotFound = "Machine_NotFound";

    /// <summary>Open {0}</summary>
    public const string MachineOpen = "Machine_Open";

    /// <summary>Pending</summary>
    public const string MessageStatusPending = "MessageStatus_Pending";

    /// <summary>Counted</summary>
    public const string MessageStatusProcessed = "MessageStatus_Processed";

    /// <summary>Not a use</summary>
    public const string MessageStatusIgnored = "MessageStatus_Ignored";

    /// <summary>Failed</summary>
    public const string MessageStatusFailed = "MessageStatus_Failed";

    /// <summary>just now</summary>
    public const string TimeJustNow = "Time_JustNow";

    /// <summary>{0} min ago</summary>
    public const string TimeMinutesAgo = "Time_MinutesAgo";

    /// <summary>{0} h ago</summary>
    public const string TimeHoursAgo = "Time_HoursAgo";

    /// <summary>{0} d ago</summary>
    public const string TimeDaysAgo = "Time_DaysAgo";

    /// <summary>{0} min</summary>
    public const string DurationMinutes = "Duration_Minutes";

    /// <summary>{0} h</summary>
    public const string DurationHours = "Duration_Hours";

    /// <summary>{0} d</summary>
    public const string DurationDays = "Duration_Days";

    /// <summary>Administration</summary>
    public const string NavAdmin = "Nav_Admin";

    /// <summary>Stations</summary>
    public const string NavStations = "Nav_Stations";

    /// <summary>New machines</summary>
    public const string NavInbox = "Nav_Inbox";

    /// <summary>Users</summary>
    public const string NavUsers = "Nav_Users";

    /// <summary>Services</summary>
    public const string NavServices = "Nav_Services";

    /// <summary>Change password</summary>
    public const string NavChangePassword = "Nav_ChangePassword";

    /// <summary>Save</summary>
    public const string CommonSave = "Common_Save";

    /// <summary>Edit</summary>
    public const string CommonEdit = "Common_Edit";

    /// <summary>Saved.</summary>
    public const string CommonSaved = "Common_Saved";

    /// <summary>In service</summary>
    public const string CommonActive = "Common_Active";

    /// <summary>Out of service</summary>
    public const string CommonInactive = "Common_Inactive";

    /// <summary>Administration</summary>
    public const string AdminTitle = "Admin_Title";

    /// <summary>Machines that have reported but belong to no station yet</summary>
    public const string AdminInboxHint = "Admin_InboxHint";

    /// <summary>Gas stations, and where each machine is installed</summary>
    public const string AdminStationsHint = "Admin_StationsHint";

    /// <summary>Who can sign in, and what they can see</summary>
    public const string AdminUsersHint = "Admin_UsersHint";

    /// <summary>Names, refill tracking and low-level thresholds</summary>
    public const string AdminServicesHint = "Admin_ServicesHint";

    /// <summary>Code</summary>
    public const string FieldCode = "Field_Code";

    /// <summary>Name in English</summary>
    public const string FieldNameEn = "Field_NameEn";

    /// <summary>Name in Arabic</summary>
    public const string FieldNameAr = "Field_NameAr";

    /// <summary>City</summary>
    public const string FieldCity = "Field_City";

    /// <summary>Address</summary>
    public const string FieldAddress = "Field_Address";

    /// <summary>In service</summary>
    public const string FieldInService = "Field_InService";

    /// <summary>Machine name</summary>
    public const string FieldMachineName = "Field_MachineName";

    /// <summary>For example "Bay 2 – vacuum". Leave empty to show the MAC address.</summary>
    public const string FieldMachineNameHint = "Field_MachineNameHint";

    /// <summary>Station</summary>
    public const string FieldStation = "Field_Station";

    /// <summary>No station (stays in New machines)</summary>
    public const string FieldNoStation = "Field_NoStation";

    /// <summary>User name</summary>
    public const string FieldUserName = "Field_UserName";

    /// <summary>E-mail</summary>
    public const string FieldEmail = "Field_Email";

    /// <summary>Full name</summary>
    public const string FieldDisplayName = "Field_DisplayName";

    /// <summary>Role</summary>
    public const string FieldRole = "Field_Role";

    /// <summary>Can sign in</summary>
    public const string FieldCanSignIn = "Field_CanSignIn";

    /// <summary>Temporary password</summary>
    public const string FieldTemporaryPassword = "Field_TemporaryPassword";

    /// <summary>At least 8 characters. They choose their own at the first sign-in.</summary>
    public const string FieldTemporaryPasswordHint = "Field_TemporaryPasswordHint";

    /// <summary>Track refills</summary>
    public const string FieldTracksRefill = "Field_TracksRefill";

    /// <summary>Default capacity (uses)</summary>
    public const string FieldDefaultCapacity = "Field_DefaultCapacity";

    /// <summary>Low level at (%)</summary>
    public const string FieldLowThreshold = "Field_LowThreshold";

    /// <summary>Order</summary>
    public const string FieldSortOrder = "Field_SortOrder";

    /// <summary>Icon</summary>
    public const string FieldIcon = "Field_Icon";

    /// <summary>Stations</summary>
    public const string StationsTitle = "Stations_Title";

    /// <summary>Add station</summary>
    public const string StationsAdd = "Stations_Add";

    /// <summary>No station yet. Add the first one, then assign machines to it.</summary>
    public const string StationsEmpty = "Stations_Empty";

    /// <summary>New station</summary>
    public const string StationsNew = "Stations_New";

    /// <summary>Edit station</summary>
    public const string StationsEdit = "Stations_Edit";

    /// <summary>{0} machines</summary>
    public const string StationsMachines = "Stations_Machines";

    /// <summary>{0} users</summary>
    public const string StationsUsers = "Stations_Users";

    /// <summary>New machines</summary>
    public const string InboxTitle = "Inbox_Title";

    /// <summary>These machines have sent messages but belong to no station. Name each one and assign it to its station; until then, station users cannot see it.</summary>
    public const string InboxIntro = "Inbox_Intro";

    /// <summary>No new machines: every machine has a station.</summary>
    public const string InboxEmpty = "Inbox_Empty";

    /// <summary>Name and assign</summary>
    public const string InboxAssign = "Inbox_Assign";

    /// <summary>First seen {0}</summary>
    public const string InboxFirstSeen = "Inbox_FirstSeen";

    /// <summary>Machine settings</summary>
    public const string MachineEdit = "Machine_Edit";

    /// <summary>Users</summary>
    public const string UsersTitle = "Users_Title";

    /// <summary>Add user</summary>
    public const string UsersAdd = "Users_Add";

    /// <summary>New user</summary>
    public const string UsersNew = "Users_New";

    /// <summary>Edit user</summary>
    public const string UsersEdit = "Users_Edit";

    /// <summary>Reset password</summary>
    public const string UsersResetPassword = "Users_ResetPassword";

    /// <summary>Password reset. Give it to {0}: they must change it at the next sign-in.</summary>
    public const string UsersResetDone = "Users_ResetDone";

    /// <summary>Locked until {0}</summary>
    public const string UsersLocked = "Users_Locked";

    /// <summary>Must choose a password</summary>
    public const string UsersMustChange = "Users_MustChange";

    /// <summary>Last sign-in {0}</summary>
    public const string UsersLastSignIn = "Users_LastSignIn";

    /// <summary>Never signed in</summary>
    public const string UsersNeverSignedIn = "Users_NeverSignedIn";

    /// <summary>You</summary>
    public const string UsersYou = "Users_You";

    /// <summary>Services</summary>
    public const string ServicesTitle = "Services_Title";

    /// <summary>Services appear here on their own the first time a machine reports one. Review new ones, and set how refills are tracked.</summary>
    public const string ServicesIntro = "Services_Intro";

    /// <summary>New – review</summary>
    public const string ServicesNeedsReview = "Services_NeedsReview";

    /// <summary>Refills tracked</summary>
    public const string ServicesTracked = "Services_Tracked";

    /// <summary>Refills not tracked</summary>
    public const string ServicesNotTracked = "Services_NotTracked";

    /// <summary>Default {0} uses</summary>
    public const string ServicesCapacity = "Services_Capacity";

    /// <summary>Low at {0}%</summary>
    public const string ServicesThreshold = "Services_Threshold";

    /// <summary>Edit service</summary>
    public const string ServicesEdit = "Services_Edit";

    /// <summary>Change password</summary>
    public const string PasswordTitle = "Password_Title";

    /// <summary>At least 8 characters. Your other devices are signed out.</summary>
    public const string PasswordIntro = "Password_Intro";

    /// <summary>Choose your own password before you continue.</summary>
    public const string PasswordMustChange = "Password_MustChange";

    /// <summary>Current password</summary>
    public const string PasswordCurrent = "Password_Current";

    /// <summary>New password</summary>
    public const string PasswordNew = "Password_New";

    /// <summary>Confirm the new password</summary>
    public const string PasswordConfirm = "Password_Confirm";

    /// <summary>The two new passwords do not match.</summary>
    public const string PasswordMismatch = "Password_Mismatch";

    /// <summary>Your password has been changed.</summary>
    public const string PasswordChanged = "Password_Changed";

    /// <summary>Continue</summary>
    public const string PasswordContinue = "Password_Continue";

    /// <summary>Levels</summary>
    public const string LevelsTitle = "Levels_Title";

    /// <summary>No service on this machine is tracked for refills.</summary>
    public const string LevelsNoneTracked = "Levels_NoneTracked";

    /// <summary>OK</summary>
    public const string LevelOk = "Level_Ok";

    /// <summary>Low</summary>
    public const string LevelLow = "Level_Low";

    /// <summary>Empty</summary>
    public const string LevelEmpty = "Level_Empty";

    /// <summary>Used after empty</summary>
    public const string LevelOverused = "Level_Overused";

    /// <summary>No refill recorded</summary>
    public const string LevelNeverRefilled = "Level_NeverRefilled";

    /// <summary>{0} of {1} uses left</summary>
    public const string LevelLeft = "Level_Left";

    /// <summary>{0} uses after empty</summary>
    public const string LevelOverBy = "Level_OverBy";

    /// <summary>Runs out in ~{0}</summary>
    public const string LevelRunsOutIn = "Level_RunsOutIn";

    /// <summary>expected {0}</summary>
    public const string LevelRunsOutAt = "Level_RunsOutAt";

    /// <summary>Refilled {0}</summary>
    public const string LevelRefilledAgo = "Level_RefilledAgo";

    /// <summary>Refill</summary>
    public const string RefillAction = "Refill_Action";

    /// <summary>Record a refill</summary>
    public const string RefillTitle = "Refill_Title";

    /// <summary>Services refilled</summary>
    public const string RefillServices = "Refill_Services";

    /// <summary>All services</summary>
    public const string RefillSelectAll = "Refill_SelectAll";

    /// <summary>Uses until the next refill</summary>
    public const string RefillCapacity = "Refill_Capacity";

    /// <summary>Now</summary>
    public const string RefillNow = "Refill_Now";

    /// <summary>Earlier</summary>
    public const string RefillEarlier = "Refill_Earlier";

    /// <summary>When was it refilled?</summary>
    public const string RefillWhen = "Refill_When";

    /// <summary>Date</summary>
    public const string RefillDate = "Refill_Date";

    /// <summary>Time</summary>
    public const string RefillTime = "Refill_Time";

    /// <summary>Notes (optional)</summary>
    public const string RefillNotes = "Refill_Notes";

    /// <summary>Save refill</summary>
    public const string RefillSave = "Refill_Save";

    /// <summary>Refill saved. The levels update in a moment.</summary>
    public const string RefillSaved = "Refill_Saved";

    /// <summary>No service is tracked for refills yet. A Super Admin can turn tracking on under Services.</summary>
    public const string RefillNoneTracked = "Refill_NoneTracked";

    /// <summary>Now: {0}</summary>
    public const string RefillNowLevel = "Refill_NowLevel";

    /// <summary>Refill history</summary>
    public const string RefillHistoryTitle = "RefillHistory_Title";

    /// <summary>No refill recorded yet.</summary>
    public const string RefillHistoryEmpty = "RefillHistory_Empty";

    /// <summary>by {0}</summary>
    public const string RefillBy = "Refill_By";

    /// <summary>Void</summary>
    public const string RefillVoid = "Refill_Void";

    /// <summary>Voided</summary>
    public const string RefillVoided = "Refill_Voided";

    /// <summary>Voided by {0}</summary>
    public const string RefillVoidedBy = "Refill_VoidedBy";

    /// <summary>Void this refill?</summary>
    public const string RefillVoidTitle = "Refill_VoidTitle";

    /// <summary>The levels are recomputed as if it had never been recorded. It stays in the history, struck through.</summary>
    public const string RefillVoidHint = "Refill_VoidHint";

    /// <summary>Reason</summary>
    public const string RefillVoidReason = "Refill_VoidReason";

    /// <summary>Void refill</summary>
    public const string RefillVoidConfirm = "Refill_VoidConfirm";

    /// <summary>Refill voided.</summary>
    public const string RefillVoidDone = "Refill_VoidDone";

    /// <summary>Refill saved</summary>
    public const string RefillPageDone = "RefillPage_Done";

    /// <summary>Open the machine</summary>
    public const string RefillPageOpenMachine = "RefillPage_OpenMachine";

    /// <summary>QR sticker</summary>
    public const string StickerTitle = "Sticker_Title";

    /// <summary>Scan to record a refill</summary>
    public const string StickerScan = "Sticker_Scan";

    /// <summary>Print</summary>
    public const string StickerPrint = "Sticker_Print";

    /// <summary>Print this and stick it on the machine. Scanning it with a phone camera opens this machine's refill page.</summary>
    public const string StickerHint = "Sticker_Hint";

    /// <summary>Need a refill</summary>
    public const string KpiNeedsRefill = "Kpi_NeedsRefill";

    /// <summary>Low, empty or used after empty</summary>
    public const string KpiNeedsRefillHint = "Kpi_NeedsRefillHint";

    /// <summary>Run out within 24 hours</summary>
    public const string KpiRunsOutSoon = "Kpi_RunsOutSoon";

    /// <summary>At their usual pace</summary>
    public const string KpiRunsOutSoonHint = "Kpi_RunsOutSoonHint";

    /// <summary>Needs attention</summary>
    public const string OverviewAttention = "Overview_Attention";

    /// <summary>Nothing needs attention right now.</summary>
    public const string OverviewAllGood = "Overview_AllGood";

    /// <summary>Needs refill</summary>
    public const string FilterNeedsRefill = "Filter_NeedsRefill";

    /// <summary>Alerts</summary>
    public const string NavAlerts = "Nav_Alerts";

    /// <summary>Alerts</summary>
    public const string AlertsTitle = "Alerts_Title";

    /// <summary>Raised when a service runs low or out, or a machine goes quiet. Recording a refill closes the refill alerts by itself.</summary>
    public const string AlertsIntro = "Alerts_Intro";

    /// <summary>Open</summary>
    public const string AlertsFilterOpen = "Alerts_FilterOpen";

    /// <summary>All</summary>
    public const string AlertsFilterAll = "Alerts_FilterAll";

    /// <summary>All types</summary>
    public const string AlertsKindAll = "Alerts_KindAll";

    /// <summary>Refill</summary>
    public const string AlertsKindRefill = "Alerts_KindRefill";

    /// <summary>No activity</summary>
    public const string AlertsKindNoActivity = "Alerts_KindNoActivity";

    /// <summary>No open alerts. Every machine is fine.</summary>
    public const string AlertsNoneOpen = "Alerts_NoneOpen";

    /// <summary>No alerts yet.</summary>
    public const string AlertsNone = "Alerts_None";

    /// <summary>Show more</summary>
    public const string AlertsLoadMore = "Alerts_LoadMore";

    /// <summary>View all alerts</summary>
    public const string AlertsViewAll = "Alerts_ViewAll";

    /// <summary>{0} open</summary>
    public const string AlertsOpenCount = "Alerts_OpenCount";

    /// <summary>Alerts: {0} need attention</summary>
    public const string AlertsBellLabel = "Alerts_BellLabel";

    /// <summary>No open alerts</summary>
    public const string AlertsAllGood = "Alerts_AllGood";

    /// <summary>Alert sound: on</summary>
    public const string AlertsSoundOn = "Alerts_SoundOn";

    /// <summary>Alert sound: off</summary>
    public const string AlertsSoundOff = "Alerts_SoundOff";

    /// <summary>{0} new alerts</summary>
    public const string AlertsNew = "Alerts_New";

    /// <summary>Open</summary>
    public const string AlertsOpenAction = "Alerts_OpenAction";

    /// <summary>View</summary>
    public const string AlertsViewAction = "Alerts_ViewAction";

    /// <summary>Acknowledge</summary>
    public const string AlertAcknowledge = "Alert_Acknowledge";

    /// <summary>Acknowledged by {0} · {1}</summary>
    public const string AlertAcknowledgedBy = "Alert_AcknowledgedBy";

    /// <summary>Acknowledged · {0}</summary>
    public const string AlertAcknowledged = "Alert_Acknowledged";

    /// <summary>Runs out soon</summary>
    public const string AlertTypeRunsOutSoon = "AlertType_RunsOutSoon";

    /// <summary>Low</summary>
    public const string AlertTypeLow = "AlertType_Low";

    /// <summary>Empty</summary>
    public const string AlertTypeEmpty = "AlertType_Empty";

    /// <summary>Used after empty</summary>
    public const string AlertTypeUsedAfterEmpty = "AlertType_UsedAfterEmpty";

    /// <summary>No activity</summary>
    public const string AlertTypeNoActivity = "AlertType_NoActivity";

    /// <summary>{0} on {1} will run out in about {2}.</summary>
    public const string AlertTextRunsOutSoon = "AlertText_RunsOutSoon";

    /// <summary>{0} on {1} was expected to run out around {2}.</summary>
    public const string AlertTextRunsOutSoonAt = "AlertText_RunsOutSoonAt";

    /// <summary>{0} is low on {1}: {2} of {3} uses left.</summary>
    public const string AlertTextLow = "AlertText_Low";

    /// <summary>{0} has run out on {1}.</summary>
    public const string AlertTextEmpty = "AlertText_Empty";

    /// <summary>{0} on {1} is still being used after running out.</summary>
    public const string AlertTextUsedAfterEmpty = "AlertText_UsedAfterEmpty";

    /// <summary>Nothing from {0} since {1}.</summary>
    public const string AlertTextNoActivity = "AlertText_NoActivity";

    /// <summary>Closed: refilled</summary>
    public const string AlertResolutionRefilled = "AlertResolution_Refilled";

    /// <summary>Closed: replaced by a more urgent alert</summary>
    public const string AlertResolutionEscalated = "AlertResolution_Escalated";

    /// <summary>Closed: no longer applies</summary>
    public const string AlertResolutionCleared = "AlertResolution_Cleared";

    /// <summary>Closed: the machine reported again</summary>
    public const string AlertResolutionActivityResumed = "AlertResolution_ActivityResumed";

    /// <summary>Closed: machine out of service or unassigned</summary>
    public const string AlertResolutionNotEligible = "AlertResolution_NotEligible";

    /// <summary>Alerts</summary>
    public const string MachineAlerts = "Machine_Alerts";

    /// <summary>No alerts for this machine.</summary>
    public const string MachineAlertsNone = "Machine_AlertsNone";

    /// <summary>Alert after hours without activity</summary>
    public const string FieldNoActivityHours = "Field_NoActivityHours";

    /// <summary>Leave empty for the default: {0} hours, not counting the station's quiet hours.</summary>
    public const string FieldNoActivityHoursHint = "Field_NoActivityHoursHint";

    /// <summary>Open 24 hours</summary>
    public const string FieldOpen24Hours = "Field_Open24Hours";

    /// <summary>Quiet hours from</summary>
    public const string FieldQuietFrom = "Field_QuietFrom";

    /// <summary>Until</summary>
    public const string FieldQuietTo = "Field_QuietTo";

    /// <summary>Silence during quiet hours, usually while the station is closed, does not raise a no-activity alert.</summary>
    public const string FieldQuietHint = "Field_QuietHint";

    /// <summary>Quiet {0}–{1}</summary>
    public const string StationsQuietHours = "Stations_QuietHours";

    /// <summary>Open 24 hours</summary>
    public const string StationsOpen24 = "Stations_Open24";

    /// <summary>Finance</summary>
    public const string NavFinance = "Nav_Finance";

    /// <summary>Finance</summary>
    public const string FinanceTitle = "Finance_Title";

    /// <summary>Period</summary>
    public const string FinancePeriod = "Finance_Period";

    /// <summary>Today</summary>
    public const string FinancePresetToday = "Finance_PresetToday";

    /// <summary>Yesterday</summary>
    public const string FinancePresetYesterday = "Finance_PresetYesterday";

    /// <summary>This week</summary>
    public const string FinancePresetThisWeek = "Finance_PresetThisWeek";

    /// <summary>This month</summary>
    public const string FinancePresetThisMonth = "Finance_PresetThisMonth";

    /// <summary>Last month</summary>
    public const string FinancePresetLastMonth = "Finance_PresetLastMonth";

    /// <summary>Last 30 days</summary>
    public const string FinancePresetLast30Days = "Finance_PresetLast30Days";

    /// <summary>This year</summary>
    public const string FinancePresetThisYear = "Finance_PresetThisYear";

    /// <summary>Custom range</summary>
    public const string FinancePresetCustom = "Finance_PresetCustom";

    /// <summary>From – to</summary>
    public const string FinanceCustomRange = "Finance_CustomRange";

    /// <summary>Stations</summary>
    public const string FinanceStations = "Finance_Stations";

    /// <summary>All stations</summary>
    public const string FinanceAllStations = "Finance_AllStations";

    /// <summary>Services</summary>
    public const string FinanceServices = "Finance_Services";

    /// <summary>All services</summary>
    public const string FinanceAllServices = "Finance_AllServices";

    /// <summary>Machine</summary>
    public const string FinanceMachine = "Finance_Machine";

    /// <summary>All machines</summary>
    public const string FinanceAllMachines = "Finance_AllMachines";

    /// <summary>{0} selected</summary>
    public const string FinanceSelectedCount = "Finance_SelectedCount";

    /// <summary>Excel</summary>
    public const string FinanceExportExcel = "Finance_ExportExcel";

    /// <summary>CSV</summary>
    public const string FinanceExportCsv = "Finance_ExportCsv";

    /// <summary>Revenue</summary>
    public const string FinanceKpiRevenue = "Finance_KpiRevenue";

    /// <summary>Uses</summary>
    public const string FinanceKpiUses = "Finance_KpiUses";

    /// <summary>Average per day</summary>
    public const string FinanceKpiAverage = "Finance_KpiAverage";

    /// <summary>Unpriced uses</summary>
    public const string FinanceKpiUnpriced = "Finance_KpiUnpriced";

    /// <summary>vs {0} – {1}</summary>
    public const string FinanceVsPrevious = "Finance_VsPrevious";

    /// <summary>Same as the period before</summary>
    public const string FinanceSameAsPrevious = "Finance_SameAsPrevious";

    /// <summary>Over {0} days</summary>
    public const string FinanceAverageHint = "Finance_AverageHint";

    /// <summary>Set prices to count them</summary>
    public const string FinanceUnpricedHint = "Finance_UnpricedHint";

    /// <summary>Ask an administrator to set the prices</summary>
    public const string FinanceUnpricedHintStation = "Finance_UnpricedHintStation";

    /// <summary>Every use has a price</summary>
    public const string FinanceAllPriced = "Finance_AllPriced";

    /// <summary>{0} uses on machines not assigned to a station are not included.</summary>
    public const string FinanceUnassignedNote = "Finance_UnassignedNote";

    /// <summary>Assign machines</summary>
    public const string FinanceAssignMachines = "Finance_AssignMachines";

    /// <summary>Revenue over time</summary>
    public const string FinanceChartTitle = "Finance_ChartTitle";

    /// <summary>{0} in total · {1} – {2}</summary>
    public const string FinanceChartSummary = "Finance_ChartSummary";

    /// <summary>Measure</summary>
    public const string FinanceMeasure = "Finance_Measure";

    /// <summary>Revenue</summary>
    public const string FinanceMeasureRevenue = "Finance_MeasureRevenue";

    /// <summary>Uses</summary>
    public const string FinanceMeasureUses = "Finance_MeasureUses";

    /// <summary>Group by</summary>
    public const string FinanceGrainLabel = "Finance_GrainLabel";

    /// <summary>Day</summary>
    public const string FinanceGrainDay = "Finance_GrainDay";

    /// <summary>Week</summary>
    public const string FinanceGrainWeek = "Finance_GrainWeek";

    /// <summary>Month</summary>
    public const string FinanceGrainMonth = "Finance_GrainMonth";

    /// <summary>Breakdown</summary>
    public const string FinanceBreakdownTitle = "Finance_BreakdownTitle";

    /// <summary>By station</summary>
    public const string FinanceByStation = "Finance_ByStation";

    /// <summary>By service</summary>
    public const string FinanceByService = "Finance_ByService";

    /// <summary>By machine</summary>
    public const string FinanceByMachine = "Finance_ByMachine";

    /// <summary>Uses</summary>
    public const string FinanceColUses = "Finance_ColUses";

    /// <summary>Revenue ({0})</summary>
    public const string FinanceColRevenue = "Finance_ColRevenue";

    /// <summary>Share</summary>
    public const string FinanceColShare = "Finance_ColShare";

    /// <summary>Unpriced</summary>
    public const string FinanceColUnpriced = "Finance_ColUnpriced";

    /// <summary>No uses in this period.</summary>
    public const string FinanceNoRows = "Finance_NoRows";

    /// <summary>JOD</summary>
    public const string FinanceCurrency = "Finance_Currency";

    /// <summary>Prices · {0}</summary>
    public const string FinancePricesTitle = "Finance_PricesTitle";

    /// <summary>Edit prices</summary>
    public const string FinanceEditPrices = "Finance_EditPrices";

    /// <summary>No price</summary>
    public const string FinanceNoPrice = "Finance_NoPrice";

    /// <summary>since {0}</summary>
    public const string FinanceSince = "Finance_Since";

    /// <summary>Service</summary>
    public const string FieldService = "Field_Service";

    /// <summary>Price per use</summary>
    public const string FieldPrice = "Field_Price";

    /// <summary>Effective from</summary>
    public const string FieldEffectiveFrom = "Field_EffectiveFrom";

    /// <summary>Note (optional)</summary>
    public const string FieldPriceNote = "Field_PriceNote";

    /// <summary>Done</summary>
    public const string CommonDone = "Common_Done";

    /// <summary>Prices</summary>
    public const string StationsPrices = "Stations_Prices";

    /// <summary>No prices set</summary>
    public const string StationsNoPrices = "Stations_NoPrices";

    /// <summary>Prices · {0}</summary>
    public const string PricesTitle = "Prices_Title";

    /// <summary>Set a price</summary>
    public const string PricesSetTitle = "Prices_SetTitle";

    /// <summary>A price applies from the chosen day on, and earlier days keep theirs. Choosing a past day revalues the uses since then.</summary>
    public const string PricesHint = "Prices_Hint";

    /// <summary>Save price</summary>
    public const string PricesSet = "Prices_Set";

    /// <summary>Price saved</summary>
    public const string PricesSaved = "Prices_Saved";

    /// <summary>No price: uses are unpriced</summary>
    public const string PricesNone = "Prices_None";

    /// <summary>{0} from {1}</summary>
    public const string PricesUpcoming = "Prices_Upcoming";

    /// <summary>Price history ({0})</summary>
    public const string PricesHistory = "Prices_History";

    /// <summary>Set by</summary>
    public const string PricesSetBy = "Prices_SetBy";

    /// <summary>Remove</summary>
    public const string PricesRemove = "Prices_Remove";

    /// <summary>Remove this price?</summary>
    public const string PricesRemoveTitle = "Prices_RemoveTitle";

    /// <summary>The {0} price of {1} from {2} will be removed, and the price before it applies again from that day.</summary>
    public const string PricesRemoveText = "Prices_RemoveText";

    /// <summary>Price removed</summary>
    public const string PricesRemoved = "Prices_Removed";

    /// <summary>Summary</summary>
    public const string ExportSheetSummary = "Export_SheetSummary";

    /// <summary>Periods</summary>
    public const string ExportSheetPeriods = "Export_SheetPeriods";

    /// <summary>Stations</summary>
    public const string ExportSheetStations = "Export_SheetStations";

    /// <summary>Services</summary>
    public const string ExportSheetServices = "Export_SheetServices";

    /// <summary>Machines</summary>
    public const string ExportSheetMachines = "Export_SheetMachines";

    /// <summary>Detail</summary>
    public const string ExportSheetDetail = "Export_SheetDetail";

    /// <summary>Currency</summary>
    public const string ExportCurrency = "Export_Currency";

    /// <summary>Date</summary>
    public const string ExportDate = "Export_Date";

    /// <summary>Machine code</summary>
    public const string ExportMachineCode = "Export_MachineCode";

    /// <summary>From</summary>
    public const string ExportPeriodStart = "Export_PeriodStart";

    /// <summary>To</summary>
    public const string ExportPeriodEnd = "Export_PeriodEnd";

    /// <summary>Previous period</summary>
    public const string ExportPreviousPeriod = "Export_PreviousPeriod";

    /// <summary>Revenue change</summary>
    public const string ExportRevenueChange = "Export_RevenueChange";

    /// <summary>Uses on unassigned machines</summary>
    public const string ExportUnassignedUses = "Export_UnassignedUses";
}
