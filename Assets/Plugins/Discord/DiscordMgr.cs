using System;
using UnityEngine;

public class DiscordMgr : MonoBehaviour
{
    Discord.Discord discord;
    long startTime = 0;
    const long ApplicationId = 1557224882784178246;
    const float ActivityUpdateInterval = 20f / 5f;
    float nextActivityUpdateTime = 0;

    void Start()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
        try
        {
            discord = new Discord.Discord(ApplicationId, (ulong)Discord.CreateFlags.NoRequireDiscord);
        }
        catch (Exception e)
        {

            Debug.LogWarning("DiscordMgr: Discord unavailable: " + e.Message);
            discord = null;
            return;
        }
        UpdateActivity();
        nextActivityUpdateTime = Time.unscaledTime + ActivityUpdateInterval;
#endif
    }

    void Update()
    {
        // Discord applies at most 5 activity updates per 20 seconds, so send one every 4 seconds
        if (Time.unscaledTime >= nextActivityUpdateTime)
        {
            nextActivityUpdateTime = Time.unscaledTime + ActivityUpdateInterval;
            UpdateActivity();
        }
        if (discord != null)
        {
            try
            {
                discord.RunCallbacks();
            }
            catch (Discord.ResultException e)
            {
                // Discord was closed while the game is running
                Debug.LogWarning("DiscordMgr: lost connection to Discord: " + e.Result);
                discord.Dispose();
                discord = null;
            }
        }
    }

    public void UpdateActivity()
    {
        if (discord == null || discord.GetActivityManager() == null) return;

        if (startTime == 0)
        {
            startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // Sets the time we started the game to the current unix time
        }

        var activity = new Discord.Activity
        {
            State = "Playing", // The large text that tells someone the main thing you're doing, such as 'In a Battle.'
            Details = "The Game", // The small text in the rich presence popup that describes the main thing you're doing, such as (5 pigs left)
            Timestamps =
            {
                Start = startTime // Tells discord when the activity started
            },
            Assets =
            {
                LargeImage = "icon", // replace icon with whatever you called your icon asset
            },
        };
        discord.GetActivityManager().UpdateActivity(activity, result =>
        {
        });
    }

    private void OnDestroy()
    {
        if (discord != null)
        {
            discord.Dispose(); // will dispose of the api client
            discord = null;
        }
    }
}
