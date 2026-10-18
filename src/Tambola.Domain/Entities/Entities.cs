namespace Tambola.Domain.Entities;

public enum RoomStatus { Lobby = 0, Running = 1, Paused = 2, Completed = 3, Cancelled = 4 }
public enum CallMode { Manual = 0, Auto = 1 }
/// <summary>Manual = called numbers are highlighted on tickets; ManualNoHighlight = players must find and tap numbers themselves; Auto = cut for everyone.</summary>
public enum CutMode { Manual = 0, Auto = 1, ManualNoHighlight = 2 }
public enum PrizePattern
{
    FourCorners = 0, FirstRow = 1, SecondRow = 2, ThirdRow = 3, FullHouse = 4, EarlyFive = 5,
    Star = 6, Pyramid = 7, Diamond = 8, LetterH = 9, LetterX = 10
}
public enum ClaimResult { Valid = 0, Bogey = 1, Late = 2 }

public class Employee
{
    public int Id { get; set; }
    public string WindowsUser { get; set; } = "";   // DOMAIN\user, unique
    public string EmployeeCode { get; set; } = "";  // unique
    public string Email { get; set; } = "";         // unique
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsHost { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    /// <summary>PBKDF2 hash ("v1.iterations.salt.hash"). Only used in Manual (Linux) auth mode; null in Windows mode.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>Set by an Admin password reset: the user must choose a new password at next login.</summary>
    public bool MustChangePassword { get; set; }
}

public class Room
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int HostId { get; set; }
    public Employee? Host { get; set; }
    public RoomStatus Status { get; set; } = RoomStatus.Lobby;
    public CallMode CallMode { get; set; }
    public CutMode CutMode { get; set; }
    public int CallIntervalSeconds { get; set; } = 6;
    public int MaxPlayers { get; set; } = 100;
    public int MaxTicketsPerPlayer { get; set; } = 3;
    public bool IsLocked { get; set; }
    /// <summary>Wrong claims a player may make per prize before it is locked for them. 0 = unlimited.</summary>
    public int MaxWrongClaims { get; set; } = 3;
    /// <summary>Soft delete: hidden from everyone except Admin reports.</summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    /// <summary>Server-secret draw order (comma separated permutation of 1..90). Never sent to clients.</summary>
    public string DrawOrder { get; set; } = "";
    public DateTime? NextCallAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public List<Prize> Prizes { get; set; } = new();
    public List<RoomPlayer> Players { get; set; } = new();
    public List<CalledNumber> Calls { get; set; } = new();
}

public class RoomPlayer
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public bool IsRemoved { get; set; }
}

public class Ticket
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int Serial { get; set; }
    /// <summary>27 comma separated values, row-major 3x9, 0 = blank.</summary>
    public string Grid { get; set; } = "";
    /// <summary>Numbers the player cut manually (comma separated).</summary>
    public string Marks { get; set; } = "";
}

public class CalledNumber
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int Sequence { get; set; }
    public int Number { get; set; }
    public DateTime CalledAt { get; set; } = DateTime.UtcNow;
}

public class Prize
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public PrizePattern Pattern { get; set; }
    public int WinnerCount { get; set; } = 1;
    public string? RewardText { get; set; }
    public List<PrizeWinner> Winners { get; set; } = new();
}

public class PrizeWinner
{
    public int Id { get; set; }
    public int PrizeId { get; set; }
    public int Slot { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int TicketId { get; set; }
    public int AwardedAtSequence { get; set; }
    public DateTime AwardedAt { get; set; } = DateTime.UtcNow;
}

public class Claim
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int PrizeId { get; set; }
    public int EmployeeId { get; set; }
    public int TicketId { get; set; }
    public ClaimResult Result { get; set; }
    public string? Reason { get; set; }
    public int AtSequence { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public int Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Details { get; set; }
}

/// <summary>Admin-managed global setting (key/value), e.g. the caller sound options.</summary>
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
