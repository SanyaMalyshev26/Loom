using System.ComponentModel.DataAnnotations;

namespace Loom.Models;

public sealed class ForgotPasswordModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";
}

public sealed class ResetPasswordModel
{
    [Required, StringLength(256)]
    public string UserId { get; set; } = "";

    [Required, StringLength(4096)]
    public string Token { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 6), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required, Compare(nameof(Password), ErrorMessage = "Пароли не совпадают."), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";
}
