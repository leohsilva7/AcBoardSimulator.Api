using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AcBoardSimulator.Api.Models;

[Index(nameof(MacAddress), IsUnique = true)]
public class Board
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [StringLength(20)] public string MacAddress { get; set; } = string.Empty;

    /*public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }*/
    public DateOnly InstallationDate { get; set; }

    [StringLength(120)] public string Location { get; set; } = string.Empty;

    /*public ICollection<BoardLog> BoardLogs { get; set; } = new List<BoardLog>();
    public ICollection<Report>? Reports { get; set; } = new List<Report>();
    public ICollection<Failure>? Failures { get; set; } = new List<Failure>();*/
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}