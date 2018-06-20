using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using Discord.Commands;
using PacManBot.Extensions;

namespace PacManBot.Commands
{
    [Name(CustomEmoji.Staff + "Mod"), Remarks("5")]
    [BetterRequireUserPermission(GuildPermission.ManageMessages)]
    public class ModModule : BaseCustomModule
    {
        public ModModule(IServiceProvider services) : base(services) { }


        string ErrorMessage => $"Please try again or, if the problem persists, contact the bot author using `{Prefix}feedback`.";


        [Command("say"), Remarks("Make the bot say anything")]
        [Summary("Repeats back the message provided. Only users with the Manage Messages permission can use this command.")]
        public async Task Say([Remainder]string message)
            => await ReplyAsync(message.SanitizeMentions());


        [Command("clear"), Alias("clean", "cl"), Remarks("Clear this bot's messages and commands")]
        [Summary("Clears all commands and messages for *this bot only*, from the last [amount] messages, " +
                 "or the last 10 messages by default.\nOnly users with the Manage Messages permission can use this command.")]
        [BetterRequireBotPermission(ChannelPermission.ReadMessageHistory)]
        public async Task ClearCommandMessages(int amount = 10)
        {
            int _ = 0;
            bool canDelete = Context.BotCan(ChannelPermission.ManageMessages);

            var messages = (await Context.Channel.GetMessagesAsync(amount).FlattenAsync())
                .Select(x => x as IUserMessage).Where(x => x != null)
                .Where(x => x.Author.Id == Context.Client.CurrentUser.Id
                     || canDelete &&
                        (x.Content.StartsWith(AbsolutePrefix) && !x.Content.StartsWith("<@")
                        || x.HasMentionPrefix(Context.Client.CurrentUser, ref _)));

            foreach (var message in messages)
            {
                try
                {
                    await message.DeleteAsync(DefaultOptions);
                }
                catch (Exception e) when (e is HttpException || e is TimeoutException)
                {
                    await logger.Log(LogSeverity.Warning,
                                     $"Couldn't delete message {message.Id} in {Context.Channel.FullName()}: {e.Message}");
                }
            }
        }


        [Command("setprefix"), Remarks("Set a custom prefix for this server (Admin)")]
        [Summary("Change the custom prefix for this server. Only server Administrators can use this command.\n" +
                 "Prefixes can't contain these characters: \\* \\_ \\~ \\` \\\\")]
        [BetterRequireUserPermission(GuildPermission.Administrator)]
        public async Task SetServerPrefix(string prefix)
        {
            if (prefix.ContainsAny("*", "_", "~", "`", "\\"))
            {
                await ReplyAsync($"{CustomEmoji.Cross} The prefix can't contain markdown special characters: *_~\\`\\\\");
                return;
            }

            try
            {
                storage.SetPrefix(Context.Guild.Id, prefix);
                await ReplyAsync($"{CustomEmoji.Check} Prefix for this server has been successfully set to `{prefix}`");
                await logger.Log(LogSeverity.Info, $"Prefix for server {Context.Guild.Id} set to {prefix}");
            }
            catch (Exception e)
            {
                await logger.Log(LogSeverity.Error, $"{e}");
                await ReplyAsync($"{CustomEmoji.Cross} There was a problem setting the prefix. {ErrorMessage}");
            }
        }


        [Command("togglewaka"), Remarks("Toggle \"waka\" autoresponse from the bot")]
        [Summary("The bot normally responds every time a message contains purely multiples of \"waka\", " +
                 "unless it's turned off server-wide using this command. Requires the user to be a Moderator.")]
        public async Task ToggleWakaResponse()
        {
            try
            {
                bool nowaka = storage.ToggleWaka(Context.Guild.Id);
                await ReplyAsync($"{CustomEmoji.Check} \"Waka\" responses turned **{(nowaka ? "ON" : "OFF")}** in this server.");
            }
            catch (Exception e)
            {
                await logger.Log(LogSeverity.Error, $"{e}");
                await ReplyAsync($"{CustomEmoji.Cross} Oops, something went wrong. {ErrorMessage}");
            }
        }
    }
}
