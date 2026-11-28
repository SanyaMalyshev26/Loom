using System.Net;

namespace Loom.Services;

internal static class LoginCodeEmail
{
    public static string Render(string code, int minutes) => $"""
        <!doctype html>
        <html lang="ru">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Код входа Loom</title>
        </head>
        <body bgcolor="#0a0a0a" style="margin:0;padding:0;background-color:#0a0a0a;color:#f5f0e8;font-family:Arial,Helvetica,sans-serif;">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" bgcolor="#0a0a0a">
                <tr><td align="center" style="padding:24px 12px;">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" bgcolor="#151515"
                           style="max-width:520px;border:1px solid #3d3933;background-color:#151515;">
                        <tr><td style="padding:20px 24px;border-bottom:1px solid #3d3933;color:#f5f0e8;font-size:34px;line-height:36px;font-weight:900;letter-spacing:-2px;">LOOM</td></tr>
                        <tr><td style="padding:28px 24px;">
                            <h1 style="margin:0 0 12px;color:#f5f0e8;font-size:28px;line-height:34px;">Код входа</h1>
                            <p style="margin:0 0 20px;color:#d6d1c9;font-size:16px;line-height:24px;">Введите этот код на странице входа в Loom.</p>
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" bgcolor="#c8b89a">
                                <tr><td align="center" dir="ltr" style="padding:16px 0;background-color:#c8b89a;color:#0a0a0a;font-family:'Courier New',monospace;font-size:36px;line-height:44px;letter-spacing:4px;font-weight:bold;white-space:nowrap;">{WebUtility.HtmlEncode(code)}</td></tr>
                            </table>
                            <p style="margin:12px 0 0;color:#bcb6ae;font-size:14px;line-height:21px;">Срок действия кода — {minutes} мин.</p>
                        </td></tr>
                        <tr><td style="padding:16px 24px;border-top:1px solid #3d3933;color:#bcb6ae;font-size:13px;line-height:20px;">Если вы не запрашивали код, проигнорируйте письмо.</td></tr>
                    </table>
                </td></tr>
            </table>
        </body>
        </html>
        """;
}
