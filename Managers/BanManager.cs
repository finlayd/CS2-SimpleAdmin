﻿using CounterStrikeSharp.API;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace CS2_SimpleAdmin;

internal class BanManager(Database database, CS2_SimpleAdminConfig config)
{
	private readonly Database _database = database;
	private readonly CS2_SimpleAdminConfig _config = config;

	public async Task BanPlayer(PlayerInfo player, PlayerInfo issuer, string reason, int time = 0)
	{
		DateTime now = DateTime.UtcNow.ToLocalTime();
		DateTime futureTime = now.AddMinutes(time).ToLocalTime();

		try
		{
			await using MySqlConnection connection = await _database.GetConnectionAsync();

			var sql = "INSERT INTO `sa_bans` (`player_steamid`, `player_name`, `player_ip`, `admin_steamid`, `admin_name`, `reason`, `duration`, `ends`, `created`, `server_id`) " +
				"VALUES (@playerSteamid, @playerName, @playerIp, @adminSteamid, @adminName, @banReason, @duration, @ends, @created, @serverid)";

			await connection.ExecuteAsync(sql, new
			{
				playerSteamid = player.SteamId,
				playerName = player.Name,
				playerIp = _config.BanType == 1 ? player.IpAddress : null,
				adminSteamid = issuer.SteamId ?? "Console",
				adminName = issuer.Name ?? "Console",
				banReason = reason,
				duration = time,
				ends = futureTime,
				created = now,
				serverid = CS2_SimpleAdmin.ServerId
			});
		}
		catch { }
	}

	public async Task AddBanBySteamid(string playerSteamId, PlayerInfo issuer, string reason, int time = 0)
	{
		if (string.IsNullOrEmpty(playerSteamId)) return;

		DateTime now = DateTime.UtcNow.ToLocalTime();
		DateTime futureTime = now.AddMinutes(time).ToLocalTime();

		try
		{
			await using MySqlConnection connection = await _database.GetConnectionAsync();

			var sql = "INSERT INTO `sa_bans` (`player_steamid`, `admin_steamid`, `admin_name`, `reason`, `duration`, `ends`, `created`, `server_id`) " +
				"VALUES (@playerSteamid, @adminSteamid, @adminName, @banReason, @duration, @ends, @created, @serverid)";

			await connection.ExecuteAsync(sql, new
			{
				playerSteamid = playerSteamId,
				adminSteamid = issuer.SteamId ?? "Console",
				adminName = issuer.Name ?? "Console",
				banReason = reason,
				duration = time,
				ends = futureTime,
				created = now,
				serverid = CS2_SimpleAdmin.ServerId
			});
		}
		catch { }
	}

	public async Task AddBanByIp(string playerIp, PlayerInfo issuer, string reason, int time = 0)
	{
		if (string.IsNullOrEmpty(playerIp)) return;

		DateTime now = DateTime.UtcNow.ToLocalTime();
		DateTime futureTime = now.AddMinutes(time).ToLocalTime();

		try
		{
			await using MySqlConnection connection = await _database.GetConnectionAsync();

			var sql = "INSERT INTO `sa_bans` (`player_ip`, `admin_steamid`, `admin_name`, `reason`, `duration`, `ends`, `created`, `server_id`) " +
				"VALUES (@playerIp, @adminSteamid, @adminName, @banReason, @duration, @ends, @created, @serverid)";

			await connection.ExecuteAsync(sql, new
			{
				playerIp,
				adminSteamid = issuer.SteamId ?? "Console",
				adminName = issuer.Name ?? "Console",
				banReason = reason,
				duration = time,
				ends = futureTime,
				created = now,
				serverid = CS2_SimpleAdmin.ServerId
			});
		}
		catch { }
	}

