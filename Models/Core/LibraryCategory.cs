using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class LibraryCategory : AuditableEntity
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string NameEn { get; set; } = "";

    [Required, MaxLength(150)]
    public string NameAr { get; set; } = "";

    public int SortOrder { get; set; } = 1;

    public ICollection<LibraryItem> Items { get; set; } = new List<LibraryItem>();
}
