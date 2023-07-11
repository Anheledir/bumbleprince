﻿using BaseBotService.Commands.Modals;
using BaseBotService.Core.Base;
using BaseBotService.Data.Interfaces;
using BaseBotService.Data.Models;
using BaseBotService.Utilities;
using BaseBotService.Utilities.Attributes;
using BaseBotService.Utilities.Enums;
using BaseBotService.Utilities.Extensions;
using Discord.Rest;
using Discord.WebSocket;
using System.Text;

namespace BaseBotService.Commands;

// TODO: Add role check for guild moderators

[Group("polls", "Create and manage polls for your server.")]
[EnabledInDm(false)]
[RequireContext(ContextType.Guild)]
public class PollModule : BaseModule
{
    private readonly IPollRepository _pollRepository;
    private readonly IGuildRepository _guildRepository;

    public PollModule(ILogger logger, IPollRepository pollRepository, IGuildRepository guildRepository)
    {
        Logger = logger.ForContext<PollModule>();
        _pollRepository = pollRepository;
        _guildRepository = guildRepository;
    }

    [ModeratorsOnly(Logger, _guildRepository)]
    [SlashCommand("create", "Create a new poll in this server.")]
    public async Task CreatePoll()
    {
        // The default channel is the current one
        IChannel channel = Context.Channel;

        // Check if the channel is a text channel
        if (channel is not ITextChannel)
        {
            Logger.Information($"User {Caller.Id} tried to create a new poll on {GuildId} in {channel.Id}, which is not a text channel.");
            await RespondOrFollowupAsync(TranslationService.GetString("error-poll-create-invalid-channel"), ephemeral: true);
            return;
        }

        // Check if the bot has permissions to send messages in the channel
        ChannelPermissions usrPermissions = ((IGuildUser)Caller).GetPermissions(channel as IGuildChannel);
        if (!usrPermissions.SendMessages)
        {
            Logger.Information($"Cannot create a new poll, as the calling user does not have permissions to create messages in {channel.Id} on {GuildId}.");
            await RespondOrFollowupAsync(TranslationService.GetString("error-poll-create-no-permissions"), ephemeral: true);
        }

        var modal = new ModalBuilder()
            .WithCustomId("polls.create")
            .WithTitle(TranslationService.GetAttrString("polls", "create"))
            .AddTextInput(TranslationService.GetAttrString("polls", "create-poll-title"), "title", TextInputStyle.Short, minLength: 3, required: true)
            .AddTextInput(TranslationService.GetAttrString("polls", "create-poll-description"), "description", TextInputStyle.Paragraph, required: false);
        await RespondWithModalAsync(modal.Build());
    }

    [ModalInteraction("polls.create", ignoreGroupNames: true)]
    public async Task CreateNewPollWizard(PollsCreateNewModal data)
    {
        RestUserMessage pollMessage = await Context.Channel.SendMessageAsync(TranslationService.GetAttrString("polls", "create-msg-processing"));

        var pollData = new PollHC
        {
            PollId = pollMessage.Id,
            ChannelId = Context.Channel.Id,
            GuildId = Context.Guild.Id,
            CreatorId = Caller.Id,
            Title = data.PollTitle,
            Description = data.Description,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(1),
            AreResultsPublic = true,
            AreVotersHidden = false,
            IsClosed = false
        };
        _pollRepository.AddPoll(pollData);

        await RespondOrFollowupAsync("Please finish setting up your poll...", embed: GetPollEmbed(pollData, true).Build(), components: GetCreatePollButtons(pollMessage.Id).Build(), ephemeral: true);
    }

    [ComponentInteraction("polls.create.toggle.results:*", ignoreGroupNames: true)]
    public async Task CreateNewPollToggleResults(string pollId)
    {
        Logger.Debug($"Toggle results for poll {pollId}");
        await DeferAsync();
        PollHC? newPoll = await GetPollData(pollId);
        if (newPoll == null) return;

        newPoll.AreResultsPublic = !newPoll.AreResultsPublic;
        _pollRepository.UpdatePoll(newPoll);

        _ = await ModifyOriginalResponseAsync(msg => msg.Embed = GetPollEmbed(newPoll, true).Build());
    }

    [ComponentInteraction("polls.create.toggle.voters:*", ignoreGroupNames: true)]
    public async Task CreateNewPollToggleVoters(string pollId)
    {
        Logger.Debug($"Toggle voters for poll {pollId}");
        await DeferAsync();
        PollHC? newPoll = await GetPollData(pollId);
        if (newPoll == null) return;

        newPoll.AreVotersHidden = !newPoll.AreVotersHidden;
        _pollRepository.UpdatePoll(newPoll);

        _ = await ModifyOriginalResponseAsync(msg => msg.Embed = GetPollEmbed(newPoll, true).Build());
    }

