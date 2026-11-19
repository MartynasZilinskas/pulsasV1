using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Pulsas.Persistence;

public static class PostgresWriteErrors
{
    public static (string Table, string Constraint)? PublicUniqueViolation(
        DbUpdateException exception
    ) =>
        exception.InnerException
            is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                SchemaName: "public",
                TableName: { } table,
                ConstraintName: { } constraint,
            }
            ? (table, constraint)
            : null;
}