	public async Task<bool> IsPlayerBanned(PlayerInfo player)
	{
		if (player.SteamId == null || player.IpAddress == null)
		{
			return false;
		}

#if DEBUG
		if (CS2_SimpleAdmin._logger!= null)
			CS2_SimpleAdmin._logger.LogCritical($"IsPlayerBanned for {player.Name}");
#endif

		int banCount = 0;

		DateTime currentTime = DateTime.Now.ToLocalTime();

		try
		{
			string sql = "";

			if (_config.MultiServerMode)
			{
				sql = @"
					UPDATE sa_bans
					SET player_ip = CASE WHEN player_ip IS NULL THEN @PlayerIP ELSE player_ip END,
						player_name = CASE WHEN player_name IS NULL THEN @PlayerName ELSE player_name END
					WHERE (player_steamid = @PlayerSteamID OR player_ip = @PlayerIP)
					AND status = 'ACTIVE'
					AND (duration = 0 OR ends > @CurrentTime);

					SELECT COUNT(*) FROM sa_bans
					WHERE (player_steamid = @PlayerSteamID OR player_ip = @PlayerIP)
					AND status = 'ACTIVE'
					AND (duration = 0 OR ends > @CurrentTime);";
			}
			else
			{
				sql = @"
					UPDATE sa_bans
					SET player_ip = CASE WHEN player_ip IS NULL THEN @PlayerIP ELSE player_ip END,
						player_name = CASE WHEN player_name IS NULL THEN @PlayerName ELSE player_name END
					WHERE (player_steamid = @PlayerSteamID OR player_ip = @PlayerIP)
					AND status = 'ACTIVE'
					AND (duration = 0 OR ends > @CurrentTime) AND server_id = @serverid;

					SELECT COUNT(*) FROM sa_bans
					WHERE (player_steamid = @PlayerSteamID OR player_ip = @PlayerIP)
					AND status = 'ACTIVE'
					AND (duration = 0 OR ends > @CurrentTime) AND server_id = @serverid;";
			}

			await using MySqlConnection connection = await _database.GetConnectionAsync();

			var parameters = new
			{
				PlayerSteamID = player.SteamId,
				PlayerIP = _config.BanType == 0 || string.IsNullOrEmpty(player.IpAddress) ? null : player.IpAddress,
				PlayerName = !string.IsNullOrEmpty(player.Name) ? player.Name : string.Empty,
				CurrentTime = currentTime,
				serverid = CS2_SimpleAdmin.ServerId
			};

			banCount = await connection.ExecuteScalarAsync<int>(sql, parameters);
		}
		catch (Exception)
		{
			return false;
		}

		return banCount > 0;
	}

	public async Task<int> GetPlayerBans(PlayerInfo player)
	{
		try
		{
			string sql = "";

			if (_config.MultiServerMode)
			{
				sql = "SELECT COUNT(*) FROM sa_bans WHERE (player_steamid = @PlayerSteamID OR player_ip = @PlayerIP) AND server_id = @serverid";
			}
			else
			{
				sql = "SELECT COUNT(*) FROM sa_bans WHERE (player_steamid = @PlayerSteamID OR player_ip = @PlayerIP)";
			}

			int banCount;

			await using MySqlConnection connection = await _database.GetConnectionAsync();

			if (!string.IsNullOrEmpty(player.IpAddress))
			{
				banCount = await connection.ExecuteScalarAsync<int>(sql, new { PlayerSteamID = player.SteamId, PlayerIP = player.IpAddress, serverid = CS2_SimpleAdmin.ServerId });
			}
			else
			{
				banCount = await connection.ExecuteScalarAsync<int>(sql, new { PlayerSteamID = player.SteamId, PlayerIP = DBNull.Value, serverid = CS2_SimpleAdmin.ServerId });
			}

			return banCount;
		}
		catch { }

		return 0;
	}

	public async Task UnbanPlayer(string playerPattern, string adminSteamId, string reason)
	{
		if (playerPattern == null || playerPattern.Length <= 1)
		{
			return;
		}
		try
		{
			await using MySqlConnection connection = await _database.GetConnectionAsync();

			string sqlRetrieveBans = "";
			if (_config.MultiServerMode)
			{
				sqlRetrieveBans = "SELECT id FROM sa_bans WHERE (player_steamid = @pattern OR player_name = @pattern OR player_ip = @pattern) AND status = 'ACTIVE' " +
					"AND server_id = @serverid";
			}
			else
			{
				sqlRetrieveBans = "SELECT id FROM sa_bans WHERE (player_steamid = @pattern OR player_name = @pattern OR player_ip = @pattern) AND status = 'ACTIVE'";
			}
			var bans = await connection.QueryAsync(sqlRetrieveBans, new { pattern = playerPattern, serverid = CS2_SimpleAdmin.ServerId });

			if (!bans.Any())
				return;

			string sqlAdmin = "SELECT id FROM sa_admins WHERE player_steamid = @adminSteamId";
			string sqlInsertUnban = "INSERT INTO sa_unbans (ban_id, admin_id, reason) VALUES (@banId, @adminId, @reason); SELECT LAST_INSERT_ID();";

			int? sqlAdminId = await connection.ExecuteScalarAsync<int?>(sqlAdmin, new { adminSteamId });
			int adminId = sqlAdminId ?? 0;

			foreach (var ban in bans)
			{
				int banId = ban.id;
				int? unbanId;

				// Insert into sa_unbans
				if (reason != null)
				{
					unbanId = await connection.ExecuteScalarAsync<int>(sqlInsertUnban, new { banId, adminId, reason });
				}
				else
				{
					sqlInsertUnban = "INSERT INTO sa_unbans (ban_id, admin_id) VALUES (@banId, @adminId); SELECT LAST_INSERT_ID();";
					unbanId = await connection.ExecuteScalarAsync<int>(sqlInsertUnban, new { banId, adminId });
				}

				// Update sa_bans to set unban_id
				string sqlUpdateBan = "UPDATE sa_bans SET status = 'UNBANNED', unban_id = @unbanId WHERE id = @banId";
				await connection.ExecuteAsync(sqlUpdateBan, new { unbanId, banId });
			}

			/*
			string sqlUnban = "UPDATE sa_bans SET status = 'UNBANNED' WHERE player_steamid = @pattern OR player_name = @pattern OR player_ip = @pattern AND status = 'ACTIVE'";
			await connection.ExecuteAsync(sqlUnban, new { pattern = playerPattern });
			*/

		}
		catch { }
	}

