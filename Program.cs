using Discord;
using Discord.Rest;
using Discord.WebSocket;
using FezMetadataBot.ConfigUtil;
using System.Threading.Channels;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using MetadataExtractor.Formats.Png;
using System.Reflection.Metadata;

internal class Program {
    private static DiscordSocketClient _client;
    private static List<ulong> _channels = new List<ulong> { 1259895562984226908, 1259848043092770928, 678077350231015437 };

    private static async Task Main() {

        EnsureConfigExists();

        _client = new DiscordSocketClient(new DiscordSocketConfig {
            LogLevel = LogSeverity.Info,
            MessageCacheSize = 5000,
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMessageReactions | GatewayIntents.MessageContent
        });


        _client.Log += LogAsync;
        _client.MessageReceived += MessageReceivedAsync;
        _client.ReactionAdded += ReactionAddedAsync;

        await _client.LoginAsync(TokenType.Bot, Configuration.Load().Token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private static async Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> msg, Cacheable<IMessageChannel, ulong> textChannel, SocketReaction reaction) {
        if(reaction.User.IsSpecified && !reaction.User.Value.IsBot && reaction.Emote.Name == "🔍") {
            // Ignore messages from channels that are not in the list
            if(!_channels.Contains(textChannel.Id))
                return;

            var message = await msg.GetOrDownloadAsync();
            var user = await _client.GetUserAsync(reaction.UserId);
            if(user is not null) {
                try {
                    var response = await ExtractEXIFDataFromAttachment(message.Attachments.First());
                    if(response != "") {
                        var embedBuilder = new EmbedBuilder()
                            .WithTitle("Metadata")
                            .WithDescription($"```json\n{response}```")
                            .WithColor(Color.Blue); // Customize as needed

                        await user.SendMessageAsync(embed: embedBuilder.Build());
                    }
                    else {
                        await user.SendMessageAsync("No EXIF data found.");
                    }
                }
                catch(Exception ex) {
                    Console.WriteLine($"Failed to send DM to user {user.Username}: {ex.Message}");
                }
            }
        }
        await Task.CompletedTask;
    }

    private static async Task<string?> ExtractEXIFDataFromAttachment(IAttachment img) {
        using(var httpClient = new HttpClient()) {
            try {
                var response = await httpClient.GetAsync(img.Url);
                response.EnsureSuccessStatusCode();

                byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();

                var directories = ImageMetadataReader.ReadMetadata(new MemoryStream(imageBytes));
                var pngtEXtDirectory = directories.OfType<PngDirectory>().ToArray()[1];

                string exifData = "";

                foreach(var tag in pngtEXtDirectory.Tags) {
                    exifData += $"{tag.Name}: {tag.Description}\n";
                }

                if(exifData == "") {
                    return "No EXIF data found.";
                }
                return exifData;

            }
            catch(Exception ex) {
                Console.WriteLine($"Error extracting EXIF data: {ex.Message}");
                return null;
            }
        }
    }

    private static async Task MessageReceivedAsync(SocketMessage message) {
        // Ignore messages from bot itself and non-GuildText channels
        if(!(message is IUserMessage userMessage) || message.Author.IsBot || !(message.Channel is ITextChannel textChannel))
            return;

        // Ignore messages from channels that are not in the list
        if(!_channels.Contains(textChannel.Id))
            return;

        // Check if the message has attachments
        if(message.Attachments.Count > 0) {
            // Check if the first attachment is an image (you can refine this logic further)
            var attachment = message.Attachments.First();
            if(IsImage(attachment)) {
                // Add reaction :mag: to the message
                await userMessage.AddReactionAsync(new Emoji("🔍"));
            }
        }

        await Task.CompletedTask;
    }

    private static bool IsImage(IAttachment attachment) {
        // Simple check if the attachment is an image based on its URL
        string[] imageContentTypes = { "image/png", "image/jpg", "image/jpeg", "image/gif", "image/bmp" };
        return imageContentTypes.Any(ext => attachment.ContentType.Contains(ext, StringComparison.OrdinalIgnoreCase));
    }


    private static Task LogAsync(LogMessage log) {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }


    private static async void EnsureConfigExists() {
        if(!System.IO.Directory.Exists(Path.Combine(AppContext.BaseDirectory, "data")))
            System.IO.Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "data"));

        var loc = Path.Combine(AppContext.BaseDirectory, "data/configuration.json");

        if(!File.Exists(loc) || Configuration.Load().Token == null) {
            Console.WriteLine("Configuration not found...\nCreating...");
            var config = new Configuration();
            Console.WriteLine("Token :");
            config.Token = Console.ReadLine();
            Console.WriteLine("Default Prefix :");
            config.Prefix = Console.ReadLine();
            config.Save();
        }
        else {
            Console.WriteLine("Configuration found...\nLoading...");
        }
    }
}