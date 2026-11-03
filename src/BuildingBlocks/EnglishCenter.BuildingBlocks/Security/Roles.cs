namespace EnglishCenter.BuildingBlocks.Security;

public static class Roles
{
    public const string Student = "Student";
    public const string Teacher = "Teacher";
    public const string Staff = "Staff";
    public const string Admin = "Admin";
    public const string AccountManagers = Staff + "," + Admin;
}

public static class AuthClaims
{
    public const string Subject = "sub";
    public const string Role = "role";
}