	public async Task CheckOnlinePlayers(List<(string? IpAddress, ulong SteamID, int? UserId)> players)
	{
		try
		{
			await using MySqlConnection connection = await _database.GetConnectionAsync();
			string sql = "";

			if (_config.MultiServerMode)
			{
				sql = "SELECT COUNT(*) FROM sa_bans WHERE (player_steamid = @PlayerSteamID OR player_ip = @PlayerIP) AND status = 'ACTIVE' AND" +
					" server_id = @serverid";
			}
			else
			{
				sql = "SELECT COUNT(*) FROM sa_bans WHERE (player_steamid = @PlayerSteamID OR player_ip = @PlayerIP) AND status = 'ACTIVE'";
			}

			foreach (var (IpAddress, SteamID, UserId) in players)
			{
				if (!UserId.HasValue) continue;

				int banCount = 0;
				if (!string.IsNullOrEmpty(IpAddress))
				{
					banCount = await connection.ExecuteScalarAsync<int>(sql, new { PlayerSteamID = SteamID, PlayerIP = IpAddress, serverid = CS2_SimpleAdmin.ServerId });
				}
				else
				{
					banCount = await connection.ExecuteScalarAsync<int>(sql, new { PlayerSteamID = SteamID, PlayerIP = DBNull.Value, serverid = CS2_SimpleAdmin.ServerId });
				}

				if (banCount > 0)
				{
					Server.NextFrame(() =>
					{
						Helper.KickPlayer(UserId.Value, "Banned");
					});
				}
			}
		}
		catch { }
	}

	public async Task ExpireOldBans()
	{
		try
		{
			DateTime currentTime = DateTime.UtcNow.ToLocalTime();

			await using MySqlConnection connection = await _database.GetConnectionAsync();

			/*
			string sql = "";
			await using MySqlConnection connection = await _database.GetConnectionAsync();

			sql = "UPDATE sa_bans SET status = 'EXPIRED' WHERE status = 'ACTIVE' AND `duration` > 0 AND ends <= @CurrentTime";
			await connection.ExecuteAsync(sql, new { CurrentTime = DateTime.UtcNow.ToLocalTime() });
			*/

			string sql = "";

			if (_config.MultiServerMode)
			{
				sql = @"
				UPDATE sa_bans
				SET
					status = 'EXPIRED'
				WHERE
					status = 'ACTIVE'
					AND
					`duration` > 0
					AND
					ends <= @currentTime
					AND server_id = @serverid";
			}
			else
			{
				sql = @"
				UPDATE sa_bans
				SET
					status = 'EXPIRED'
				WHERE
					status = 'ACTIVE'
					AND
					`duration` > 0
					AND
					ends <= @currentTime";
			}

			await connection.ExecuteAsync(sql, new { currentTime, serverid = CS2_SimpleAdmin.ServerId });

			if (_config.ExpireOldIpBans > 0)
			{
				DateTime ipBansTime = currentTime.AddDays(-_config.ExpireOldIpBans).ToLocalTime();
				if (_config.MultiServerMode)
				{
					sql = @"
				UPDATE sa_bans
				SET
					player_ip = NULL
				WHERE
					status = 'ACTIVE'
					AND
					ends <= @ipBansTime
					AND server_id = @serverid";
				}
				else
				{
					sql = @"
				UPDATE sa_bans
				SET
					player_ip = NULL
				WHERE
					status = 'ACTIVE'
					AND
					ends <= @ipBansTime";
				}

				await connection.ExecuteAsync(sql, new { ipBansTime, CS2_SimpleAdmin.ServerId });
			}
		}
		catch (Exception)
		{
			CS2_SimpleAdmin._logger?.LogCritical("Unable to remove expired bans");
		}
	}
}