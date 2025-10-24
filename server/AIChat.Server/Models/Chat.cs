using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIChat.Server.Models;

public class Chat
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [ForeignKey("User")]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation properties
    /// </summary>
    public virtual User User { get; set; } = null!;
    public virtual ICollection<Message> Messages { get; set; } = [];
}
