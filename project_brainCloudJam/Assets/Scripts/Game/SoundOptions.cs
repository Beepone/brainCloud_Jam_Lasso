using UnityEngine;

namespace LootLasso
{
    // SoundOptions: the player's music and sound effect volumes, set on the OPTIONS screen (MainMenu).
    //
    // Each is 0 to 1, and every sound multiplies its own volume by it: GameMusic by Music, LassoSound and
    // the UI sounds (UiKit) by Sfx. Saved on this device with PlayerPrefs, like the player's name.
    //
    // The OPTIONS screen's - and + buttons step them by 10: OFF, 10, 20 ... 100.
    public static class SoundOptions
    {
        const int Steps = 10; // tenths

        // Where they start, until the player changes them: the music at half, so it sits under the sound effects.
        public const float DefaultMusic = 0.5f;
        public const float DefaultSfx = 1f;

        const string MusicKey = "LootLasso.MusicVolume";
        const string SfxKey = "LootLasso.SfxVolume";

        public static float Music => PlayerPrefs.GetFloat(MusicKey, DefaultMusic);
        public static float Sfx => PlayerPrefs.GetFloat(SfxKey, DefaultSfx);

        // step = +1 for a tenth louder, -1 for a tenth quieter (stopping at OFF and 100).
        public static void ChangeMusic(int step) => Save(MusicKey, Step(Music, step));
        public static void ChangeSfx(int step) => Save(SfxKey, Step(Sfx, step));

        static float Step(float level, int step) =>
            Mathf.Clamp(Mathf.RoundToInt(level * Steps) + step, 0, Steps) / (float)Steps;

        static void Save(string key, float level)
        {
            PlayerPrefs.SetFloat(key, level);
            PlayerPrefs.Save();
        }

        // How a level reads on screen: 10 to 100, or OFF (the pixel font has no % sign).
        public static string Describe(float level) => level <= 0f ? "OFF" : Mathf.RoundToInt(level * 100f).ToString();
    }
}
