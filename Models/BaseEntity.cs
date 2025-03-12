using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BackendApi.Models;
public class BaseEntity
{
    public int Id { get; set; }

    [Display(Name = "Active?")]
    public bool IsActive { get; set; } = true;

    [MinLength(3), MaxLength(500)]
    [Display(Prompt = "Enter Comments (if any) here...")]
    public virtual string? Comments { get; set; }

    [Required, MaxLength(50)]
    [Column(TypeName = "varchar")]
    [Display(Name = "Created By")]
    public string CreatedBy { get; set; } = Environment.UserName;

    [Required]
    [Display(Name = "Created (UTC)")]
    public DateTime? CreatedOn { get; set; } = DateTime.UtcNow;

    [MaxLength(50)]
    [Column(TypeName = "varchar")]
    [Display(Name = "Modified By")]
    public string? ModifiedBy { get; set; }

    [Display(Name = "Modified (UTC)")]
    public DateTime? ModifiedOn { get; set; }
}
