namespace EnglishCenter.Modules.Academic.Domain.Attendance;

public enum AttendanceStatus
{
    Present = 1,
    AbsentUnexcused = 2,
    AbsentExcused = 3,
    Late = 4,
    LateOver20Min = 5,
    LeftEarly = 6
}
