namespace GroupService.Entities;

/// <summary>
/// Represents an invite to a group
/// </summary>
public class GroupInvite
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string InviteCode { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int CurrentUses { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public Group Group { get; set; } = null!;
}
