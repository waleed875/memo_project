using Microsoft.AspNetCore.Antiforgery;

namespace MemoDashboard.Backend.Services;

public static class WebAdminSecurity
{
    public static async Task<bool> IsCsrfValidAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            return await antiforgery.IsRequestValidAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}