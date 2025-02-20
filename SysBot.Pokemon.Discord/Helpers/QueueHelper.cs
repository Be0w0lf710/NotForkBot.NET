﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Net;
using PKHeX.Core;
using System.Threading.Tasks;
using static SysBot.Pokemon.Discord.Helpers.ColorHelper;
using static PKHeX.Core.AutoMod.Aesthetics;

namespace SysBot.Pokemon.Discord
{
    public static class QueueHelper<T> where T : PKM, new()
    {
        private static EmbedBuilder Embed { get; set; } = new();
        private static string? ETA;
        private static string? EmbedMsg;
        private static string? Queuepos;
        private static QueueResultAdd? Added;
        private const uint MaxTradeCode = 9999_9999;

        public static async Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, SocketUser trader, int catchID = 0)
        {
            if ((uint)code > MaxTradeCode)
            {
                await context.Channel.SendMessageAsync("Trade code should be 00000000-99999999!").ConfigureAwait(false);
                return;
            }

            try
            {
                const string helper = "I've added you to the queue! I'll message you here when your trade is starting.";
                IUserMessage test = await trader.SendMessageAsync(helper).ConfigureAwait(false);
                var hub = SysCord<T>.Runner.Hub;

                // Try adding
                var result = AddToTradeQueue(context, trade, code, trainer, sig, routine, type, trader, out var msg, out var msg2, catchID);

                // Notify in channel
                if (!hub.Config.Trade.UseTradeEmbeds)
                {
                    await context.Channel.SendMessageAsync(msg).ConfigureAwait(false);
                }

                if (hub.Config.Trade.UseTradeEmbeds)
                {
                    if (Added == QueueResultAdd.AlreadyInQueue)
                    {
                        await context.Channel.SendMessageAsync(msg).ConfigureAwait(false);
                    }
                    if (type is PokeTradeType.Specific && Added != QueueResultAdd.AlreadyInQueue)
                    {
                        await AddToTradeQueueEmbed(trade, trader);
                        await context.Channel.SendMessageAsync(embed: Embed.Build()).ConfigureAwait(false);
                    }
                }
                // Notify in PM to mirror what is said in the channel.
                await trader.SendMessageAsync($"{msg}\nYour trade code will be **{code:0000 0000}**.").ConfigureAwait(false);

                // Clean Up
                if (result)
                {
                    // Delete the user's join message for privacy
                    if (!context.IsPrivate)
                        await context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
                }
                else
                {
                    // Delete our "I'm adding you!", and send the same message that we sent to the general channel.
                    await test.DeleteAsync().ConfigureAwait(false);
                }
            }
            catch (HttpException ex)
            {
                await HandleDiscordExceptionAsync(context, trader, ex).ConfigureAwait(false);
            }
        }

