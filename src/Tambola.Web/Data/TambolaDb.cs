using Microsoft.EntityFrameworkCore;
using Tambola.Domain.Entities;

namespace Tambola.Web.Data;

public class TambolaDb : DbContext
{
    public TambolaDb(DbContextOptions<TambolaDb> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomPlayer> RoomPlayers => Set<RoomPlayer>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<CalledNumber> CalledNumbers => Set<CalledNumber>();
    public DbSet<Prize> Prizes => Set<Prize>();
    public DbSet<PrizeWinner> PrizeWinners => Set<PrizeWinner>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.WindowsUser).IsUnique();   // one registration per Windows account
            e.HasIndex(x => x.EmployeeCode).IsUnique();  // one email per code ...
            e.HasIndex(x => x.Email).IsUnique();         // ... and one code per email
        });
        b.Entity<Room>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasQueryFilter(x => !x.IsDeleted);   // soft delete; Admin reports use IgnoreQueryFilters()
            e.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Prizes).WithOne().HasForeignKey(x => x.RoomId);
            e.HasMany(x => x.Players).WithOne().HasForeignKey(x => x.RoomId);
            e.HasMany(x => x.Calls).WithOne().HasForeignKey(x => x.RoomId);
        });
        b.Entity<RoomPlayer>(e =>
        {
            e.HasIndex(x => new { x.RoomId, x.EmployeeId }).IsUnique();
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId);
        });
        b.Entity<Ticket>(e =>
        {
            e.HasIndex(x => x.RoomId);
            e.HasIndex(x => new { x.RoomId, x.Grid }).IsUnique();   // no two identical tickets in a room
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId);
        });
        b.Entity<CalledNumber>().HasIndex(x => new { x.RoomId, x.Sequence }).IsUnique();
        b.Entity<Prize>(e =>
        {
            e.HasIndex(x => new { x.RoomId, x.Pattern }).IsUnique();
            e.HasMany(x => x.Winners).WithOne().HasForeignKey(x => x.PrizeId);
        });
        b.Entity<PrizeWinner>(e =>
        {
            e.HasIndex(x => new { x.PrizeId, x.Slot }).IsUnique();
            e.HasIndex(x => new { x.PrizeId, x.EmployeeId }).IsUnique();
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId);
        });
        b.Entity<Claim>().HasIndex(x => new { x.PrizeId, x.EmployeeId });
    }
}
