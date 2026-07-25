using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Admin;

internal static class AdminPostgresParameters
{
    public static NpgsqlParameter AddPositional(
        NpgsqlParameterCollection parameters,
        NpgsqlDbType type,
        object? value)
    {
        var parameter = new NpgsqlParameter
        {
            NpgsqlDbType = type,
            Value = value ?? DBNull.Value
        };
        parameters.Add(parameter);
        return parameter;
    }
}
