using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Models;

public class User
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(255)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? ProviderUserId { get; set; }

    [MaxLength(500)]
    public string? ProfileImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation properties
    /// </summary>
    public virtual ICollection<Chat> Chats { get; set; } = [];
}
