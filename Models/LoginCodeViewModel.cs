using System.ComponentModel.DataAnnotations;

namespace Loom.Models;

public sealed class LoginCodeViewModel
{
    [Required(ErrorMessage = "Введите код из письма.")]
    [RegularExpression("[0-9]{6}", ErrorMessage = "Код должен состоять из шести цифр.")]
    public string Code { get; set; } = "";
}