    [ComponentInteraction("polls.create.finish:*", ignoreGroupNames: true)]
    public async Task CreateNewPollFinish(string pollId)
    {
        Logger.Debug($"Finish poll {pollId}");
        await DeferAsync();
        PollHC? newPoll = await GetPollData(pollId);
        if (newPoll == null) return;

        var message = await GetPollMessage(newPoll);
        var pollEmbed = GetPollEmbed(newPoll, false);
        pollEmbed.Url = message.GetJumpUrl();

        await message!.ModifyAsync(async msg => { msg.Embed = pollEmbed.Build(); msg.Content = string.Empty; msg.Components = (await GetPollVotingButtons(pollId)).Build(); });
        await message!.PinAsync();
        await ModifyOriginalResponseAsync(msg => { msg.Embed = null; msg.Content = "Poll created successfully."; msg.Components = null; });
    }

    [ComponentInteraction("polls.create.cancel:*", ignoreGroupNames: true)]
    public async Task CreateNewPollCancel(string pollId)
    {
        Logger.Debug($"Cancel poll {pollId}");
        await DeferAsync();
        PollHC? newPoll = await GetPollData(pollId);
        if (newPoll == null) return;

        _pollRepository.DeletePoll(newPoll.PollId);

        var message = await GetPollMessage(newPoll);
        await message!.DeleteAsync();

        await ModifyOriginalResponseAsync(msg => { msg.Content = "Poll creation was aborted."; msg.Embed = null; msg.Components = null; });
    }

    [ComponentInteraction("polls.create.option.add:*", ignoreGroupNames: true)]
    public async Task CreateNewPollAddOption(string pollId)
    {
        Logger.Debug($"Add new option to poll {pollId}");
        var modal = new ModalBuilder()
            .WithCustomId($"polls.create.option.adding:{pollId}")
            .WithTitle(TranslationService.GetAttrString("polls", "create-poll-option-adding"))
            .AddTextInput(TranslationService.GetAttrString("polls", "create-poll-option-text"), "text", TextInputStyle.Short, minLength: 3, required: true)
            .AddTextInput(TranslationService.GetAttrString("polls", "create-poll-option-emoji"), "emoji", TextInputStyle.Short, placeholder: ":one: or leave empty for now", required: false);
        await RespondWithModalAsync(modal.Build());
    }

    [ModalInteraction("polls.create.option.adding:*", ignoreGroupNames: true)]
    public async Task CreateNewPollOptionSave(string pollId, PollsCreateOptionModal data)
    {
        Logger.Debug($"Save new option to poll {pollId}");
        await DeferAsync();
        PollHC? newPoll = await GetPollData(pollId);
        if (newPoll == null) return;

        int newOrder = (newPoll.Options.OrderByDescending(o => o.Order).FirstOrDefault()?.Order ?? 0) + 1;
        Logger.Debug($"New option with order {newOrder}");
        _pollRepository.AddPollOption(newPoll, UnicodeEmojiHelper.CircleFromNumber(newOrder), data.OptionName!, newOrder);

        _ = await ModifyOriginalResponseAsync(msg => msg.Embed = GetPollEmbed(newPoll, true).Build());
    }

    [ComponentInteraction("polls.vote:*,*", ignoreGroupNames: true)]
    public async Task VoteForPoll(string pollId, string optionId)
    {
        Logger.Debug($"User {Caller.Id} voted in {Context.Guild.Id} for {pollId} with {optionId}");
        await DeferAsync();
        PollHC? pollData = await GetPollData(pollId);
        if (pollData == null) return;

        if (pollData.IsClosed || pollData.EndDate < DateTime.UtcNow)
        {
            _ = await ModifyOriginalResponseAsync(msg => msg.Content = TranslationService.GetString("error-poll-vote-closed"));
            return;
        }

        _pollRepository.AddPollVote(pollData, optionId, Caller.Id);

        _ = await ModifyOriginalResponseAsync(msg => msg.Embed = GetPollEmbed(pollData, false).Build());

        if (!pollData.AreVotersHidden) {
            Dictionary<string, object> messageArguments = TranslationHelper.Arguments(
                "user", Caller.Mention,
                "poll", pollData.Title!,
                "duration", pollData.EndDate.ToDiscordTimestamp(TranslationService, DiscordTimestampFormat.RelativeTime));
            string votingMessage = TranslationService.GetString("poll-voted", messageArguments);
            await Context.Channel.SendMessageAsync(votingMessage, allowedMentions: AllowedMentions.None, messageReference: new MessageReference(pollData.PollId));
        }
    }

