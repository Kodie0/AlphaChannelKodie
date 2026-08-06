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

    private void DrawAccountSettings()
    {
        if (CurrentSession is { } session)
        {
            ImGui.TextColored(Good, $"Signed in as @{session.Handle}");
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

        var races = onboardingRaces.ToArray();
        var wantsToSeeLalafellContent = onboardingWantsToSeeLalafellContent;
        signInState = SignInState.Succeeded;
        onSessionChanged(session);
        pendingOnboardingSession = null;

        _ = Task.Run(() => authClient.SubmitOnboardingAsync(session.Token, races, wantsToSeeLalafellContent));
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
                if (ImGui.Button("Done"))
                {
                    SubmitOnboarding();
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
