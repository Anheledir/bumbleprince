﻿using BaseBotService.Data.Interfaces;
using BaseBotService.Data.Models;
using LiteDB;

namespace BaseBotService.Data;

public class GuildMemberHCRepository : IGuildMemberHCRepository
{
    private readonly ILiteCollection<GuildMemberHC> _guildMembers;

    public GuildMemberHCRepository(ILiteCollection<GuildMemberHC> guildMembers)
    {
        _guildMembers = guildMembers;
    }

    public GuildMemberHC GetUser(ulong guildId, ulong userId)
    {
        return _guildMembers.FindOne(a => a.GuildId == guildId && a.MemberId == userId);
    }

    public void AddUser(GuildMemberHC user)
    {
        _guildMembers.Insert(user);
    }

    public bool UpdateUser(GuildMemberHC user)
    {
        return _guildMembers.Update(user);
    }

    public bool DeleteUser(ulong guildId, ulong userId)
    {
        var user = GetUser(guildId, userId);
        if (user != null)
        {
            return _guildMembers.Delete(user.Id);
        }
        return false;
    }
}