    private async Task<PollHC?> GetPollData(string pollId)
    {
        PollHC? newPoll = _pollRepository.GetPoll(ulong.Parse(pollId), false);
        if (newPoll == null)
        {
            _ = await ModifyOriginalResponseAsync(msg => msg.Content = TranslationService.GetString("error-poll-create-invalid-poll"));
            return null;
        }
        return newPoll;
    }

    private ComponentBuilder GetCreatePollButtons(ulong pollId) => new ComponentBuilder()
        .WithButton(TranslationService.GetAttrString("polls", "create-button-add-option"), $"polls.create.option.add:{pollId}", ButtonStyle.Primary)
        .WithButton(TranslationService.GetAttrString("polls", "create-button-remove-option"), $"polls.create.option.delete:{pollId}", ButtonStyle.Primary, disabled: true)
        .WithButton(TranslationService.GetAttrString("polls", "create-button-toggle-public-results"), $"polls.create.toggle.results:{pollId}", ButtonStyle.Secondary)
        .WithButton(TranslationService.GetAttrString("polls", "create-button-toggle-voters-hidden"), $"polls.create.toggle.voters:{pollId}", ButtonStyle.Secondary)
        .WithButton(TranslationService.GetAttrString("polls", "create-button-change-duration"), $"polls.create.duration:{pollId}", ButtonStyle.Secondary, disabled: true)
        .WithButton(TranslationService.GetAttrString("polls", "create-button-finish"), $"polls.create.finish:{pollId}", ButtonStyle.Success)
        .WithButton(TranslationService.GetAttrString("polls", "create-button-cancel"), $"polls.create.cancel:{pollId}", ButtonStyle.Danger);

    private EmbedBuilder GetPollEmbed(PollHC poll, bool isPreview = false)
    {
        string votesHidden = poll.AreVotersHidden
            ? TranslationService.GetAttrString("polls", "create-poll-voters-hidden-desc")
            : TranslationService.GetAttrString("polls", "create-poll-public-results-desc");
        string footer = isPreview
            ? TranslationService.GetAttrString("polls", "create-poll-footer", TranslationHelper.Arguments("votesHidden", votesHidden))
            : TranslationService.GetAttrString("polls", "footer", TranslationHelper.Arguments("user", Context.Guild.GetUser(poll.CreatorId).Username));
        var result = new EmbedBuilder()
            .WithTitle(poll.Title)
            .WithDescription(poll.Description)
            .WithColor(isPreview ? Color.Red : Color.Green)
            .WithFooter(footer)
            .WithTimestamp(poll.EndDate);
        if (poll.AreResultsPublic)
        {
            GetPollResults(ref result, poll);
        }
        return result;
    }

    private static void GetPollResults(ref EmbedBuilder pollEmbed, PollHC poll)
    {
        const int votesMaxSteps = 15;
        int totalVotes = poll.Votes.Count;

        foreach (var option in poll.Options)
        {
            int optionVotes = poll.Votes.Count(v => v.OptionId == option.Id);
            double votePercentage = totalVotes <= 0 ? 0 : (double)optionVotes / totalVotes;

            int voteSquares = 0;
            if (votePercentage > 0)
            {
                voteSquares = (int)Math.Round(votePercentage * votesMaxSteps);
            }

            var voteBar = new StringBuilder()
                .Append(UnicodeEmojiHelper.greenSquare.Repeat(voteSquares))
                .Append(UnicodeEmojiHelper.whiteSquare.Repeat(votesMaxSteps - voteSquares))
                .AppendFormat(" ({0})", optionVotes);

            pollEmbed.AddField($"{option.Emoji} {option.Text}", voteBar.ToString(), false);
        }
    }

    private async Task<ComponentBuilder> GetPollVotingButtons(string pollId)
    {
        var result = new ComponentBuilder();
        PollHC? poll = await GetPollData(pollId);
        if (poll == null) return result;

        foreach (var option in poll.Options)
        {
            if (!Emoji.TryParse(option.Emoji, out Emoji emoji))
            {
                emoji = Emoji.Parse(UnicodeEmojiHelper.CircleFromNumber(option.Order));
            }
            result.WithButton(label: option.Text, customId: $"polls.vote:{pollId},{option.Id}", style: ButtonStyle.Primary, emote: emoji);
            Logger.Debug($"Voting button for poll {pollId}: 'polls.vote:{pollId},{option.Id}'");
        }
        return result;
    }

    private async Task<IUserMessage?> GetPollMessage(PollHC? newPoll) =>
        await ((SocketTextChannel)Context.Guild.GetChannel(newPoll!.ChannelId)).GetMessageAsync(newPoll.PollId) as IUserMessage;
}
