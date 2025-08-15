using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using PEAKLib.Core;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PEAKLobbyBrowser
{
    public enum LobbyVisibility { Public, FriendsOnly, Private }

    [BepInDependency(CorePlugin.Id)]
    [BepInDependency(UIPlugin.Id)]
    [BepInPlugin("PEAKLobbyBrowser", "PEAK Lobby Browser", "1.1.2")]
    public class PEAKLobbyBrowser : BaseUnityPlugin
    {
        public static PEAKLobbyBrowser Instance { get; private set; }
        internal static ManualLogSource Log;

        private bool isSearchingForLobbies = false;
        private List<LobbyDetails> publicLobbies = new List<LobbyDetails>();
        private const string LobbyBrowserKey = "peak_lobby_browser_public";
        protected Callback<LobbyMatchList_t> m_LobbyMatchList;
        private static PeakCustomPage lobbyBrowserPage;
        private static PeakScrollableContent lobbyListContainer;
        private static PeakCustomPage lobbySettingsPage;
        private static PeakTextInput lobbyNameInput;
        private static PeakTextInput lobbyLanguageInput;
        private static PeakTextInput lobbyDescriptionInput;
        private static PeakTextInput modpackIdInput;
        private static PeakButton visibilityButton;
        private static LobbyVisibility currentVisibility = LobbyVisibility.Public;
        private static PeakTextInput searchInput;
        private static string currentSearchTerm = "";
        private static int currentPage = 0;
        private const int LobbiesPerPage = 5;
        private static PeakText pageInfoText;
        private static PeakMenuButton previousButton;
        private static PeakMenuButton nextButton;

        private bool isModConfigInstalled;

        private void Awake()
        {
            Instance = this;
            Log = BepInEx.Logging.Logger.CreateLogSource("PEAKLobbyBrowser");
            m_LobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
            InitializeInGameUI();

            isModConfigInstalled = Chainloader.PluginInfos.ContainsKey("com.github.PEAKModding.PEAKLib.ModConfig");

            SceneManager.sceneLoaded += OnSceneLoaded;

            Log.LogInfo("PEAK Lobby Browser Initialized!");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (m_LobbyMatchList != null)
            {
                m_LobbyMatchList.Dispose();
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Title")
            {
                StartCoroutine(CreateButtonWhenMenuIsReady());
            }
        }

        private IEnumerator CreateButtonWhenMenuIsReady()
        {
            if (GameObject.Find("Button_LobbyBrowser") != null)
            {
                yield break;
            }

            var buttonsContainerPath = "MainMenu/Canvas/MainPage/Menu/Buttons";
            yield return new WaitUntil(() => GameObject.Find(buttonsContainerPath) != null);

            yield return new WaitForSeconds(2.5f);

            LobbyBrowserMainMenuButton();
        }

        private void InitializeInGameUI()
        {
            try
            {
                MenuAPI.AddToPauseMenu(parent => LobbySettingsPauseMenuButton(parent));
            }
            catch (Exception e)
            {
                Log.LogFatal($"Could not initialize PEAKLib.UI buttons: {e.Message}");
            }
        }

        #region Main Menu Browser Page

        private IEnumerator ShowPageAfterCreation(PeakCustomPage page)
        {
            yield return new WaitForEndOfFrame();
            if (page != null)
            {
                page.Show();
            }
        }

        private void LobbyBrowserMainMenuButton()
        {
            try
            {
                var originalButton = GameObject.Find("MainMenu/Canvas/MainPage/Menu/Buttons/Button_PlaySolo");
                var newParent = GameObject.Find("MainMenu/Canvas/MainPage");

                if (originalButton == null || newParent == null)
                {
                    Log.LogFatal("Could not find the original button or the new parent transform.");
                    return;
                }

                var newButtonObject = Instantiate(originalButton, newParent.transform);

                newButtonObject.SetActive(true);

                var animator = newButtonObject.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.Play("Normal");
                }

                StartCoroutine(ModifyLobbyBrowserButton(newButtonObject));
            }
            catch (Exception e) { Log.LogFatal($"Failed to build lobby browser menu button: {e.Message}"); }
        }

        private IEnumerator ModifyLobbyBrowserButton(GameObject buttonObject)
        {
            yield return new WaitForEndOfFrame();

            buttonObject.name = "Button_LobbyBrowser";

            var nextLevelUI = GameObject.Find("MainMenu/Canvas/MainPage/NextLevelUI");
            if (nextLevelUI == null)
            {
                Log.LogFatal("Could not find NextLevelUI to position the button.");
                yield break;
            }

            var buttonRect = buttonObject.GetComponent<RectTransform>();
            var nextLevelRect = nextLevelUI.GetComponent<RectTransform>();

            buttonRect.anchorMin = nextLevelRect.anchorMin;
            buttonRect.anchorMax = nextLevelRect.anchorMax;

            buttonRect.pivot = new Vector2(0.5f, 0.5f);

            float verticalOffset = nextLevelRect.rect.height + 15f;

            var fineTuneOffset = new Vector2(-180f, 40f);

            buttonRect.anchoredPosition = nextLevelRect.anchoredPosition - new Vector2(0, verticalOffset) + fineTuneOffset;

            buttonObject.transform.localScale = new Vector3(-1f, 1.3f, 1f);

            var buttonText = buttonObject.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = "LOBBY BROWSER";

                buttonText.transform.localScale = new Vector3(-0.9f, 0.7f, 0.9f);
            }

            var buttonComponent = buttonObject.GetComponentInChildren<Button>();
            if (buttonComponent != null)
            {
                buttonComponent.onClick.RemoveAllListeners();
                buttonComponent.onClick.AddListener(() => OpenLobbyBrowserPage());
            }
        }

        #endregion

        #region Pause Menu Settings Page

        private void LobbySettingsPauseMenuButton(Transform backButton)
        {
            try
            {
                Transform realParent = backButton.parent;

                PeakMenuButton lobbyButton = MenuAPI.CreatePauseMenuButton("LOBBY SETTINGS")
                    .ParentTo(realParent)
                    .OnClick(() =>
                    {
                        UIInputHandler.SetSelectedObject(null);
                        OpenLobbySettingsPage();
                    });

                lobbyButton.gameObject.name = "Button_LobbySettings";

                StartCoroutine(PositionLobbySettingsButton(lobbyButton.gameObject, backButton, isModConfigInstalled));
            }
            catch (Exception e)
            {
                Log.LogFatal($"Failed to create lobby settings pause button: {e.Message}");
            }
        }

        private IEnumerator PositionLobbySettingsButton(GameObject lobbyButton, Transform backButton, bool modConfigInstalled)
        {
            yield return new WaitForEndOfFrame();

            var lobbyButtonRect = lobbyButton.GetComponent<RectTransform>();
            var backButtonRect = backButton.GetComponent<RectTransform>();
            float spacing = 15f;

            if (modConfigInstalled)
            {
                float modSettingsButtonHeight = 70f;
                lobbyButtonRect.anchoredPosition = new Vector2(
                    171f,
                    -230f - modSettingsButtonHeight - spacing
                );
            }
            else
            {
                lobbyButtonRect.anchoredPosition = new Vector2(
                    backButtonRect.anchoredPosition.x,
                    backButtonRect.anchoredPosition.y - backButtonRect.rect.height - spacing
                );
            }
        }

        #endregion

        #region Create Lobby Settings Page

        private void CreateLobbyBrowserPage()
        {
            if (lobbyBrowserPage != null) return;
            lobbyBrowserPage = MenuAPI.CreatePageWithBackground("Lobby Browser Page");
            MenuAPI.CreateText("Public Lobbies").SetFontSize(48f).ParentTo(lobbyBrowserPage.transform).SetAnchorMinMax(new Vector2(0.5f, 1f)).SetPivot(new Vector2(0.5f, 1f)).SetPosition(new Vector2(0, -50f));
            searchInput = MenuAPI.CreateTextInput("SearchInput")
                .ParentTo(lobbyBrowserPage.transform)
                .SetPlaceholder("Search for Lobby Name, Host, Language or Modpack.....")
                .SetAnchorMinMax(new Vector2(0.5f, 1f))
                .SetPivot(new Vector2(0.5f, 1f))
                .SetPosition(new Vector2(-75, -120f))
                .SetSize(new Vector2(650f, 50f));
            MenuAPI.CreateMenuButton("Search")
                .ParentTo(lobbyBrowserPage.transform)
                .SetAnchorMinMax(new Vector2(0.5f, 1f))
                .SetPivot(new Vector2(0.5f, 1f))
                .SetPosition(new Vector2(325, -120f))
                .SetSize(new Vector2(150f, 50f))
                .OnClick(() => {
                    currentPage = 0;
                    currentSearchTerm = searchInput.InputField.text;
                    PopulateLobbyPage();
                });
            lobbyListContainer = MenuAPI.CreateScrollableContent("LobbyList")
                .ParentTo(lobbyBrowserPage.transform)
                .SetAnchorMinMax(new Vector2(0.5f, 1f))
                .SetPivot(new Vector2(0.5f, 1f))
                .SetPosition(new Vector2(0, -180f))
                .SetSize(new Vector2(800f, 625f));
            var layoutGroup = lobbyListContainer.Content.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.spacing = 5f;
            }

            previousButton = MenuAPI.CreateMenuButton("<< Previous").ParentTo(lobbyBrowserPage.transform).SetAnchorMinMax(new Vector2(0.5f, 0f)).SetPivot(new Vector2(2.2f, 0f)).SetPosition(new Vector2(0, 100f)).SetWidth(200f).OnClick(() => GoToPreviousPage());
            nextButton = MenuAPI.CreateMenuButton("Next >>").ParentTo(lobbyBrowserPage.transform).SetAnchorMinMax(new Vector2(0.5f, 0f)).SetPivot(new Vector2(-1.2f, 0f)).SetPosition(new Vector2(0, 100f)).SetWidth(200f).OnClick(() => GoToNextPage());
            MenuAPI.CreateMenuButton("Refresh").ParentTo(lobbyBrowserPage.transform).SetAnchorMinMax(new Vector2(0.5f, 0f)).SetPivot(new Vector2(1.1f, 0f)).SetPosition(new Vector2(0, 100f)).SetWidth(200f).OnClick(() => {
                if (searchInput != null) searchInput.InputField.text = "";
                currentSearchTerm = "";
                currentPage = 0;
                RefreshLobbyList();
            });
            MenuAPI.CreateMenuButton("Close").ParentTo(lobbyBrowserPage.transform).SetAnchorMinMax(new Vector2(0.5f, 0f)).SetPivot(new Vector2(-0.1f, 0f)).SetPosition(new Vector2(0, 100f)).SetWidth(200f).OnClick(() => lobbyBrowserPage.Hide());

            pageInfoText = MenuAPI.CreateText("Page 1 / 1")
                .ParentTo(lobbyBrowserPage.transform)
                .SetAnchorMinMax(new Vector2(0.5f, 0f))
                .SetPivot(new Vector2(0.5f, 0f))
                .SetPosition(new Vector2(0, 70f))
                .SetFontSize(24f);
        }

        private void PopulateLobbyPage()
        {
            if (lobbyListContainer == null) return;
            foreach (Transform child in lobbyListContainer.Content) { Destroy(child.gameObject); }

            var lobbiesToDisplay = string.IsNullOrWhiteSpace(currentSearchTerm)
                ? publicLobbies
                : publicLobbies.Where(lobby =>
                    (lobby.LobbyName != null && lobby.LobbyName.IndexOf(currentSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (lobby.HostName != null && lobby.HostName.IndexOf(currentSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (lobby.Language != null && lobby.Language.IndexOf(currentSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (lobby.ModpackId != null && lobby.ModpackId.IndexOf(currentSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                  ).ToList();
            int totalLobbies = lobbiesToDisplay.Count;
            int totalPages = (int)Math.Ceiling((double)totalLobbies / LobbiesPerPage);
            if (totalPages == 0) totalPages = 1;
            var pagedLobbies = lobbiesToDisplay.Skip(currentPage * LobbiesPerPage).Take(LobbiesPerPage);

            if (pageInfoText != null) pageInfoText.TextMesh.text = $"Page {currentPage + 1} / {totalPages}";
            if (previousButton != null) previousButton.Button.interactable = (currentPage > 0);
            if (nextButton != null) nextButton.Button.interactable = (currentPage < totalPages - 1);
            if (!pagedLobbies.Any())
            {
                MenuAPI.CreateText(isSearchingForLobbies ? "Searching..." : "No matching lobbies found.").ParentTo(lobbyListContainer.Content).SetFontSize(28f).SetColor(Color.yellow);
                return;
            }

            foreach (var lobby in pagedLobbies)
            {
                var lobbyEntry = MenuAPI.CreateButton($"Entry_{lobby.HostName}")
                    .ParentTo(lobbyListContainer.Content)
                    .SetHeight(120f)
                    .OnClick(() => {
                        if (!string.IsNullOrEmpty(lobby.ModpackId))
                        {
                            GUIUtility.systemCopyBuffer = lobby.ModpackId;
                            Log.LogInfo($"Copied Modpack ID to clipboard: {lobby.ModpackId}");
                        }
                    });
                if (lobbyEntry.Text != null && lobbyEntry.Text.TextMesh != null)
                {
                    lobbyEntry.Text.TextMesh.text = "";
                }

                string fullDescription = $"{lobby.LobbyName}\n<color=grey><i>{lobby.Description}</i></color>";
                if (!string.IsNullOrEmpty(lobby.ModpackId))
                {
                    fullDescription += $"\n<color=yellow>Modpack:</color> {lobby.ModpackId}";
                }
                fullDescription += $"\n<color=orange>Host:</color> {lobby.HostName} | <color=orange>Language:</color> {lobby.Language} | <color=orange>Players:</color> {lobby.MemberCount}/{lobby.MaxMembers}";
                MenuAPI.CreateText(fullDescription)
                    .ParentTo(lobbyEntry.transform).SetAnchorMinMax(new Vector2(0f, 0.5f)).SetPivot(new Vector2(0f, 0.5f)).SetPosition(new Vector2(20f, 0)).SetFontSize(20f);
                MenuAPI.CreateMenuButton("Join").ParentTo(lobbyEntry.transform).SetAnchorMinMax(new Vector2(1f, 0.5f)).SetPivot(new Vector2(1f, 0.5f)).SetPosition(new Vector2(-20f, 0)).SetWidth(150f).OnClick(() => {
                    AttemptToJoinLobby(lobby.LobbyID.ToString());
                    lobbyBrowserPage.Hide();
                });
            }
        }

        private void GoToNextPage()
        {
            var filteredLobbies = string.IsNullOrWhiteSpace(currentSearchTerm) ? publicLobbies : publicLobbies.Where(lobby =>
                   (lobby.LobbyName != null && lobby.LobbyName.IndexOf(currentSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (lobby.HostName != null && lobby.HostName.IndexOf(currentSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                 ).ToList();
            int totalPages = (int)Math.Ceiling((double)filteredLobbies.Count / LobbiesPerPage);
            if (currentPage < totalPages - 1)
            {
                currentPage++;
                PopulateLobbyPage();
            }
        }

        private void GoToPreviousPage()
        {
            if (currentPage > 0)
            {
                currentPage--;
                PopulateLobbyPage();
            }
        }

        private void OpenLobbyBrowserPage()
        {
            CreateLobbyBrowserPage();
            StartCoroutine(ShowPageAfterCreation(lobbyBrowserPage));

            if (searchInput != null) searchInput.InputField.text = "";
            currentSearchTerm = "";
            currentPage = 0;
            lobbyBrowserPage.Show();
            RefreshLobbyList();
        }

        private void CreateLobbySettingsPage()
        {
            if (lobbySettingsPage != null) return;
            lobbySettingsPage = MenuAPI.CreatePageWithBackground("Lobby Settings Page");
            var pageCanvas = lobbySettingsPage.GetComponent<Canvas>();
            if (pageCanvas != null)
            {
                pageCanvas.overrideSorting = true;
                pageCanvas.sortingOrder = 999;
            }
            MenuAPI.CreateText("Lobby Settings").SetFontSize(48f).ParentTo(lobbySettingsPage.transform).SetAnchorMinMax(new Vector2(0.5f, 1f)).SetPivot(new Vector2(0.5f, 1f)).SetPosition(new Vector2(0, -50f));
            var settingsContainer = MenuAPI.CreateScrollableContent("SettingsContainer").ParentTo(lobbySettingsPage.transform).SetAnchorMinMax(new Vector2(0.5f, 0.5f)).SetPivot(new Vector2(0.5f, 0.5f)).SetSize(new Vector2(600f, 600f));
            var content = settingsContainer.Content;

            MenuAPI.CreateText("Lobby Name:").ParentTo(content).SetFontSize(24f);
            lobbyNameInput = MenuAPI.CreateTextInput("LobbyNameInput").ParentTo(content);
            lobbyNameInput.InputField.characterLimit = 40;

            MenuAPI.CreateText("Language:").ParentTo(content).SetFontSize(24f);
            lobbyLanguageInput = MenuAPI.CreateTextInput("LobbyLanguageInput").ParentTo(content);
            lobbyLanguageInput.InputField.characterLimit = 10;

            MenuAPI.CreateText("Description:").ParentTo(content).SetFontSize(22f);
            lobbyDescriptionInput = MenuAPI.CreateTextInput("LobbyDescriptionInput").ParentTo(content);
            lobbyDescriptionInput.InputField.characterLimit = 60;

            MenuAPI.CreateText("Modpack:").ParentTo(content).SetFontSize(18f);
            modpackIdInput = MenuAPI.CreateTextInput("ModpackIdInput").ParentTo(content);
            modpackIdInput.InputField.characterLimit = 44;

            visibilityButton = MenuAPI.CreateButton("Visibility: Public").ParentTo(content).SetHeight(50f).OnClick(() => CycleLobbyVisibility());
            MenuAPI.CreateMenuButton("Apply Settings").ParentTo(lobbySettingsPage.transform).SetAnchorMinMax(new Vector2(0.5f, 0f)).SetPivot(new Vector2(1.1f, 0f)).SetPosition(new Vector2(0, 100f)).SetWidth(200f).OnClick(() => ApplyLobbySettings());
            MenuAPI.CreateMenuButton("Close").ParentTo(lobbySettingsPage.transform).SetAnchorMinMax(new Vector2(0.5f, 0f)).SetPivot(new Vector2(-0.1f, 0f)).SetPosition(new Vector2(0, 100f)).SetWidth(200f).OnClick(() => lobbySettingsPage.Hide());
        }

        private void CycleLobbyVisibility()
        {
            currentVisibility = (LobbyVisibility)(((int)currentVisibility + 1) % 3);
            UpdateVisibilityButtonText();
        }

        private void UpdateVisibilityButtonText()
        {
            if (visibilityButton != null && visibilityButton.Text != null && visibilityButton.Text.TextMesh != null)
            {
                visibilityButton.Text.TextMesh.text = $"Visibility: {currentVisibility}";
            }
        }

        private void OpenLobbySettingsPage()
        {
            CreateLobbySettingsPage();
            StartCoroutine(ShowPageAfterCreation(lobbyBrowserPage));

            if (GameHandler.GetService<SteamLobbyHandler>().InSteamLobby(out CSteamID lobbyID))
            {
                lobbyNameInput.InputField.text = SteamMatchmaking.GetLobbyData(lobbyID, "lobbyName");
                lobbyLanguageInput.InputField.text = SteamMatchmaking.GetLobbyData(lobbyID, "language");
                lobbyDescriptionInput.InputField.text = SteamMatchmaking.GetLobbyData(lobbyID, "description");
                modpackIdInput.InputField.text = SteamMatchmaking.GetLobbyData(lobbyID, "modpackId");
                if (string.IsNullOrWhiteSpace(lobbyLanguageInput.InputField.text))
                {
                    string steamLanguage = SteamApps.GetCurrentGameLanguage();
                    if (!string.IsNullOrEmpty(steamLanguage))
                    {
                        steamLanguage = char.ToUpper(steamLanguage[0]) + steamLanguage.Substring(1);
                    }
                    lobbyLanguageInput.InputField.text = steamLanguage;
                }

                if (SteamMatchmaking.GetLobbyData(lobbyID, LobbyBrowserKey) == "true")
                {
                    currentVisibility = LobbyVisibility.Public;
                }
                else
                {
                    currentVisibility = LobbyVisibility.Public;
                }
                UpdateVisibilityButtonText();
            }
            lobbySettingsPage.Show();
        }

        private void ApplyLobbySettings()
        {
            if (GameHandler.GetService<SteamLobbyHandler>().InSteamLobby(out CSteamID lobbyID))
            {
                Log.LogInfo($"Applying new lobby settings. Visibility: {currentVisibility}");
                SteamMatchmaking.SetLobbyData(lobbyID, "hostName", SteamFriends.GetPersonaName());
                SteamMatchmaking.SetLobbyData(lobbyID, "lobbyName", lobbyNameInput.InputField.text);
                SteamMatchmaking.SetLobbyData(lobbyID, "language", lobbyLanguageInput.InputField.text);
                SteamMatchmaking.SetLobbyData(lobbyID, "description", lobbyDescriptionInput.InputField.text);
                SteamMatchmaking.SetLobbyData(lobbyID, "modpackId", modpackIdInput.InputField.text);
                switch (currentVisibility)
                {
                    case LobbyVisibility.Public:
                        SteamMatchmaking.SetLobbyType(lobbyID, ELobbyType.k_ELobbyTypePublic);
                        SteamMatchmaking.SetLobbyData(lobbyID, LobbyBrowserKey, "true");
                        break;
                    case LobbyVisibility.FriendsOnly:
                        SteamMatchmaking.SetLobbyType(lobbyID, ELobbyType.k_ELobbyTypeFriendsOnly);
                        SteamMatchmaking.SetLobbyData(lobbyID, LobbyBrowserKey, "false");
                        break;
                    case LobbyVisibility.Private:
                        SteamMatchmaking.SetLobbyType(lobbyID, ELobbyType.k_ELobbyTypePrivate);
                        SteamMatchmaking.SetLobbyData(lobbyID, LobbyBrowserKey, "false");
                        break;
                }
            }
            else { Log.LogWarning("Cannot apply settings, not in a Steam lobby."); }
            lobbySettingsPage.Hide();
        }

        #endregion

        #region Steamworks Logic

        public void RefreshLobbyList()
        {
            if (isSearchingForLobbies) return;
            Log.LogInfo("Requesting public lobby list...");
            isSearchingForLobbies = true;
            publicLobbies.Clear();
            PopulateLobbyPage();
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyBrowserKey, "true", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.RequestLobbyList();
        }


        private void OnLobbyMatchList(LobbyMatchList_t pCallback)
        {
            Log.LogInfo($"Found {pCallback.m_nLobbiesMatching} public lobbies.");
            publicLobbies.Clear();
            for (int i = 0; i < pCallback.m_nLobbiesMatching; i++)
            {
                CSteamID lobbyID = SteamMatchmaking.GetLobbyByIndex(i);
                publicLobbies.Add(new LobbyDetails
                {
                    LobbyID = lobbyID,
                    HostName = SteamMatchmaking.GetLobbyData(lobbyID, "hostName"),
                    MemberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyID),
                    MaxMembers = SteamMatchmaking.GetLobbyMemberLimit(lobbyID),
                    LobbyName = SteamMatchmaking.GetLobbyData(lobbyID, "lobbyName"),
                    Language = SteamMatchmaking.GetLobbyData(lobbyID, "language"),
                    Description = SteamMatchmaking.GetLobbyData(lobbyID, "description"),
                    ModpackId = SteamMatchmaking.GetLobbyData(lobbyID, "modpackId")
                });
            }
            isSearchingForLobbies = false;
            currentPage = 0;
            PopulateLobbyPage();
        }

        public void AttemptToJoinLobby(string lobbyInput)
        {
            if (string.IsNullOrWhiteSpace(lobbyInput))
            {
                Log.LogError("Lobby ID field is empty.");
                return;
            }
            if (ulong.TryParse(lobbyInput.Trim(), out ulong steamIdUlong))
            {
                Log.LogInfo($"Attempting to join lobby by ID: {steamIdUlong}");
                CSteamID lobbyID = new CSteamID(steamIdUlong);
                GameHandler.GetService<SteamLobbyHandler>().TryJoinLobby(lobbyID);
            }
        }

        public struct LobbyDetails
        {
            public CSteamID LobbyID;
            public string HostName;
            public int MemberCount;
            public int MaxMembers;
            public string LobbyName;
            public string Language;
            public string Description;
            public string ModpackId;
        }

        #endregion

    }
}