        public static async Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, int catchID = 0)
        {
            await AddToQueueAsync(context, code, trainer, sig, trade, routine, type, context.User, catchID).ConfigureAwait(false);
        }

        private static bool AddToTradeQueue(SocketCommandContext context, T pk, int code, string trainerName, RequestSignificance sig, PokeRoutineType type, PokeTradeType t, SocketUser trader, out string msg, out string msg2, int catchID = 0)
        {
            var userID = trader.Id;
            var name = trader.Username;

            var trainer = new PokeTradeTrainerInfo(trainerName, userID);
            var notifier = new DiscordTradeNotifier<T>(pk, trainer, code, trader);
            var detail = new PokeTradeDetail<T>(pk, trainer, notifier, t, code, sig == RequestSignificance.Favored);
            var trade = new TradeEntry<T>(detail, userID, type, name);
            var mgr = SysCordSettings.Manager;
            var hub = SysCord<T>.Runner.Hub;
            var Info = hub.Queues.Info;

            Added = Info.AddToTradeQueue(trade, userID, sig == RequestSignificance.Owner);

            if (Added == QueueResultAdd.AlreadyInQueue && !mgr.CanUseSudo(trader.Id))
            {
                msg = "Sorry, you are already in the queue.";
                msg2 = "Sorry, you are already in the queue.";
                return false;
            }

            if (detail.Type == PokeTradeType.TradeCord)
                TradeCordHelper<T>.TradeCordTrades.Add(trader.Id, catchID);

            var position = Info.CheckPosition(userID, type);

            var ticketID = "";
            if (TradeStartModule<T>.IsStartChannel(context.Channel.Id))
                ticketID = $", unique ID: {detail.ID}";

            var pokeName = "";
            if ((t == PokeTradeType.Specific || t == PokeTradeType.TradeCord || t == PokeTradeType.SupportTrade || t == PokeTradeType.Giveaway) && pk.Species != 0)
                pokeName = $"{(hub.Config.Trade.UseTradeEmbeds ? "" : t == PokeTradeType.SupportTrade && pk.Species != (int)Species.Ditto && pk.HeldItem != 0 ? $" Receiving: {(Species)pk.Species} ({ShowdownParsing.GetShowdownText(pk).Split('@', '\n')[1].Trim()})" : $" Receiving: {(Species)pk.Species}. ")}";
            string? pokeName2 = $" Receiving: {(t == PokeTradeType.SupportTrade && pk.Species != (int)Species.Ditto && pk.HeldItem != 0 ? $"{(Species)pk.Species} ({ShowdownParsing.GetShowdownText(pk).Split('@', '\n')[1].Trim()})" : $"{(Species)pk.Species}")}.";
            msg = $"{trader.Mention} - Added to the {type} queue{ticketID}. Current Position: {position.Position}.{pokeName2}";
            msg2 = $"{trader.Mention} - Added to the {type} queue{ticketID}. ";
            EmbedMsg = msg2;
            Queuepos = $"Current Position: {position.Position}.{pokeName}";

            var botct = Info.Hub.Bots.Count;
            if (position.Position > botct)
            {
                var eta = Info.Hub.Config.Queues.EstimateDelay(position.Position, botct);
                ETA = $"Estimated Wait: {eta:F1} minutes.";
                msg += ETA;
            }
            return true;
        }

        private static Task AddToTradeQueueEmbed(T pk, SocketUser trader)
        {
            var user = trader;
            var name = user.Username;

            var author = new EmbedAuthorBuilder
            {
                Name = $"{name}'s{(pk.IsShiny ? " Shiny" : "")} Pokémon",
                IconUrl = $"https://raw.githubusercontent.com/BakaKaito/HomeImages/main/Ballimg/80x80/{((Ball)pk.Ball).ToString().ToLower()}ball.png"
            };
            var footer = new EmbedFooterBuilder
            {
                Text = Queuepos + "\n" + ETA,
            };
            GameStrings strings = GameInfo.GetStrings(GameLanguage.DefaultLanguage);
            var items = strings.GetItemStrings((EntityContext)8, GameVersion.SWSH);
            var itemName = items[pk.HeldItem];
            string movesList = "";
            for (int i = 0; i < pk.Moves.Length; i++)
            {
                if (pk.Moves[i] != 0)
                {
                    movesList += $"- {(Move)pk.Moves[i]}\n";
                }
            }

            Embed = new EmbedBuilder
            {
                Color = GetDiscordColor(pk.IsShiny ? ShinyMap[((Species)pk.Species, pk.Form)] : (PersonalColor)pk.PersonalInfo.Color),
                ThumbnailUrl = TradeExtensions<PK9>.PokeImg(pk, false, false),
                Author = author,
                Footer = footer,
            };
            Embed.AddField(x =>
            {
                x.Name = $"**{(pk.IsShiny ? "★ " : "")}{(Species)pk.Species}{(pk.Gender == 0 ? " (M)" : pk.Gender == 1 ? " (F)" : "")} {(pk.HeldItem != 0 ? $"- {itemName}**" : "**")}";
                x.Value = $"Level:{pk.CurrentLevel}\nAbility: {(Ability)pk.Ability}\n{(Nature)pk.Nature} Nature\nIVs: {pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}\nMoves:\n{movesList}\n{EmbedMsg}";
                x.IsInline = false;
            });

            ETA = "";

            return Task.CompletedTask;
        }

        private static async Task HandleDiscordExceptionAsync(SocketCommandContext context, SocketUser trader, HttpException ex)
        {
            string message = string.Empty;
            switch (ex.DiscordCode)
            {
                case DiscordErrorCode.InsufficientPermissions or DiscordErrorCode.MissingPermissions:
                    {
                        // Check if the exception was raised due to missing "Send Messages" or "Manage Messages" permissions. Nag the bot owner if so.
                        var permissions = context.Guild.CurrentUser.GetPermissions(context.Channel as IGuildChannel);
                        if (!permissions.SendMessages)
                        {
                            // Nag the owner in logs.
                            message = "You must grant me \"Send Messages\" permissions!";
                            Base.LogUtil.LogError(message, "QueueHelper");
                            return;
                        }
                        if (!permissions.ManageMessages)
                        {
                            var app = await context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
                            var owner = app.Owner.Id;
                            message = $"<@{owner}> You must grant me \"Manage Messages\" permissions!";
                        }
                    }
                    break;
                case DiscordErrorCode.CannotSendMessageToUser:
                    {
                        // The user either has DMs turned off, or Discord thinks they do.
                        message = context.User == trader ? "You must enable private messages in order to be queued!" : "The mentioned user must enable private messages in order for them to be queued!";
                    }
                    break;
                default:
                    {
                        // Send a generic error message.
                        message = ex.DiscordCode != null ? $"Discord error {(int)ex.DiscordCode}: {ex.Reason}" : $"Http error {(int)ex.HttpCode}: {ex.Message}";
                    }
                    break;
            }
            await context.Channel.SendMessageAsync(message).ConfigureAwait(false);
        }

    }
}
