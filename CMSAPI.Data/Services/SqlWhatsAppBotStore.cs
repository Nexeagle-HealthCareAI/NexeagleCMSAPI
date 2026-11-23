using System.Data;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.Data.SqlClient;

namespace CMSAPI.Data.Services;

/// <summary>Where the WhatsApp bot's database is. Set up in Program.cs.</summary>
public sealed record WhatsAppBotConnection(string ConnectionString);

/// <summary>
/// Reads the WhatsApp bot's tables with plain SQL. The schema belongs to the bot
/// (Whatsapp-Webhook sql/schema.sql); only columns that script creates are used here, and nothing
/// is ever written.
/// </summary>
public class SqlWhatsAppBotStore : IWhatsAppBotStore
{
    private const int ChunkSize = 900;   // SQL Server allows 2100 parameters a statement
    private readonly string _connectionString;

    public SqlWhatsAppBotStore(WhatsAppBotConnection connection)
    {
        _connectionString = connection.ConnectionString;
    }

    private async Task<T> RunAsync<T>(Func<SqlConnection, Task<T>> work)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            return await work(conn);
        }
        catch (SqlException ex)
        {
            throw new WhatsAppBotUnavailableException("The WhatsApp bot's database could not be read: " + ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new WhatsAppBotUnavailableException("The WhatsApp bot's database is not set up for the CMS: " + ex.Message, ex);
        }
    }

    private static SqlCommand Command(SqlConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 60;
        return cmd;
    }

    private static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();

    private const string SessionColumns =
        "session_id, phone_number, started_at, last_activity_at, last_step, appointment_id, transcript_json";

    private static BotSessionRow ReadSession(SqlDataReader r) => new BotSessionRow
    {
        SessionId = r.GetString(0),
        Phone = r.GetString(1),
        StartedAt = DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
        LastActivityAt = DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc),
        LastStep = Str(r, 4),
        BookingId = r.IsDBNull(5) ? null : r.GetGuid(5),
        TranscriptJson = Str(r, 6),
    };

    public Task<List<BotSessionRow>> SessionsAsync(DateTime fromUtc, DateTime toUtc) => RunAsync(async conn =>
    {
        await using var cmd = Command(conn,
            $"SELECT {SessionColumns} FROM dbo.conversation_sessions WHERE started_at >= @from AND started_at < @to ORDER BY started_at DESC");
        cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = fromUtc;
        cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = toUtc;
        var list = new List<BotSessionRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadSession(r));
        return list;
    });

    public Task<BotSessionRow?> SessionAsync(string sessionId) => RunAsync(async conn =>
    {
        await using var cmd = Command(conn, $"SELECT {SessionColumns} FROM dbo.conversation_sessions WHERE session_id = @id");
        cmd.Parameters.Add("@id", SqlDbType.NVarChar, 100).Value = sessionId;
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadSession(r) : null;
    });

    /// <summary>Runs one query per chunk of values, "@p0, @p1 …" standing for {0} in the SQL.</summary>
    private async Task<List<T>> InChunksAsync<TValue, T>(IReadOnlyCollection<TValue> values, string sqlWithIn, SqlDbType type, Func<SqlDataReader, T> read)
    {
        var list = new List<T>();
        if (values.Count == 0) return list;
        foreach (var chunk in values.Distinct().Chunk(ChunkSize))
        {
            var part = await RunAsync(async conn =>
            {
                var names = chunk.Select((_, i) => "@p" + i).ToList();
                await using var cmd = Command(conn, string.Format(sqlWithIn, string.Join(", ", names)));
                for (var i = 0; i < chunk.Length; i++) cmd.Parameters.Add(names[i], type).Value = chunk[i]!;
                var rows = new List<T>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) rows.Add(read(r));
                return rows;
            });
            list.AddRange(part);
        }
        return list;
    }

    public async Task<Dictionary<string, DateTime>> FirstSeenAsync(IReadOnlyCollection<string> phones)
    {
        var rows = await InChunksAsync(phones,
            "SELECT phone_number, MIN(started_at) FROM dbo.conversation_sessions WHERE phone_number IN ({0}) GROUP BY phone_number",
            SqlDbType.NVarChar, r => (Phone: r.GetString(0), First: DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc)));
        return rows.ToDictionary(x => x.Phone, x => x.First);
    }

    public Task<List<BotNluRow>> NluAsync(IReadOnlyCollection<string> sessionIds) =>
        InChunksAsync(sessionIds,
            "SELECT session_id, utterance, specialty, symptom, doctor_name, routed_step, created_at FROM dbo.nlu_logs WHERE session_id IN ({0})",
            SqlDbType.NVarChar, r => new BotNluRow
            {
                SessionId = r.GetString(0), Utterance = Str(r, 1), Specialty = Str(r, 2), Symptom = Str(r, 3), DoctorName = Str(r, 4),
                RoutedStep = Str(r, 5), CreatedAt = r.IsDBNull(6) ? null : r.GetDateTime(6),
            });

    public Task<List<BotBookingRow>> BookingsAsync(IReadOnlyCollection<Guid> bookingIds) =>
        InChunksAsync(bookingIds,
            "SELECT id, doctor_name, doctor_id, hospital_id, preferred_date, patient_display_name, booking_for, status FROM dbo.pending_appointments WHERE id IN ({0})",
            SqlDbType.UniqueIdentifier, r => new BotBookingRow
            {
                Id = r.GetGuid(0), DoctorName = Str(r, 1), DoctorId = Str(r, 2), HospitalId = Str(r, 3),
                PreferredDate = r.IsDBNull(4) ? null : r.GetDateTime(4), PatientName = Str(r, 5), BookingFor = Str(r, 6), Status = Str(r, 7),
            });

    public Task<List<BotLastSearchRow>> LastSearchesAsync(IReadOnlyCollection<string> phones) =>
        InChunksAsync(phones,
            "SELECT phone_number, last_city, last_location_text FROM dbo.patient_last_search WHERE phone_number IN ({0})",
            SqlDbType.NVarChar, r => new BotLastSearchRow { Phone = r.GetString(0), City = Str(r, 1), LocationText = Str(r, 2) });

    public Task<List<(string Phone, DateTime StartedAt)>> StartsSinceAsync(DateTime fromUtc) => RunAsync(async conn =>
    {
        await using var cmd = Command(conn, "SELECT phone_number, started_at FROM dbo.conversation_sessions WHERE started_at >= @from");
        cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = fromUtc;
        var list = new List<(string, DateTime)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetString(0), DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc)));
        return list;
    });
}
