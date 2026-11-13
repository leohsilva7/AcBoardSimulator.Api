using AcBoardSimulator.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AcBoardSimulator.Api.Data;

public class AppDbContext: DbContext
{
    public AppDbContext(DbContextOptions options):base(options)
    {
        
    }

    public DbSet<Board> Boards { get; set; }
}