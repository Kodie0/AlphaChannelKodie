using AlphaChannel.Contracts;
using AlphaChannel.Plugin.Auth;
using AlphaChannel.Plugin.Crypto;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Alpha Chat (E2E DMs). Decrypts on render via DmCipher/KeyVault - the plaintext cache is keyed by
// message id so re-renders every ImGui frame don't re-run the ECDH+AES-GCM math each time, same
// "decrypt once, cache the result" idiom Aetherphone's own MessageCipher uses.
internal sealed partial class MainWindow
{
    private bool conversationsDirty = true;
    private bool conversationsLoading;
    private ConversationSummaryDto[] conversations = [];
    private string? openConversationId;
    private string? openConversationOtherAccountId;
    private string? openConversationOtherHandle;
    private MessageDto[] openMessages = [];
    private readonly Dictionary<string, string> decryptedCache = new();
    private string messageComposerInput = string.Empty;
    private bool messagesLoading;
    private string? messagesError;

    private void DrawMessages()
    {
        if (CurrentSession is not { } session)
        {
            ImGui.TextColored(MutedText, "Sign in to use Alpha Chat.");
            if (ImGui.Button("Go to Settings"))
            {
                currentPage = HomePage.Settings;
            }

            return;
        }

        if (openConversationId is { } openId)
        {
            DrawThread(session, openId);
            return;
        }

        if (conversationsDirty && !conversationsLoading)
        {
            RefreshConversations(session.Token);
        }

        if (conversations.Length == 0)
        {
            ImGui.TextDisabled(conversationsLoading ? "Loading..." : "No conversations yet - message a friend from the Friends Channel.");
            return;
        }

        foreach (var conversation in conversations)
        {
            ImGui.PushID(conversation.ConversationId);
            ImGui.Text($"@{conversation.OtherHandle}");
            if (conversation.UnreadCount > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(Accent, $"({conversation.UnreadCount} unread)");
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Open"))
            {
                OpenConversation(session, conversation.ConversationId, conversation.OtherAccountId, conversation.OtherHandle);
            }

            ImGui.PopID();
        }
    }

    // Called from MainWindow.Social.cs's "Message" button too, not just from within this page -
    // starts (or resumes) a conversation with a friend and switches straight to the thread view.
    private void StartOrOpenConversation(CharacterSession session, string otherAccountId, string otherHandle)
    {
        currentPage = HomePage.Messages;
        _ = Task.Run(async () =>
        {
            var conversationId = await dmClient.StartConversationAsync(session.Token, otherAccountId);
            if (conversationId is not null)
            {
                OpenConversation(session, conversationId, otherAccountId, otherHandle);
            }
        });
    }

    private void OpenConversation(CharacterSession session, string conversationId, string otherAccountId, string otherHandle)
    {
        openConversationId = conversationId;
        openConversationOtherAccountId = otherAccountId;
        openConversationOtherHandle = otherHandle;
        openMessages = [];
        messageComposerInput = string.Empty;
        messagesError = null;
        RefreshMessages(session);
    }

    private void DrawThread(CharacterSession session, string conversationId)
    {
        if (ImGui.Button("< Back"))
        {
            openConversationId = null;
            conversationsDirty = true;
            return;
        }

        ImGui.SameLine();
        ImGui.TextColored(Accent, $"@{openConversationOtherHandle}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using (var child = ImRaii.Child("##thread", new Vector2(0, -60)))
        {
            if (child)
            {
                foreach (var message in openMessages)
                {
                    var mine = message.SenderAccountId == session.AccountId;
                    var text = decryptedCache.GetValueOrDefault(message.Id, messagesLoading ? "..." : "(couldn't decrypt)");
                    ImGui.TextColored(mine ? MutedText : Vector4.One, (mine ? "You: " : $"@{openConversationOtherHandle}: ") + text);
                    if (!mine)
                    {
                        ImGui.SameLine();
                        ImGui.PushID(message.Id);
                        if (ImGui.SmallButton("Report"))
                        {
                            ReportMessage(session, message, text);
                        }

                        ImGui.PopID();
                    }
                }
            }
        }

        if (messagesError is { Length: > 0 } error)
        {
            ImGui.TextColored(Danger, error);
        }

        ImGui.SetNextItemWidth(-80f);
        var sent = ImGui.InputTextWithHint("##composer", "Message...", ref messageComposerInput, 2000, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button("Send") || sent) && messageComposerInput.Trim().Length > 0)
        {
            SendMessage(session, conversationId, messageComposerInput.Trim());
            messageComposerInput = string.Empty;
        }
    }

    // Fired from StreamClient's receive loop when a dm.message push arrives (see
    // AlphaChannel.Contracts.SocialSignalType) - the push carries the full sealed message
    // (Ciphertext/Nonce/Tag), so a message to the currently-open thread can be decrypted and
    // appended immediately with no REST round-trip. Anything else just marks the conversation list
    // stale so its unread count catches up next time it's drawn.
    private void ApplyIncomingDm(SocialControl update)
    {
        conversationsDirty = true;

        if (update.ConversationId != openConversationId ||
            update.MessageId is not { Length: > 0 } messageId ||
            update.AccountId is not { Length: > 0 } senderId ||
            update.Ciphertext is not { Length: > 0 } ciphertextBase64 ||
            update.Nonce is not { Length: > 0 } nonceBase64 ||
            update.Tag is not { Length: > 0 } tagBase64 ||
            CurrentSession is not { } session ||
            openConversationOtherAccountId is not { } otherAccountId)
        {
            return;
        }

        var message = new MessageDto(messageId, senderId, ciphertextBase64, nonceBase64, tagBase64, update.TimestampUnix ?? 0, null);
        openMessages = [.. openMessages, message];

        _ = Task.Run(async () =>
        {
            var myIdentity = await keyVault.EnsureIdentityAsync(session.AccountId, session.Token);
            var otherKey = await keyVault.GetOtherPartyKeyAsync(otherAccountId, session.Token);
            if (otherKey is not null)
            {
                DecryptAndCache(message, myIdentity, otherKey);
            }

            await dmClient.MarkReadAsync(session.Token, openConversationId!);
        });
    }

    private void RefreshConversations(string bearerToken)
    {
        conversationsDirty = false;
        conversationsLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                conversations = await dmClient.GetConversationsAsync(bearerToken) ?? [];
            }
            finally
            {
                conversationsLoading = false;
            }
        });
    }

    private void RefreshMessages(CharacterSession session)
    {
        messagesLoading = true;
        var conversationId = openConversationId;
        var otherAccountId = openConversationOtherAccountId;
        if (conversationId is null || otherAccountId is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var page = await dmClient.GetMessagesAsync(session.Token, conversationId, before: null);
                if (page is null)
                {
                    messagesError = "Couldn't load messages.";
                    return;
                }

                var myIdentity = await keyVault.EnsureIdentityAsync(session.AccountId, session.Token);
                var otherKey = await keyVault.GetOtherPartyKeyAsync(otherAccountId, session.Token);

                var ordered = page.Items.OrderBy(m => m.SentAtUnix).ToArray();
                openMessages = ordered;

                if (otherKey is not null)
                {
                    foreach (var message in ordered)
                    {
                        DecryptAndCache(message, myIdentity, otherKey);
                    }
                }

                await dmClient.MarkReadAsync(session.Token, conversationId);
            }
            finally
            {
                messagesLoading = false;
            }
        });
    }

    private void SendMessage(CharacterSession session, string conversationId, string plaintext)
    {
        var otherAccountId = openConversationOtherAccountId;
        if (otherAccountId is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var myIdentity = await keyVault.EnsureIdentityAsync(session.AccountId, session.Token);
            var otherKey = await keyVault.GetOtherPartyKeyAsync(otherAccountId, session.Token);
            if (otherKey is null)
            {
                messagesError = $"@{openConversationOtherHandle} hasn't set up encryption yet - they need to sign in at least once.";
                return;
            }

            var sealedMessage = DmCipher.Encrypt(myIdentity, otherKey, plaintext);
            var request = new SendMessageRequest(
                Convert.ToBase64String(sealedMessage.Ciphertext),
                Convert.ToBase64String(sealedMessage.Nonce),
                Convert.ToBase64String(sealedMessage.Tag),
                Convert.ToBase64String(sealedMessage.CommitmentTag));

            var sent = await dmClient.SendMessageAsync(session.Token, conversationId, request);
            if (sent is not null)
            {
                decryptedCache[sent.Id] = plaintext;
                openMessages = [.. openMessages, sent];
            }
        });
    }

    private void DecryptAndCache(MessageDto message, System.Security.Cryptography.ECDiffieHellman myIdentity, System.Security.Cryptography.ECDiffieHellman otherKey)
    {
        if (decryptedCache.ContainsKey(message.Id))
        {
            return;
        }

        var opened = DmCipher.Decrypt(myIdentity, otherKey,
            Convert.FromBase64String(message.Ciphertext), Convert.FromBase64String(message.Nonce), Convert.FromBase64String(message.Tag));
        if (opened is not null)
        {
            decryptedCache[message.Id] = opened.Plaintext;
        }
    }

    private void ReportMessage(CharacterSession session, MessageDto message, string revealedPlaintext)
    {
        _ = Task.Run(async () =>
        {
            var myIdentity = await keyVault.EnsureIdentityAsync(session.AccountId, session.Token);
            var otherKey = openConversationOtherAccountId is { } otherId ? await keyVault.GetOtherPartyKeyAsync(otherId, session.Token) : null;
            if (otherKey is null)
            {
                return;
            }

            var opened = DmCipher.Decrypt(myIdentity, otherKey,
                Convert.FromBase64String(message.Ciphertext), Convert.FromBase64String(message.Nonce), Convert.FromBase64String(message.Tag));
            if (opened is null)
            {
                return;
            }

            await reportClient.SubmitAsync(session.Token, "harassment", null, openConversationOtherAccountId, message.Id,
                revealedPlaintext, Convert.ToBase64String(opened.FrankingKey));
        });
    }
}
