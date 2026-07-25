using System.Net;
using Hechao.Api.Admin;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Tests;

public sealed class AdminPostgresParametersTests
{
    [Fact]
    public void AddPositional_PreservesPositionalParameterModeForTypedValues()
    {
        using var command = new NpgsqlCommand(
            "SELECT $1::inet, $2::jsonb, $3::jsonb;");

        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Inet,
            IPAddress.Loopback);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            """{"enabled":true}""");
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            null);

        Assert.All(
            command.Parameters.Cast<NpgsqlParameter>(),
            parameter => Assert.Equal(string.Empty, parameter.ParameterName));
        Assert.Equal(NpgsqlDbType.Inet, command.Parameters[0].NpgsqlDbType);
        Assert.Equal(NpgsqlDbType.Jsonb, command.Parameters[1].NpgsqlDbType);
        Assert.Equal(NpgsqlDbType.Jsonb, command.Parameters[2].NpgsqlDbType);
        Assert.Equal(DBNull.Value, command.Parameters[2].Value);
    }
}
