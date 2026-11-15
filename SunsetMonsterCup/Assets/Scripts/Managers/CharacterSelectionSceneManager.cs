using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class CharacterSelectionSceneManager : MonoBehaviour
{
    [SerializeField]
    private GameData pData;

    [SerializeField]
    private Transform _playerPanel;
    [SerializeField]
    private GameObject controlsImage;

    [SerializeField]
    private int _playersAmount;

    [SerializeField]
    private List<GameObject> _playerPrefabs;
    private bool _isLoading;
    public static CharacterSelectionSceneManager Instance { get; private set; }
    public event Action<int, GameObject> PlayerJoined = delegate { };
    public event Action<int, GameObject> PlayerLeft = delegate { };
    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        controlsImage.SetActive(false);
        //DontDestroyOnLoad(PlayerInputManager.instance.gameObject);
    }

    private void Update()
    {
        if (!_isLoading && Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
        {
            StartGameWithKeyboardBypass();
        }
    }

    private void OnPlayerJoined(UnityEngine.InputSystem.PlayerInput playerInput)
    {
        PlayerJoined?.Invoke(playerInput.playerIndex, playerInput.gameObject);

        playerInput.gameObject.transform.SetParent(_playerPanel);

        _playersAmount++;
        if (_playersAmount == PlayerInputManager.instance.maxPlayerCount)
        {
            PlayerInputManager.instance.DisableJoining();
        }

        GameData.Instance.AddOrSetPlayerConfiguration(playerInput.playerIndex, playerInput.devices[0].device);

        //Debug.LogWarning($"Player Joined");
    }
    private void OnPlayerLeft(UnityEngine.InputSystem.PlayerInput playerInput)
    {
        PlayerLeft?.Invoke(playerInput.playerIndex, playerInput.gameObject);

        _playersAmount--;
        if (_playersAmount < PlayerInputManager.instance.maxPlayerCount)
        {
            PlayerInputManager.instance.EnableJoining();
        }

        //GameData.Instance.RemovePlayerConfiguration(playerInput.playerIndex);

        //Debug.LogWarning($"Player Left");
    }

    public void StartGameOnAllPlayersReady(bool allPlayersReady, int currentAmountOfPlayers)
    {
        if (!_isLoading && allPlayersReady && (pData.Testing || PlayerInputManager.instance.maxPlayerCount == currentAmountOfPlayers))
        {
            StartGame(currentAmountOfPlayers);
        }
    }

    private void StartGameWithKeyboardBypass()
    {
        int currentAmountOfPlayers = PlayerInputManager.instance.playerCount;
        int selectedAmountOfPlayers = pData.GetNumberOfPlayers();
        bool validBypassCount = selectedAmountOfPlayers == 2 && currentAmountOfPlayers == 1
            || selectedAmountOfPlayers == 4 && currentAmountOfPlayers == 3;

        if (!validBypassCount)
        {
            return;
        }

        PlayerUIController[] playerControllers = _playerPanel.GetComponentsInChildren<PlayerUIController>(true);
        if (playerControllers.Length != currentAmountOfPlayers)
        {
            return;
        }

        foreach (PlayerUIController playerController in playerControllers)
        {
            if (playerController.State != PlayerUIController.PlayerState.Ready)
            {
                return;
            }
        }

        StartGame(currentAmountOfPlayers);
    }

    private void StartGame(int currentAmountOfPlayers)
    {
        _isLoading = true;
        pData.PlayersPlaying = currentAmountOfPlayers;
        StartCoroutine(LoadScene(pData.GetNumberOfPlayers()));
    }

    private IEnumerator LoadScene(int amount)
    {
        yield return new WaitForSeconds(1f);

        if (amount == 0)
        {
            //Debug.LogError($"Amount of players wasn't set!");
        }

        controlsImage.SetActive(true);

        //Debug.Log($"Amount of players to spawn: {amount}");
        yield return new WaitForSeconds(5f);

        if (amount == 2)
        {
            SceneManager.LoadScene("Game Scene");
        }
        if (amount == 4)
        {
            SceneManager.LoadScene("Game Scene");
        }
    }
}
