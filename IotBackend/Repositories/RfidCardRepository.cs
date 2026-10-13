using Dapper;
using IotBackend.Models;
using Npgsql;

namespace IotBackend.Repositories;

public sealed class RfidCardRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public RfidCardRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    private const string ListSql = """
        SELECT uid, label, is_active, created_at, updated_at
        FROM rfid_cards
        ORDER BY created_at DESC
        """;

    public async Task<List<RfidCardRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<RfidCardRecord>(
            new CommandDefinition(ListSql, cancellationToken: cancellationToken));

        return rows.AsList();
    }

    private const string ListActiveUidsSql = "SELECT uid FROM rfid_cards WHERE is_active = true ORDER BY uid";

    public async Task<List<string>> ListActiveUidsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<string>(
            new CommandDefinition(ListActiveUidsSql, cancellationToken: cancellationToken));

        return rows.AsList();
    }

    private const string InsertSql = """
        INSERT INTO rfid_cards (uid, label, is_active, created_at, updated_at)
        VALUES (@uid, @label, true, NOW(), NOW())
        ON CONFLICT (uid) DO NOTHING
        """;

    public async Task<int> InsertAsync(string uid, string? label, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new { uid, label };
        return await connection.ExecuteAsync(new CommandDefinition(InsertSql, parameters, cancellationToken: cancellationToken));
    }

    private const string UpdateSql = """
        UPDATE rfid_cards
        SET label = COALESCE(@label, label),
            is_active = COALESCE(@is_active, is_active),
            updated_at = NOW()
        WHERE uid = @uid
        """;

    public async Task<int> UpdateAsync(
        string uid, bool? isActive, string? label, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new { uid, is_active = isActive, label };
        return await connection.ExecuteAsync(new CommandDefinition(UpdateSql, parameters, cancellationToken: cancellationToken));
    }

    private const string DeleteSql = "DELETE FROM rfid_cards WHERE uid = @uid";

    public async Task<int> DeleteAsync(string uid, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(DeleteSql, new { uid }, cancellationToken: cancellationToken));
    }
}
