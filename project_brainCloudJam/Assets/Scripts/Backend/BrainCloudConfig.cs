using UnityEngine;

/// <summary>
/// App credentials, loaded from Assets/Resources/BrainCloudConfig.json.
/// That file is gitignored. Copy BrainCloudConfig.example.json to create it.
/// </summary>
[System.Serializable]
public class BrainCloudConfig
{
    public string appId;
    public string appSecret;
    public string appVersion = "1.0.0";
    public string serverUrl = "https://api.braincloudservers.com/dispatcherv2";

    const string ResourceName = "BrainCloudConfig";

    public static BrainCloudConfig Load()
    {
        var asset = Resources.Load<TextAsset>(ResourceName);
        if (asset == null)
        {
            Debug.LogError("[brainCloud] Missing Assets/Resources/BrainCloudConfig.json. Copy BrainCloudConfig.example.json and fill in your App ID and secret.");
            return null;
        }

        var config = JsonUtility.FromJson<BrainCloudConfig>(asset.text);
        if (string.IsNullOrEmpty(config.appId) || string.IsNullOrEmpty(config.appSecret))
        {
            Debug.LogError("[brainCloud] BrainCloudConfig.json is missing appId or appSecret.");
            return null;
        }
        return config;
    }
}
