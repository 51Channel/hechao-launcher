using Microsoft.AspNetCore.Antiforgery;

namespace Hechao.Api.Admin;

public sealed class AdminAntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (HttpMethods.IsPost(context.HttpContext.Request.Method) ||
            HttpMethods.IsPut(context.HttpContext.Request.Method) ||
            HttpMethods.IsPatch(context.HttpContext.Request.Method) ||
            HttpMethods.IsDelete(context.HttpContext.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Problem(
                    title: "请求校验失败",
                    detail: "页面安全令牌无效，请刷新管理后台后重试。",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return await next(context);
    }
}
