using Microsoft.Extensions.Options;

namespace Hechao.Api.Admin;

public sealed class AdminWebHostFilter(
    IOptions<AdminWebOptions> options) : IEndpointFilter
{
    private readonly AdminWebOptions _options = options.Value;

    public ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!_options.Enabled ||
            !_options.IsExpectedHost(context.HttpContext.Request.Host))
        {
            return ValueTask.FromResult<object?>(Results.NotFound());
        }

        return next(context);
    }
}
