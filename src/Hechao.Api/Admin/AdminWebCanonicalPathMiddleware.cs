namespace Hechao.Api.Admin;

public sealed class AdminWebCanonicalPathMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/admin")
        {
            context.Response.Redirect("/admin/");
            return Task.CompletedTask;
        }

        return next(context);
    }
}
