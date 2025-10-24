using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIChat.Server.Models;

public class Message
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [ForeignKey("Chat")]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// &#39;user&#39;, &#39;assistant&#39;, &#39;system&#39;
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Required]
    public int SequenceNumber { get; set; }

    /// <summary>
    /// Navigation properties
    /// </summary>
    public virtual Chat Chat { get; set; } = null!;
}
