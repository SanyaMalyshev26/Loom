using Loom.Models;
using Loom.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Loom.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PasswordController(
    UserManager<ApplicationUser> users,
    IConfiguration configuration,
    StoreEmail mail) : Controller
{
    [HttpGet]
    public IActionResult Forgot() => View();

    [HttpPost, EnableRateLimiting("password-recovery")]
    public async Task<IActionResult> Forgot(ForgotPasswordModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var baseUrl = configuration["PasswordRecovery:PublicBaseUrl"];
        if (!mail.IsConfigured ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) ||
            (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback)))
        {
            ModelState.AddModelError("", "Восстановление по почте пока недоступно.");
            return View(model);
        }

        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user?.Email is { } email)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var link = QueryHelpers.AddQueryString(new Uri(origin, Url.Action(nameof(Reset))!).AbsoluteUri,
                new Dictionary<string, string?> { ["userId"] = user.Id, ["token"] = token });
            await mail.SendAsync(email, "Сброс пароля Loom",
                $"Чтобы задать новый пароль, откройте ссылку:\n{link}\n\nЕсли вы не запрашивали сброс, проигнорируйте письмо.",
                HttpContext.RequestAborted);
        }

        TempData["PasswordRecoveryMessage"] = "Если аккаунт существует и почта доступна, на него придёт ссылка для сброса.";
        return RedirectToAction(nameof(Forgot));
    }

    [HttpGet]
    public IActionResult Reset(string? userId, string? token)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (string.IsNullOrEmpty(userId) || userId.Length > 256 || string.IsNullOrEmpty(token) || token.Length > 4096)
            return BadRequest("Некорректная ссылка для восстановления.");

        return View(new ResetPasswordModel { UserId = userId, Token = token });
    }

    [HttpPost, EnableRateLimiting("password-recovery")]
    public async Task<IActionResult> Reset(ResetPasswordModel model)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!ModelState.IsValid)
            return View(model);

        var user = await users.FindByIdAsync(model.UserId);
        if (user != null && (await users.ResetPasswordAsync(user, model.Token, model.Password)).Succeeded)
        {
            TempData["PasswordRecoveryMessage"] = "Пароль обновлён. Войдите с новым паролем.";
            return RedirectToAction("Login", "Account");
        }

        ModelState.AddModelError("", "Не удалось сменить пароль. Запросите новую ссылку.");
        return View(model);
    }
}
