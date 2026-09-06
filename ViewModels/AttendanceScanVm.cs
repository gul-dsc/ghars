using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.ViewModels;

public class AttendanceScanVm
{
    [Required]
    public string Token { get; set; } = "";
}
