namespace Hamnava.Domain.Constants;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";
    public const string Student = "Student";
    public const string Referrer = "Referrer";

    public static readonly IReadOnlyList<string> All =
    [
        Admin,
        Teacher,
        Student,
        Referrer
    ];
}
