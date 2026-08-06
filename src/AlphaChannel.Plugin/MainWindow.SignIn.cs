using AlphaChannel.Contracts;
using AlphaChannel.Plugin.Auth;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Sign-in/link UI for the XIVAuth device flow (see Auth/SignInFlow.cs for the actual network
// orchestration - this file is purely presentation plus the small bit of state needed to poll it
// from Draw() each frame). Reachable from the Settings page's Account section.
internal sealed partial class MainWindow
{
    private enum SignInState
    {
        Idle,
        Requesting,
        AwaitingBrowser,
        Onboarding,
        Succeeded,
        Failed,
    }

    private static readonly string[] KnownRaces =
        ["Hyur", "Elezen", "Lalafell", "Miqo'te", "Roegadyn", "Au Ra", "Hrothgar", "Viera"];

    private SignInState signInState = SignInState.Idle;
    private string signInVerificationUri = string.Empty;
    private string signInUserCode = string.Empty;
    private string signInStatusMessage = string.Empty;
    private bool signInModalPending;
    private CancellationTokenSource? signInCts;
    private CharacterSession? pendingOnboardingSession;
    private readonly HashSet<string> onboardingRaces = [];
    private bool onboardingWantsToSeeLalafellContent = true;

    private LinkedCharacterDto[]? myLinkedCharacters;
    private string displayNameInput = string.Empty;
    private string? lastDisplayNameSyncedFor;
    private string? displayNameError;
    private bool inviteCodeRefreshing;
    private string onboardingNameInput = string.Empty;
    private string? onboardingNameError;
    private bool onboardingNameSubmitting;

    private string? lastProfileSyncedFor;
    private string? profileIconInput;
    private string profileColorInput = "#9966FA";
    private string profileBioInput = string.Empty;
    private string profileStatusInput = string.Empty;
    private bool profileSaving;
    private string? profileError;

    private void DrawAccountSettings()
    {
        if (CurrentSession is { } session)
        {
            // Syncs the input once per session change rather than every frame, so typing isn't
            // fought by a field that keeps resetting to the server value mid-edit.
            if (lastDisplayNameSyncedFor != session.AccountId)
            {
                displayNameInput = session.DisplayName;
                lastDisplayNameSyncedFor = session.AccountId;
            }

            ImGui.TextColored(Good, $"Signed in as {session.DisplayName} (@{session.Handle})");

            // DisplayName still equals the random Handle means onboarding never actually finished
            // (dismissed early, or the modal closed for some other reason) - friends have no
            // memorable name to search for until this is fixed, and it's otherwise a silent trap
            // (the account works fine for everything except being findable).
            if (session.DisplayName == session.Handle)
            {
                ImGui.TextColored(Danger, "You haven't picked a name yet - friends can't find or add you until you do. Set one below.");
            }

            var displayNameValid = DisplayNameRules.IsValid(displayNameInput);
            using (displayNameValid ? default : ImRaii.PushColor(ImGuiCol.Text, Danger))
            {
                ImGui.SetNextItemWidth(200f);
                ImGui.InputText("##displayName", ref displayNameInput, DisplayNameRules.MaxLength);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(!displayNameValid || displayNameInput.Trim() == session.DisplayName))
            {
                if (ImGui.SmallButton("Save name"))
                {
                    var token = session.Token;
                    var newName = displayNameInput.Trim();
                    _ = Task.Run(async () =>
                    {
                        var outcome = await authClient.UpdateDisplayNameAsync(token, newName);
                        if (outcome.Account is { } updated)
                        {
                            session.DisplayName = updated.DisplayName;
                            onSessionChanged(session);
                            displayNameError = null;
                        }
                        else
                        {
                            displayNameError = outcome.NameTaken
                                ? "That name's already taken - try another."
                                : outcome.InvalidFormat
                                    ? "That name doesn't fit the rules below."
                                    : "Couldn't save that name.";
                        }
                    });
                }
            }

            ImGui.TextColored(MutedText,
                $"{DisplayNameRules.MinLength}-{DisplayNameRules.MaxLength} characters: letters, numbers, single spaces, _ or -.");
            if (displayNameError is { Length: > 0 } nameError)
            {
                ImGui.TextColored(Danger, nameError);
            }

            ImGui.Spacing();
            ImGui.TextColored(MutedText, "Your invite code - share it (Discord, voice chat) and whoever redeems it becomes an instant friend, no name search needed:");
            var inviteCodeDisplay = session.InviteCode;
            ImGui.SetNextItemWidth(120f);
            ImGui.InputText("##inviteCode", ref inviteCodeDisplay, 16, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy code"))
            {
                ImGui.SetClipboardText(session.InviteCode);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(inviteCodeRefreshing))
            {
                if (ImGui.SmallButton("Refresh"))
                {
                    inviteCodeRefreshing = true;
                    var token = session.Token;
                    _ = Task.Run(async () =>
                    {
                        var summary = await authClient.GetMeAsync(token);
                        inviteCodeRefreshing = false;
                        if (summary is not null)
                        {
                            session.InviteCode = summary.InviteCode;
                            onSessionChanged(session);
                        }
                    });
                }
            }

            ImGui.TextColored(MutedText, "Rotates automatically each time someone redeems it, so an old shared copy stops working - hit Refresh to see the current one.");

            if (ImGui.Button("Sign out"))
            {
                _ = authClient.RevokeAsync(session.Token);
                onSessionChanged(null);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Show linked characters"))
            {
                var token = session.Token;
                _ = Task.Run(async () => myLinkedCharacters = await authClient.GetMyCharactersAsync(token));
            }

            if (myLinkedCharacters is { } characters)
            {
                foreach (var character in characters)
                {
                    ImGui.BulletText($"{character.CharacterName} @ {character.World}" + (character.IsPrimary ? " (primary)" : ""));
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawProfileEditor(session);
        }
        else
        {
            ImGui.TextColored(MutedText, "Sign in to use Friends, Messages, Activity, and Watch-along.");

            var canSignIn = !string.IsNullOrEmpty(CurrentCharacterName) && !string.IsNullOrEmpty(CurrentWorldName);
            using (ImRaii.Disabled(!canSignIn || signInState is SignInState.Requesting or SignInState.AwaitingBrowser))
            {
                if (ImGui.Button("Sign in with XIVAuth"))
                {
                    StartSignIn(linkUsing: null);
                }
            }

            // Offers to link this (not-yet-signed-in) character onto an account already
            // established on a different character on this install, rather than making a second
            // account - see Auth/AccountService's FindOrCreateAccountForCharacterAsync.
            if (Plugin.Cfg.CharacterSessions.Values.FirstOrDefault() is { } existing)
            {
                ImGui.SameLine();
                using (ImRaii.Disabled(signInState is SignInState.Requesting or SignInState.AwaitingBrowser))
                {
                    if (ImGui.Button($"Link to @{existing.Handle}"))
                    {
                        StartSignIn(existing);
                    }
                }
            }
        }

        if (signInState == SignInState.Failed && signInStatusMessage is { Length: > 0 } failure)
        {
            ImGui.TextColored(Danger, failure);
        }
    }

    private void DrawProfileEditor(CharacterSession session)
    {
        if (lastProfileSyncedFor != session.AccountId)
        {
            profileIconInput = session.AvatarIcon;
            profileColorInput = session.AvatarColorHex;
            profileBioInput = session.Bio ?? string.Empty;
            profileStatusInput = session.StatusMessage ?? string.Empty;
            lastProfileSyncedFor = session.AccountId;
        }

        SectionHeader("Profile");
        DrawAvatarChip(profileIconInput, profileColorInput, 48);
        ImGui.SameLine();
        ImGui.TextColored(MutedText, "Shown next to your name everywhere - Friends, Alpha Chat, Tweeter.");

        ImGui.Spacing();
        ImGui.TextColored(MutedText, "Icon");
        DrawIconPicker(ref profileIconInput);

        ImGui.Spacing();
        ImGui.TextColored(MutedText, "Color");
        DrawColorPicker(ref profileColorInput);

        ImGui.Spacing();
        ImGui.SetNextItemWidth(300f);
        ImGui.InputTextWithHint("##status", "Status (e.g. \"LFG\", \"AFK\")", ref profileStatusInput, 64);

        ImGui.SetNextItemWidth(300f);
        ImGui.InputTextMultiline("##bio", ref profileBioInput, 160, new Vector2(300, 60));

        ImGui.Spacing();
        using (ImRaii.Disabled(profileSaving))
        {
            if (ImGui.Button(profileSaving ? "Saving..." : "Save profile"))
            {
                SaveProfile(session);
            }
        }

        if (profileError is { Length: > 0 } error)
        {
            ImGui.TextColored(Danger, error);
        }
    }

    private void SaveProfile(CharacterSession session)
    {
        var token = session.Token;
        var request = new UpdateProfileRequest(null, profileIconInput, profileColorInput, profileBioInput, profileStatusInput);

        profileSaving = true;
        profileError = null;
        _ = Task.Run(async () =>
        {
            var outcome = await authClient.UpdateProfileAsync(token, request);
            profileSaving = false;
            if (outcome.Account is { } updated)
            {
                session.AvatarIcon = updated.AvatarIcon;
                session.AvatarColorHex = updated.AvatarColorHex;
                session.Bio = updated.Bio;
                session.StatusMessage = updated.StatusMessage;
                onSessionChanged(session);
            }
            else
            {
                profileError = "Couldn't save your profile.";
            }
        });
    }

    private void StartSignIn(CharacterSession? linkUsing)
    {
        var characterName = CurrentCharacterName;
        var world = CurrentWorldName;
        if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(world))
        {
            return;
        }

        signInCts?.Cancel();
        signInCts = new CancellationTokenSource();
        var cancellationToken = signInCts.Token;

        signInState = SignInState.Requesting;
        signInVerificationUri = string.Empty;
        signInUserCode = string.Empty;
        signInStatusMessage = string.Empty;
        signInModalPending = true;

        var isLalafell = CurrentIsLalafell;
        _ = Task.Run(async () =>
        {
            var result = await signInFlow.RunAsync(characterName, world, isLalafell, linkUsing?.Token, start =>
            {
                signInVerificationUri = start.VerificationUriComplete ?? start.VerificationUri;
                signInUserCode = start.UserCode;
                signInState = SignInState.AwaitingBrowser;
            }, cancellationToken);

            switch (result.Outcome)
            {
                case SignInOutcome.Success when result.IsNewAccount:
                    // Don't persist the session yet for a brand-new account - onboarding still
                    // needs to run, and CurrentSession flipping non-null here would let the
                    // sign-in UI disappear mid-onboarding (DrawAccountSettings only shows it when
                    // CurrentSession is null). SubmitOnboarding below both submits the answers and
                    // is what actually calls onSessionChanged.
                    pendingOnboardingSession = result.Session;
                    onboardingRaces.Clear();
                    onboardingWantsToSeeLalafellContent = true;
                    onboardingNameInput = string.Empty;
                    onboardingNameError = null;
                    onboardingNameSubmitting = false;
                    signInState = SignInState.Onboarding;
                    break;
                case SignInOutcome.Success:
                    onSessionChanged(result.Session);
                    signInState = SignInState.Succeeded;
                    break;
                case SignInOutcome.Cancelled:
                    signInState = SignInState.Idle;
                    break;
                default:
                    signInStatusMessage = result.Message ?? "Sign-in failed.";
                    signInState = SignInState.Failed;
                    break;
            }
        }, cancellationToken);
    }

    private void SubmitOnboarding()
    {
        if (pendingOnboardingSession is not { } session)
        {
            return;
        }

        var name = onboardingNameInput.Trim();
        if (!DisplayNameRules.IsValid(name))
        {
            onboardingNameError = "Pick a name so friends can find you.";
            return;
        }

        var races = onboardingRaces.ToArray();
        var wantsToSeeLalafellContent = onboardingWantsToSeeLalafellContent;
        onboardingNameError = null;
        onboardingNameSubmitting = true;

        _ = Task.Run(async () =>
        {
            // The gamer tag is what other players actually search/add by (see FriendService.
            // FindAccountByDisplayNameAsync), so it has to be reserved before onboarding can finish -
            // unlike races/Lalafell-visibility below, which are fire-and-forget preferences.
            var outcome = await authClient.UpdateDisplayNameAsync(session.Token, name);
            if (outcome.Account is not { } updated)
            {
                onboardingNameError = outcome.NameTaken
                    ? "That name's already taken - try another."
                    : outcome.InvalidFormat
                        ? "That name doesn't fit the rules above."
                        : "Couldn't save that name, try again.";
                onboardingNameSubmitting = false;
                return;
            }

            session.DisplayName = updated.DisplayName;
            await authClient.SubmitOnboardingAsync(session.Token, races, wantsToSeeLalafellContent);

            signInState = SignInState.Succeeded;
            onSessionChanged(session);
            pendingOnboardingSession = null;
            onboardingNameSubmitting = false;
        });
    }

    private void DrawSignInModal()
    {
        if (signInModalPending)
        {
            ImGui.OpenPopup("Sign in with XIVAuth");
            signInModalPending = false;
        }

        ImGui.SetNextWindowSize(new Vector2(360, 0));
        if (!ImGui.BeginPopupModal("Sign in with XIVAuth", ImGuiWindowFlags.NoResize))
        {
            return;
        }

        switch (signInState)
        {
            case SignInState.Requesting:
                ImGui.TextWrapped("Starting sign-in...");
                break;

            case SignInState.AwaitingBrowser:
                ImGui.TextWrapped("A browser window should have opened. If it didn't, open this link:");
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputText("##verificationUri", ref signInVerificationUri, 256, ImGuiInputTextFlags.ReadOnly);
                if (ImGui.SmallButton("Copy link"))
                {
                    ImGui.SetClipboardText(signInVerificationUri);
                }

                ImGui.Spacing();
                ImGui.TextWrapped("Code:");
                ImGui.TextColored(Accent, signInUserCode);
                ImGui.SameLine();
                if (ImGui.SmallButton("Copy code"))
                {
                    ImGui.SetClipboardText(signInUserCode);
                }

                ImGui.Spacing();
                ImGui.TextColored(MutedText, "Waiting for confirmation...");
                break;

            case SignInState.Onboarding:
                ImGui.TextColored(Good, "Signed in! A couple of quick questions:");
                ImGui.Spacing();
                ImGui.TextWrapped("Pick a gamer tag - this is what friends search for and see everywhere (not your character name).");
                var onboardingNameValid = DisplayNameRules.IsValid(onboardingNameInput);
                ImGui.SetNextItemWidth(-1f);
                using (ImRaii.Disabled(onboardingNameSubmitting))
                using (onboardingNameValid || onboardingNameInput.Length == 0 ? default : ImRaii.PushColor(ImGuiCol.Text, Danger))
                {
                    ImGui.InputText("##onboardingName", ref onboardingNameInput, DisplayNameRules.MaxLength);
                }

                ImGui.TextColored(MutedText,
                    $"{DisplayNameRules.MinLength}-{DisplayNameRules.MaxLength} characters: letters, numbers, single spaces, _ or -.");
                if (onboardingNameError is { Length: > 0 } nameOnboardError)
                {
                    ImGui.TextColored(Danger, nameOnboardError);
                }

                ImGui.Spacing();
                ImGui.TextWrapped("Which races do you play? (optional)");
                foreach (var race in KnownRaces)
                {
                    var selected = onboardingRaces.Contains(race);
                    if (ImGui.Checkbox(race, ref selected))
                    {
                        if (selected)
                        {
                            onboardingRaces.Add(race);
                        }
                        else
                        {
                            onboardingRaces.Remove(race);
                        }
                    }
                }

                ImGui.Spacing();
                ImGui.TextWrapped("Do you want to see Lalafell content in Friends/Activity/etc.? (you can change this later in Settings)");
                ImGui.Checkbox("Yes, show me Lalafell content", ref onboardingWantsToSeeLalafellContent);

                ImGui.Spacing();
                using (ImRaii.Disabled(!onboardingNameValid || onboardingNameSubmitting))
                {
                    if (ImGui.Button(onboardingNameSubmitting ? "Saving..." : "Done"))
                    {
                        SubmitOnboarding();
                    }
                }

                break;

            case SignInState.Succeeded:
                ImGui.TextColored(Good, "Signed in!");
                if (ImGui.Button("Close"))
                {
                    signInState = SignInState.Idle;
                    ImGui.CloseCurrentPopup();
                }

                break;

            case SignInState.Failed:
                ImGui.TextColored(Danger, signInStatusMessage.Length > 0 ? signInStatusMessage : "Sign-in failed.");
                if (ImGui.Button("Close"))
                {
                    signInState = SignInState.Idle;
                    ImGui.CloseCurrentPopup();
                }

                break;

            default:
                ImGui.CloseCurrentPopup();
                break;
        }

        if (signInState is SignInState.Requesting or SignInState.AwaitingBrowser)
        {
            ImGui.Spacing();
            if (ImGui.Button("Cancel"))
            {
                signInCts?.Cancel();
                signInState = SignInState.Idle;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
    }
}
