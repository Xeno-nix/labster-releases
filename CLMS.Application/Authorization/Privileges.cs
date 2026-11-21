using CLMS.Domain.Enums;

namespace CLMS.Application.Authorization;

public sealed record PrivilegeDefinition(string Id, string Module, string Description, bool SuperAdminOnly = false);

public static class Privileges
{
    public const string Prefix = "Privilege:";
    public const string DashboardView = "dashboard.view", LaboratoriesView = "laboratories.view";
    public const string UsersView = "users.view", UsersCreate = "users.create", UsersEdit = "users.edit", UsersDelete = "users.delete",
        UsersAssignRoles = "users.assign_roles", UsersManagePrivileges = "users.manage_privileges";
    public const string UnitsView = "units.view", UnitsMonitor = "units.monitor", UnitsManage = "units.manage";
    public const string MonitoringView = "monitoring.view", MonitoringConfigure = "monitoring.configure",
        MonitoringLabScreens = "monitoring.lab_screens", MonitoringSessionsView = "monitoring.sessions.view";
    public const string InventoryView = "inventory.view", InventoryManage = "inventory.manage";
    public const string CoursesView = "courses.view", CoursesManage = "courses.manage", TermsView = "terms.view", TermsManage = "terms.manage";
    public const string SchedulesView = "schedules.view", SchedulesManage = "schedules.manage", BookingsView = "bookings.view", BookingsManage = "bookings.manage";
    public const string ClassListsView = "classlists.view", ClassListsManage = "classlists.manage";
    public const string LogbookView = "logbook.view", LogbookExport = "logbook.export";
    public const string BackupsView = "backups.view", BackupsCreate = "backups.create", BackupsRestore = "backups.restore", SecurityManage = "security.manage";

    public static readonly IReadOnlyList<PrivilegeDefinition> All = Array.AsReadOnly(new[]
    {
        new PrivilegeDefinition(DashboardView, "Dashboard", "View the dashboard"),
        new(LaboratoriesView, "Laboratories", "View laboratories"),
        new(UsersView, "Users", "View users and their access"), new(UsersCreate, "Users", "Create users"),
        new(UsersEdit, "Users", "Edit user profiles"), new(UsersDelete, "Users", "Delete users"),
        new(UsersAssignRoles, "Users", "Assign ordinary staff roles"), new(UsersManagePrivileges, "Users", "Manage user privilege overrides", true),
        new(UnitsView, "Units", "View units"), new(UnitsMonitor, "Units", "Monitor applications and individual screens"), new(UnitsManage, "Units", "Manage unit enrollment and inventory"),
        new(MonitoringView, "Monitoring", "View abnormal activities"), new(MonitoringConfigure, "Monitoring", "Manage the application registry and icons"),
        new(MonitoringLabScreens, "Monitoring", "Manage laboratory screen sessions"), new(MonitoringSessionsView, "Monitoring", "View workstation user sessions"),
        new(InventoryView, "Inventory", "View equipment and supplies"), new(InventoryManage, "Inventory", "Manage equipment, assignments, and supplies"),
        new(CoursesView, "Classes", "View courses"), new(CoursesManage, "Classes", "Manage courses"),
        new(TermsView, "Classes", "View academic terms"), new(TermsManage, "Classes", "Manage academic terms"),
        new(ClassListsView, "Classes", "View permitted class lists"), new(ClassListsManage, "Classes", "Manage permitted rosters, imports, and synchronization"),
        new(SchedulesView, "Scheduling", "View schedules"), new(SchedulesManage, "Scheduling", "Manage class schedules"),
        new(BookingsView, "Scheduling", "View bookings"), new(BookingsManage, "Scheduling", "Manage bookings"),
        new(LogbookView, "Logbook", "View attendance logs"), new(LogbookExport, "Logbook", "Export attendance logs"),
        new(BackupsView, "Backups", "View database backups"), new(BackupsCreate, "Backups", "Create database backups"),
        new(BackupsRestore, "Backups", "Restore a database snapshot", true), new(SecurityManage, "Security", "Manage role privilege defaults", true)
    });
    private static readonly IReadOnlyDictionary<string, PrivilegeDefinition> Catalog = All.ToDictionary(p => p.Id, StringComparer.Ordinal);
    private static readonly HashSet<string> AdminOnly = [UsersView, UsersCreate, UsersEdit, UsersDelete, UsersAssignRoles,
        UnitsManage, MonitoringConfigure, MonitoringLabScreens, MonitoringSessionsView, BackupsView, BackupsCreate];
    public static bool IsKnown(string privilege) => privilege is not null && Catalog.ContainsKey(privilege);
    public static bool IsProtected(string privilege) => Catalog.TryGetValue(privilege, out var definition) && definition.SuperAdminOnly;
    public static IReadOnlySet<string> Defaults(Role role) => All.Where(p => role == Role.SuperAdmin ||
        (!p.SuperAdminOnly && (role == Role.Admin || role == Role.Faculty && !AdminOnly.Contains(p.Id)))).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
    public static bool IsStaff(Role role) => role is Role.Admin or Role.Faculty or Role.SuperAdmin;
    public static bool HasAdministrativeScope(Role role) => role is Role.Admin or Role.SuperAdmin;
}
